using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.Dim
{
    // Creates a reviewable native Stair alternative. The source Stair and its
    // dimensions, tags, railings and hosted elements are not replaced implicitly.
    [Transaction(TransactionMode.Manual)]
    public class StairFlatLandingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uidoc = data.Application.ActiveUIDocument;
            if (uidoc == null) return Result.Cancelled;
            try
            {
                var doc = uidoc.Document;
                var source = uidoc.Selection.GetElementIds().Select(doc.GetElement).OfType<Stairs>().FirstOrDefault();
                if (source == null)
                    source = doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new StairFilter(),
                        "Chọn Stair Sketched có hai góc vuông cần đổi bậc quạt")) as Stairs;
                var plan = StairFlatLandingPlan.Analyze(source);
                var dialog = new TaskDialog("KTool - Chiếu nghỉ phẳng")
                {
                    MainInstruction = "Phương án thang với hai chiếu nghỉ đi lại phẳng",
                    MainContent = plan.Description + "\n\nTạo Stair phương án để kiểm tra ô thang, sàn và tường trước khi thay thang hiện hữu.",
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Tạo phương án và mở view kiểm tra");
                if (dialog.Show() != TaskDialogResult.CommandLink1) return Result.Cancelled;
                ElementId id;
                View3D view;
                using (var group = new TransactionGroup(doc, "KTool - Phương án chiếu nghỉ phẳng"))
                {
                    group.Start();
                    id = plan.Create(doc);
                    using (var tx = new Transaction(doc, "View phương án chiếu nghỉ"))
                    {
                        tx.Start();
                        var vt = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                            .First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
                        view = View3D.CreateIsometric(doc, vt.Id);
                        view.Name = "KTool_ChieuNghiPhang_" + id.Value;
                        view.DetailLevel = ViewDetailLevel.Fine;
                        view.DisplayStyle = DisplayStyle.Shading;
                        view.IsolateElementsTemporary(new[] { id });
                        tx.Commit();
                    }
                    group.Assimilate();
                }
                uidoc.Selection.SetElementIds(new[] { id });
                uidoc.RequestViewChange(view);
                TaskDialog.Show("KTool - Chiếu nghỉ phẳng", "Đã tạo Stair phương án " + id.Value + ".\n" + plan.Description +
                    (plan.Warnings.Count == 0 ? "" : "\n\nCảnh báo Revit:\n" + string.Join("\n", plan.Warnings)));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
        private sealed class StairFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) { return e is Stairs; }
            public bool AllowReference(Reference r, XYZ p) { return false; }
        }
    }

    internal sealed class StairFlatLandingPlan
    {
        private const double Mm = 1.0 / 304.8;
        private Stairs source;
        private XYZ[] points, directions;
        private double width, tread, height;
        private int[] risers;
        private int count;
        public readonly List<string> Warnings = new List<string>();
        public double ExtensionMm { get; private set; }
        public string Description { get; private set; }

        public static StairFlatLandingPlan Analyze(Stairs stair)
        {
            if (stair == null) throw new InvalidOperationException("Chưa chọn Stair.");
            var doc = stair.Document;
            if (stair.GetStairsRuns().Count != 1 || stair.GetStairsLandings().Count != 0)
                throw new InvalidOperationException("Phương án hiện hỗ trợ một vế Sketched chữ U có hai góc vuông.");
            var run = (StairsRun)doc.GetElement(stair.GetStairsRuns().First());
            var path = run.GetStairsPath().ToList();
            if (run.StairsRunStyle != StairsRunStyle.Sketched || path.Count != 3 || path.Any(c => !(c is Line)))
                throw new InvalidOperationException("Cần đường đi Sketched gồm ba đoạn thẳng chữ U.");
            var p = new StairFlatLandingPlan { source = stair };
            p.points = new[] { path[0].GetEndPoint(0), path[0].GetEndPoint(1), path[1].GetEndPoint(1), path[2].GetEndPoint(1) };
            p.directions = path.Select(c => new XYZ(c.GetEndPoint(1).X - c.GetEndPoint(0).X,
                c.GetEndPoint(1).Y - c.GetEndPoint(0).Y, 0).Normalize()).ToArray();
            if (path[1].GetEndPoint(0).DistanceTo(p.points[1]) > Mm || path[2].GetEndPoint(0).DistanceTo(p.points[2]) > Mm ||
                Math.Abs(p.directions[0].DotProduct(p.directions[1])) > 1e-6 ||
                Math.Abs(p.directions[1].DotProduct(p.directions[2])) > 1e-6 || p.directions[0].DotProduct(p.directions[2]) > -.999999)
                throw new InvalidOperationException("Đường đi phải liên tục, chữ U với hai góc 90 độ.");
            p.width = StairSlabSoffitGeometry.PathWidth(run);
            var type = doc.GetElement(stair.GetTypeId());
            if (!(type is StairsType stairsType) || stairsType.ConstructionMethod != StairsConstructionMethod.CastInPlace)
                throw new InvalidOperationException("Phương án hiện chỉ hỗ trợ Stair BTCT đổ tại chỗ (Cast-In-Place).");
            // Revit 2024 Stair type parameters (the newer STAIRSTYPE_* enums
            // do not resolve on the existing cast-in-place Stair type).
            var maximum = type.get_Parameter((BuiltInParameter)(-1007202));
            var minimum = type.get_Parameter((BuiltInParameter)(-1007203));
            if (maximum == null || minimum == null || maximum.AsDouble() <= 0 || minimum.AsDouble() <= 0)
                throw new InvalidOperationException("Không đọc được giới hạn cổ bậc/mặt bậc từ Stair Type.");
            p.tread = Math.Max(stair.ActualTreadDepth, minimum.AsDouble());
            p.height = stair.TopElevation - stair.BaseElevation;
            p.count = (int)Math.Ceiling(p.height / maximum.AsDouble() - 1e-8);
            p.risers = new int[3];
            for (int i = 0; i < 2; i++)
            {
                double available = path[i].Length - p.width * (i == 0 ? .5 : 1);
                int treads = (int)Math.Round(available / p.tread);
                if (treads < 1 || Math.Abs(available - treads * p.tread) > Mm)
                    throw new InvalidOperationException("Vế " + (i + 1) + " không đủ hoặc không khớp bước bậc sau khi dành chỗ cho chiếu nghỉ. Cần bố trí lại ô thang.");
                p.risers[i] = treads + 1;
            }
            p.risers[2] = p.count - p.risers[0] - p.risers[1];
            if (p.risers[2] < 2) throw new InvalidOperationException("Không đủ cổ bậc cho vế cuối; cần bố trí lại ba vế.");
            p.ExtensionMm = ((p.risers[2] - 1) * p.tread - (path[2].Length - p.width / 2)) / Mm;
            p.Description = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Rộng {0:0} mm; mặt bậc {1:0} mm; {2} cổ bậc × {3:0.00} mm.\nHai chiếu nghỉ {0:0} × {0:0} mm.\nVế cuối thay đổi chiều dài {4:+0;-0;0} mm.\nGiữ Stair Type: {5} (chiều dày bản theo Run/Landing Type).",
                p.width / Mm, p.tread / Mm, p.count, p.height / p.count / Mm, p.ExtensionMm, type.Name);
            return p;
        }

        public ElementId Create(Document doc)
        {
            if (doc.IsModifiable) throw new InvalidOperationException("Cần tạo Stair ngoài Transaction đang mở.");
            if (doc.GetElement(source.Id)?.UniqueId != source.UniqueId)
                throw new InvalidOperationException("Stair nguồn không thuộc tài liệu hiện tại.");
            using (var scope = new StairsEditScope(doc, "KTool - Tạo phương án chiếu nghỉ"))
            {
                var id = scope.Start(source.get_Parameter((BuiltInParameter)(-1007200)).AsElementId(),
                    source.get_Parameter((BuiltInParameter)(-1007201)).AsElementId());
                using (var tx = new Transaction(doc, "Ba vế và hai chiếu nghỉ"))
                {
                    tx.Start();
                    var stair = (Stairs)doc.GetElement(id);
                    stair.ChangeTypeId(source.GetTypeId());
                    foreach (int parameter in new[] { -1007218, -1007219 })
                        stair.get_Parameter((BuiltInParameter)parameter).Set(source.get_Parameter((BuiltInParameter)parameter).AsDouble());
                    stair.DesiredRisersNumber = count;
                    stair.ActualTreadDepth = tread;
                    stair.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set("KTool chiếu nghỉ phẳng; Stair nguồn " + source.Id.Value);
                    doc.Regenerate();
                    double h = stair.ActualRiserHeight;
                    int total = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        XYZ start = i == 0 ? points[0] : points[i] + directions[i] * width / 2;
                        start = new XYZ(start.X, start.Y, stair.BaseElevation + total * h);
                        var run = StairsRun.CreateStraightRun(doc, id, Line.CreateBound(start,
                            start + directions[i] * ((risers[i] - 1) * tread)), StairsRunJustification.Center);
                        run.ActualRunWidth = width;
                        total += risers[i];
                        if (i == 2) continue;
                        XYZ c = points[i + 1], u = directions[i] * width / 2, v = directions[i + 1] * width / 2;
                        var corners = new[] { c - u - v, c + u - v, c + u + v, c - u + v };
                        var loop = new CurveLoop();
                        for (int j = 0; j < 4; j++)
                            loop.Append(Line.CreateBound(new XYZ(corners[j].X, corners[j].Y, 0),
                                new XYZ(corners[(j + 1) % 4].X, corners[(j + 1) % 4].Y, 0)));
                        StairsLanding.CreateSketchedLanding(doc, id, loop, total * h);
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Không tạo được các vế/chiếu nghỉ.");
                }
                scope.Commit(new Failures(Warnings));
                var result = doc.GetElement(id) as Stairs;
                if (result == null || result.ActualRisersNumber != count || result.GetStairsRuns().Count != 3 ||
                    result.GetStairsLandings().Count != 2)
                    throw new InvalidOperationException("Stair không đạt số vế, chiếu nghỉ hoặc cổ bậc yêu cầu.");
                return id;
            }
        }
        private sealed class Failures : IFailuresPreprocessor
        {
            private readonly List<string> warnings;
            public Failures(List<string> values) { warnings = values; }
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (var failure in accessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() != FailureSeverity.Warning) return FailureProcessingResult.ProceedWithRollBack;
                    warnings.Add(failure.GetDescriptionText());
                    accessor.DeleteWarning(failure);
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}

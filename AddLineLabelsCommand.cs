using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Ghi chú tên lên các Detail Line đã vẽ:
    ///  - Ống nước: nhận theo Line Style của hệ thống (CW, C, HW, WP, RP, VP, SP, DP), ghi ký hiệu hệ thống.
    ///  - Dây điện: nhận theo Line Style "Dây ...", ghi đúng tên Line Style (ví dụ "Dây 2.5").
    /// Chọn sẵn các Detail Line để chỉ ghi chú các đường đó, nếu không thì xử lý tất cả trong view.
    /// Đường đã có ghi chú cùng nội dung ở gần giữa đoạn sẽ được bỏ qua (chạy lại không bị trùng).
    /// </summary>
    public abstract class LabelCommandBase : IExternalCommand
    {
        private static readonly string[] PIPE_CODES = { "CW", "C", "HW", "WP", "RP", "VP", "SP", "DP" };
        private const string WIRE_PREFIX = "Dây ";

        protected abstract string Title { get; }
        protected abstract bool IncludePipes { get; }
        protected abstract bool IncludeWires { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;
                View view = doc.ActiveView;

                if (!LineLabelHelper.SupportsDetailLines(view))
                {
                    TaskDialog.Show(Title, "View hiện tại không hỗ trợ Detail Line.");
                    return Result.Cancelled;
                }

                // Line Style -> nội dung ghi chú (ống nước)
                Dictionary<string, string> pipeLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (IncludePipes)
                {
                    foreach (string code in PIPE_CODES)
                    {
                        WaterPipeDefinition def = WaterPipeDefinitions.Get(code);
                        if (def != null && !string.IsNullOrEmpty(def.StyleName))
                            pipeLabels[def.StyleName] = def.Code;
                    }
                }

                // Detail Line: ưu tiên các đường đang chọn, nếu không thì tất cả trong view
                List<DetailCurve> lines = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .OfType<DetailCurve>()
                    .Where(d => d.OwnerViewId == view.Id)
                    .ToList();

                if (lines.Count == 0)
                {
                    lines = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_Lines)
                        .WhereElementIsNotElementType()
                        .OfType<DetailCurve>()
                        .ToList();
                }

                if (lines.Count == 0)
                {
                    TaskDialog.Show(Title, "Không có Detail Line nào trong view này.");
                    return Result.Cancelled;
                }

                TextNoteType textType = LineLabelHelper.DefaultTextNoteType(doc);
                double offset = LineLabelHelper.Offset(textType, view);

                // Ghi chú đã có trong view (để không tạo trùng)
                List<TextNote> existing = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .ToList();

                int created = 0, already = 0, tooShort = 0, unknown = 0;

                using (Transaction tx = new Transaction(doc, Title))
                {
                    tx.Start();
                    try
                    {
                        foreach (DetailCurve dc in lines)
                        {
                            Line ln = dc.GeometryCurve as Line;
                            GraphicsStyle gs = dc.LineStyle as GraphicsStyle;
                            if (ln == null || gs == null)
                            {
                                unknown++;
                                continue;
                            }

                            string label = ResolveLabel(gs.Name, pipeLabels);
                            if (label == null)
                            {
                                unknown++;
                                continue;
                            }

                            XYZ p0 = ln.GetEndPoint(0);
                            XYZ p1 = ln.GetEndPoint(1);

                            // Đoạn quá ngắn không đủ chỗ ghi chữ
                            if (p0.DistanceTo(p1) < LineLabelHelper.EstimatedWidth(label, textType, view))
                            {
                                tooShort++;
                                continue;
                            }

                            XYZ mid = (p0 + p1) / 2.0;
                            double tol = offset * 1.6;
                            bool hasLabel = existing.Any(t =>
                                string.Equals((t.Text ?? "").Trim(), label, StringComparison.OrdinalIgnoreCase)
                                && t.Coord.DistanceTo(mid) <= tol);
                            if (hasLabel)
                            {
                                already++;
                                continue;
                            }

                            TextNote note = LineLabelHelper.CreateLabel(doc, view, textType, p0, p1, label);
                            if (note != null)
                            {
                                existing.Add(note);
                                created++;
                            }
                        }
                        tx.Commit();
                    }
                    catch
                    {
                        tx.RollBack();
                        throw;
                    }
                }

                string msg = "Đã tạo " + created + " ghi chú.";
                if (already > 0) msg += "\nBỏ qua " + already + " đường đã có ghi chú.";
                if (tooShort > 0) msg += "\nBỏ qua " + tooShort + " đoạn quá ngắn để ghi chữ.";
                if (unknown > 0) msg += "\nBỏ qua " + unknown + " đường không thuộc " + Scope() + ".";
                TaskDialog.Show(Title, msg);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private string ResolveLabel(string styleName, Dictionary<string, string> pipeLabels)
        {
            string label;
            if (IncludePipes && pipeLabels.TryGetValue(styleName, out label))
                return label;

            if (IncludeWires && styleName.StartsWith(WIRE_PREFIX, StringComparison.OrdinalIgnoreCase))
                return styleName;

            return null;
        }

        private string Scope()
        {
            if (IncludePipes && IncludeWires) return "ống nước hoặc dây điện";
            return IncludePipes ? "ống nước" : "dây điện";
        }
    }

    /// <summary>Ghi chú tên (ký hiệu hệ thống) lên các đường ống nước.</summary>
    [Transaction(TransactionMode.Manual)]
    public class AddPipeLabelsCommand : LabelCommandBase
    {
        protected override string Title { get { return "Ghi chú tên ống nước"; } }
        protected override bool IncludePipes { get { return true; } }
        protected override bool IncludeWires { get { return false; } }
    }

    /// <summary>Ghi chú tên (cỡ dây) lên các đường dây điện.</summary>
    [Transaction(TransactionMode.Manual)]
    public class AddWireLabelsCommand : LabelCommandBase
    {
        protected override string Title { get { return "Ghi chú tên dây điện"; } }
        protected override bool IncludePipes { get { return false; } }
        protected override bool IncludeWires { get { return true; } }
    }
}

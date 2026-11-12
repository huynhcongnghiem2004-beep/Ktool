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
    [Transaction(TransactionMode.Manual)]
    public sealed partial class StairDeploymentCommand : IExternalCommand
    {
        internal const string BuildTag = "DIM3-R2-20261030";
        private const string Title = "Triển khai cầu thang bộ | " + BuildTag;
        private const double Tol = .001;
        private sealed class StairFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is Stairs || e is StairsRun;
            public bool AllowReference(Reference r, XYZ p) => false;
        }
        private sealed class FaceRef { internal Reference Reference; internal double Position; internal double RunPosition, MidZ, NormalSign; internal XYZ Center; }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            var doc = ui.Document;
            try
            {
                Element selected = ui.Selection.GetElementIds().Select(doc.GetElement).FirstOrDefault(e => e is Stairs || e is StairsRun);
                if (selected == null) selected = doc.GetElement(ui.Selection.PickObject(ObjectType.Element, new StairFilter(), "Chọn cầu thang bộ hoặc một vế thang trong model hiện tại."));
                var stair = selected is StairsRun selectedRun ? selectedRun.GetStairs() : selected as Stairs;
                if (stair == null) throw new InvalidOperationException("Chọn Stairs dạng component trong model hiện tại.");
                var bbox = stair.get_BoundingBox(null);
                if (bbox == null) throw new InvalidOperationException("Cầu thang chưa có hình học.");
                var points = Corners(bbox); double bottom = points.Min(p => p.Z), top = points.Max(p => p.Z);
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Where(l => l.Elevation >= bottom - 1 && l.Elevation <= top + 1).OrderBy(l => l.Elevation).ToList();
                var dimensionTypes = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().Where(t => t.StyleType == DimensionStyleType.Linear).OrderBy(t => t.Name).ToList();
                var settings = new StairDeploymentWindow(doc, levels, dimensionTypes);
                new System.Windows.Interop.WindowInteropHelper(settings).Owner = data.Application.MainWindowHandle;
                if (settings.ShowDialog() != true) return Result.Cancelled;
                var notes = new List<string>(); var created = new List<View>(); var summary = new List<string>();
                var runs = stair.GetStairsRuns().Select(id => (StairsRun)doc.GetElement(id)).ToList();
                var paths = runs.SelectMany(r => r.GetStairsPath()).OfType<Line>().OrderByDescending(l => l.Length).ToList();
                if (paths.Count == 0) throw new InvalidOperationException("Bản này cần có ít nhất một đoạn chạy thẳng để xác định hướng cắt dọc/ngang.");
                var direction = paths[0].Direction; var along = new XYZ(direction.X, direction.Y, 0).Normalize(); var across = XYZ.BasisZ.CrossProduct(along).Normalize();
                var components = new List<Element>(); components.AddRange(runs);
                components.AddRange(stair.GetStairsLandings().Select(doc.GetElement));
                if (components.Count == 0) components.Add(stair);
                ViewSheet sheet = null;
                using (var tx = new Transaction(doc, Title))
                {
                    tx.Start();
                    try
                    {
                        var planType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == ViewFamily.FloorPlan);
                        var sectionType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == ViewFamily.Section);
                        foreach (var levelId in settings.Plans ? settings.Levels : new List<ElementId>())
                        {
                            var level = (Level)doc.GetElement(levelId);
                            var existing = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel != null && v.GenLevel.Id == levelId && v.CanViewBeDuplicated(ViewDuplicateOption.Duplicate));
                            ViewPlan plan;
                            if (existing != null) plan = (ViewPlan)doc.GetElement(existing.Duplicate(ViewDuplicateOption.Duplicate));
                            else { if (planType == null) throw new InvalidOperationException("Dự án thiếu Floor Plan Type."); plan = ViewPlan.Create(doc, planType.Id, levelId); }
                            Configure(doc, plan, settings, settings.Prefix + " - MB " + level.Name, settings.PlanTemplate, false);
                            CropPlan(doc, plan, points, along, across, level.Elevation, settings.CropMargin);
                            ApplyTemplate(doc, plan, settings.PlanTemplate, settings.Dimensions);
                            created.Add(plan);
                        }
                        if ((settings.Longitudinal || settings.Transverse) && sectionType == null) throw new InvalidOperationException("Dự án thiếu Section Type.");
                        if (settings.Longitudinal) created.Add(CreateSection(doc, sectionType.Id, points, across, paths[0].Evaluate(.5, true), settings, settings.Prefix + " - CẮT DỌC"));
                        if (settings.Transverse) created.Add(CreateSection(doc, sectionType.Id, points, along, paths[0].Evaluate(.5, true), settings, settings.Prefix + " - CẮT NGANG"));
                        foreach (var view in created)
                        {
                            doc.Regenerate();
                            int count = settings.Dimensions && view is ViewSection
                                ? DimensionStairSectionThreeLayers(doc, view, stair, components, settings, notes) : 0;
                            if (view is ViewPlan plan)
                            {
                                if (settings.PlanDimensions) count += PlanDimensionAnnotations(doc, plan, stair, components, along, across, settings, notes);
                                if(settings.Dimensions)count+=DimensionStair(doc,view,components,settings,along,across,notes);
                                AnnotateStairPlan(doc, plan, stair, runs, settings, notes);
                            }
                            notes.Add(DescribeDimensionLayers(doc, view, along, across, runs.Count, settings));
                            summary.Add(view.Name + ": " + count + " đường Dim" + (settings.Dimensions && count == 0 ? " — thiếu reference, xem chi tiết" : ""));
                            if (settings.Fit) notes.Add(FitToCropCommand.FitInTransaction(doc, view, FitCropSettings.Load()));
                            if(settings.Fit && settings.Dimensions)SeparateGridHeadsFromDimensions(doc,view,settings,notes);
                            if (settings.OverrideCrop)
                            {
                                try { settings.CropStyle.Apply(doc, view); }
                                catch (Exception ex) { notes.Add("Override Crop " + view.Name + ": " + ex.Message); }
                            }
                        }
                        if (settings.Sheet) sheet = MakeSheet(doc, created, settings, notes);
                        if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu được bộ triển khai.");
                    }
                    catch { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); throw; }
                }
                try { ui.ActiveView = (View)sheet ?? created.First(); } catch (Exception ex) { notes.Add(ex.Message); }
                var report = new TaskDialog(Title) { MainInstruction = "Đã tạo " + created.Count + " view cầu thang",
                    MainContent = string.Join("\n", summary) + (sheet == null ? "" : "\nSheet: " + sheet.SheetNumber),
                    ExpandedContent = "Build: " + BuildTag + "\nDLL: " + typeof(StairDeploymentCommand).Assembly.Location
                        + "\nMVID: " + typeof(StairDeploymentCommand).Assembly.ManifestModule.ModuleVersionId
                        + "\nDim bật: " + settings.Dimensions + "; Dim mặt bằng 3 lớp: " + settings.PlanDimensions
                        + "\nStairs Id: " + stair.Id + "\nSố cổ bậc: " + stair.ActualRisersNumber
                        + "\nCao cổ bậc: " + (stair.ActualRiserHeight * 304.8).ToString("0.#") + " mm"
                        + "\nMặt bậc: " + (stair.ActualTreadDepth * 304.8).ToString("0.#") + " mm\n" + string.Join("\n", notes.Distinct())
                        + "\nSheet chưa có khung tên. Chưa tự tạo cao độ, chú thích cấu tạo hoặc chi tiết lan can.", CommonButtons = TaskDialogCommonButtons.Close };
                report.Show(); return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
        private static List<XYZ> Corners(BoundingBoxXYZ box)
        {
            var result = new List<XYZ>(); for (int i = 0; i < 8; i++) result.Add(box.Transform.OfPoint(new XYZ((i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z))); return result;
        }
        private static void Configure(Document doc, View view, StairDeploymentWindow settings, string name, ElementId template, bool apply)
        {
            view.ViewTemplateId = ElementId.InvalidElementId; view.Scale = settings.Scale;
            var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            string unique = name; int suffix = 2; while (names.Contains(unique)) unique = name + " (" + suffix++ + ")"; view.Name = unique;
            view.DetailLevel = ViewDetailLevel.Fine; view.DisplayStyle = DisplayStyle.HLR;
            var scope = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP); if (scope != null && !scope.IsReadOnly) scope.Set(ElementId.InvalidElementId);
            var annotation = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE); if (annotation != null && !annotation.IsReadOnly) annotation.Set(0);
            if (apply) ApplyTemplate(doc, view, template, settings.Dimensions);
        }
        private static void ApplyTemplate(Document doc, View view, ElementId template, bool dimensions)
        {
            if (template != ElementId.InvalidElementId)
            {
                if (!view.IsValidViewTemplate(template)) throw new InvalidOperationException("Template không phù hợp với " + view.Name);
                view.ViewTemplateId = template; doc.Regenerate();
            }
            if (!view.CropBoxActive) throw new InvalidOperationException("Template cần bật Crop View cho " + view.Name);
            if (!dimensions) return;
            if (view.GetCategoryHidden(new ElementId(BuiltInCategory.OST_Dimensions))) throw new InvalidOperationException("Template đang ẩn Dimensions: " + view.Name);
            var crop = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (crop != null && !crop.IsReadOnly) crop.Set(0);
            else if (crop != null && crop.AsInteger() != 0) throw new InvalidOperationException("Template đang khóa Annotation Crop bật: " + view.Name);
        }
        private static void CropPlan(Document doc, ViewPlan view, List<XYZ> points, XYZ x, XYZ y, double z, double margin)
        {
            view.CropBoxActive = true; doc.Regenerate();
            double x0 = points.Min(p => p.DotProduct(x)) - margin, x1 = points.Max(p => p.DotProduct(x)) + margin;
            double y0 = points.Min(p => p.DotProduct(y)) - margin, y1 = points.Max(p => p.DotProduct(y)) + margin;
            var corners = new[] { x * x0 + y * y0, x * x1 + y * y0, x * x1 + y * y1, x * x0 + y * y1 }.Select(p => new XYZ(p.X, p.Y, z)).ToList();
            using (var manager = view.GetCropRegionShapeManager())
            { if (manager.Split) manager.RemoveSplit(); var loop = new CurveLoop(); for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(corners[i], corners[(i + 1) % 4])); manager.SetCropShape(loop); }
        }
        private static ViewSection CreateSection(Document doc, ElementId type, List<XYZ> points, XYZ normal, XYZ cutPoint, StairDeploymentWindow settings, string name)
        {
            var x = XYZ.BasisZ.CrossProduct(normal).Normalize(); double margin = Math.Max(settings.CropMargin, 50 / 304.8);
            double lo = points.Min(p => p.DotProduct(x)), hi = points.Max(p => p.DotProduct(x));
            double near = points.Min(p => p.DotProduct(normal)), far = points.Max(p => p.DotProduct(normal));
            double bottom = points.Min(p => p.Z) - margin, top = points.Max(p => p.Z) + margin;
            // The cut plane passes through the stair, rather than outside the stair's envelope.
            double cut = cutPoint.DotProduct(normal);
            var transform = Transform.Identity; transform.BasisX = x; transform.BasisY = XYZ.BasisZ; transform.BasisZ = normal;
            transform.Origin = x * ((lo + hi) / 2) + normal * cut + XYZ.BasisZ * bottom;
            var box = new BoundingBoxXYZ { Transform = transform, Min = new XYZ(-(hi - lo) / 2 - margin, 0, 0), Max = new XYZ((hi - lo) / 2 + margin, top - bottom, far - cut + margin) };
            var view = ViewSection.CreateSection(doc, type, box); Configure(doc, view, settings, name, settings.SectionTemplate, true); return view;
        }
        private static void Faces(GeometryElement geometry, Transform transform, XYZ[] axes, List<FaceRef>[] refs)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is GeometryInstance instance) Faces(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), axes, refs);
                else if (obj is Solid solid) foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace planar) || planar.Reference == null) continue;
                    var normal = transform.OfVector(planar.FaceNormal).Normalize(); var point = transform.OfPoint(planar.Origin);
                    for (int axis = 0; axis < 2; axis++) if (Math.Abs(normal.DotProduct(axes[axis])) > .999999) refs[axis].Add(new FaceRef { Reference = planar.Reference, Position = point.DotProduct(axes[axis]) });
                }
            }
        }
        private static int DimensionStair(Document doc, View view, List<Element> components, StairDeploymentWindow settings, XYZ along, XYZ across, List<string> notes)
        {
            var axes = view is ViewPlan ? new[] { along, across } : new[] { view.RightDirection.Normalize(), view.UpDirection.Normalize() };
            var refs = new[] { new List<FaceRef>(), new List<FaceRef>() };
            if (settings.OnlyRisers)
            {
                int result = 0, runIndex = 0;
                foreach (var run in components.OfType<StairsRun>())
                {
                    foreach (var path in run.GetStairsPath().OfType<Line>())
                    {
                    var runAxis = new XYZ(path.Direction.X, path.Direction.Y, 0).Normalize();
                    var runRefs = new[] { new List<FaceRef>(), new List<FaceRef>() };
                    using (var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine })
                    { var geometry = run.get_Geometry(options); if (geometry != null) RiserFaces(geometry, Transform.Identity, axes, runAxis, run.GetStairs().ActualRiserHeight, DimensionRunWidth(run), runRefs); }
                    FilterExposedRisers(run, path, runRefs);
                    int runCount = DimensionReferences(doc, view, axes, runRefs, settings, notes, runIndex++, path.Evaluate(.5,true), DimensionRunWidth(run)*.5);
                    result += runCount;
                    if (runCount == 0) notes.Add(view.Name + ": vế " + run.Id + " không có hai mặt Riser thật phù hợp trong crop/phương đo.");
                    }
                }
                return result;
            }
            foreach (var component in components)
            using (var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine })
            { var geometry = component.get_Geometry(options); if (geometry != null) Faces(geometry, Transform.Identity, axes, refs); }
            return DimensionReferences(doc, view, axes, refs, settings, notes);
        }
        private static void RiserFaces(GeometryElement geometry, Transform transform, XYZ[] axes, XYZ runAxis, double height, double width, List<FaceRef>[] refs)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is GeometryInstance instance) { RiserFaces(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), axes, runAxis, height, width, refs); continue; }
                if (!(obj is Solid solid)) continue;
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace planar) || planar.Reference == null) continue;
                    var normal = transform.OfVector(planar.FaceNormal).Normalize();
                    if (Math.Abs(normal.Z) > .000001 || Math.Abs(normal.DotProduct(runAxis)) < .999999) continue;
                    var points = new List<XYZ>(); foreach (EdgeArray loop in planar.EdgeLoops) foreach (Edge edge in loop) points.AddRange(edge.Tessellate().Select(transform.OfPoint));
                    if (points.Count == 0) continue;
                    double z0 = points.Min(p => p.Z), z1 = points.Max(p => p.Z), faceHeight = z1 - z0;
                    if (faceHeight < height * .5 || faceHeight > height * 1.5 + Tol) continue;
                    var lateral = XYZ.BasisZ.CrossProduct(runAxis);
                    if (width > Tol && points.Max(p => p.DotProduct(lateral)) - points.Min(p => p.DotProduct(lateral)) < width * .5) continue;
                    var origin = transform.OfPoint(planar.Origin);
                    for (int axis = 0; axis < 2; axis++) if (Math.Abs(normal.DotProduct(axes[axis])) > .999999)
                        refs[axis].Add(new FaceRef { Reference = planar.Reference, Position = origin.DotProduct(axes[axis]), RunPosition = origin.DotProduct(runAxis), MidZ = (z0 + z1) / 2, NormalSign = normal.DotProduct(runAxis), Center = new XYZ(points.Average(p=>p.X),points.Average(p=>p.Y),points.Average(p=>p.Z)) });
                }
            }
        }
        private static int DimensionReferences(Document doc, View view, XYZ[] axes, List<FaceRef>[] refs, StairDeploymentWindow settings, List<string> notes, int runIndex = 0, XYZ placementCenter = null, double halfWidth = 0)
        {
            var cropPoints = Corners(view.CropBox); int count = 0;
            for (int axis = 0; axis < 2; axis++)
            {
                double min = cropPoints.Min(p => p.DotProduct(axes[axis])), max = cropPoints.Max(p => p.DotProduct(axes[axis]));
                var ordered = new List<FaceRef>(); foreach (var r in refs[axis].Where(r => r.Position >= min - Tol && r.Position <= max + Tol).OrderBy(r => r.Position))
                    if (ordered.Count == 0 || r.Position - ordered.Last().Position > Tol) ordered.Add(r);
                if (ordered.Count < 2) { if (!settings.OnlyRisers) notes.Add(view.Name + ": thiếu hai reference theo phương " + (axis == 0 ? "ngang" : "dọc/đứng") + "."); continue; }
                for (int layer = 0; layer < (!settings.OnlyRisers && ordered.Count > 2 ? 2 : 1); layer++)
                using (var attempt = new SubTransaction(doc))
                {
                    attempt.Start();
                    try
                    {
                        var selected = layer == 0 ? ordered : new List<FaceRef> { ordered.First(), ordered.Last() };
                        var perpendicular = axes[1 - axis]; double baseline = cropPoints.Min(p => p.DotProduct(perpendicular)) - settings.Gap / 304.8 * view.Scale * (layer + 1 + runIndex);
                        if (view is ViewPlan && placementCenter != null)
                        {
                            double center=placementCenter.DotProduct(perpendicular);
                            int sign=center<(cropPoints.Min(p=>p.DotProduct(perpendicular))+cropPoints.Max(p=>p.DotProduct(perpendicular)))*.5?-1:1;
                            baseline=center+sign*(halfWidth+settings.Gap/304.8*view.Scale);
                            double outside=(sign<0?cropPoints.Min(p=>p.DotProduct(perpendicular)):cropPoints.Max(p=>p.DotProduct(perpendicular)))+sign*settings.Gap/304.8*view.Scale;
                            baseline=sign<0?Math.Min(baseline,outside):Math.Max(baseline,outside);
                        }
                        var origin = perpendicular * baseline + view.ViewDirection * view.Origin.DotProduct(view.ViewDirection);
                        var array = new ReferenceArray(); foreach (var reference in selected) array.Append(reference.Reference);
                        var dim = doc.Create.NewDimension(view, Line.CreateBound(origin + axes[axis] * selected.First().Position, origin + axes[axis] * selected.Last().Position), array, settings.DimType); doc.Regenerate();
                        var values = dim.NumberOfSegments > 1 ? dim.Segments.Cast<DimensionSegment>().Select(s => s.Value).ToList() : new List<double?> { dim.Value };
                        if (values.Count != selected.Count - 1) throw new InvalidOperationException("Số đoạn không khớp reference.");
                        for (int i = 0; i < values.Count; i++) if (!values[i].HasValue || Math.Abs(values[i].Value - (selected[i + 1].Position - selected[i].Position)) > Tol) throw new InvalidOperationException("Số đo không khớp hình học.");
                        if (attempt.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận Dim."); count++;
                    }
                    catch (Exception ex) { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); notes.Add(view.Name + ": " + ex.Message); }
                }
            }
            return count;
        }
        private static ViewSheet MakeSheet(Document doc, List<View> views, StairDeploymentWindow settings, List<string> notes)
        {
            var sheet = ViewSheet.Create(doc, ElementId.InvalidElementId); sheet.Name = settings.Prefix + " - TRIỂN KHAI CẦU THANG";
            var numbers = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber));
            string number = settings.Prefix; int suffix = 2; while (numbers.Contains(number)) number = settings.Prefix + "-" + suffix++; sheet.SheetNumber = number;
            double y = 0, gap = 40 / 304.8;
            foreach (var row in new[] { views.Where(v => v is ViewPlan).ToList(), views.Where(v => v is ViewSection).ToList() })
            {
                var ports = row.Select(v => Viewport.Create(doc, sheet.Id, v.Id, XYZ.Zero)).ToList(); doc.Regenerate(); double x = 0, rowHeight = 0;
                foreach (var port in ports)
                {
                    using (var outline = port.GetBoxOutline())
                    {
                        double width = outline.MaximumPoint.X - outline.MinimumPoint.X, height = outline.MaximumPoint.Y - outline.MinimumPoint.Y;
                        port.SetBoxCenter(new XYZ(x + width / 2, y - height / 2, 0)); x += width + gap; rowHeight = Math.Max(rowHeight, height);
                    }
                    if (settings.Titles) using (var item = new SubTransaction(doc))
                    {
                        item.Start(); try { AlignViewportTitleCommand.CenterTitle(doc, port); item.Commit(); }
                        catch (Exception ex) { if (item.GetStatus() == TransactionStatus.Started) item.RollBack(); notes.Add("Title " + port.ViewId + ": " + ex.Message); }
                    }
                }
                if (ports.Count > 0) y -= rowHeight + gap + 20 / 304.8;
            }
            return sheet;
        }
        internal static System.Windows.Media.ImageSource CreateIcon()
        {
            var drawing = new System.Windows.Media.DrawingGroup(); using (var dc = drawing.Open())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new System.Windows.Rect(0, 0, 32, 32));
                var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.RoyalBlue, 2.5);
                for (int i = 0; i < 4; i++) { dc.DrawLine(pen, new System.Windows.Point(3 + i * 7, 28 - i * 7), new System.Windows.Point(10 + i * 7, 28 - i * 7)); dc.DrawLine(pen, new System.Windows.Point(10 + i * 7, 28 - i * 7), new System.Windows.Point(10 + i * 7, 21 - i * 7)); }
            }
            var icon = new System.Windows.Media.DrawingImage(drawing); icon.Freeze(); return icon;
        }
    }
}


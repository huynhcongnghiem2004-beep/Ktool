using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    internal static partial class CalloutThreeDim
    {
        private static readonly Guid WcViewSchemaId = new Guid("AF38281A-C72A-437E-A915-623143F49C8A");
        private static Schema WcSchema()
        {
            var schema = Schema.Lookup(WcViewSchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(WcViewSchemaId);
            builder.SetSchemaName("KToolWcRoomView"); builder.AddSimpleField("Room", typeof(string));
            return builder.Finish();
        }
        private static void MarkWcView(View view, Room room)
        {
            var schema = WcSchema(); var entity = new Entity(schema);
            entity.Set<string>(schema.GetField("Room"), room.UniqueId); view.SetEntity(entity);
        }
        internal static Result RunWc(ExternalCommandData data, bool create, ref string message,
            Room roomOverride = null, CalloutDimWindow settingsOverride = null, ViewPlan parentOverride = null,
            List<string> batchSummary = null)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null) return Result.Cancelled;
            var doc = ui.Document; var parent = (View)(parentOverride ?? ui.ActiveView);
            if (parent.IsTemplate || (create && (!(parent is ViewPlan) || parent.ViewType != ViewType.FloorPlan)))
            { TaskDialog.Show(Title, "Mở mặt bằng tầng có Room WC để triển khai."); return Result.Cancelled; }
            var parentType = doc.GetElement(parent.GetTypeId()) as ViewFamilyType;
            var types = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(t => parentType != null && t.ViewFamily == parentType.ViewFamily).OrderBy(t => t.Name).ToList();
            var dims = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear).OrderBy(t => t.Name).ToList();
            if (dims.Count == 0 || (create && types.Count == 0))
            { TaskDialog.Show(Title, "Thiếu kiểu mặt bằng hoặc kiểu Dim tuyến tính."); return Result.Cancelled; }
            var window = settingsOverride;
            if (window == null)
            {
                window = new CalloutDimWindow(types, dims, parent, create);
                new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                if (window.ShowDialog() != true) return Result.Cancelled;
            }
            Room room; XYZ selectedPoint = null;
            if (create)
            {
                if (roomOverride != null)
                {
                    room = roomOverride;
                    selectedPoint = FindRoomInteriorPoint(room);
                    if (selectedPoint == null)
                    {
                        if (batchSummary != null) { batchSummary.Add("Bỏ qua " + room.Number + " - " + room.Name + ": không tìm được điểm bên trong Room."); return Result.Cancelled; }
                        TaskDialog.Show(Title, "Không tìm được điểm bên trong Room " + room.Number + "."); return Result.Cancelled;
                    }
                }
                else
                {
                    try
                    {
                        var p = ui.Selection.PickPoint("Bấm một điểm bên trong Room WC cần triển khai (Esc để hủy).");
                        selectedPoint = p;
                        var level = parent.GenLevel;
                        var phaseId = parent.get_Parameter(BuiltInParameter.VIEW_PHASE).AsElementId();
                        room = doc.GetRoomAtPoint(new XYZ(p.X, p.Y, level.Elevation + 1), doc.GetElement(phaseId) as Phase);
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                }
            }
            else
            {
                var entity = parent.GetEntity(WcSchema());
                room = entity.IsValid() ? doc.GetElement(entity.Get<string>(WcSchema().GetField("Room"))) as Room : null;
            }
            if (room == null || room.Area <= 0)
            { TaskDialog.Show(Title, create ? "Không tìm thấy Room kín tại điểm chọn. Hãy đặt Room WC và kiểm tra phase của view." : "View chưa gắn Room WC. Hãy tạo bộ triển khai WC trước."); return Result.Cancelled; }
            var elevationType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Elevation);
            if (create && window.CreateElevations && elevationType == null)
            { TaskDialog.Show(Title, "Dự án thiếu kiểu Elevation để tạo mặt đứng nội thất."); return Result.Cancelled; }
            var views = new List<View>(); var notes = new List<string>(); var summaries = new List<string>(); int dimensionCount = 0;
            View equipment = parent; ViewSheet sheet = null;
            using (var tx = new Transaction(doc, Title))
            {
                try
                {
                    tx.Start();
                    if (create)
                    {
                        var bounds = RoomPoints(room); var right = parent.RightDirection; var up = parent.UpDirection;
                        double x0 = bounds.Min(p => p.DotProduct(right)), x1 = bounds.Max(p => p.DotProduct(right));
                        double y0 = bounds.Min(p => p.DotProduct(up)), y1 = bounds.Max(p => p.DotProduct(up));
                        // Crop margin includes wall thickness; dimension datums still come from Room finish boundaries.
                        double margin = 300 / 304.8; double z = parent.GenLevel.Elevation;
                        XYZ a = right * (x0 - margin) + up * (y0 - margin) + XYZ.BasisZ * z;
                        XYZ b = right * (x1 + margin) + up * (y1 + margin) + XYZ.BasisZ * z;
                        string prefix = roomOverride == null ? window.ViewName : window.ViewName + " - " + room.Number;
                        equipment = ViewSection.CreateCallout(doc, parent.Id, window.CalloutType.Id, a, b);
                        ConfigureWcView(equipment, room, window.ScaleValue, UniqueName(doc, prefix + " - MB ĐỊNH VỊ THIẾT BỊ"), parent);
                        // Duplicate before annotations and templates; only one callout is created.
                        if (window.CreateFloorPlan)
                        {
                            if (!equipment.CanViewBeDuplicated(ViewDuplicateOption.Duplicate)) throw new InvalidOperationException("Loại view WC hiện tại không hỗ trợ Duplicate mặt bằng.");
                            var floor = doc.GetElement(equipment.Duplicate(ViewDuplicateOption.Duplicate)) as ViewPlan;
                            ConfigureWcView(floor, room, window.ScaleValue, UniqueName(doc, prefix + " - MB LÁT SÀN"), parent);
                            ApplyWcTemplate(doc, floor, window.TemplateFor("Lát sàn"));
                            views.Add(floor);
                            var floorReport = BuildWcDimensions(doc, floor, room, window.DimType, window.GapMm, window, false);
                            summaries.Add(WcDimSummary(floor, floorReport)); dimensionCount += floorReport.Count; notes.AddRange(floorReport.Notes.Select(n => "Lát sàn: " + n));
                        }
                        ApplyWcTemplate(doc, equipment, window.TemplateFor("Thiết bị"));
                        views.Add(equipment); doc.Regenerate();
                        var planReport = BuildWcDimensions(doc, equipment, room, window.DimType, window.GapMm, window, true);
                        summaries.Add(WcDimSummary(equipment, planReport)); dimensionCount += planReport.Count; notes.AddRange(planReport.Notes.Select(n => "Thiết bị: " + n));
                        if (window.CreateCeilingPlan)
                        {
                            var existingCeiling = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                                .FirstOrDefault(v => !v.IsTemplate && v.ViewType == ViewType.CeilingPlan && v.GenLevel != null
                                    && v.GenLevel.Id == parent.GenLevel.Id && v.CanViewBeDuplicated(ViewDuplicateOption.Duplicate));
                            ViewPlan ceiling;
                            if (existingCeiling != null) ceiling = (ViewPlan)doc.GetElement(existingCeiling.Duplicate(ViewDuplicateOption.Duplicate));
                            else
                            {
                                var ceilingType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                                    .FirstOrDefault(t => t.ViewFamily == ViewFamily.CeilingPlan);
                                if (ceilingType == null) throw new InvalidOperationException("Dự án thiếu loại Reflected Ceiling Plan.");
                                ceiling = ViewPlan.Create(doc, ceilingType.Id, parent.GenLevel.Id);
                            }
                            ConfigureWcView(ceiling, room, window.ScaleValue, UniqueName(doc, prefix + " - MB TRẦN"), parent);
                            ceiling.CropBoxActive = true;
                            doc.Regenerate();
                            using (var manager = ceiling.GetCropRegionShapeManager())
                            {
                                if (manager.Split) manager.RemoveSplit();
                                var loop = new CurveLoop();
                                var corners = new[] { a, right * b.DotProduct(right) + up * a.DotProduct(up) + XYZ.BasisZ * z,
                                    b, right * a.DotProduct(right) + up * b.DotProduct(up) + XYZ.BasisZ * z };
                                for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(corners[i], corners[(i + 1) % 4]));
                                manager.SetCropShape(loop);
                            }
                            ApplyWcTemplate(doc, ceiling, window.TemplateFor("Trần")); views.Add(ceiling);
                            var ceilingReport = BuildWcDimensions(doc, ceiling, room, window.DimType, window.GapMm, window, false);
                            summaries.Add(WcDimSummary(ceiling, ceilingReport)); dimensionCount += ceilingReport.Count; notes.AddRange(ceilingReport.Notes.Select(n => "Trần: " + n));
                        }
                        if (window.CreateElevations)
                        {
                            double height = window.ElevationHeightMm / 304.8;
                            var directions = new[] { -right, up, right, -up };
                            var letters = new[] { "A", "B", "C", "D" };
                            XYZ origin = new XYZ(selectedPoint.X, selectedPoint.Y, z + 100 / 304.8);
                            var marker = ElevationMarker.CreateElevationMarker(doc, elevationType.Id, origin, window.ScaleValue);
                            if (marker.MaximumViewCount < 4) throw new InvalidOperationException("Kiểu Elevation được chọn không hỗ trợ bốn hướng nhìn.");
                            var elevations = new List<ViewSection>();
                            for (int index = 0; index < 4; index++) elevations.Add(marker.CreateElevation(doc, equipment.Id, index));
                            doc.Regenerate();
                            var initial = elevations[0].ViewDirection.Normalize(); var desired = directions[0].Normalize();
                            double angle = Math.Atan2(XYZ.BasisZ.DotProduct(initial.CrossProduct(desired)), initial.DotProduct(desired));
                            ElementTransformUtils.RotateElement(doc, marker.Id, Line.CreateBound(origin, origin + XYZ.BasisZ), angle);
                            doc.Regenerate();
                            for (int side = 0; side < 4; side++)
                            {
                                var elevation = elevations.OrderByDescending(v => v.ViewDirection.DotProduct(directions[side])).First();
                                if (elevation.ViewDirection.DotProduct(directions[side]) < .999999) throw new InvalidOperationException("Không căn được hướng Elevation theo mặt bằng nguồn.");
                                if (!window.IsElevationEnabled(side)) { doc.Delete(elevation.Id); elevations.Remove(elevation); continue; }
                                ConfigureWcView(elevation, room, window.ScaleValue, UniqueName(doc, prefix + " - MẶT ĐỨNG " + letters[side]), parent);
                                ApplyWcTemplate(doc, elevation, window.TemplateFor(letters[side]));
                                CropWcElevation(doc, elevation, (ViewPlan)equipment, z, height, window.BottomOffsetMm / 304.8);
                                views.Add(elevation); doc.Regenerate();
                                var report = BuildWcDimensions(doc, elevation, room, window.DimType, window.GapMm, window, true);
                                summaries.Add(WcDimSummary(elevation, report)); dimensionCount += report.Count; notes.AddRange(report.Notes.Select(n => letters[side] + ": " + n));
                                elevations.Remove(elevation);
                            }
                        }
                    }
                    else
                    {
                        var scaleParameter = parent.get_Parameter(BuiltInParameter.VIEW_SCALE);
                        if (scaleParameter == null || !scaleParameter.IsReadOnly) parent.Scale = window.ScaleValue;
                        if (window.AdjustBottom && parent.ViewType == ViewType.Elevation)
                            AdjustWcBottom(doc, parent, (doc.GetElement(room.LevelId) as Level).Elevation + window.BottomOffsetMm / 304.8);
                        var report = BuildWcDimensions(doc, parent, room, window.DimType, window.GapMm, window, true);
                        summaries.Add(WcDimSummary(parent, report)); dimensionCount = report.Count; notes.AddRange(report.Notes); views.Add(parent);
                    }
                    if (dimensionCount == 0) throw new InvalidOperationException("Không lấy được reference để tạo Dim. Đã hoàn tác; kiểm tra Room, tường và Family thiết bị.");
                    if (window.FitCrop)
                        foreach (var view in views) notes.Add(FitToCropCommand.FitInTransaction(doc, view, window.DatumFitSettings));
                    if (create && window.CreateSheet) sheet = ArrangeWcSheet(doc, views, room, window.ViewName, window.CenterTitles, notes);
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận bộ triển khai.");
                }
                catch (Exception ex)
                { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); message = ex.Message; return Result.Failed; }
            }
            if (batchSummary != null)
            {
                batchSummary.Add("Đã tạo " + room.Number + " - " + room.Name + ": " + views.Count + " view, " + dimensionCount + " Dim" + (sheet == null ? "" : ", sheet " + sheet.SheetNumber));
                return Result.Succeeded;
            }
            try { ui.ActiveView = (View)sheet ?? equipment; } catch (Exception ex) { notes.Add(ex.Message); }
            var resultDialog = new TaskDialog(Title)
            {
                MainInstruction = "Room: " + room.Number + " - " + room.Name,
                MainContent = "Đã xử lý " + views.Count + " view, " + dimensionCount + " đường Dim.\n"
                    + string.Join("\n", summaries)
                    + (sheet == null ? "" : "\nSheet: " + sheet.SheetNumber + " - " + sheet.Name),
                ExpandedContent = string.Join("\n", notes.Distinct())
                    + "\n\nRon gạch và vật liệu theo model. Sheet chưa có khung tên; cần bổ sung ghi chú, độ dốc, cao độ trước phát hành.",
                CommonButtons = TaskDialogCommonButtons.Close
            };
            resultDialog.Show();
            return Result.Succeeded;
        }
        internal static Result RunWcBatch(ExternalCommandData data, ref string message)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null) return Result.Cancelled;
            var doc = ui.Document;
            var activePlan = ui.ActiveView as ViewPlan;
            if (activePlan == null || activePlan.ViewType != ViewType.FloorPlan || activePlan.IsTemplate)
            { TaskDialog.Show(Title, "Mở một mặt bằng tầng để thiết lập cấu hình triển khai WC."); return Result.Cancelled; }
            var rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                .Cast<Room>().Where(r => r.Area > 0 && doc.GetElement(r.LevelId) is Level)
                .OrderBy(r => ((Level)doc.GetElement(r.LevelId)).Elevation).ThenBy(r => r.Number).ToList();
            if (rooms.Count == 0) { TaskDialog.Show(Title, "Không tìm thấy Room đã đặt kín trong dự án."); return Result.Cancelled; }
            var picker = new WcBatchRoomWindow(rooms);
            new System.Windows.Interop.WindowInteropHelper(picker).Owner = data.Application.MainWindowHandle;
            if (picker.ShowDialog() != true || picker.SelectedRooms.Count == 0) return Result.Cancelled;
            var types = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(t => t.ViewFamily == ViewFamily.FloorPlan).OrderBy(t => t.Name).ToList();
            var dims = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear).OrderBy(t => t.Name).ToList();
            if (types.Count == 0 || dims.Count == 0) { TaskDialog.Show(Title, "Thiếu View Type mặt bằng hoặc kiểu Dim tuyến tính."); return Result.Cancelled; }
            var settings = new CalloutDimWindow(types, dims, activePlan, true);
            new System.Windows.Interop.WindowInteropHelper(settings).Owner = data.Application.MainWindowHandle;
            if (settings.ShowDialog() != true) return Result.Cancelled;
            var floorPlans = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel != null).ToList();
            var outcomes = new List<string>(); int succeeded = 0;
            foreach (Room room in picker.SelectedRooms)
            {
                var parent = room.LevelId == activePlan.GenLevel.Id ? activePlan
                    : floorPlans.FirstOrDefault(v => v.GenLevel.Id == room.LevelId);
                if (parent == null)
                { outcomes.Add("Bỏ qua " + room.Number + " - " + room.Name + ": chưa có mặt bằng kiến trúc ở tầng " + ((Level)doc.GetElement(room.LevelId)).Name + "."); continue; }
                string localMessage = null;
                Result result = RunWc(data, true, ref localMessage, room, settings, parent, outcomes);
                if (result == Result.Succeeded) succeeded++;
                else if (!string.IsNullOrWhiteSpace(localMessage)) outcomes.Add("Lỗi " + room.Number + ": " + localMessage);
            }
            var dialog = new TaskDialog(Title)
            {
                MainInstruction = "Hoàn tất triển khai WC hàng loạt",
                MainContent = "Thành công: " + succeeded + " / " + picker.SelectedRooms.Count + " Room. Mỗi Room dùng cấu hình đã chọn một lần.",
                ExpandedContent = string.Join("\n", outcomes),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.Show();
            return Result.Succeeded;
        }

        private static XYZ FindRoomInteriorPoint(Room room)
        {
            var level = room.Document.GetElement(room.LevelId) as Level;
            if (level == null) return null;
            var location = room.Location as LocationPoint;
            if (location != null)
            {
                var p = new XYZ(location.Point.X, location.Point.Y, level.Elevation + 1);
                if (room.IsPointInRoom(p)) return p;
            }
            var box = room.get_BoundingBox(null);
            if (box == null) return null;
            for (int rings = 0; rings < 7; rings++)
            {
                for (int ix = 1; ix < 8; ix++)
                for (int iy = 1; iy < 8; iy++)
                {
                    var p = new XYZ(box.Min.X + (box.Max.X - box.Min.X) * ix / 8.0,
                        box.Min.Y + (box.Max.Y - box.Min.Y) * iy / 8.0, level.Elevation + 1);
                    if (room.IsPointInRoom(p)) return p;
                }
            }
            return null;
        }

        private static string WcDimSummary(View view, DimReport report)
        {
            return (report.Count == 0 ? "THIẾU DIM - " : "") + view.Name + ": lớp 1/2/3 = "
                + string.Join("/", report.Layers.Select(n => n.ToString())) + (report.Notes.Count > 0 ? " (xem chi tiết)" : "");
        }
        private static void ApplyWcTemplate(Document doc, View view, ElementId template)
        {
            if (template == ElementId.InvalidElementId) return;
            if (!view.IsValidViewTemplate(template)) throw new InvalidOperationException("View Template không tương thích: " + view.Name);
            view.ViewTemplateId = template; doc.Regenerate();
            if (view.GetCategoryHidden(new ElementId(BuiltInCategory.OST_Dimensions)))
                throw new InvalidOperationException("Template " + doc.GetElement(template).Name + " đang ẩn Dimensions trong " + view.Name + ".");
            var annotationCrop = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (annotationCrop != null && annotationCrop.IsReadOnly && annotationCrop.AsInteger() != 0)
                throw new InvalidOperationException("Template " + doc.GetElement(template).Name + " đang khóa Annotation Crop bật. Tắt mục này trong template để thấy Dim ngoài Crop.");
        }
        private static void CropWcElevation(Document doc, ViewSection view, ViewPlan plan, double bottom, double height, double bottomOffset)
        {
            // Read the final plan crop after its template has been applied. Boundary
            // curves are in model coordinates, including rotated crop regions.
            var bounds = new List<XYZ>();
            if (!plan.CropBoxActive) throw new InvalidOperationException("Mặt bằng WC phải bật Crop View để căn crop các hướng Elevation.");
            using (var manager = plan.GetCropRegionShapeManager())
                foreach (var loop in manager.GetCropShape())
                    foreach (Curve curve in loop) bounds.AddRange(curve.Tessellate());
            if (bounds.Count == 0) throw new InvalidOperationException("Không đọc được biên crop mặt bằng WC.");
            var scopeBox = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (scopeBox != null && scopeBox.AsElementId() != ElementId.InvalidElementId)
            {
                if (scopeBox.IsReadOnly) throw new InvalidOperationException("Template đang khóa Scope Box của " + view.Name + "; bỏ quản lý Scope Box để căn theo crop mặt bằng.");
                scopeBox.Set(ElementId.InvalidElementId);
            }
            doc.Regenerate(); var crop = view.CropBox; var inverse = crop.Transform.Inverse;
            var points = bounds.SelectMany(p => new[] { inverse.OfPoint(new XYZ(p.X, p.Y, bottom + bottomOffset)), inverse.OfPoint(new XYZ(p.X, p.Y, bottom + height)) }).ToList();
            crop.Min = new XYZ(points.Min(p => p.X), points.Min(p => p.Y), crop.Min.Z);
            crop.Max = new XYZ(points.Max(p => p.X), points.Max(p => p.Y), crop.Max.Z);
            view.CropBox = crop; view.CropBoxActive = true;
            double depth = bounds.Max(p => (p - view.Origin).DotProduct(view.ViewDirection));
            if (depth <= doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Crop mặt bằng không nằm phía trước hướng nhìn " + view.Name + ".");
            var farMode = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING);
            if (farMode != null && !farMode.IsReadOnly) farMode.Set(1);
            else if (farMode != null && farMode.AsInteger() == 0)
                throw new InvalidOperationException("Template đang khóa Far Clipping tắt trong " + view.Name + ".");
            var far = view.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
            if (far != null && !far.IsReadOnly) far.Set(depth);
            else if (far == null || Math.Abs(far.AsDouble() - depth) > Tol)
                throw new InvalidOperationException("Template đang khóa Far Clip Offset; bỏ quản lý tham số này để căn " + view.Name + " theo crop mặt bằng.");
            doc.Regenerate();
            var result = view.CropBox;
            if (!view.CropBoxActive || Math.Abs(result.Min.X - crop.Min.X) > Tol || Math.Abs(result.Max.X - crop.Max.X) > Tol
                || Math.Abs(result.Min.Y - crop.Min.Y) > Tol || Math.Abs(result.Max.Y - crop.Max.Y) > Tol)
                throw new InvalidOperationException("Không căn được crop " + view.Name + "; kiểm tra Template và Scope Box.");
        }
        private static void AdjustWcBottom(Document doc, View view, double elevation)
        {
            if (!view.CropBoxActive) throw new InvalidOperationException("Bật Crop View trước khi chỉnh cạnh dưới.");
            doc.Regenerate(); var crop = view.CropBox;
            double y = crop.Transform.Inverse.OfPoint(new XYZ(view.Origin.X, view.Origin.Y, elevation)).Y;
            if (y >= crop.Max.Y - doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Cạnh dưới phải thấp hơn cạnh trên crop hiện tại.");
            crop.Min = new XYZ(crop.Min.X, y, crop.Min.Z); view.CropBox = crop; doc.Regenerate();
            if (Math.Abs(view.CropBox.Min.Y - y) > Tol) throw new InvalidOperationException("Template hoặc Scope Box đang khóa cạnh dưới crop.");
        }
        private static ViewSheet ArrangeWcSheet(Document doc, List<View> views, Room room, string name, bool centerTitles, List<string> notes)
        {
            var sheet = ViewSheet.Create(doc, ElementId.InvalidElementId); sheet.Name = name + " - TRIỂN KHAI WC";
            var numbers = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber));
            string prefix = "WC-" + room.Number, number = prefix; int suffix = 2;
            while (numbers.Contains(number)) number = prefix + "-" + suffix++;
            sheet.SheetNumber = number;
            var rows = new[] { views.Where(v => v is ViewPlan).ToList(), views.Where(v => !(v is ViewPlan)).ToList() };
            double gap = 35 / 304.8, rowTop = 0;
            foreach (var row in rows)
            {
                var ports = new List<Viewport>(); var widths = new List<double>(); var heights = new List<double>();
                foreach (var view in row)
                {
                    if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id)) throw new InvalidOperationException("Không thể đặt view lên sheet: " + view.Name);
                    var port = Viewport.Create(doc, sheet.Id, view.Id, XYZ.Zero); doc.Regenerate();
                    var outline = port.GetBoxOutline(); ports.Add(port);
                    widths.Add(outline.MaximumPoint.X - outline.MinimumPoint.X); heights.Add(outline.MaximumPoint.Y - outline.MinimumPoint.Y);
                }
                if (ports.Count == 0) continue;
                double x = -(widths.Sum() + gap * (ports.Count - 1)) / 2;
                for (int i = 0; i < ports.Count; i++)
                {
                    ports[i].SetBoxCenter(new XYZ(x + widths[i] / 2, rowTop - heights[i] / 2, 0));
                    if (centerTitles)
                    using (var item = new SubTransaction(doc))
                    {
                        item.Start();
                        try
                        {
                            AlignViewportTitleCommand.CenterTitle(doc, ports[i]);
                            if (item.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu căn Title.");
                        }
                        catch (Exception ex)
                        {
                            if (item.GetStatus() == TransactionStatus.Started) item.RollBack();
                            notes.Add("Title " + doc.GetElement(ports[i].ViewId).Name + ": " + ex.Message);
                        }
                    }
                    x += widths[i] + gap;
                }
                rowTop -= heights.Max() + gap + 20 / 304.8;
            }
            return sheet;
        }
        private static List<XYZ> RoomPoints(Room room)
        {
            using (var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish })
            {
                var loops = room.GetBoundarySegments(options);
                if (loops == null || loops.Count == 0) throw new InvalidOperationException("Room không có biên khép kín.");
                return loops.SelectMany(l => l).SelectMany(s => s.GetCurve().Tessellate()).ToList();
            }
        }
        private static void ConfigureWcView(View view, Room room, int scale, string name, View source)
        {
            view.ViewTemplateId = ElementId.InvalidElementId; view.Name = name; view.Scale = scale;
            var scopeBox = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
            if (scopeBox != null && !scopeBox.IsReadOnly) scopeBox.Set(ElementId.InvalidElementId);
            view.DetailLevel = ViewDetailLevel.Fine; view.DisplayStyle = DisplayStyle.HLR;
            var crop = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (crop != null && !crop.IsReadOnly) crop.Set(0);
            view.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Dimensions), false);
            foreach (var id in new[] { BuiltInParameter.VIEW_PHASE, BuiltInParameter.VIEW_PHASE_FILTER })
            {
                var from = source.get_Parameter(id); var to = view.get_Parameter(id);
                if (from != null && to != null && !to.IsReadOnly) to.Set(from.AsElementId());
            }
            MarkWcView(view, room);
        }

        private static DimReport BuildWcDimensions(Document doc, View view, Room room, DimensionType type, double gapMm, CalloutDimWindow settings, bool devices)
        {
            var report = new DimReport(); var schema = GetSchema(); doc.Regenerate();
            var old = new FilteredElementCollector(doc).OfClass(typeof(Dimension))
                .Where(e => e.OwnerViewId == view.Id && e.GetEntity(schema).IsValid()).Select(e => e.Id).ToList();
            if (old.Count > 0) doc.Delete(old);
            var annotationCrop = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if (annotationCrop != null && !annotationCrop.IsReadOnly) annotationCrop.Set(0);
            var axes = new[] { view.RightDirection.Normalize(), view.UpDirection.Normalize() };
            var walls = new[] { new List<MeasureRef>(), new List<MeasureRef>() };
            var overall = new[] { new List<MeasureRef>(), new List<MeasureRef>() };
            var centers = new[] { new List<MeasureRef>(), new List<MeasureRef>() };
            var openings = new[] { new List<MeasureRef>(), new List<MeasureRef>() };
            var boundaryWallIds = new HashSet<ElementId>();
            var roomPoints = RoomPoints(room);
            using (var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish })
            foreach (var segment in room.GetBoundarySegments(options).SelectMany(l => l))
            {
                var wall = doc.GetElement(segment.ElementId) as Wall;
                if (wall == null) { report.Notes.Add("Biên Room không phải tường: không dùng làm mốc Dim."); continue; }
                boundaryWallIds.Add(wall.Id);
                var refs = new[] { new List<MeasureRef>(), new List<MeasureRef>() };
                // Model references must not depend on a view's cut plane, detail level,
                // or category display; RCP and Elevation may hide the required side face.
                using (var geomOptions = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine })
                { var geometry = wall.get_Geometry(geomOptions); if (geometry != null) CollectFaces(geometry, Transform.Identity, axes[0], axes[1], refs); }
                try
                {
                    foreach (var shell in new[] { ShellLayerType.Interior, ShellLayerType.Exterior })
                    foreach (var reference in HostObjectUtils.GetSideFaces(wall, shell))
                    {
                        var face = wall.GetGeometryObjectFromReference(reference) as PlanarFace;
                        if (face == null) continue;
                        for (int axis = 0; axis < 2; axis++)
                            if (Math.Abs(face.FaceNormal.Normalize().DotProduct(axes[axis])) > .999999)
                                refs[axis].Add(new MeasureRef { Reference = reference, Position = face.Origin.DotProduct(axes[axis]) });
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { /* Unsupported host: retain geometric face references. */ }
                XYZ midpoint = segment.GetCurve().Evaluate(.5, true);
                XYZ tangent = (segment.GetCurve().GetEndPoint(1) - segment.GetCurve().GetEndPoint(0)).Normalize();
                for (int axis = 0; axis < 2; axis++)
                {
                    if (Math.Abs(axes[axis].Z) > .9 || Math.Abs(tangent.DotProduct(axes[axis])) > .001) continue;
                    if (refs[axis].Count > 0)
                    {
                        overall[axis].Add(refs[axis].OrderBy(r => r.Position).First());
                        overall[axis].Add(refs[axis].OrderBy(r => r.Position).Last());
                    }
                    double expected = midpoint.DotProduct(axes[axis]);
                    var closest = refs[axis].OrderBy(r => Math.Abs(r.Position - expected)).FirstOrDefault();
                    if (closest != null && Math.Abs(closest.Position - expected) < 2 / 304.8) walls[axis].Add(closest);
                    else report.Notes.Add("Tường " + wall.Id + ": thiếu reference mặt hoàn thiện trùng biên Room.");
                }
            }
            // Vertical datums are real horizontal faces, never a fabricated level/height reference.
            if (!(view is ViewPlan))
            {
                foreach (var element in new FilteredElementCollector(doc).WherePasses(new ElementMulticlassFilter(new[] { typeof(Floor), typeof(Ceiling) })).WhereElementIsNotElementType())
                {
                    var phase = view.get_Parameter(BuiltInParameter.VIEW_PHASE);
                    if (phase != null)
                    {
                        var status = element.GetPhaseStatus(phase.AsElementId());
                        if (status != ElementOnPhaseStatus.Existing && status != ElementOnPhaseStatus.New) continue;
                    }
                    var host = (HostObject)element;
                    var horizontalRefs = element is Floor ? HostObjectUtils.GetTopFaces(host) : HostObjectUtils.GetBottomFaces(host);
                    var sample = (room.Location as LocationPoint)?.Point;
                    if (sample == null) continue;
                    var elevationCrop = view.CropBox;
                    var valid = new List<MeasureRef>();
                    foreach (var reference in horizontalRefs)
                    {
                        var face = element.GetGeometryObjectFromReference(reference) as PlanarFace;
                        if (face == null || Math.Abs(face.FaceNormal.DotProduct(axes[1])) < .999999) continue;
                        var projected = face.Project(sample);
                        if (projected == null || !face.IsInside(projected.UVPoint)) continue;
                        double localY = elevationCrop.Transform.Inverse.OfPoint(projected.XYZPoint).Y;
                        if (localY < elevationCrop.Min.Y - Tol || localY > elevationCrop.Max.Y + Tol) continue;
                        valid.Add(new MeasureRef { Reference = reference, Position = face.Origin.DotProduct(axes[1]) });
                    }
                    if (valid.Count > 0) walls[1].Add(element is Floor ? valid.OrderByDescending(r => r.Position).First() : valid.OrderBy(r => r.Position).First());
                }
            }
            for (int axis = 0; axis < 2; axis++) walls[axis] = Unique(walls[axis]);
            for (int axis = 0; axis < 2; axis++)
            {
                overall[axis] = Unique(overall[axis].Concat(walls[axis]));
                if (overall[axis].Count > 1) overall[axis] = new List<MeasureRef> { overall[axis].First(), overall[axis].Last() };
            }
            foreach (FamilyInstance opening in new FilteredElementCollector(doc, view.Id).OfClass(typeof(FamilyInstance)))
            {
                if (opening.Category == null || (opening.Category.Id != new ElementId(BuiltInCategory.OST_Doors)
                    && opening.Category.Id != new ElementId(BuiltInCategory.OST_Windows)) || opening.Host == null || !boundaryWallIds.Contains(opening.Host.Id)) continue;
                var location = opening.Location as LocationPoint; if (location == null) continue;
                for (int axis = 0; axis < 2; axis++)
                {
                    if (walls[axis].Count < 2 || Math.Abs(opening.GetTransform().BasisX.DotProduct(axes[axis])) < .999999) continue;
                    foreach (var refType in new[] { FamilyInstanceReferenceType.Left, FamilyInstanceReferenceType.Right })
                    foreach (var reference in opening.GetReferences(refType) ?? new List<Reference>())
                    {
                        var measured = ResolveCenter(doc, view, type, walls[axis].First(), reference, axes[axis], location.Point);
                        if (measured != null && measured.Position >= walls[axis].First().Position - Tol && measured.Position <= walls[axis].Last().Position + Tol) openings[axis].Add(measured);
                    }
                }
            }
            if (devices)
            foreach (FamilyInstance family in new FilteredElementCollector(doc, view.Id).OfClass(typeof(FamilyInstance)))
            {
                var category = family.Category;
                if (category == null || !new[] { BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_PlumbingEquipment,
                    BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_SpecialityEquipment, BuiltInCategory.OST_GenericModel,
                    BuiltInCategory.OST_Furniture, BuiltInCategory.OST_Casework }.Any(c => category.Id == new ElementId(c))) continue;
                var location = family.Location as LocationPoint; if (location == null) continue;
                var roomBounds = room.get_BoundingBox(null);
                double testZ = roomBounds != null ? (roomBounds.Min.Z + roomBounds.Max.Z) / 2 : (doc.GetElement(room.LevelId) as Level).Elevation + 1;
                var viewPhase = doc.GetElement(view.get_Parameter(BuiltInParameter.VIEW_PHASE).AsElementId()) as Phase;
                var associatedRoom = viewPhase == null ? null : family.get_Room(viewPhase);
                if (associatedRoom != null ? associatedRoom.Id != room.Id : !room.IsPointInRoom(new XYZ(location.Point.X, location.Point.Y, testZ))) continue;
                var transform = family.GetTransform(); bool found = false;
                for (int axis = 0; axis < 2; axis++)
                {
                    if (walls[axis].Count < 1) continue;
                    var refTypes = new[] { FamilyInstanceReferenceType.CenterLeftRight, FamilyInstanceReferenceType.CenterFrontBack, FamilyInstanceReferenceType.CenterElevation };
                    var normals = new[] { transform.BasisX, transform.BasisY, transform.BasisZ };
                    for (int index = 0; index < 3; index++)
                    {
                        if (Math.Abs(normals[index].Normalize().DotProduct(axes[axis])) < .999999) continue;
                        var references = family.GetReferences(refTypes[index]); if (references == null) continue;
                        foreach (var reference in references)
                        {
                            var measured = ResolveCenter(doc, view, type, walls[axis].First(), reference, axes[axis], location.Point);
                            var familyBounds = family.get_BoundingBox(view);
                            if (measured != null && familyBounds != null)
                            {
                                var points = new List<XYZ>();
                                for (int i = 0; i < 8; i++) points.Add(familyBounds.Transform.OfPoint(new XYZ((i & 1) == 0 ? familyBounds.Min.X : familyBounds.Max.X,
                                    (i & 2) == 0 ? familyBounds.Min.Y : familyBounds.Max.Y, (i & 4) == 0 ? familyBounds.Min.Z : familyBounds.Max.Z)));
                                if (measured.Position < points.Min(p => p.DotProduct(axes[axis])) - Tol || measured.Position > points.Max(p => p.DotProduct(axes[axis])) + Tol) measured = null;
                            }
                            if (measured != null && measured.Position > walls[axis].First().Position + Tol
                                && (walls[axis].Count == 1 || measured.Position < walls[axis].Last().Position - Tol))
                            { centers[axis].Add(measured); found = true; }
                        }
                    }
                }
                if (!found) report.Notes.Add("Thiết bị " + family.Id + ": thiếu reference tâm phù hợp, không thay bằng mép hoặc BoundingBox.");
            }
            var crop = view.CropBox; var cropPoints = new List<XYZ>();
            for (int i = 0; i < 8; i++) cropPoints.Add(crop.Transform.OfPoint(new XYZ((i & 1) == 0 ? crop.Min.X : crop.Max.X, (i & 2) == 0 ? crop.Min.Y : crop.Max.Y, (i & 4) == 0 ? crop.Min.Z : crop.Max.Z)));
            for (int axis = 0; axis < 2; axis++) for (int layer = 0; layer < 3; layer++) for (int side = 0; side < 4; side++)
            {
                if (!settings.IsSideEnabled(layer, side) || (axis == 0 ? side < 2 : side >= 2)) continue;
                if (layer == 0 && !devices) continue;
                var refs = layer == 0 ? (centers[axis].Count == 0 || walls[axis].Count == 0 ? new List<MeasureRef>() : Unique(centers[axis].Concat(new[] { walls[axis].First(), walls[axis].Last() })))
                    : layer == 1 ? Unique(walls[axis].Concat(openings[axis])) : overall[axis];
                if (refs.Count < 2)
                {
                    report.Notes.Add("Lớp " + (layer + 1) + " " + new[] { "trái", "phải", "trên", "dưới" }[side]
                        + ": thiếu " + (layer == 0 ? "reference tâm thiết bị và mốc mặt hoàn thiện" : "hai reference mặt hoàn thiện") + ".");
                    continue;
                }
                bool positive = side == 1 || side == 2; var perpendicular = axes[1 - axis];
                double baseline = (positive ? cropPoints.Max(p => p.DotProduct(perpendicular)) : cropPoints.Min(p => p.DotProduct(perpendicular)))
                    + (positive ? 1 : -1) * gapMm / 304.8 * view.Scale * (layer + 1);
                XYZ origin = perpendicular * baseline + view.ViewDirection * view.Origin.DotProduct(view.ViewDirection);
                using (var attempt = new SubTransaction(doc))
                {
                    attempt.Start();
                    try
                    {
                        var array = new ReferenceArray(); foreach (var r in refs) array.Append(r.Reference);
                        var dim = doc.Create.NewDimension(view, Line.CreateBound(origin + axes[axis] * refs.First().Position, origin + axes[axis] * refs.Last().Position), array, type);
                        doc.Regenerate(); ValidateWcDim(dim, refs);
                        var entity = new Entity(schema); entity.Set<int>(schema.GetField("Layer"), layer + 1); dim.SetEntity(entity);
                        if (attempt.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Dim bị Revit từ chối.");
                        report.Layers[layer]++;
                    }
                    catch (Exception ex) { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); report.Notes.Add("Lớp " + (layer + 1) + ": " + ex.Message); }
                }
            }
            return report;
        }
        private static MeasureRef ResolveCenter(Document doc, View view, DimensionType type, MeasureRef anchor, Reference reference, XYZ axis, XYZ location)
        {
            using (var temp = new SubTransaction(doc))
            {
                temp.Start();
                try
                {
                    var refs = new ReferenceArray(); refs.Append(anchor.Reference); refs.Append(reference);
                    XYZ basePoint = view.Origin - axis * view.Origin.DotProduct(axis);
                    var dimension = doc.Create.NewDimension(view, Line.CreateBound(basePoint + axis * anchor.Position, basePoint + axis * (anchor.Position + 1)), refs, type);
                    doc.Regenerate(); double? value = dimension.Value;
                    if (!value.HasValue || value.Value <= Tol) return null;
                    return new MeasureRef { Reference = reference, Position = anchor.Position + value.Value };
                }
                catch { return null; }
                finally { if (temp.GetStatus() == TransactionStatus.Started) temp.RollBack(); }
            }
        }
        private static void ValidateWcDim(Dimension dim, List<MeasureRef> refs)
        {
            if (dim == null) throw new InvalidOperationException("Không tạo được Dim.");
            var values = dim.NumberOfSegments > 1 ? dim.Segments.Cast<DimensionSegment>().Select(s => s.Value).ToList() : new List<double?> { dim.Value };
            if (values.Count != refs.Count - 1) throw new InvalidOperationException("Số đoạn Dim không khớp reference.");
            for (int i = 0; i < values.Count; i++) if (!values[i].HasValue || Math.Abs(values[i].Value - (refs[i + 1].Position - refs[i].Position)) > Tol)
                throw new InvalidOperationException("Reference không khớp số đo; đã hủy Dim.");
        }
    }
}




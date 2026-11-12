using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public sealed class PlaceViewsCommand : IExternalCommand
    {
        internal static System.Windows.Media.ImageSource CreateIcon(int size)
        {
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var ink = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 117, 43));
                var pen = new System.Windows.Media.Pen(ink, 1.6);
                dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new System.Windows.Rect(0, 0, 32, 32));
                dc.DrawRoundedRectangle(System.Windows.Media.Brushes.Honeydew, pen, new System.Windows.Rect(11, 3, 18, 26), 1, 1);
                dc.DrawRectangle(null, pen, new System.Windows.Rect(23, 6, 3, 20));
                dc.DrawRectangle(System.Windows.Media.Brushes.DarkSeaGreen, pen, new System.Windows.Rect(2, 10, 12, 10));
                dc.DrawLine(new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 1), new System.Windows.Point(4, 17), new System.Windows.Point(8, 13));
                dc.DrawLine(new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 1), new System.Windows.Point(8, 13), new System.Windows.Point(12, 17));
                var arrow = new System.Windows.Media.StreamGeometry();
                using (var path = arrow.Open())
                {
                    path.BeginFigure(new System.Windows.Point(12, 21), true, true);
                    path.PolyLineTo(new[] { new System.Windows.Point(19, 21), new System.Windows.Point(19, 18), new System.Windows.Point(24, 23), new System.Windows.Point(19, 28), new System.Windows.Point(19, 25), new System.Windows.Point(12, 25) }, true, false);
                }
                dc.DrawGeometry(ink, null, arrow);
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96.0 * size / 32, 96.0 * size / 32, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze(); return bitmap;
        }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            var doc = ui.Document;
            var selectedViews = new HashSet<long>(); var selectedSheets = new HashSet<long>();
            foreach (var id in ui.Selection.GetElementIds())
            {
                var element = doc.GetElement(id);
                if (element is ViewSheet selectedSheet) selectedSheets.Add(selectedSheet.Id.Value);
                else if (element is View selectedView) selectedViews.Add(selectedView.Id.Value);
                else if (element is Viewport selectedPort) { selectedViews.Add(selectedPort.ViewId.Value); selectedSheets.Add(selectedPort.SheetId.Value); }
                else if (element is ScheduleSheetInstance selectedSchedule) { selectedViews.Add(selectedSchedule.ScheduleId.Value); selectedSheets.Add(selectedSchedule.OwnerViewId.Value); }
            }
            var viewRows = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && Eligible(v))
                .OrderBy(v => v.ViewType).ThenBy(v => v.Name).Select(v => new PlaceViewRow { Id = v.Id.Value, Name = v.Name, Group = v.ViewType.ToString(),
                    Initial = selectedViews.Contains(v.Id.Value), Selected = selectedViews.Contains(v.Id.Value), Repeatable = v.ViewType == ViewType.Legend || v is ViewSchedule }).ToList();
            var sheetRows = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber).Select(s => new PlaceSheetRow { Id = s.Id.Value, Name = s.SheetNumber + " — " + s.Name,
                    Initial = selectedSheets.Contains(s.Id.Value), Selected = selectedSheets.Contains(s.Id.Value) }).ToList();
            if (sheetRows.Count == 0) { TaskDialog.Show("Đặt View vào Sheet", "Tạo ít nhất một sheet trước."); return Result.Cancelled; }
            var placements = new List<PlacementRow>();
            foreach (Viewport port in new FilteredElementCollector(doc).OfClass(typeof(Viewport)))
                placements.Add(new PlacementRow { PlacementId = port.Id.Value, ViewId = port.ViewId.Value, SheetId = port.SheetId.Value, Name = doc.GetElement(port.ViewId)?.Name ?? "View" });
            foreach (ScheduleSheetInstance schedule in new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance)))
                if (!schedule.IsTitleblockRevisionSchedule) placements.Add(new PlacementRow { PlacementId = schedule.Id.Value, ViewId = schedule.ScheduleId.Value, SheetId = schedule.OwnerViewId.Value, Name = doc.GetElement(schedule.ScheduleId)?.Name ?? "Schedule" });
            var window = new PlaceViewsWindow(viewRows, sheetRows, placements);
            new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
            if (window.ShowDialog() != true) return Result.Cancelled;
            var errors = new List<string>(); int added = 0, removed = 0; bool operationFailed = false;
            using (var tx = new Transaction(doc, "KTool - Đặt View vào Sheet"))
            {
                try
                {
                    tx.Start();
                    foreach (var placement in placements.Where(p => p.Remove))
                    using (var attempt = new SubTransaction(doc))
                    {
                        attempt.Start();
                        try
                        {
                            var element = doc.GetElement(new ElementId(placement.PlacementId));
                            if (!(element is Viewport) && !(element is ScheduleSheetInstance)) throw new InvalidOperationException("Vị trí đặt không còn tồn tại.");
                            doc.Delete(element.Id);
                            if (attempt.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu thao tác gỡ."); removed++;
                        }
                        catch (Exception ex) { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); operationFailed = true; errors.Add("Gỡ " + placement.Name + ": " + ex.Message); }
                    }
                    doc.Regenerate();
                    foreach (var request in window.Additions)
                    using (var attempt = new SubTransaction(doc))
                    {
                        attempt.Start();
                        try
                        {
                            var sheet = (ViewSheet)doc.GetElement(new ElementId(request.SheetId)); var view = (View)doc.GetElement(new ElementId(request.ViewId));
                            var occupied = Occupied(doc, sheet);
                            Element element;
                            if (view is ViewSchedule schedule)
                            {
                                if (new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().Any(p => p.ScheduleId == view.Id)) throw new InvalidOperationException("Schedule đã có trên sheet này.");
                                element = ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id, XYZ.Zero);
                            }
                            else
                            {
                                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id)) throw new InvalidOperationException("View đã lên sheet khác hoặc không thể đặt. Chọn Gỡ vị trí cũ trước nếu muốn chuyển.");
                                var port = Viewport.Create(doc, sheet.Id, view.Id, XYZ.Zero); element = port;
                                if (window.CenterTitles)
                                using (var titleAttempt = new SubTransaction(doc))
                                {
                                    titleAttempt.Start(); try { AlignViewportTitleCommand.CenterTitle(doc, port); titleAttempt.Commit(); }
                                    catch (Exception ex) { if (titleAttempt.GetStatus() == TransactionStatus.Started) titleAttempt.RollBack(); errors.Add("Title " + view.Name + ": " + ex.Message); }
                                }
                            }
                            doc.Regenerate(); var box = Bounds(element, sheet);
                            if (box == null) throw new InvalidOperationException("Không đo được vùng viewport/schedule.");
                            var paper = UsablePaper(doc, sheet, window.PaperMargin, window.RightReserve);
                            var location = SheetPlacementLayout.FindPlace(paper, occupied, box.Width, box.Height, window.Gap);
                            if (location == null) throw new InvalidOperationException("Không đủ chỗ trống ở tỷ lệ hiện tại. Chọn sheet lớn hơn, giảm tỷ lệ hoặc điều chỉnh lề/vùng khung tên.");
                            ElementTransformUtils.MoveElement(doc, element.Id, new XYZ(location.X0 - box.X0, location.Y0 - box.Y0, 0)); doc.Regenerate();
                            if (attempt.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu vị trí đặt."); added++;
                            if (paper == null) errors.Add(sheet.SheetNumber + ": không có khung tên; xếp dọc và chưa kiểm tra giới hạn khổ giấy.");
                        }
                        catch (Exception ex) { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); operationFailed = true; errors.Add("Đặt " + request.Name + ": " + ex.Message); }
                    }
                    if (operationFailed && removed > 0)
                    {
                        // A move is planned as remove + add. Preserve the original
                        // placement when any destination cannot accept the view.
                        tx.RollBack(); added = 0; removed = 0;
                        errors.Insert(0, "Đã hoàn tác toàn bộ kế hoạch có thao tác gỡ vì một mục không thực hiện được; giữ các vị trí ban đầu.");
                    }
                    else if (added + removed == 0) tx.RollBack();
                    else if (tx.Commit() != TransactionStatus.Committed) { message = "Revit không lưu thao tác đặt/gỡ view."; return Result.Failed; }
                }
                catch (Exception ex) { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); message = ex.Message; return Result.Failed; }
            }
            new TaskDialog("Đặt View vào Sheet") { MainInstruction = "Đã đặt " + added + " view, gỡ " + removed + " vị trí đặt",
                MainContent = "Không đổi tỷ lệ view. Có thể Undo cả lượt. " + (errors.Count > 0 ? "Mở chi tiết để xem các mục cần kiểm tra." : ""), ExpandedContent = string.Join("\n", errors.Distinct()), CommonButtons = TaskDialogCommonButtons.Close }.Show();
            return added + removed > 0 ? Result.Succeeded : Result.Cancelled;
        }
        private static bool Eligible(View view) => view is ViewPlan || view is ViewSection || view is View3D three && !three.IsPerspective || view.ViewType == ViewType.Legend || view.ViewType == ViewType.DraftingView
            || view is ViewSchedule schedule && !schedule.IsTitleblockRevisionSchedule;
        private static SheetPlacementBox Bounds(Element element, ViewSheet sheet)
        {
            if (element is Viewport port)
            {
                using (var outline = port.GetBoxOutline())
                {
                    var box = new SheetPlacementBox { X0 = outline.MinimumPoint.X, Y0 = outline.MinimumPoint.Y, X1 = outline.MaximumPoint.X, Y1 = outline.MaximumPoint.Y };
                    try { using (var label = port.GetLabelOutline()) if (!label.IsEmpty) { box.X0 = Math.Min(box.X0, label.MinimumPoint.X); box.Y0 = Math.Min(box.Y0, label.MinimumPoint.Y); box.X1 = Math.Max(box.X1, label.MaximumPoint.X); box.Y1 = Math.Max(box.Y1, label.MaximumPoint.Y); } } catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
                    return box;
                }
            }
            var bounds = element.get_BoundingBox(sheet); return bounds == null ? null : new SheetPlacementBox { X0 = bounds.Min.X, X1 = bounds.Max.X, Y0 = bounds.Min.Y, Y1 = bounds.Max.Y };
        }
        private static List<SheetPlacementBox> Occupied(Document doc, ViewSheet sheet)
        {
            return new FilteredElementCollector(doc, sheet.Id).WhereElementIsNotElementType().Where(e => e is Viewport || e is ScheduleSheetInstance)
                .Select(e => Bounds(e, sheet)).Where(b => b != null).ToList();
        }
        private static SheetPlacementBox UsablePaper(Document doc, ViewSheet sheet, double margin, double reserve)
        {
            var blocks = new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType()
                .Select(e => e.get_BoundingBox(sheet)).Where(b => b != null).ToList();
            if (blocks.Count == 0) return null;
            var largest = blocks.OrderByDescending(b => (b.Max.X - b.Min.X) * (b.Max.Y - b.Min.Y)).First();
            var result = new SheetPlacementBox { X0 = largest.Min.X + margin, Y0 = largest.Min.Y + margin, X1 = largest.Max.X - margin - reserve, Y1 = largest.Max.Y - margin };
            if (result.Width <= 0 || result.Height <= 0) throw new InvalidOperationException("Lề/vùng khung tên lớn hơn khổ sheet."); return result;
        }
    }
}


using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows.Media;

namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public class AlignViewportTitleCommand : IExternalCommand
    {
        private const string Title = "Align Title - Căn giữa tiêu đề viewport";
        private const double Tolerance = 1e-6;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument ui = commandData.Application.ActiveUIDocument;
            if (ui == null || !(ui.ActiveView is ViewSheet))
            {
                TaskDialog.Show(Title, "Hãy mở Sheet trước khi chạy lệnh.");
                return Result.Cancelled;
            }

            Document doc = ui.Document;
            List<Viewport> viewports = ui.Selection.GetElementIds()
                .Select(doc.GetElement).OfType<Viewport>()
                .Where(v => v.SheetId == ui.ActiveView.Id).ToList();
            if (viewports.Count == 0)
            {
                try
                {
                    viewports = ui.Selection.PickObjects(ObjectType.Element,
                        new ViewportFilter(ui.ActiveView.Id),
                        "Chọn các viewport cần căn giữa tiêu đề, rồi bấm Finish.")
                        .Select(r => doc.GetElement(r)).OfType<Viewport>().ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }
            if (viewports.Count == 0) return Result.Cancelled;

            int aligned = 0;
            var skipped = new List<string>();
            using (var transaction = new Transaction(doc, Title))
            {
                try
                {
                    transaction.Start();
                    foreach (Viewport viewport in viewports)
                    {
                        using (var item = new SubTransaction(doc))
                        {
                            item.Start();
                            try
                            {
                                CenterTitle(doc, viewport);
                                if (item.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                                aligned++;
                            }
                            catch (Exception ex)
                            {
                                if (item.GetStatus() == TransactionStatus.Started) item.RollBack();
                                skipped.Add(viewport.Id + ": " + ex.Message);
                            }
                        }
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                    {
                        message = "Revit không lưu được thao tác căn tiêu đề.";
                        return Result.Failed;
                    }
                }
                catch (Exception ex)
                {
                    if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                    message = ex.Message;
                    return Result.Failed;
                }
            }
            TaskDialog.Show(Title, "Đã căn giữa " + aligned + " tiêu đề viewport."
                + (skipped.Count == 0 ? "" : "\nBỏ qua " + skipped.Count + ":\n"
                    + string.Join("\n", skipped.Take(10))));
            return Result.Succeeded;
        }

        private static XYZ LabelCenter(Viewport viewport)
        {
            using (Outline outline = viewport.GetLabelOutline())
            {
                if (outline.IsEmpty)
                    throw new InvalidOperationException("Không có tiêu đề hiển thị.");
                return (outline.MinimumPoint + outline.MaximumPoint) * 0.5;
            }
        }

        internal static void CenterTitle(Document doc, Viewport viewport)
        {
            doc.Regenerate();
            XYZ center = LabelCenter(viewport);
            double dx = viewport.GetBoxCenter().X - center.X;
            if (Math.Abs(dx) < Tolerance) return;

            // LabelOffset uses the unrotated viewport coordinate system. Measure
            // its two sheet-space basis vectors to also support rotated viewports.
            XYZ original = viewport.LabelOffset;
            const double step = 0.01;
            viewport.LabelOffset = original + new XYZ(step, 0, 0);
            doc.Regenerate();
            XYZ basisX = (LabelCenter(viewport) - center) / step;
            viewport.LabelOffset = original + new XYZ(0, step, 0);
            doc.Regenerate();
            XYZ basisY = (LabelCenter(viewport) - center) / step;
            double determinant = basisX.X * basisY.Y - basisY.X * basisX.Y;
            if (Math.Abs(determinant) < Tolerance)
                throw new InvalidOperationException("Không xác định được hướng dịch tiêu đề.");
            viewport.LabelOffset = original + new XYZ(
                dx * basisY.Y / determinant, -dx * basisX.Y / determinant, 0);
            doc.Regenerate();
            XYZ result = LabelCenter(viewport);
            if (Math.Abs(result.X - viewport.GetBoxCenter().X) > Tolerance
                || Math.Abs(result.Y - center.Y) > Tolerance)
                throw new InvalidOperationException("Không thể căn giữa với kiểu tiêu đề hiện tại.");
        }

        internal static ImageSource CreateIcon(int size)
        {
            var drawing = new DrawingGroup();
            using (DrawingContext dc = drawing.Open())
            {
                dc.DrawRectangle(Brushes.Transparent, null, new System.Windows.Rect(0, 0, 32, 32));
                var pen = new Pen(Brushes.SteelBlue, 2);
                dc.DrawRectangle(null, pen, new System.Windows.Rect(4, 3, 24, 16));
                dc.DrawLine(new Pen(Brushes.Gray, 1), new System.Windows.Point(16, 1), new System.Windows.Point(16, 30));
                dc.DrawRectangle(Brushes.SteelBlue, null, new System.Windows.Rect(9, 23, 14, 3));
                dc.DrawLine(pen, new System.Windows.Point(6, 29), new System.Windows.Point(26, 29));
            }
            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }

        private sealed class ViewportFilter : ISelectionFilter
        {
            private readonly ElementId sheetId;
            public ViewportFilter(ElementId sheetId) { this.sheetId = sheetId; }
            public bool AllowElement(Element element)
            {
                return element is Viewport viewport && viewport.SheetId == sheetId;
            }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }
    }
}

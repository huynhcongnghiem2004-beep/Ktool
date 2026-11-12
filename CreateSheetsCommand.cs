using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateSheetsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            var doc = ui.Document;
            var titles = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilySymbol>()
                .OrderBy(t => t.FamilyName).ThenBy(t => t.Name).Select(t => new SheetTitleChoice { Id = t.Id.Value, Name = t.FamilyName + " : " + t.Name }).ToList();
            titles.Add(new SheetTitleChoice { Id = -1, Name = "(Không khung tên)" });
            var window = new CreateSheetsWindow(titles, new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber));
            new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
            if (window.ShowDialog() != true) return Result.Cancelled;
            var errors = new List<string>(); var created = new List<string>();
            using (var tx = new Transaction(doc, "KTool - Tạo Sheets"))
            {
                try
                {
                    tx.Start();
                    foreach (var row in window.Rows)
                    using (var attempt = new SubTransaction(doc))
                    {
                        attempt.Start();
                        try
                        {
                            var id = new ElementId(row.TitleBlock.Id); var symbol = doc.GetElement(id) as FamilySymbol;
                            if (symbol != null && !symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
                            var sheet = ViewSheet.Create(doc, id); sheet.SheetNumber = row.Number; sheet.Name = row.Name;
                            if (attempt.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu sheet.");
                            created.Add(sheet.SheetNumber + " — " + sheet.Name);
                        }
                        catch (Exception ex) { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); errors.Add(row.Number + ": " + ex.Message); }
                    }
                    if (created.Count == 0) tx.RollBack();
                    else if (tx.Commit() != TransactionStatus.Committed) { message = "Revit không lưu được các sheet."; return Result.Failed; }
                }
                catch (Exception ex) { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); message = ex.Message; return Result.Failed; }
            }
            new TaskDialog("Tạo Sheets") { MainInstruction = "Đã tạo " + created.Count + "/" + window.Rows.Count + " sheet",
                MainContent = errors.Count == 0 ? "Có thể Undo cả lượt tạo." : errors.Count + " dòng chưa tạo được. Mở chi tiết để xem.",
                ExpandedContent = string.Join("\n", created) + "\n" + string.Join("\n", errors), CommonButtons = TaskDialogCommonButtons.Close }.Show();
            return created.Count > 0 ? Result.Succeeded : Result.Cancelled;
        }
        internal static System.Windows.Media.ImageSource CreateIcon()
        {
            var drawing = new System.Windows.Media.DrawingGroup(); using (var dc = drawing.Open())
            {
                var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.OliveDrab, 1.5);
                dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new System.Windows.Rect(0, 0, 32, 32));
                dc.DrawRectangle(System.Windows.Media.Brushes.Honeydew, pen, new System.Windows.Rect(3, 3, 25, 25));
                dc.DrawRectangle(null, pen, new System.Windows.Rect(20, 6, 5, 19));
                dc.DrawLine(pen, new System.Windows.Point(8, 15), new System.Windows.Point(18, 15)); dc.DrawLine(pen, new System.Windows.Point(13, 10), new System.Windows.Point(13, 20));
            }
            var image = new System.Windows.Media.DrawingImage(drawing); image.Freeze(); return image;
        }
    }
}

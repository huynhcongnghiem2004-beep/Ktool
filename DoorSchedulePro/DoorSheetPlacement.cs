using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using SDK_HD.Dim;

namespace DoorDetailLegend
{
    internal static class DoorSheetPlacement
    {
        internal static List<ElementId> Place(Document doc, IList<ElementId> views, WizardRequest request, Action<string> log)
        {
            var result = new List<ElementId>();
            if (!request.PlaceOnSheet || views.Count == 0) return result;
            if (request.MarginMm < 0 || request.GapMm < 0 || request.RightReserveMm < 0)
                throw new InvalidOperationException("Lề, khoảng cách và vùng khung tên phải không âm.");
            ViewSheet initial = request.TargetSheetId > 0 ? doc.GetElement(new ElementId(request.TargetSheetId)) as ViewSheet : null;
            if (request.TargetSheetId > 0 && (initial == null || initial.IsPlaceholder))
                throw new InvalidOperationException("Sheet đích không còn hợp lệ.");
            FamilySymbol block = request.TitleBlockId > 0 ? doc.GetElement(new ElementId(request.TitleBlockId)) as FamilySymbol : null;
            if (block == null && initial != null)
                block = new FilteredElementCollector(doc).OwnedByView(initial.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Select(f => f.Symbol).FirstOrDefault();
            if (initial == null && block == null) throw new InvalidOperationException("Hãy chọn loại khung tên để tạo Sheet.");
            var pending = new List<string>();
            using (var tx = new Transaction(doc, "Đặt chi tiết cửa vừa Sheet"))
            {
                tx.Start();
                ViewSheet sheet = initial;
                if (sheet == null) sheet = NewSheet(doc, block, request);
                foreach (ElementId viewId in views.Distinct())
                {
                    View view = doc.GetElement(viewId) as View;
                    if (view == null || view.ViewType != ViewType.Legend)
                        throw new InvalidOperationException("Chỉ đặt Legend chi tiết cửa đã tạo.");
                    bool placed = TryPlace(doc, sheet, view, request, out string reason);
                    if (!placed)
                    {
                        // An empty page that cannot fit the view cannot be fixed by adding
                        // identical pages. Stop with the measured failure instead of looping.
                        bool hasContent = Occupied(doc, sheet).Count > 0;
                        if (!request.OverflowSheets || !hasContent || block == null)
                            throw new InvalidOperationException(view.Name + ": " + reason +
                                " Chọn khung tên lớn hơn hoặc tăng mẫu số tỷ lệ trước khi triển khai.");
                        sheet = NewSheet(doc, block, request);
                        if (!TryPlace(doc, sheet, view, request, out reason))
                            throw new InvalidOperationException(view.Name + ": không vừa Sheet mới. " + reason);
                    }
                    if (!result.Contains(sheet.Id)) result.Add(sheet.Id);
                    if (view.Scale != request.ViewScale) pending.Add("Tự chọn tỷ lệ 1:" + view.Scale + " để chi tiết vừa khổ giấy (yêu cầu ban đầu 1:" + request.ViewScale + ").");
                    pending.Add("Đã đặt " + view.Name + " → " + sheet.SheetNumber + " (1:" + view.Scale + ").");
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit đã hủy sắp xếp Sheet.");
            }
            foreach (string line in pending) log?.Invoke(line);
            return result;
        }

        private static ViewSheet NewSheet(Document doc, FamilySymbol block, WizardRequest request)
        {
            if (block == null || block.Category?.Id.Value != (long)BuiltInCategory.OST_TitleBlocks)
                throw new InvalidOperationException("Loại khung tên không hợp lệ.");
            if (!block.IsActive) block.Activate();
            var sheet = ViewSheet.Create(doc, block.Id);
            string prefix = string.IsNullOrWhiteSpace(request.SheetPrefix) ? "CT-CUA" : request.SheetPrefix.Trim();
            var used = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>().Where(s => s.Id != sheet.Id).Select(s => s.SheetNumber), StringComparer.OrdinalIgnoreCase);
            int n = 1;
            while (used.Contains(prefix + "-" + n.ToString("00"))) n++;
            sheet.SheetNumber = prefix + "-" + n.ToString("00");
            sheet.Name = string.IsNullOrWhiteSpace(request.SheetName) ? "CHI TIẾT CỬA" : request.SheetName.Trim();
            doc.Regenerate();
            return sheet;
        }

        internal static SheetPlacementBox Paper(Document doc, ViewSheet sheet, WizardRequest req)
        {
            var boxes = new FilteredElementCollector(doc).OwnedByView(sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType().Select(e => e.get_BoundingBox(sheet)).Where(b => b != null).ToList();
            if (boxes.Count == 0) throw new InvalidOperationException(sheet.SheetNumber + ": cần khung tên để xác định khổ giấy.");
            BoundingBoxXYZ box = boxes.OrderByDescending(b => (b.Max.X-b.Min.X)*(b.Max.Y-b.Min.Y)).First();
            double margin = req.MarginMm / 304.8;
            var paper = new SheetPlacementBox { X0=box.Min.X+margin, Y0=box.Min.Y+margin,
                X1=box.Max.X-margin-req.RightReserveMm/304.8, Y1=box.Max.Y-margin };
            if(paper.Width<=0 || paper.Height<=0)throw new InvalidOperationException("Lề hoặc vùng khung tên lớn hơn khổ giấy.");
            return paper;
        }

        internal static SheetPlacementBox Bounds(Viewport port)
        {
            using (Outline outline=port.GetBoxOutline())
            {
                var box = new SheetPlacementBox { X0=outline.MinimumPoint.X,Y0=outline.MinimumPoint.Y,
                    X1=outline.MaximumPoint.X,Y1=outline.MaximumPoint.Y };
                using (Outline label=port.GetLabelOutline())
                    if(!label.IsEmpty) {box.X0=Math.Min(box.X0,label.MinimumPoint.X);box.Y0=Math.Min(box.Y0,label.MinimumPoint.Y);
                        box.X1=Math.Max(box.X1,label.MaximumPoint.X);box.Y1=Math.Max(box.Y1,label.MaximumPoint.Y);}
                return box;
            }
        }

        private static List<SheetPlacementBox> Occupied(Document doc, ViewSheet sheet)
        {
            var result = new List<SheetPlacementBox>();
            foreach (Viewport port in new FilteredElementCollector(doc).OwnedByView(sheet.Id).OfClass(typeof(Viewport)).Cast<Viewport>())
                result.Add(Bounds(port));
            foreach (Element schedule in new FilteredElementCollector(doc).OwnedByView(sheet.Id).OfClass(typeof(ScheduleSheetInstance)))
            {var b=schedule.get_BoundingBox(sheet);if(b!=null)result.Add(new SheetPlacementBox{X0=b.Min.X,Y0=b.Min.Y,X1=b.Max.X,Y1=b.Max.Y});}
            return result;
        }

        private static bool TryPlace(Document doc, ViewSheet sheet, View view, WizardRequest request, out string reason)
        {
            reason="";
            using(var attempt=new SubTransaction(doc))
            {
                attempt.Start();
                try
                {
                    var paper=Paper(doc,sheet,request);
                    var occupied=Occupied(doc,sheet);
                    if(!Viewport.CanAddViewToSheet(doc,sheet.Id,view.Id))
                        throw new InvalidOperationException("Legend đã có trên Sheet này hoặc không thể đặt.");
                    var port=Viewport.Create(doc,sheet.Id,view.Id,XYZ.Zero);
                    doc.Regenerate();
                    using (Outline label = port.GetLabelOutline())
                        if (!label.IsEmpty) AlignViewportTitleCommand.CenterTitle(doc,port);
                    doc.Regenerate();
                    var box=Bounds(port);
                    // Only reduce an oversized detail to fit the whole usable page.
                    // Lack of remaining space instead creates another sheet at that scale.
                    if (request.AutoFitSheet && (box.Width > paper.Width || box.Height > paper.Height))
                    {
                        foreach (int scale in new[] { 10, 15, 20, 25, 50, 75, 100, 125, 150, 200, 250, 500 }.Where(s => s > view.Scale))
                        {
                            view.Scale = scale;
                            DoorDetailLegendCommand.ReflowDetail(doc,view);
                            doc.Regenerate();
                            using (Outline label = port.GetLabelOutline())
                                if (!label.IsEmpty) AlignViewportTitleCommand.CenterTitle(doc,port);
                            doc.Regenerate();
                            box = Bounds(port);
                            if (box.Width <= paper.Width && box.Height <= paper.Height) break;
                        }
                    }
                    double gap=request.GapMm/304.8;
                    var location=SheetPlacementLayout.FindPlace(paper,occupied,box.Width,box.Height,gap);
                    if(location==null)
                    { reason="Vùng trống không đủ cho viewport và tiêu đề ("+Math.Round(box.Width*304.8)+" × "+Math.Round(box.Height*304.8)+" mm).";attempt.RollBack();return false; }
                    ElementTransformUtils.MoveElement(doc,port.Id,new XYZ(location.X0-box.X0,location.Y0-box.Y0,0));
                    doc.Regenerate();
                    box=Bounds(port);
                    const double tol=0.1/304.8;
                    if(box.X0<paper.X0-tol||box.Y0<paper.Y0-tol||box.X1>paper.X1+tol||box.Y1>paper.Y1+tol||occupied.Any(b=>box.Overlap(b,gap-tol)))
                        throw new InvalidOperationException("Viewport nằm ngoài vùng giấy hoặc chồng nội dung có sẵn.");
                    if(attempt.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Không lưu được vị trí viewport.");
                    return true;
                }
                catch(Exception ex){if(attempt.GetStatus()==TransactionStatus.Started)attempt.RollBack();reason=ex.Message;return false;}
            }
        }
    }
}

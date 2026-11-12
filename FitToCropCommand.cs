using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Tự động chỉnh đầu Grid và Level (2D extent trong view) theo khung Crop View.
    ///  - Mặt bằng: chỉnh các Grid.
    ///  - Mặt đứng / mặt cắt: chỉnh Grid (đường đứng) và Level (đường ngang).
    /// Đầu mỗi Grid/Level được đặt cách mép Crop một đoạn offset (cài trong nút "Offset Crop"):
    ///  - đầu có bubble dùng offset Bubble, đầu không có bubble dùng offset Tail;
    ///  - Ngang/Đứng tính theo hướng trên màn hình của view.
    /// Cách dùng:
    ///  - Chạy trong view đang mở: xử lý toàn bộ Grid + Level thấy được trong view
    ///    (hoặc chỉ các Grid/Level đang chọn sẵn).
    ///  - Hoặc chọn nhiều view trong Project Browser rồi chạy: xử lý tất cả các view đã chọn.
    /// Chỉ thay đổi 2D extent của view (ViewSpecific), không đổi hình học model của Grid/Level.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FitToCropCommand : IExternalCommand
    {
        private const string TITLE = "Fit Grid & Level vào Crop";
        private const double MIN_LENGTH = 0.01; // ft: bỏ qua nếu đoạn kết quả quá ngắn

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View active = doc.ActiveView;

            // 1. Xác định các view cần xử lý
            List<View> selectedViews = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<View>()
                .Where(v => !v.IsTemplate && (v is ViewPlan || v is ViewSection))
                .ToList();

            List<View> views;
            List<DatumPlane> limitTo = null;
            int viewsWithoutCrop = 0;

            if (selectedViews.Count > 0)
            {
                views = selectedViews.Where(v => v.CropBoxActive).ToList();
                viewsWithoutCrop = selectedViews.Count - views.Count;
            }
            else
            {
                if (!(active is ViewPlan || active is ViewSection) || active.IsTemplate)
                {
                    TaskDialog.Show(TITLE, "Hãy mở một view mặt bằng, mặt đứng hoặc mặt cắt rồi chạy lại lệnh.");
                    return Result.Cancelled;
                }
                if (!active.CropBoxActive)
                {
                    TaskDialog.Show(TITLE, "View này chưa bật Crop View. Hãy bật Crop View (Crop Region) rồi chạy lại lệnh.");
                    return Result.Cancelled;
                }
                views = new List<View> { active };

                // Nếu đã chọn sẵn Grid/Level thì chỉ xử lý các đối tượng đó
                limitTo = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .OfType<DatumPlane>()
                    .Where(d => d is Grid || d is Level)
                    .ToList();
            }

            if (views.Count == 0)
            {
                TaskDialog.Show(TITLE, "Các view đã chọn đều chưa bật Crop View.");
                return Result.Cancelled;
            }

            // 2. Đọc offset đã cài đặt
            FitCropSettings settings = FitCropSettings.Load();

            // 3. Xử lý
            int gridDone = 0, levelDone = 0, skipped = 0, customShape = 0;

            using (Transaction tx = new Transaction(doc, TITLE))
            {
                tx.Start();
                try
                {
                    foreach (View view in views)
                    {
                        if (HasCustomCropShape(view)) customShape++;

                        BoundingBoxXYZ crop = view.CropBox;
                        Transform inv = crop.Transform.Inverse;

                        foreach (DatumPlane d in CollectDatums(doc, view, limitTo))
                        {
                            bool ok;
                            try
                            {
                                ok = FitDatum(d, view, crop, inv, settings);
                            }
                            catch (Exception)
                            {
                                ok = false;
                            }

                            if (!ok) skipped++;
                            else if (d is Grid) gridDone++;
                            else levelDone++;
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    message = ex.Message;
                    return Result.Failed;
                }
            }

            // 4. Báo kết quả
            string msg = "Đã chỉnh " + gridDone + " Grid và " + levelDone + " Level trong " + views.Count + " view.";
            msg += "\nOffset: Ngang (Tail " + FitCropSettings.FormatLength(settings.HorizontalTailMm)
                 + ", Bubble " + FitCropSettings.FormatLength(settings.HorizontalBubbleMm)
                 + "), Đứng (Tail " + FitCropSettings.FormatLength(settings.VerticalTailMm)
                 + ", Bubble " + FitCropSettings.FormatLength(settings.VerticalBubbleMm) + ").";
            if (skipped > 0)
                msg += "\nBỏ qua " + skipped + " đối tượng (nằm ngoài khung Crop, không phải đường thẳng, hoặc không chỉnh được).";
            if (viewsWithoutCrop > 0)
                msg += "\nBỏ qua " + viewsWithoutCrop + " view chưa bật Crop View.";
            if (customShape > 0)
                msg += "\n" + customShape + " view có Crop Region dạng tự do: lệnh dùng khung chữ nhật bao quanh.";
            TaskDialog.Show(TITLE, msg);

            return Result.Succeeded;
        }

        internal static string FitInTransaction(Document doc, View view, FitCropSettings settings)
        {
            if (!view.CropBoxActive) return view.Name + ": chưa bật Crop View, bỏ qua Fit Crop.";
            doc.Regenerate();
            var crop = view.CropBox; var inverse = crop.Transform.Inverse;
            int done = 0, skipped = 0;
            foreach (var datum in CollectDatums(doc, view, null))
            using (var item = new SubTransaction(doc))
            {
                item.Start();
                try
                {
                    if (!FitDatum(datum, view, crop, inverse, settings)) { item.RollBack(); skipped++; continue; }
                    if (item.Commit() == TransactionStatus.Committed) done++; else skipped++;
                }
                catch { if (item.GetStatus() == TransactionStatus.Started) item.RollBack(); skipped++; }
            }
            return view.Name + ": Fit Crop " + done + " Grid/Level" + (skipped > 0 ? ", bỏ qua " + skipped : "") + ".";
        }

        /// <summary>Grid + Level hiển thị trong view (lọc theo danh sách chọn sẵn nếu có).</summary>
        private static List<DatumPlane> CollectDatums(Document doc, View view, List<DatumPlane> limitTo)
        {
            List<DatumPlane> all = new List<DatumPlane>();
            all.AddRange(new FilteredElementCollector(doc, view.Id).OfClass(typeof(Grid)).Cast<DatumPlane>());
            all.AddRange(new FilteredElementCollector(doc, view.Id).OfClass(typeof(Level)).Cast<DatumPlane>());

            if (limitTo != null && limitTo.Count > 0)
            {
                HashSet<ElementId> ids = new HashSet<ElementId>(limitTo.Select(x => x.Id));
                all = all.Where(x => ids.Contains(x.Id)).ToList();
            }
            return all;
        }

        /// <summary>
        /// Cắt đường thẳng chứa Grid/Level theo khung Crop (trong hệ toạ độ của view),
        /// kéo mỗi đầu ra ngoài mép Crop một đoạn offset, rồi đặt lại 2D extent trong view.
        /// </summary>
        private static bool FitDatum(DatumPlane d, View view, BoundingBoxXYZ crop, Transform inv, FitCropSettings st)
        {
            // Đường đang hiển thị của đối tượng trong view
            IList<Curve> curves = d.GetCurvesInView(DatumExtentType.ViewSpecific, view);
            if (curves == null || curves.Count == 0)
                curves = d.GetCurvesInView(DatumExtentType.Model, view);
            if (curves == null || curves.Count != 1) return false;

            Line ln = curves[0] as Line;
            if (ln == null) return false;

            XYZ A = ln.GetEndPoint(0);
            XYZ B = ln.GetEndPoint(1);
            double len = A.DistanceTo(B);
            if (len < 1e-9) return false;

            // Cắt đường thẳng A + (B-A)*t theo hình chữ nhật Crop (toạ độ view)
            XYZ a = inv.OfPoint(A);
            XYZ b = inv.OfPoint(B);

            double tIn = double.NegativeInfinity, tOut = double.PositiveInfinity;
            if (!ClipAxis(a.X, b.X, crop.Min.X, crop.Max.X, ref tIn, ref tOut)) return false;
            if (!ClipAxis(a.Y, b.Y, crop.Min.Y, crop.Max.Y, ref tIn, ref tOut)) return false;
            if (double.IsInfinity(tIn) || double.IsInfinity(tOut) || tOut - tIn < 1e-9) return false;

            // Ngang / Đứng theo hướng trên màn hình của view
            bool horizontal = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
            double tailFt = (horizontal ? st.HorizontalTailMm : st.VerticalTailMm) / 304.8;
            double bubbleFt = (horizontal ? st.HorizontalBubbleMm : st.VerticalBubbleMm) / 304.8;

            // Mỗi đầu xét riêng: có bubble -> offset Bubble, không bubble -> offset Tail
            double off0 = d.IsBubbleVisibleInView(DatumEnds.End0, view) ? bubbleFt : tailFt;
            double off1 = d.IsBubbleVisibleInView(DatumEnds.End1, view) ? bubbleFt : tailFt;

            // End0 ở phía t nhỏ, End1 ở phía t lớn; offset dương = vươn ra ngoài mép Crop
            tIn -= off0 / len;
            tOut += off1 / len;
            if (tOut - tIn < 1e-9) return false;

            XYZ dir = B - A;
            XYZ p0 = A + dir * tIn;
            XYZ p1 = A + dir * tOut;
            if (p0.DistanceTo(p1) < MIN_LENGTH) return false;

            d.SetDatumExtentType(DatumEnds.End0, view, DatumExtentType.ViewSpecific);
            d.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);
            d.SetCurveInView(DatumExtentType.ViewSpecific, view, Line.CreateBound(p0, p1));
            return true;
        }

        /// <summary>Thu hẹp khoảng tham số [tIn, tOut] theo một trục của hình chữ nhật Crop.</summary>
        private static bool ClipAxis(double a, double b, double min, double max, ref double tIn, ref double tOut)
        {
            double d = b - a;
            if (Math.Abs(d) < 1e-9)
                return a >= min - 1e-9 && a <= max + 1e-9; // song song trục: phải nằm trong dải

            double t1 = (min - a) / d;
            double t2 = (max - a) / d;
            if (t1 > t2)
            {
                double tmp = t1;
                t1 = t2;
                t2 = tmp;
            }
            if (t1 > tIn) tIn = t1;
            if (t2 < tOut) tOut = t2;
            return true;
        }

        private static bool HasCustomCropShape(View view)
        {
            try
            {
                return view.GetCropRegionShapeManager().ShapeSet;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}

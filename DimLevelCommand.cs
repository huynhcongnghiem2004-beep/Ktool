using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Tự động Dim Level (chạy trong view mặt đứng / mặt cắt):
    ///  - Dim 1: từng nhịp (chuỗi Dim qua tất cả các Level), đặt tại điểm người dùng chọn
    ///  - Dim 2: Dim tổng (Level thấp nhất -> cao nhất), đặt xa hơn Dim 1
    /// Thứ tự thao tác: bấm nút -> click chọn 1 Level -> chọn điểm đặt đường Dim.
    /// Lệnh lấy tất cả Level trong view (click để biết đang Dim đúng view).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DimLevelCommand : IExternalCommand
    {
        private const string TITLE = "Dim Level";
        private const double TOL_SPACING = 0.001;    // ft (~0.3 mm): coi 2 level trùng cao độ
        private const double TOTAL_OFFSET_MM = 10.0; // khoảng cách giấy giữa Dim nhịp và Dim tổng

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (!(view is ViewSection))
            {
                TaskDialog.Show(TITLE, "Hãy mở một view mặt đứng (Elevation) hoặc mặt cắt (Section) rồi chạy lại lệnh.");
                return Result.Cancelled;
            }

            XYZ up = view.UpDirection;
            XYZ right = view.RightDirection;
            if (Math.Abs(up.Z) < 0.999)
            {
                TaskDialog.Show(TITLE, "View này không thẳng đứng nên không Dim Level được.");
                return Result.Cancelled;
            }

            // 1. Click chọn 1 Level -> lấy tất cả Level trong view
            Level clicked;
            try
            {
                clicked = PickHelper.PickElement<Level>(uidoc, "Click chọn 1 Level để Dim tất cả Level trong view");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            if (clicked == null) return Result.Cancelled;

            List<Level> levels = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();

            // 2. Sắp xếp theo cao độ, loại Level trùng cao độ
            List<Level> sorted = levels.OrderBy(l => l.Elevation).ToList();
            List<Level> unique = new List<Level>();
            foreach (Level l in sorted)
            {
                if (unique.Count == 0 || l.Elevation - unique[unique.Count - 1].Elevation > TOL_SPACING)
                    unique.Add(l);
            }

            if (unique.Count < 2)
            {
                TaskDialog.Show(TITLE, "Không tìm thấy ít nhất 2 Level (khác cao độ) trong view này.");
                return Result.Cancelled;
            }

            // 3. Chọn điểm đặt đường Dim
            XYZ pick;
            try
            {
                pick = PickPointOnView(uidoc, doc, view);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            // 4. Phạm vi các Level theo phương ngang của view -> biết đặt Dim tổng về phía nào
            double tMin = double.MaxValue, tMax = double.MinValue;
            foreach (Level l in unique)
            {
                BoundingBoxXYZ bb = l.get_BoundingBox(view);
                if (bb == null) continue;
                double a = bb.Min.DotProduct(right);
                double b = bb.Max.DotProduct(right);
                tMin = Math.Min(tMin, Math.Min(a, b));
                tMax = Math.Max(tMax, Math.Max(a, b));
            }

            double sign = 1.0;
            if (tMin <= tMax)
                sign = pick.DotProduct(right) >= (tMin + tMax) / 2.0 ? 1.0 : -1.0;

            double zMin = unique.First().Elevation;
            double zMax = unique.Last().Elevation;
            double offset = TOTAL_OFFSET_MM / 304.8 * view.Scale; // feet
            XYZ shift = right * (sign * offset);

            XYZ a1 = new XYZ(pick.X, pick.Y, zMin);
            XYZ b1 = new XYZ(pick.X, pick.Y, zMax);

            // 5. Tạo Dim
            using (Transaction tx = new Transaction(doc, TITLE))
            {
                tx.Start();
                try
                {
                    // Dim từng nhịp
                    ReferenceArray allRefs = new ReferenceArray();
                    foreach (Level l in unique)
                        allRefs.Append(new Reference(l));
                    doc.Create.NewDimension(view, Line.CreateBound(a1, b1), allRefs);

                    // Dim tổng (chỉ cần khi có > 2 Level)
                    if (unique.Count > 2)
                    {
                        ReferenceArray endRefs = new ReferenceArray();
                        endRefs.Append(new Reference(unique.First()));
                        endRefs.Append(new Reference(unique.Last()));
                        doc.Create.NewDimension(view, Line.CreateBound(a1 + shift, b1 + shift), endRefs);
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

            return Result.Succeeded;
        }

        /// <summary>
        /// View mặt đứng / mặt cắt thường chưa có Work Plane nên PickPoint báo lỗi
        /// "No work plane set in current view". Hàm này đặt Work Plane tạm trùng mặt phẳng view,
        /// cho người dùng chọn điểm, rồi huỷ (RollBack) để không để lại thay đổi nào trong model.
        /// </summary>
        internal static XYZ PickPointOnView(UIDocument uidoc, Document doc, View view)
        {
            TransactionGroup tg = null;

            if (view.SketchPlane == null)
            {
                tg = new TransactionGroup(doc, "Work Plane tạm");
                tg.Start();

                using (Transaction t = new Transaction(doc, "Đặt Work Plane"))
                {
                    t.Start();
                    Plane plane = Plane.CreateByNormalAndOrigin(view.ViewDirection, view.Origin);
                    view.SketchPlane = SketchPlane.Create(doc, plane);
                    t.Commit();
                }
            }

            try
            {
                return uidoc.Selection.PickPoint("Chọn điểm đặt đường Dim (ESC để huỷ)");
            }
            finally
            {
                if (tg != null) tg.RollBack();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Tự động Dim lưới trục:
    ///  - Dim 1: từng nhịp (chuỗi Dim qua tất cả các trục song song)
    ///  - Dim 2: Dim tổng (trục đầu -> trục cuối), đặt xa hơn Dim 1
    /// Thứ tự thao tác: bấm nút -> click chọn 1 trục -> chọn điểm đặt đường Dim.
    /// Lệnh lấy tất cả trục song song với trục đã click trong view (để biết đang Dim đúng nhóm trục).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DimGridCommand : IExternalCommand
    {
        private const string TITLE = "Dim lưới trục";
        private const string PICK_PROMPT = "Click chọn 1 trục để Dim tất cả trục song song với trục đó trong view";
        private const double TOL_SPACING = 0.001;    // ft (~0.3 mm): coi 2 trục trùng nhau
        private const double TOL_PARALLEL = 1e-3;    // sin góc lệch cho phép khi gom trục song song
        private const double TOTAL_OFFSET_MM = 10.0; // khoảng cách giấy giữa Dim nhịp và Dim tổng

        private class GridInfo
        {
            public Grid Grid;
            public XYZ P0;
            public XYZ P1;
            public double S; // toạ độ theo phương pháp tuyến
        }

        private class GridGroup
        {
            public XYZ Dir;    // hướng của các trục
            public XYZ Normal; // phương vuông góc với trục (phương đo Dim)
            public List<GridInfo> Items = new List<GridInfo>();
            public double TMin, TMax; // phạm vi các trục dọc theo Dir
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // Mặt đứng / mặt cắt: xử lý riêng
            if (view is ViewSection)
                return ExecuteOnSection(uidoc, doc, (ViewSection)view, ref message);

            if (!(view is ViewPlan))
            {
                TaskDialog.Show(TITLE, "Hãy mở một view mặt bằng (Plan), mặt đứng (Elevation) hoặc mặt cắt (Section) rồi chạy lại lệnh.");
                return Result.Cancelled;
            }

            // 1. Click chọn 1 trục -> lấy tất cả trục song song với trục đó trong view
            Grid clicked;
            try
            {
                clicked = PickHelper.PickElement<Grid>(uidoc, PICK_PROMPT);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            if (clicked == null) return Result.Cancelled;

            List<Grid> grids = CollectParallelGrids(doc, view, clicked);

            if (grids.Count < 2)
            {
                TaskDialog.Show(TITLE, "Không tìm thấy ít nhất 2 trục song song với trục đã chọn trong view này.");
                return Result.Cancelled;
            }

            // 2. Gom các trục thẳng thành nhóm song song
            List<GridGroup> groups = new List<GridGroup>();
            foreach (Grid g in grids)
            {
                Line ln = g.Curve as Line;
                if (ln == null) continue; // bỏ qua trục cong

                XYZ p0 = Flat(ln.GetEndPoint(0));
                XYZ p1 = Flat(ln.GetEndPoint(1));
                XYZ d = p1 - p0;
                if (d.GetLength() < 1e-9) continue;
                d = d.Normalize();
                if (d.X < -1e-9 || (Math.Abs(d.X) <= 1e-9 && d.Y < 0)) d = d.Negate();

                GridGroup grp = groups.FirstOrDefault(
                    x => Math.Abs(x.Dir.X * d.Y - x.Dir.Y * d.X) < TOL_PARALLEL);
                if (grp == null)
                {
                    grp = new GridGroup { Dir = d, Normal = new XYZ(-d.Y, d.X, 0) };
                    groups.Add(grp);
                }
                grp.Items.Add(new GridInfo { Grid = g, P0 = p0, P1 = p1 });
            }

            // 3. Sắp xếp theo vị trí, loại trục trùng, tính phạm vi
            foreach (GridGroup grp in groups)
            {
                foreach (GridInfo gi in grp.Items)
                    gi.S = gi.P0.DotProduct(grp.Normal);

                List<GridInfo> sorted = grp.Items.OrderBy(i => i.S).ToList();
                List<GridInfo> unique = new List<GridInfo>();
                foreach (GridInfo gi in sorted)
                {
                    if (unique.Count == 0 || gi.S - unique[unique.Count - 1].S > TOL_SPACING)
                        unique.Add(gi);
                }
                grp.Items = unique;

                double tMin = double.MaxValue, tMax = double.MinValue;
                foreach (GridInfo gi in grp.Items)
                {
                    double a = gi.P0.DotProduct(grp.Dir);
                    double b = gi.P1.DotProduct(grp.Dir);
                    tMin = Math.Min(tMin, Math.Min(a, b));
                    tMax = Math.Max(tMax, Math.Max(a, b));
                }
                grp.TMin = tMin;
                grp.TMax = tMax;
            }

            groups = groups.Where(x => x.Items.Count >= 2).ToList();
            if (groups.Count == 0)
            {
                TaskDialog.Show(TITLE, "Không tìm thấy ít nhất 2 trục thẳng song song trong view này.");
                return Result.Cancelled;
            }

            // 4. Chọn điểm đặt đường Dim
            XYZ pick;
            try
            {
                pick = uidoc.Selection.PickPoint("Chọn điểm đặt đường Dim (ESC để huỷ)");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            XYZ pf = Flat(pick);

            // 5. Chọn nhóm trục: nhóm mà điểm chọn nằm "ngoài đầu trục" nhiều nhất
            GridGroup best = null;
            double bestScore = double.MinValue;
            foreach (GridGroup grp in groups)
            {
                double t = pf.DotProduct(grp.Dir);
                double score;
                if (t < grp.TMin) score = grp.TMin - t;
                else if (t > grp.TMax) score = t - grp.TMax;
                else score = -Math.Min(t - grp.TMin, grp.TMax - t);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = grp;
                }
            }

            // 6. Dựng đường Dim
            double sP = pf.DotProduct(best.Normal);
            XYZ basePt = new XYZ(pf.X - best.Normal.X * sP, pf.Y - best.Normal.Y * sP, pick.Z);
            double sMin = best.Items.First().S;
            double sMax = best.Items.Last().S;

            XYZ a1 = basePt + best.Normal * sMin;
            XYZ b1 = basePt + best.Normal * sMax;

            double tMid = (best.TMin + best.TMax) / 2.0;
            double sign = pf.DotProduct(best.Dir) >= tMid ? 1.0 : -1.0;
            double offset = TOTAL_OFFSET_MM / 304.8 * view.Scale; // feet
            XYZ shift = best.Dir * (sign * offset);

            using (Transaction tx = new Transaction(doc, TITLE))
            {
                tx.Start();
                try
                {
                    // Dim từng nhịp
                    ReferenceArray allRefs = new ReferenceArray();
                    foreach (GridInfo gi in best.Items)
                        allRefs.Append(new Reference(gi.Grid));
                    doc.Create.NewDimension(view, Line.CreateBound(a1, b1), allRefs);

                    // Dim tổng (chỉ cần khi có > 2 trục)
                    if (best.Items.Count > 2)
                    {
                        ReferenceArray endRefs = new ReferenceArray();
                        endRefs.Append(new Reference(best.Items.First().Grid));
                        endRefs.Append(new Reference(best.Items.Last().Grid));
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
        /// Dim lưới trục trên view mặt đứng / mặt cắt thẳng đứng:
        /// chỉ lấy các trục chạy theo hướng nhìn (hiện thành đường đứng trong view),
        /// Dim từng nhịp + Dim tổng theo phương ngang của view.
        /// </summary>
        private Result ExecuteOnSection(UIDocument uidoc, Document doc, ViewSection view, ref string message)
        {
            XYZ up = view.UpDirection;
            XYZ right = view.RightDirection;
            if (Math.Abs(up.Z) < 0.999)
            {
                TaskDialog.Show(TITLE, "View này không thẳng đứng nên không Dim trục được.");
                return Result.Cancelled;
            }

            // 1. Click chọn 1 trục -> lấy tất cả trục cùng loại trong view
            Grid clicked;
            try
            {
                clicked = PickHelper.PickElement<Grid>(uidoc, PICK_PROMPT);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            if (clicked == null) return Result.Cancelled;

            ElementId clickedId = clicked.Id;
            List<Grid> grids = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();

            // 2. Chỉ giữ trục thẳng, vuông góc với phương ngang của view (hiện thành đường đứng)
            List<GridInfo> items = new List<GridInfo>();
            foreach (Grid g in grids)
            {
                Line ln = g.Curve as Line;
                if (ln == null) continue;

                XYZ p0 = Flat(ln.GetEndPoint(0));
                XYZ p1 = Flat(ln.GetEndPoint(1));
                XYZ d = p1 - p0;
                if (d.GetLength() < 1e-9) continue;
                d = d.Normalize();

                if (Math.Abs(d.DotProduct(right)) > 0.01) continue; // trục song song/xiên với mặt view

                items.Add(new GridInfo { Grid = g, P0 = p0, P1 = p1, S = p0.DotProduct(right) });
            }

            // 3. Sắp xếp theo vị trí ngang, loại trục trùng
            items = items.OrderBy(i => i.S).ToList();
            List<GridInfo> unique = new List<GridInfo>();
            foreach (GridInfo gi in items)
            {
                if (unique.Count == 0 || gi.S - unique[unique.Count - 1].S > TOL_SPACING)
                    unique.Add(gi);
            }

            if (clickedId != null && !unique.Any(i => i.Grid.Id == clickedId))
            {
                TaskDialog.Show(TITLE, "Trục đã chọn không chạy vuông góc với view này nên không Dim được. Hãy chọn trục hiện thành đường đứng trong view.");
                return Result.Cancelled;
            }

            if (unique.Count < 2)
            {
                TaskDialog.Show(TITLE, "Không tìm thấy ít nhất 2 trục song song chạy vuông góc với view này.");
                return Result.Cancelled;
            }

            // 4. Chọn điểm đặt đường Dim (có xử lý Work Plane tạm)
            XYZ pick;
            try
            {
                pick = DimLevelCommand.PickPointOnView(uidoc, doc, view);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            // 5. Phạm vi đứng của các trục -> đặt Dim tổng về phía trên hoặc dưới
            double zLo = double.MaxValue, zHi = double.MinValue;
            foreach (GridInfo gi in unique)
            {
                BoundingBoxXYZ bb = gi.Grid.get_BoundingBox(view);
                if (bb == null) continue;
                double a = bb.Min.DotProduct(up);
                double b = bb.Max.DotProduct(up);
                zLo = Math.Min(zLo, Math.Min(a, b));
                zHi = Math.Max(zHi, Math.Max(a, b));
            }

            double sign = 1.0;
            if (zLo <= zHi)
                sign = pick.DotProduct(up) >= (zLo + zHi) / 2.0 ? 1.0 : -1.0;

            double sP = pick.DotProduct(right);
            double sMin = unique.First().S;
            double sMax = unique.Last().S;

            XYZ a1 = pick + right * (sMin - sP);
            XYZ b1 = pick + right * (sMax - sP);

            double offset = TOTAL_OFFSET_MM / 304.8 * view.Scale; // feet
            XYZ shift = up * (sign * offset);

            // 6. Tạo Dim
            using (Transaction tx = new Transaction(doc, TITLE))
            {
                tx.Start();
                try
                {
                    ReferenceArray allRefs = new ReferenceArray();
                    foreach (GridInfo gi in unique)
                        allRefs.Append(new Reference(gi.Grid));
                    doc.Create.NewDimension(view, Line.CreateBound(a1, b1), allRefs);

                    if (unique.Count > 2)
                    {
                        ReferenceArray endRefs = new ReferenceArray();
                        endRefs.Append(new Reference(unique.First().Grid));
                        endRefs.Append(new Reference(unique.Last().Grid));
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

        /// <summary>Tất cả trục thẳng trong view song song với trục được chọn (gồm cả trục đó).</summary>
        private static List<Grid> CollectParallelGrids(Document doc, View view, Grid reference)
        {
            List<Grid> result = new List<Grid>();

            Line refLine = reference.Curve as Line;
            if (refLine == null) return result;

            XYZ rd = Flat(refLine.GetEndPoint(1) - refLine.GetEndPoint(0));
            if (rd.GetLength() < 1e-9) return result;
            rd = rd.Normalize();

            foreach (Grid g in new FilteredElementCollector(doc, view.Id).OfClass(typeof(Grid)).Cast<Grid>())
            {
                Line ln = g.Curve as Line;
                if (ln == null) continue;

                XYZ d = Flat(ln.GetEndPoint(1) - ln.GetEndPoint(0));
                if (d.GetLength() < 1e-9) continue;
                d = d.Normalize();

                if (Math.Abs(rd.X * d.Y - rd.Y * d.X) < TOL_PARALLEL)
                    result.Add(g);
            }
            return result;
        }

        private static XYZ Flat(XYZ p)
        {
            return new XYZ(p.X, p.Y, 0);
        }
    }
}

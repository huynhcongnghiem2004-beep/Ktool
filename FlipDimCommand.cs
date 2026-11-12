using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Đảo chiều đường Dim: lật các đường Dim đã có sang phía đối diện
    /// (đối xứng qua tâm của các đối tượng được Dim: trục, Level, ...).
    /// Chọn sẵn các Dim trước khi chạy lệnh, hoặc chạy lệnh rồi quét chọn Dim.
    /// Chọn cả Dim nhịp và Dim tổng cùng lúc thì thứ tự trong/ngoài được giữ nguyên.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FlipDimCommand : IExternalCommand
    {
        private const string TITLE = "Đảo chiều Dim";

        internal class DimensionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Dimension;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // 1. Lấy Dim: ưu tiên Dim đang chọn, nếu không thì cho người dùng chọn
            List<Dimension> dims = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Dimension>()
                .Where(d => d.View != null && d.View.Id == view.Id)
                .ToList();

            if (dims.Count == 0)
            {
                try
                {
                    IList<Reference> picked = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new DimensionFilter(),
                        "Chọn các đường Dim cần đảo chiều, rồi bấm Finish");

                    dims = picked
                        .Select(r => doc.GetElement(r))
                        .OfType<Dimension>()
                        .Where(d => d.View != null && d.View.Id == view.Id)
                        .ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (dims.Count == 0)
            {
                TaskDialog.Show(TITLE, "Chưa chọn đường Dim nào trong view hiện tại.");
                return Result.Cancelled;
            }

            // 2. Đảo chiều
            int done = 0, skipped = 0;
            using (Transaction tx = new Transaction(doc, TITLE))
            {
                tx.Start();
                try
                {
                    foreach (Dimension dim in dims)
                    {
                        if (TryFlip(doc, view, dim)) done++;
                        else skipped++;
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

            if (skipped > 0)
            {
                TaskDialog.Show(TITLE,
                    "Đã đảo " + done + " Dim. Bỏ qua " + skipped +
                    " Dim (không phải Dim thẳng hoặc không xác định được đối tượng được Dim).");
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// Lấy đối xứng đường Dim qua tâm của các đối tượng được Dim,
        /// theo phương vuông góc với đường Dim (nằm trong mặt phẳng view).
        /// </summary>
        private static bool TryFlip(Document doc, View view, Dimension dim)
        {
            try
            {
                Line line = dim.Curve as Line;
                if (line == null) return false;

                XYZ u = line.Direction;                          // phương đo của Dim
                XYZ v = view.ViewDirection.CrossProduct(u);      // phương vuông góc, trong mặt phẳng view
                if (v.GetLength() < 1e-9) return false;
                v = v.Normalize();

                double lo = double.MaxValue, hi = double.MinValue;
                foreach (Reference r in dim.References)
                {
                    Element e = doc.GetElement(r);
                    if (e == null) continue;

                    BoundingBoxXYZ bb = e.get_BoundingBox(view);
                    if (bb == null) continue;

                    foreach (XYZ c in Corners(bb))
                    {
                        double s = c.DotProduct(v);
                        if (s < lo) lo = s;
                        if (s > hi) hi = s;
                    }
                }
                if (lo > hi) return false;

                double sLine = line.GetEndPoint(0).DotProduct(v);
                double shift = (lo + hi) - 2.0 * sLine; // = 2 * (tâm - vị trí Dim)
                if (Math.Abs(shift) < 1e-6) return false;

                ElementTransformUtils.MoveElement(doc, dim.Id, v * shift);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IEnumerable<XYZ> Corners(BoundingBoxXYZ bb)
        {
            Transform t = bb.Transform;
            double[] xs = { bb.Min.X, bb.Max.X };
            double[] ys = { bb.Min.Y, bb.Max.Y };
            double[] zs = { bb.Min.Z, bb.Max.Z };
            foreach (double x in xs)
                foreach (double y in ys)
                    foreach (double z in zs)
                        yield return t.OfPoint(new XYZ(x, y, z));
        }
    }
}

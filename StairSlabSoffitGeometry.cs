using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
namespace SDK_HD.Dim
{
    // Footprint measurements only. Turning treads are rebuilt as native runs
    // and horizontal landings by StairFlatLandingPlan.
    internal static class StairSlabSoffitGeometry
    {
        private const double Mm = 1.0 / 304.8;
        private sealed class Flight { public XYZ Start, Direction; public double Length; }
        private static XYZ Flat(XYZ p) { return new XYZ(p.X,p.Y,0); }
        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        internal static double PathWidth(StairsRun run)
        {
            var boundary = run.GetFootprintBoundary().ToList();
            var paths = run.GetStairsPath().ToList();
            if (boundary.Any(c => !(c is Line)) || paths.Any(c => !(c is Line)))
                throw new InvalidOperationException("Chưa hỗ trợ biên hoặc đường đi cong.");
            var widths = paths.Select(c => Width(boundary, new Flight
            {
                Start = Flat(c.GetEndPoint(0)),
                Direction = (Flat(c.GetEndPoint(1)) - Flat(c.GetEndPoint(0))).Normalize(),
                Length = Flat(c.GetEndPoint(0)).DistanceTo(Flat(c.GetEndPoint(1)))
            })).ToList();
            if (widths.Count == 0 || widths.Min() <= 0 || widths.Max() - widths.Min() > Mm)
                throw new InvalidOperationException("Cần các vế có cùng bề rộng.");
            return widths[0];
        }
        private static double Width(IList<Curve> boundary, Flight f)
        {
            var widths = new List<double>();
            // A short path segment's midpoint may still be inside the corner
            // square. Infer its width from centered sections along the segment.
            foreach (double fraction in new[] { 0.05, 0.1, 0.25, 0.5, 0.75, 0.9, 0.95 })
            {
                XYZ mid = f.Start + f.Direction * (f.Length * fraction), across = XYZ.BasisZ.CrossProduct(f.Direction);
                var intersections = new List<double>();
                foreach (var edge in boundary)
                {
                    XYZ a = Flat(edge.GetEndPoint(0)), b = Flat(edge.GetEndPoint(1));
                    double da = (a - mid).DotProduct(f.Direction), db = (b - mid).DotProduct(f.Direction);
                    if (Math.Abs(db - da) < 1e-9 || da * db > 1e-8) continue;
                    var point = a + (b - a) * (-da / (db - da)); intersections.Add((point - mid).DotProduct(across));
                }
                double left = intersections.Where(t => t < -1e-6).DefaultIfEmpty(double.NaN).Max();
                double right = intersections.Where(t => t > 1e-6).DefaultIfEmpty(double.NaN).Min();
                if (Finite(left) && Finite(right) && Math.Abs(left + right) <= 5 * Mm && right - left > 100 * Mm)
                    widths.Add(right - left);
            }
            if (widths.Count == 0) throw new InvalidOperationException("Không xác định được bề rộng vế thẳng từ đường đi Stair.");
            return widths.Min();
        }
    }
}

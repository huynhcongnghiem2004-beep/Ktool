using System;
using System.Collections.Generic;
using System.Linq;

namespace SDK_HD.Dim
{
    internal sealed class SheetPlacementBox
    {
        internal double X0, Y0, X1, Y1;
        internal double Width => X1 - X0;
        internal double Height => Y1 - Y0;
        internal bool Overlap(SheetPlacementBox b, double gap) => X0 < b.X1 + gap && X1 + gap > b.X0 && Y0 < b.Y1 + gap && Y1 + gap > b.Y0;
    }
    internal static class SheetPlacementLayout
    {
        internal static SheetPlacementBox FindPlace(SheetPlacementBox paper, List<SheetPlacementBox> occupied, double width, double height, double gap)
        {
            if (width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height) || double.IsInfinity(width) || double.IsInfinity(height)) return null;
            if (paper == null) { double top = occupied.Count == 0 ? 0 : occupied.Min(b => b.Y0) - gap; return new SheetPlacementBox { X0 = 0, X1 = width, Y0 = top - height, Y1 = top }; }
            var xs = new[] { paper.X0 }.Concat(occupied.Select(b => b.X1 + gap)).Distinct().OrderBy(x => x);
            var ys = new[] { paper.Y1 - height }.Concat(occupied.Select(b => b.Y0 - gap - height)).Distinct().OrderByDescending(y => y);
            foreach (double y in ys) foreach (double x in xs)
            {
                var candidate = new SheetPlacementBox { X0 = x, Y0 = y, X1 = x + width, Y1 = y + height };
                if (candidate.X0 < paper.X0 - 1e-6 || candidate.Y0 < paper.Y0 - 1e-6 || candidate.X1 > paper.X1 + 1e-6 || candidate.Y1 > paper.Y1 + 1e-6) continue;
                if (!occupied.Any(b => candidate.Overlap(b, gap - 1e-6))) return candidate;
            }
            return null;
        }
    }
}

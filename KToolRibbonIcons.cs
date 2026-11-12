using System;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    internal enum KToolIcon
    {
        DimensionGrid, DimensionLevel, FlipDimension, WitnessLine, CropFit, CropOffset,
        AlignTitle, ViewBrowser, CreateSheet, PlaceView, ExportDwg, StairDeploy, CropRegion,
        WcCreate, WcUpdate, MultiLevelPreset, WcBatch, AutoTag, JoinGeometry, StairSlab, FlatLanding,
        WireStyle, DrawWire, WireSize, WireLabel, WireTag, PipeLabel, PipeTag,
        PipeStyle, DrawPipe, PipeType, DetailLine, Paint,
        FinishMenu, FinishFloor, FinishCeiling, FinishWall, FinishThreshold, FinishTile, Lintel, Plaster,
        DoorDetail, FileCleanup, PipeCW, PipeC, PipeHW, PipeWP, PipeRP, PipeVP, PipeSP, PipeDP
    }

    /// <summary>One consistent, inverse navy-and-coffee vector icon family for the KTool ribbon.</summary>
    internal static class KToolRibbonIcons
    {
        private static readonly Brush Navy = Frozen("#17334F");
        private static readonly Brush Coffee = Frozen("#8B6248");
        private static readonly Brush Ivory = Frozen("#F5F0E8");
        private static readonly Brush CoffeeLight = Frozen("#C7A58D");

        internal static void Set(PushButtonData item, KToolIcon icon)
        {
            item.Image = Create(icon, 16); item.LargeImage = Create(icon, 32);
        }
        internal static void Set(PulldownButtonData item, KToolIcon icon)
        {
            item.Image = Create(icon, 16); item.LargeImage = Create(icon, 32);
        }
        internal static ImageSource Create(KToolIcon icon, int size)
        {
            var group = new DrawingGroup();
            using (DrawingContext dc = group.Open())
            {
                dc.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
                bool inverse = ((int)icon % 4) == 1;
                Brush back = inverse ? Coffee : Navy;
                Brush ink = Ivory;
                Brush accent = inverse ? Navy : CoffeeLight;
                var pen = new Pen(ink, 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                var accentPen = new Pen(accent, 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                pen.Freeze(); accentPen.Freeze();
                dc.DrawRoundedRectangle(back, null, new Rect(1, 1, 30, 30), 5, 5);
                DrawGlyph(dc, icon, pen, accentPen, ink, accent);
                dc.Pop();
            }
            group.Freeze();
            var image = new DrawingImage(group); image.Freeze(); return image;
        }

        private static void DrawGlyph(DrawingContext dc, KToolIcon icon, Pen p, Pen a, Brush ink, Brush accent)
        {
            switch (icon)
            {
                case KToolIcon.DimensionGrid:
                    Path(dc, p, "M6,7 L6,25 M26,7 L26,25 M4,10 L28,10 M4,22 L28,22 M8,16 L24,16 M8,16 L11,13 M8,16 L11,19 M24,16 L21,13 M24,16 L21,19");
                    break;
                case KToolIcon.DimensionLevel:
                    Path(dc, p, "M5,22 L27,22 M8,7 L8,22 M8,8 L13,5 L18,8 M15,14 L25,14 M22,11 L25,14 L22,17");
                    Circle(dc, a, 8, 8, 3);
                    break;
                case KToolIcon.FlipDimension:
                    Path(dc, p, "M6,10 L26,10 M6,10 L9,7 M6,10 L9,13 M26,10 L23,7 M26,10 L23,13 M26,22 L6,22 M26,22 L23,19 M26,22 L23,25 M6,22 L9,19 M6,22 L9,25");
                    break;
                case KToolIcon.WitnessLine:
                    Path(dc, p, "M7,7 L7,24 M25,7 L25,24 M7,12 L25,12 M7,12 L10,9 M7,12 L10,15 M25,12 L22,9 M25,12 L22,15");
                    Circle(dc, a, 7, 25, 2); Circle(dc, a, 25, 25, 2);
                    break;
                case KToolIcon.CropFit:
                    Path(dc, p, "M5,12 L5,5 L12,5 M20,5 L27,5 L27,12 M27,20 L27,27 L20,27 M12,27 L5,27 L5,20 M10,10 L22,10 L22,22 L10,22 Z");
                    break;
                case KToolIcon.CropOffset:
                    Path(dc, p, "M5,10 L5,5 L10,5 M19,5 L24,5 L24,10 M24,19 L24,24 L19,24 M10,24 L5,24 L5,19 M9,10 L19,10 L19,20 L9,20 Z");
                    Path(dc, a, "M18,18 L27,27 M23,27 L27,27 L27,23");
                    break;
                case KToolIcon.AlignTitle:
                    Path(dc, p, "M6,7 L26,7 L26,19 L6,19 Z M4,24 L28,24 M16,4 L16,27 M13,21 L16,24 L19,21");
                    break;
                case KToolIcon.ViewBrowser:
                    Path(dc, p, "M7,5 L21,5 L26,10 L26,26 L7,26 Z M21,5 L21,10 L26,10 M10,14 L23,14 M10,18 L23,18 M10,22 L19,22");
                    break;
                case KToolIcon.CreateSheet:
                    Path(dc, p, "M7,5 L21,5 L26,10 L26,26 L7,26 Z M21,5 L21,10 L26,10 M12,17 L21,17 M16.5,12.5 L16.5,21.5");
                    break;
                case KToolIcon.PlaceView:
                    Path(dc, p, "M5,7 L20,7 L20,23 L5,23 Z M9,11 L16,11 M9,15 L16,15 M9,19 L14,19 M20,15 L27,15 M24,12 L27,15 L24,18");
                    break;
                case KToolIcon.ExportDwg:
                    Path(dc, p, "M6,5 L20,5 L25,10 L25,23 L6,23 Z M20,5 L20,10 L25,10 M14,25 L27,25 M23,21 L27,25 L23,29");
                    break;
                case KToolIcon.StairDeploy:
                    Path(dc, p, "M5,25 L5,20 L10,20 L10,16 L15,16 L15,12 L20,12 L20,8 L27,8 M5,27 L27,27");
                    Path(dc, a, "M8,23 L11,20 L14,17 L17,14 L20,11 L23,8");
                    break;
                case KToolIcon.CropRegion:
                    Path(dc, p, "M6,12 L6,6 L12,6 M20,6 L26,6 L26,12 M26,20 L26,26 L20,26 M12,26 L6,26 L6,20 M11,11 L21,11 L21,21 L11,21 Z");
                    Path(dc, a, "M22,23 L27,28 M22,28 L27,23");
                    break;
                case KToolIcon.WcCreate:
                    Path(dc, p, "M6,7 L26,7 L26,25 L6,25 Z M6,17 L14,17 L14,25 M18,12 L23,12 M20.5,10 L20.5,14");
                    Circle(dc, a, 10, 12, 2.5); Path(dc, a, "M17,20 L23,20");
                    break;
                case KToolIcon.WcUpdate:
                    Path(dc, p, "M7,16 A9,9 0 0 1 22,9 M22,9 L22,5 M22,9 L18,9 M25,16 A9,9 0 0 1 10,23 M10,23 L10,27 M10,23 L14,23");
                    Path(dc, a, "M10,16 L14,16 L14,19 M14,16 L18,16");
                    break;
                case KToolIcon.MultiLevelPreset:
                    Path(dc, p, "M6,7 L17,7 L17,12 L6,12 Z M10,14 L21,14 L21,19 L10,19 Z M14,21 L26,21 L26,26 L14,26 Z");
                    Path(dc, a, "M8,9 L15,9 M12,16 L19,16 M16,23 L24,23");
                    break;
                case KToolIcon.WcBatch:
                    Path(dc, p, "M5,6 L17,6 L17,17 L5,17 Z M20,6 L27,6 L27,17 L20,17 Z M5,20 L17,20 L17,27 L5,27 Z M20,20 L27,20 L27,27 L20,27 Z");
                    Path(dc, a, "M8,11 L10,13 L14,9 M22,10 L25,10 M8,23 L10,25 L14,21 M22,24 L25,24");
                    break;
                case KToolIcon.AutoTag:
                    Path(dc, p, "M5,11 L17,5 L27,15 L17,25 L5,19 Z M12,13 L20,13 M12,17 L18,17");
                    Circle(dc, a, 9, 15, 1.5);
                    break;
                case KToolIcon.JoinGeometry:
                    Path(dc, p, "M5,7 L18,7 L18,18 L5,18 Z M14,14 L27,14 L27,25 L14,25 Z");
                    Path(dc, a, "M14,14 L18,14 L18,18");
                    break;
                case KToolIcon.StairSlab:
                    Path(dc, p, "M5,24 L5,19 L10,19 L10,15 L15,15 L15,11 L20,11 L20,7 L27,7 M5,27 L27,27");
                    Path(dc, a, "M7,24 L25,8");
                    break;
                case KToolIcon.FlatLanding:
                    Path(dc, p, "M5,25 L5,20 L10,20 L10,16 L15,16 L15,12 L20,12 L20,8 L27,8 M5,27 L27,27 M20,8 L20,17 L27,17");
                    break;
                case KToolIcon.WireStyle:
                case KToolIcon.PipeStyle:
                    Path(dc, p, "M5,8 L27,8 M5,16 L27,16 M5,24 L27,24");
                    Path(dc, a, icon == KToolIcon.WireStyle ? "M8,8 L12,8 M8,16 L12,16 M8,24 L12,24" : "M8,5 L8,11 M8,13 L8,19 M8,21 L8,27");
                    break;
                case KToolIcon.DrawWire:
                    Path(dc, p, "M5,24 L12,24 L12,14 L21,14 L21,7 M18,9 L21,6 L24,9");
                    Path(dc, a, "M15,24 L18,21");
                    break;
                case KToolIcon.WireSize:
                    Circle(dc, p, 16, 16, 9); Circle(dc, a, 16, 16, 4); Path(dc, p, "M16,3 L16,7 M16,25 L16,29 M3,16 L7,16 M25,16 L29,16");
                    break;
                case KToolIcon.WireLabel:
                case KToolIcon.PipeLabel:
                    Path(dc, p, "M5,6 L27,6 L27,21 L15,21 L9,26 L9,21 L5,21 Z M10,11 L22,11 M10,16 L19,16");
                    Path(dc, a, "M8,26 L12,22");
                    break;
                case KToolIcon.WireTag:
                case KToolIcon.PipeTag:
                    Path(dc, p, "M5,8 L19,8 L27,16 L19,24 L5,24 Z M11,12 L11,20 M15,12 L15,20");
                    Circle(dc, a, 8, 16, 1.5);
                    break;
                case KToolIcon.PipeType:
                    Path(dc, p, "M5,9 L27,9 M5,16 L27,16 M5,23 L27,23");
                    Circle(dc, a, 7, 9, 2); Circle(dc, a, 7, 16, 2); Circle(dc, a, 7, 23, 2);
                    break;
                case KToolIcon.DrawPipe:
                    Path(dc, p, "M6,7 L6,17 Q6,21 10,21 L22,21 Q26,21 26,17 L26,11 M22,11 L26,7 L29,11");
                    Path(dc, a, "M4,7 L8,7 M24,5 L28,5");
                    break;
                case KToolIcon.DetailLine:
                    Path(dc, p, "M5,8 L27,8 M5,16 L27,16 M5,24 L27,24");
                    Path(dc, a, "M5,16 L9,16 M13,16 L17,16 M21,16 L25,16");
                    break;
                case KToolIcon.Paint:
                    Path(dc, p, "M7,6 L24,6 L24,14 L7,14 Z M12,14 L12,20 L20,20 L20,25 M20,25 L20,28");
                    Path(dc, a, "M9,9 L22,9 M6,22 L13,22 L13,27 L6,27 Z");
                    break;
                case KToolIcon.FinishMenu:
                    Path(dc, p, "M5,10 L27,10 L25,26 L7,26 Z M8,7 L24,7 M11,14 L11,22 M16,14 L16,22 M21,14 L21,22");
                    Path(dc, a, "M6,10 L9,7");
                    break;
                case KToolIcon.FinishFloor:
                case KToolIcon.FinishTile:
                    Path(dc, p, "M5,6 L27,6 L27,26 L5,26 Z M5,13 L27,13 M5,20 L27,20 M12,6 L12,13 M20,13 L20,20 M12,20 L12,26");
                    break;
                case KToolIcon.FinishCeiling:
                    Path(dc, p, "M5,8 L27,8 M8,8 L8,16 M24,8 L24,16 M11,16 L21,16 M16,16 L16,22");
                    Circle(dc, a, 16, 25, 3);
                    break;
                case KToolIcon.FinishWall:
                case KToolIcon.Plaster:
                    Path(dc, p, "M7,5 L25,5 L25,27 L7,27 Z M7,11 L25,11 M7,17 L25,17 M7,23 L25,23");
                    Path(dc, a, icon == KToolIcon.Plaster ? "M20,20 L27,13 L29,15 L22,22 Z" : "M11,7 L11,9 M17,13 L17,15 M11,19 L11,21");
                    break;
                case KToolIcon.FinishThreshold:
                    Path(dc, p, "M5,10 L27,10 M5,21 L27,21 M8,12 L8,19 M24,12 L24,19 M8,19 L24,19");
                    Path(dc, a, "M12,16 L20,16");
                    break;
                case KToolIcon.Lintel:
                    Path(dc, p, "M5,7 L27,7 L27,12 L5,12 Z M8,12 L8,27 M24,12 L24,27 M8,22 L24,22");
                    Path(dc, a, "M12,9 L20,9");
                    break;
                case KToolIcon.DoorDetail:
                    Path(dc, p, "M7,27 L7,5 L23,5 L23,27 M10,24 L20,24 L20,9 M10,24 A10,10 0 0 1 20,14 M17,20 L19,20");
                    Circle(dc, a, 18, 20, 1.5);
                    break;
                case KToolIcon.FileCleanup:
                    Path(dc, p, "M7,5 L20,5 L25,10 L25,21 L7,21 Z M20,5 L20,10 L25,10 M10,13 L18,13 M10,17 L17,17");
                    Path(dc, a, "M19,18 L27,18 L26,27 L20,27 Z M18,16 L28,16 M21,20 L21,25 M25,20 L25,25");
                    break;
                default:
                    Path(dc, p, "M6,16 L26,16 M16,6 L16,26");
                    break;
            }
            if (icon >= KToolIcon.PipeCW && icon <= KToolIcon.PipeDP) DrawPipeCode(dc, icon, p, a, ink, accent);
        }

        private static void DrawPipeCode(DrawingContext dc, KToolIcon icon, Pen p, Pen a, Brush ink, Brush accent)
        {
            // Each system has a distinct simplified symbol; labels stay out of the 16 px ribbon icon.
            int index = (int)icon - (int)KToolIcon.PipeCW;
            Path(dc, p, "M7,21 L7,12 Q7,8 11,8 L25,8 M7,21 L25,21");
            for (int i = 0; i <= index; i++) dc.DrawEllipse(accent, null, new Point(10 + (i % 4) * 4, 15 + (i / 4) * 4), 1.1, 1.1);
            Path(dc, a, index % 2 == 0 ? "M22,5 L27,5 L27,10" : "M22,24 L27,24 L27,19");
        }

        private static void Path(DrawingContext dc, Pen pen, string data)
        {
            Geometry geometry = Geometry.Parse(data); geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
        }
        private static void Circle(DrawingContext dc, Pen pen, double x, double y, double r) => dc.DrawEllipse(null, pen, new Point(x, y), r, r);
        private static Brush Frozen(string value)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value);
            brush.Freeze(); return brush;
        }
    }
}

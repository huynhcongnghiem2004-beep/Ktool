using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingStringFormat = System.Drawing.StringFormat;

namespace SDK_HD.Dim
{
    public static class WaterPanel
    {
        private const string PANEL_NAME = "Nuoc";

        public static void Register(
            UIControlledApplication application,
            string tabName,
            string dllPath)
        {
            RibbonPanel panel = application.GetRibbonPanels(tabName)
                .FirstOrDefault(p => p.Name == PANEL_NAME);

            if (panel == null)
                panel = application.CreateRibbonPanel(tabName, PANEL_NAME);

            PushButtonData styleButton = new PushButtonData(
                "SDK_HD_CreateWaterPipeStyles",
                "T\u1EA1o Style" + Environment.NewLine + "\u1ED0ng n\u01B0\u1EDBc",
                dllPath,
                typeof(CreateWaterPipeStylesCommand).FullName);

            styleButton.ToolTip =
                "T\u1EA1o Style cho c\u00E1c lo\u1EA1i \u1ED1ng n\u01B0\u1EDBc.";

            styleButton.LongDescription =
                "T\u1EA1o ho\u1EB7c c\u1EADp nh\u1EADt Line Style cho CW, C, HW, WP, RP, VP, SP v\u00E0 DP.";
            styleButton.LargeImage = CreateIcon("W", 32, 0, 102, 204);
            styleButton.Image = CreateIcon("W", 16, 0, 102, 204);

            panel.AddItem(styleButton);

            AddButton(
                panel,
                dllPath,
                "CW",
                "Cap nuoc lanh",
                typeof(DrawCwPipeCommand),
                0,
                102,
                204);

            AddButton(
                panel,
                dllPath,
                "C",
                "Cap nuoc thuy cuc",
                typeof(DrawCPipeCommand),
                0,
                153,
                255);

            AddButton(
                panel,
                dllPath,
                "HW",
                "Cap nuoc nong",
                typeof(DrawHwPipeCommand),
                255,
                0,
                0);

            AddButton(
                panel,
                dllPath,
                "WP",
                "Thoat nuoc thai",
                typeof(DrawWpPipeCommand),
                128,
                64,
                0);

            AddButton(
                panel,
                dllPath,
                "RP",
                "Thoat nuoc mua",
                typeof(DrawRpPipeCommand),
                0,
                153,
                153);

            AddButton(
                panel,
                dllPath,
                "VP",
                "Thong hoi",
                typeof(DrawVpPipeCommand),
                0,
                153,
                0);

            AddButton(
                panel,
                dllPath,
                "SP",
                "Thoat phan",
                typeof(DrawSpPipeCommand),
                64,
                64,
                64);

            AddButton(
                panel,
                dllPath,
                "DP",
                "Ong may lanh",
                typeof(DrawDpPipeCommand),
                153,
                0,
                204);
        }

        private static void AddButton(
            RibbonPanel panel,
            string dllPath,
            string code,
            string name,
            Type commandType,
            byte r,
            byte g,
            byte b)
        {
            PushButtonData button = new PushButtonData(
                "SDK_HD_DrawWater_" + code,
                code + Environment.NewLine + name,
                dllPath,
                commandType.FullName);

            button.ToolTip = "Ve duong ong " + code + " - " + name + ".";
            button.LongDescription =
                "Ve Detail Line cho he thong " + code +
                " bang cach chon lien tiep cac diem trong view.";

            button.LargeImage = CreateIcon(code, 32, r, g, b);
            button.Image = CreateIcon(code, 16, r, g, b);

            panel.AddItem(button);
        }

        private static BitmapImage CreateIcon(
            string text,
            int size,
            byte r,
            byte g,
            byte b)
        {
            using (DrawingBitmap bitmap =
                   new DrawingBitmap(
                       size,
                       size,
                       PixelFormat.Format32bppArgb))
            using (DrawingGraphics graphics =
                   DrawingGraphics.FromImage(bitmap))
            using (SolidBrush backgroundBrush =
                   new SolidBrush(DrawingColor.FromArgb(235, r, g, b)))
            using (SolidBrush textBrush =
                   new SolidBrush(DrawingColor.White))
            using (DrawingFont font =
                   new DrawingFont(
                       "Arial",
                       size >= 32 ? 9.0f : 5.0f,
                       DrawingFontStyle.Bold))
            using (DrawingStringFormat format =
                   new DrawingStringFormat())
            using (MemoryStream stream = new MemoryStream())
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(DrawingColor.Transparent);

                graphics.FillRectangle(
                    backgroundBrush,
                    0,
                    0,
                    size - 1,
                    size - 1);

                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                graphics.DrawString(
                    text,
                    font,
                    textBrush,
                    new RectangleF(0, 0, size, size),
                    format);

                bitmap.Save(stream, ImageFormat.Png);
                stream.Position = 0;

                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();

                return image;
            }
        }

        private static GraphicsStyle EnsureGraphicsStyle(
    Document doc,
    WaterPipeDefinition pipe)
        {
            Category linesCategory =
                doc.Settings.Categories.get_Item(
                    BuiltInCategory.OST_Lines);

            Category subCategory =
                linesCategory.SubCategories
                    .Cast<Category>()
                    .FirstOrDefault(
                        x => x.Name.Equals(
                            pipe.StyleName,
                            StringComparison.OrdinalIgnoreCase));

            if (subCategory == null)
            {
                throw new InvalidOperationException(
                    "Không tìm thấy Line Style: " +
                    pipe.StyleName);
            }

            GraphicsStyle style =
                subCategory.GetGraphicsStyle(
                    GraphicsStyleType.Projection);

            if (style == null)
            {
                throw new InvalidOperationException(
                    "Không lấy được GraphicsStyle: " +
                    pipe.StyleName);
            }

            return style;
        }
    }

    public sealed class WaterPipeDefinition
    {
        public string Code { get; private set; }
        public string Name { get; private set; }
        public int LineWeight { get; private set; }
        public byte R { get; private set; }
        public byte G { get; private set; }
        public byte B { get; private set; }

        public string StyleName
        {
            get { return "KTool - \u1ED0ng " + Code; }
        }

        public WaterPipeDefinition(
            string code,
            string name,
            int lineWeight,
            byte r,
            byte g,
            byte b)
        {
            Code = code;
            Name = name;
            LineWeight = lineWeight;
            R = r;
            G = g;
            B = b;
        }
    }

    public static class WaterPipeDefinitions
    {
        public static readonly List<WaterPipeDefinition> Items =
            new List<WaterPipeDefinition>
            {
                new WaterPipeDefinition(
                    "CW",
                    "C\u1EA5p n\u01B0\u1EDBc l\u1EA1nh",
                    3,
                    0,
                    102,
                    204),

                new WaterPipeDefinition(
                    "C",
                    "C\u1EA5p n\u01B0\u1EDBc th\u1EE7y c\u1EE5c",
                    3,
                    0,
                    153,
                    255),

                new WaterPipeDefinition(
                    "HW",
                    "C\u1EA5p n\u01B0\u1EDBc n\u00F3ng",
                    4,
                    255,
                    0,
                    0),

                new WaterPipeDefinition(
                    "WP",
                    "Tho\u00E1t n\u01B0\u1EDBc th\u1EA3i",
                    4,
                    128,
                    64,
                    0),

                new WaterPipeDefinition(
                    "RP",
                    "Tho\u00E1t n\u01B0\u1EDBc m\u01B0a",
                    3,
                    0,
                    153,
                    153),

                new WaterPipeDefinition(
                    "VP",
                    "Th\u00F4ng h\u01A1i",
                    2,
                    0,
                    153,
                    0),

                new WaterPipeDefinition(
                    "SP",
                    "Tho\u00E1t ph\u00E2n",
                    5,
                    64,
                    64,
                    64),

                new WaterPipeDefinition(
                    "DP",
                    "\u1ED0ng m\u00E1y l\u1EA1nh",
                    3,
                    153,
                    0,
                    204)
            };

        public static WaterPipeDefinition Get(string code)
        {
            WaterPipeDefinition definition = Items.FirstOrDefault(
                item => string.Equals(
                    item.Code,
                    code,
                    StringComparison.OrdinalIgnoreCase));

            if (definition == null)
                throw new InvalidOperationException(
                    "Khong tim thay loai ong: " + code);

            return definition;
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CreateWaterPipeStylesCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            Document doc =
                commandData.Application.ActiveUIDocument.Document;

            try
            {
                Categories categories = doc.Settings.Categories;
                Category linesCategory =
                    categories.get_Item(BuiltInCategory.OST_Lines);

                ElementId solidPatternId =
                    LinePatternElement.GetSolidPatternId();

                int created = 0;
                int updated = 0;

                using (Transaction transaction =
                       new Transaction(
                           doc,
                           "KTool - Tao Style ong nuoc"))
                {
                    transaction.Start();

                    foreach (WaterPipeDefinition pipe
                             in WaterPipeDefinitions.Items)
                    {
                        Category subCategory;

                        if (linesCategory.SubCategories.Contains(
                                pipe.StyleName))
                        {
                            subCategory =
                                linesCategory.SubCategories.get_Item(
                                    pipe.StyleName);

                            updated++;
                        }
                        else
                        {
                            subCategory =
                                categories.NewSubcategory(
                                    linesCategory,
                                    pipe.StyleName);

                            created++;
                        }

                        subCategory.SetLineWeight(
                            pipe.LineWeight,
                            GraphicsStyleType.Projection);

                        subCategory.LineColor =
                            new Autodesk.Revit.DB.Color(pipe.R, pipe.G, pipe.B);

                        subCategory.SetLinePatternId(
                            solidPatternId,
                            GraphicsStyleType.Projection);
                    }

                    transaction.Commit();
                }

                TaskDialog.Show(
                    "Tao Style ong nuoc",
                    "Style tao moi: " + created +
                    Environment.NewLine +
                    "Style cap nhat: " + updated);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    internal static class WaterPipeDrawing
    {
        public static Result Draw(
            ExternalCommandData commandData,
            ref string message,
            string code)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            WaterPipeDefinition pipe =
                WaterPipeDefinitions.Get(code);

            if (!SupportsDetailLines(view))
            {
                TaskDialog.Show(
                    "Vẽ ống " + pipe.Code,
                    "View hiện tại không hỗ trợ Detail Line.");

                return Result.Cancelled;
            }

            try
            {
                Category linesCategory =
                    doc.Settings.Categories.get_Item(
                        BuiltInCategory.OST_Lines);

                if (!linesCategory.SubCategories.Contains(
                        pipe.StyleName))
                {
                    TaskDialog.Show(
                        "V\u1EBD \u1ED1ng " + pipe.Code,
                        "Ch\u01B0a c\u00F3 Line Style \"" +
                        pipe.StyleName +
                        "\"." +
                        Environment.NewLine +
                        "H\u00E3y ch\u1EA1y l\u1EC7nh \"T\u1EA1o Style \u1ED1ng n\u01B0\u1EDBc\" tr\u01B0\u1EDBc.");

                    return Result.Cancelled;
                }

                Category subCategory =
                    linesCategory.SubCategories.get_Item(
                        pipe.StyleName);

                GraphicsStyle graphicsStyle =
                    EnsureGraphicsStyle(doc, pipe);

                double tolerance =
                    uiapp.Application.ShortCurveTolerance;

                const ObjectSnapTypes snapTypes =
                    ObjectSnapTypes.Endpoints |
                    ObjectSnapTypes.Midpoints |
                    ObjectSnapTypes.Intersections |
                    ObjectSnapTypes.Nearest |
                    ObjectSnapTypes.Centers;

                XYZ previousPoint = null;
                int createdCount = 0;

                using (TransactionGroup group =
                       new TransactionGroup(
                           doc,
                           "KTool - Vẽ ống " + pipe.Code))
                {
                    group.Start();

                    while (true)
                    {
                        XYZ pickedPoint;

                        try
                        {
                            string prompt = previousPoint == null
                                ? pipe.Code +
                                  " - Chọn điểm đầu (Esc để kết thúc)"
                                : pipe.Code +
                                  " - Chọn điểm tiếp theo (Esc để kết thúc)";

                            pickedPoint =
                                uidoc.Selection.PickPoint(
                                    snapTypes,
                                    prompt);
                        }
                        catch (Autodesk.Revit.Exceptions
                                   .OperationCanceledException)
                        {
                            break;
                        }

                        XYZ currentPoint =
                            ProjectToView(pickedPoint, view);

                        if (previousPoint != null &&
                            previousPoint.DistanceTo(currentPoint) >
                            tolerance)
                        {
                            using (Transaction transaction =
                                   new Transaction(
                                       doc,
                                       "KTool - Vẽ ống " +
                                       pipe.Code))
                            {
                                transaction.Start();

                                DetailCurve detailCurve =
                                    doc.Create.NewDetailCurve(
                                        view,
                                        Line.CreateBound(
                                            previousPoint,
                                            currentPoint));

                                detailCurve.LineStyle =
                                    graphicsStyle;

                                transaction.Commit();
                            }

                            createdCount++;
                        }

                        previousPoint = currentPoint;
                    }

                    if (createdCount > 0)
                        group.Assimilate();
                    else
                        group.RollBack();
                }

                return createdCount > 0
                    ? Result.Succeeded
                    : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static GraphicsStyle EnsureGraphicsStyle(Document doc, WaterPipeDefinition pipe)
        {
            Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

            Category subCategory = linesCategory.SubCategories
                .Cast<Category>()
                .FirstOrDefault(x => x.Name.Equals(pipe.StyleName, StringComparison.OrdinalIgnoreCase));

            if (subCategory == null)
                throw new InvalidOperationException("Không tìm thấy Line Style: " + pipe.StyleName);

            GraphicsStyle style = subCategory.GetGraphicsStyle(GraphicsStyleType.Projection);

            if (style == null)
                throw new InvalidOperationException("Không lấy được GraphicsStyle: " + pipe.StyleName);

            return style;
        }


        private static XYZ ProjectToView(
            XYZ point,
            View view)
        {
            XYZ normal = view.ViewDirection;

            double distance =
                (point - view.Origin).DotProduct(normal);

            return point - distance * normal;
        }

        private static bool SupportsDetailLines(View view)
        {
            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                case ViewType.DraftingView:
                    return true;

                default:
                    return false;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawCwPipeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return WaterPipeDrawing.Draw(
                commandData,
                ref message,
                "CW");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawCPipeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return WaterPipeDrawing.Draw(
                commandData,
                ref message,
                "C");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawHwPipeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return WaterPipeDrawing.Draw(
                commandData,
                ref message,
                "HW");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawWpPipeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return WaterPipeDrawing.Draw(
                commandData,
                ref message,
                "WP");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawRpPipeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return WaterPipeDrawing.Draw(
                commandData,
                ref message,
                "RP");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawVpPipeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return WaterPipeDrawing.Draw(
                commandData,
                ref message,
                "VP");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawSpPipeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return WaterPipeDrawing.Draw(
                commandData,
                ref message,
                "SP");
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawDpPipeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            return WaterPipeDrawing.Draw(
                commandData,
                ref message,
                "DP");
        }
    }
}
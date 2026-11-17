using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    /// <summary>Loads the flat navy-and-earth PNG icon set embedded from Resources.</summary>
    internal static class KToolRibbonIcons
    {
        private static readonly Assembly IconAssembly = Assembly.GetExecutingAssembly();
        private static readonly string[] ResourceNames = IconAssembly.GetManifestResourceNames();
        private static readonly Dictionary<string, BitmapImage> Cache =
            new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);

        internal static void Set(PushButtonData item, KToolIcon icon)
        {
            item.Image = Create(icon, 16);
            item.LargeImage = Create(icon, 32);
        }

        internal static void Set(PulldownButtonData item, KToolIcon icon)
        {
            item.Image = Create(icon, 16);
            item.LargeImage = Create(icon, 32);
        }

        internal static ImageSource Create(KToolIcon icon, int size)
        {
            if (size != 16 && size != 32)
                throw new ArgumentOutOfRangeException(nameof(size), "KTool PNG icons must be 16px or 32px.");

            string fileName = "KTool_" + icon + "_" + size + ".png";
            string resourceName = ResourceNames.FirstOrDefault(name =>
                name.EndsWith(".Resources." + fileName, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null)
                throw new InvalidOperationException("Embedded KTool icon not found: Resources\\" + fileName);

            lock (Cache)
            {
                if (Cache.TryGetValue(resourceName, out BitmapImage cached)) return cached;
                using (Stream stream = IconAssembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new InvalidOperationException("Could not open embedded KTool icon: " + resourceName);
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    Cache.Add(resourceName, image);
                    return image;
                }
            }
        }
    }
}
using System;
using System.Globalization;
using System.IO;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Offset (mm, tính trên model) của đầu Grid/Level vươn ra ngoài mép Crop View.
    /// Lưu ở %APPDATA%\KTool\FitCrop.ini nên dùng chung cho mọi project và mọi lần mở Revit.
    /// </summary>
    internal class FitCropSettings
    {
        public const double DEFAULT_MM = 2500.0;

        // Đầu không có bubble = Tail, đầu có bubble = Bubble
        public double HorizontalTailMm = DEFAULT_MM;
        public double HorizontalBubbleMm = DEFAULT_MM;
        public double VerticalTailMm = DEFAULT_MM;
        public double VerticalBubbleMm = DEFAULT_MM;

        private static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "KTool", "FitCrop.ini");
            }
        }

        public static FitCropSettings Load()
        {
            FitCropSettings s = new FitCropSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;

                    string key = line.Substring(0, i).Trim();
                    string val = line.Substring(i + 1).Trim();

                    double v;
                    if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        continue;

                    switch (key)
                    {
                        case "HorizontalTailMm": s.HorizontalTailMm = v; break;
                        case "HorizontalBubbleMm": s.HorizontalBubbleMm = v; break;
                        case "VerticalTailMm": s.VerticalTailMm = v; break;
                        case "VerticalBubbleMm": s.VerticalBubbleMm = v; break;
                    }
                }
            }
            catch (Exception)
            {
                // File lỗi thì dùng giá trị mặc định
            }
            return s;
        }

        public void Save()
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            CultureInfo inv = CultureInfo.InvariantCulture;
            File.WriteAllLines(path, new[]
            {
                "HorizontalTailMm=" + HorizontalTailMm.ToString("R", inv),
                "HorizontalBubbleMm=" + HorizontalBubbleMm.ToString("R", inv),
                "VerticalTailMm=" + VerticalTailMm.ToString("R", inv),
                "VerticalBubbleMm=" + VerticalBubbleMm.ToString("R", inv)
            });
        }

        /// <summary>Hiển thị độ dài: 2500 -> "2.5m", 300 -> "300mm".</summary>
        public static string FormatLength(double mm)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            if (Math.Abs(mm) >= 1000 && Math.Abs(mm % 100) < 1e-9)
                return (mm / 1000.0).ToString("0.###", inv) + "m";
            return mm.ToString("0.###", inv) + "mm";
        }

        /// <summary>
        /// Đọc độ dài người dùng nhập: "2000" = 2000mm, "2m" = 2000mm, "250cm" = 2500mm.
        /// Chấp nhận dấu phẩy hoặc dấu chấm thập phân, cho phép số âm.
        /// </summary>
        public static bool TryParseLength(string text, out double mm)
        {
            mm = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string t = text.Trim().ToLowerInvariant().Replace(" ", "").Replace(',', '.');
            double factor = 1.0;

            if (t.EndsWith("mm")) t = t.Substring(0, t.Length - 2);
            else if (t.EndsWith("cm")) { factor = 10.0; t = t.Substring(0, t.Length - 2); }
            else if (t.EndsWith("m")) { factor = 1000.0; t = t.Substring(0, t.Length - 1); }

            double v;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return false;

            mm = v * factor;
            return true;
        }
    }
}

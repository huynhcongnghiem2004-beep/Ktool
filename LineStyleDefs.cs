using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.Dim
{
    public enum LinePatternKind
    {
        Solid,    // nét liền
        Dash,     // nét đứt
        DashDot   // nét gạch chấm
    }

    /// <summary>
    /// Một loại đường (dây điện hoặc ống nước): Line Style và (tùy chọn) ký hiệu ghi trên đường.
    /// </summary>
    public class LineStyleDef
    {
        /// <summary>Tên hiện trong hộp thoại chọn, ví dụ "2.5" hoặc "CW - Cấp nước lạnh".</summary>
        public string Key;

        /// <summary>Tên Line Style trong Revit, ví dụ "Dây 2.5" hoặc "Ống CW - Cấp nước lạnh".</summary>
        public string StyleName;

        public int Weight;          // Line Weight 1..16
        public byte R, G, B;        // màu
        public LinePatternKind Pattern;

        /// <summary>Chữ ghi giữa đường (ví dụ "2.5", "CW"). Để null nếu không ghi.</summary>
        public string LabelText;

        public LineStyleDef(string key, string styleName, int weight, byte r, byte g, byte b,
                            LinePatternKind pattern, string labelText)
        {
            Key = key; StyleName = styleName; Weight = weight;
            R = r; G = g; B = b; Pattern = pattern; LabelText = labelText;
        }

        public bool HasLabel { get { return !string.IsNullOrEmpty(LabelText); } }

        /// <summary>Tên kiểu TextNote dùng ghi ký hiệu, ví dụ "KTool - Dây 2.5".</summary>
        public string TextTypeName { get { return "KTool - " + StyleName; } }
    }

    /// <summary>Danh mục quy ước. Sửa tại đây nếu muốn đổi màu, độ dày nét, kiểu nét hoặc thêm loại mới.</summary>
    public static class LineStyleCatalog
    {
        /// <summary>Cỡ chữ (mm trên giấy) của ký hiệu ghi trên đường.</summary>
        public const double TextSizeMm = 2.5;

        public const string DashPatternName = "KTool - Nét đứt dây";
        public const string DashDotPatternName = "KTool - Gạch chấm";

        /// <summary>Panel Điện: các cỡ dây (mm²) và dây công tắc đèn.</summary>
        public static readonly List<LineStyleDef> Wires = new List<LineStyleDef>
        {
            new LineStyleDef("1.5",  "Dây 1.5",  1,   0, 150,   0, LinePatternKind.Solid, "1.5"),   // xanh lá
            new LineStyleDef("2.5",  "Dây 2.5",  2, 255,   0,   0, LinePatternKind.Solid, "2.5"),   // đỏ
            new LineStyleDef("4.0",  "Dây 4.0",  3,   0,   0, 255, LinePatternKind.Solid, "4.0"),   // xanh dương
            new LineStyleDef("6.0",  "Dây 6.0",  4, 255, 128,   0, LinePatternKind.Solid, "6.0"),   // cam
            new LineStyleDef("10.0", "Dây 10.0", 5, 153,   0, 204, LinePatternKind.Solid, "10.0"),  // tím
            new LineStyleDef("16.0", "Dây 16.0", 6, 128,  64,   0, LinePatternKind.Solid, "16.0"),  // nâu
            // Dây công tắc đèn: nét đứt, màu đỏ, nét số 3, không ghi số
            new LineStyleDef("Công tắc đèn", "Dây công tắc đèn", 3, 255, 0, 0, LinePatternKind.Dash, null),
        };

        /// <summary>Panel Nước: 8 hệ thống ống. Ký hiệu ghi trên đường là mã hệ thống (CW, C, HW, ...).</summary>
        public static readonly List<LineStyleDef> Pipes = new List<LineStyleDef>
        {
            new LineStyleDef("CW - Cấp nước lạnh",       "Ống CW - Cấp nước lạnh",       3,   0, 102, 255, LinePatternKind.Solid,   "CW"), // xanh dương
            new LineStyleDef("C - Cấp nước thủy cục",    "Ống C - Cấp nước thủy cục",    4,   0,   0, 150, LinePatternKind.Solid,   "C"),  // xanh navy
            new LineStyleDef("HW - Cấp nước nóng",       "Ống HW - Cấp nước nóng",       3, 230,   0,   0, LinePatternKind.Solid,   "HW"), // đỏ
            new LineStyleDef("WP - Thoát nước thải",     "Ống WP - Thoát nước thải",     4, 128,  64,   0, LinePatternKind.Dash,    "WP"), // nâu
            new LineStyleDef("RP - Thoát nước mưa",      "Ống RP - Thoát nước mưa",      4,   0, 150,   0, LinePatternKind.Dash,    "RP"), // xanh lá
            new LineStyleDef("VP - Thông hơi",           "Ống VP - Thông hơi",           2, 110, 110, 110, LinePatternKind.DashDot, "VP"), // xám
            new LineStyleDef("SP - Thoát phân",          "Ống SP - Thoát phân",          5,   0,   0,   0, LinePatternKind.Dash,    "SP"), // đen
            new LineStyleDef("DP - Thoát nước máy lạnh", "Ống DP - Thoát nước máy lạnh", 2,   0, 176, 240, LinePatternKind.Dash,    "DP"), // lục lam
        };

        /// <summary>Tất cả loại đường (dùng để nhận biết ký hiệu cũ đã ghi trên bản vẽ).</summary>
        public static IEnumerable<LineStyleDef> All
        {
            get { return Wires.Concat(Pipes); }
        }

        /// <summary>Lấy Line Pattern theo kiểu nét; chưa có thì tạo. Gọi trong Transaction.</summary>
        public static ElementId GetOrCreatePatternId(Document doc, LinePatternKind kind)
        {
            switch (kind)
            {
                case LinePatternKind.Dash:
                    // đoạn 3 mm, hở 1.5 mm
                    return GetOrCreate(doc, DashPatternName,
                        new LinePatternSegment(LinePatternSegmentType.Dash, 3.0 / 304.8),
                        new LinePatternSegment(LinePatternSegmentType.Space, 1.5 / 304.8));

                case LinePatternKind.DashDot:
                    // gạch 4 mm, hở 1.5 mm, chấm, hở 1.5 mm
                    return GetOrCreate(doc, DashDotPatternName,
                        new LinePatternSegment(LinePatternSegmentType.Dash, 4.0 / 304.8),
                        new LinePatternSegment(LinePatternSegmentType.Space, 1.5 / 304.8),
                        new LinePatternSegment(LinePatternSegmentType.Dot, 0.0),
                        new LinePatternSegment(LinePatternSegmentType.Space, 1.5 / 304.8));

                default:
                    return LinePatternElement.GetSolidPatternId();
            }
        }

        private static ElementId GetOrCreate(Document doc, string name, params LinePatternSegment[] segments)
        {
            LinePatternElement existing = LinePatternElement.GetLinePatternElementByName(doc, name);
            if (existing != null) return existing.Id;

            LinePattern lp = new LinePattern(name);
            lp.SetSegments(new List<LinePatternSegment>(segments));
            return LinePatternElement.Create(doc, lp).Id;
        }
    }
}

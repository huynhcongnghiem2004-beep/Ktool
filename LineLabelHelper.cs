using System;
using Autodesk.Revit.DB;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Ghi chú tên lên đường (ống nước, dây điện): Text Note xoay theo hướng đường,
    /// đặt ngay phía trên nét và ở giữa đoạn.
    /// </summary>
    internal static class LineLabelHelper
    {
        private const double GAP_MM = 0.8;          // khe (giấy) giữa nét và chân chữ
        private const double DEFAULT_TEXT_MM = 2.5; // cỡ chữ dự phòng (giấy)

        /// <summary>Text Note Type mặc định của project (nếu chưa đặt thì lấy loại đầu tiên).</summary>
        public static TextNoteType DefaultTextNoteType(Document doc)
        {
            TextNoteType type = null;

            ElementId id = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (id != null && id != ElementId.InvalidElementId)
                type = doc.GetElement(id) as TextNoteType;

            if (type == null)
            {
                type = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .FirstElement() as TextNoteType;
            }

            if (type == null)
                throw new InvalidOperationException("Tài liệu không có TextNoteType.");

            return type;
        }

        /// <summary>Cỡ chữ (feet, trên giấy).</summary>
        public static double TextSizeFeet(TextNoteType type)
        {
            Parameter p = type.get_Parameter(BuiltInParameter.TEXT_SIZE);
            if (p != null && p.AsDouble() > 1e-9) return p.AsDouble();
            return DEFAULT_TEXT_MM / 304.8;
        }

        /// <summary>Khoảng cách (feet, trên model) từ tim đường tới gốc Text Note.</summary>
        public static double Offset(TextNoteType type, View view)
        {
            return (TextSizeFeet(type) + GAP_MM / 304.8) * view.Scale;
        }

        /// <summary>Ước lượng bề rộng chữ (feet, trên model) để biết đoạn có đủ dài để ghi chú không.</summary>
        public static double EstimatedWidth(string text, TextNoteType type, View view)
        {
            return text.Length * TextSizeFeet(type) * 0.75 * view.Scale;
        }

        /// <summary>Tạo ghi chú cho đoạn thẳng start-end. Gọi bên trong một Transaction.</summary>
        public static TextNote CreateLabel(
            Document doc, View view, TextNoteType type, XYZ start, XYZ end, string text)
        {
            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;

            XYZ dir = end - start;
            double len = dir.GetLength();
            if (len < 1e-9) return null;
            dir = dir / len;

            // Chữ luôn đọc từ trái sang phải (đường đứng thì đọc từ dưới lên)
            double dr = dir.DotProduct(right);
            double du = dir.DotProduct(up);
            if (dr < -1e-6 || (Math.Abs(dr) <= 1e-6 && du < 0))
            {
                dr = -dr;
                du = -du;
            }

            double theta = Math.Atan2(du, dr);
            double c = Math.Cos(theta);
            double s = Math.Sin(theta);

            // Pháp tuyến hướng về phía "trên" của chữ
            XYZ normal = right * (-s) + up * c;

            // Gốc Text Note (căn giữa, mép trên): đẩy lên một đoạn bằng chiều cao chữ + khe
            XYZ mid = (start + end) / 2.0;
            XYZ position = mid + normal * Offset(type, view);

            TextNoteOptions options = new TextNoteOptions(type.Id)
            {
                HorizontalAlignment = HorizontalTextAlignment.Center,
                VerticalAlignment = VerticalTextAlignment.Top,
                Rotation = theta
            };

            return TextNote.Create(doc, view.Id, position, text, options);
        }

        public static bool SupportsDetailLines(View view)
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
}

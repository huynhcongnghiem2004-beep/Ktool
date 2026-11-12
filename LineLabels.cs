using System;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Ghi ký hiệu (số cỡ dây "2.5" hoặc mã ống "CW") ngay giữa đoạn Detail Line bằng TextNote.
    /// Revit không cho nhúng chữ vào Line Style, nên ký hiệu được đặt thành TextNote:
    /// căn giữa đoạn, xoay theo đường, màu trùng màu Line Style.
    /// </summary>
    public static class LineLabels
    {
        /// <summary>true: nền chữ đặc, đường như bị ngắt bởi ký hiệu. false: nền trong suốt.</summary>
        public const bool OpaqueBackground = true;

        /// <summary>Đoạn ngắn hơn giá trị này (mm trên giấy) thì không ghi ký hiệu.</summary>
        private const double MinSegmentPaperMm = 12.0;

        /// <summary>Sai số khi tìm ký hiệu cũ nằm giữa đoạn (mm trên giấy).</summary>
        private const double MatchPaperMm = 2.0;

        private const double MmToFeet = 1.0 / 304.8;

        public static TextNoteType FindTextType(Document doc, string name)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(t => t.Name == name);
        }

        /// <summary>Tạo (hoặc cập nhật) kiểu TextNote cho một loại đường: màu, cỡ chữ, chữ đậm, nền.</summary>
        public static TextNoteType CreateOrUpdateTextType(Document doc, LineStyleDef def, out bool created)
        {
            created = false;
            TextNoteType type = FindTextType(doc, def.TextTypeName);

            if (type == null)
            {
                TextNoteType baseType =
                    doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) as TextNoteType;
                if (baseType == null) return null;

                type = baseType.Duplicate(def.TextTypeName) as TextNoteType;
                if (type == null) return null;
                created = true;
            }

            SetDouble(type, BuiltInParameter.TEXT_SIZE, LineStyleCatalog.TextSizeMm * MmToFeet);
            SetInt(type, BuiltInParameter.LINE_COLOR, def.R | (def.G << 8) | (def.B << 16));
            SetInt(type, BuiltInParameter.TEXT_STYLE_BOLD, 1);
            SetInt(type, BuiltInParameter.TEXT_BOX_VISIBILITY, 0);                    // không viền
            SetInt(type, BuiltInParameter.TEXT_BACKGROUND, OpaqueBackground ? 0 : 1); // 0 = Opaque, 1 = Transparent

            return type;
        }

        /// <summary>
        /// Ghi ký hiệu giữa đoạn. Nếu đã có ký hiệu cũ ở đúng vị trí thì cập nhật (nội dung + kiểu),
        /// nếu chưa có thì chỉ tạo mới khi createIfMissing = true.
        /// Loại đường không có ký hiệu (HasLabel = false) thì xóa ký hiệu cũ (nếu có).
        /// Gọi bên trong một Transaction đang mở.
        /// </summary>
        public static void EnsureLabel(
            Document doc, View view, DetailCurve curve, LineStyleDef def, bool createIfMissing)
        {
            Line line = curve.GeometryCurve as Line;
            if (line == null) return;

            int scale = Math.Max(view.Scale, 1);
            XYZ mid = line.Evaluate(0.5, true);

            if (!def.HasLabel)
            {
                TextNote old = FindLabelAt(doc, view, mid, MatchPaperMm * MmToFeet * scale);
                if (old != null) doc.Delete(old.Id);
                return;
            }

            TextNoteType type = FindTextType(doc, def.TextTypeName);
            if (type == null) return;

            TextNote existing = FindLabelAt(doc, view, mid, MatchPaperMm * MmToFeet * scale);
            if (existing != null)
            {
                existing.Text = def.LabelText;
                existing.ChangeTypeId(type.Id);
                return;
            }

            if (!createIfMissing) return;
            if (line.Length < MinSegmentPaperMm * MmToFeet * scale) return;

            TextNoteOptions opts = new TextNoteOptions(type.Id)
            {
                HorizontalAlignment = HorizontalTextAlignment.Center,
                VerticalAlignment = VerticalTextAlignment.Middle,
                Rotation = ReadableAngle(line.Direction, view)
            };
            TextNote.Create(doc, view.Id, mid, def.LabelText, opts);
        }

        /// <summary>Tìm ký hiệu cũ (nội dung trùng một ký hiệu đã biết) nằm gần điểm giữa đoạn.</summary>
        private static TextNote FindLabelAt(Document doc, View view, XYZ mid, double tol)
        {
            XYZ n = view.ViewDirection;
            foreach (TextNote tn in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(TextNote))
                         .Cast<TextNote>())
            {
                XYZ d = tn.Coord - mid;
                d = d - d.DotProduct(n) * n; // chỉ xét khoảng cách trong mặt phẳng view
                if (d.GetLength() > tol) continue;

                string txt = (tn.Text ?? string.Empty).Trim();
                if (LineStyleCatalog.All.Any(i => i.HasLabel && i.LabelText == txt)) return tn;
            }
            return null;
        }

        /// <summary>Góc xoay chữ theo đường, giữ chữ luôn đọc được (không bị lộn ngược).</summary>
        private static double ReadableAngle(XYZ dir, View view)
        {
            double x = dir.DotProduct(view.RightDirection);
            double y = dir.DotProduct(view.UpDirection);
            double a = Math.Atan2(y, x);
            if (a > Math.PI / 2) a -= Math.PI;
            else if (a <= -Math.PI / 2) a += Math.PI;
            return a;
        }

        private static void SetDouble(Element e, BuiltInParameter bip, double value)
        {
            Parameter p = e.get_Parameter(bip);
            if (p != null && !p.IsReadOnly) p.Set(value);
        }

        private static void SetInt(Element e, BuiltInParameter bip, int value)
        {
            Parameter p = e.get_Parameter(bip);
            if (p != null && !p.IsReadOnly) p.Set(value);
        }
    }
}

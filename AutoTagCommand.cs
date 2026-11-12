using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Tự động đánh Tag cho cấu kiện kết cấu: Cột, Dầm, Móng, Sàn.
    ///  - Chạy trong view mặt bằng / mặt đứng / mặt cắt đang mở,
    ///    hoặc chọn nhiều view trong Project Browser rồi chạy.
    ///  - Nếu đã chọn sẵn cấu kiện trong view thì chỉ Tag các cấu kiện đó.
    ///  - Dùng family Tag mặc định của từng loại (cần nạp sẵn vào project).
    /// Vị trí Tag: Cột ở góc trên phải, Móng ở góc dưới trái, Dầm ở giữa dầm (cạnh trên hoặc cạnh phải
    /// tuỳ dầm nằm ngang hay đứng trên màn hình), Sàn ở tâm.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AutoTagCommand : IExternalCommand
    {
        private const string TITLE = "Auto Tag kết cấu";
        private const double GAP_MM = 3.0;         // khoảng cách (giấy) từ cấu kiện tới Tag
        private const double BEAM_OFFSET_MM = 4.0; // khoảng cách (giấy) từ tim dầm tới Tag

        private enum Kind { Column, Beam, Foundation, Floor }

        private class Cat
        {
            public Kind Kind;
            public string Name;
            public BuiltInCategory Element;
            public BuiltInCategory TagCategory;
        }

        private static readonly Cat[] CATS =
        {
            new Cat { Kind = Kind.Column, Name = "Cột", Element = BuiltInCategory.OST_StructuralColumns, TagCategory = BuiltInCategory.OST_StructuralColumnTags },
            new Cat { Kind = Kind.Beam, Name = "Dầm", Element = BuiltInCategory.OST_StructuralFraming, TagCategory = BuiltInCategory.OST_StructuralFramingTags },
            new Cat { Kind = Kind.Foundation, Name = "Móng", Element = BuiltInCategory.OST_StructuralFoundation, TagCategory = BuiltInCategory.OST_StructuralFoundationTags },
            new Cat { Kind = Kind.Floor, Name = "Sàn", Element = BuiltInCategory.OST_Floors, TagCategory = BuiltInCategory.OST_FloorTags }
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View active = doc.ActiveView;

            // 1. Xác định view cần xử lý
            List<View> selectedViews = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<View>()
                .Where(v => !v.IsTemplate && (v is ViewPlan || v is ViewSection))
                .ToList();

            List<View> views;
            HashSet<ElementId> limit = null;

            if (selectedViews.Count > 0)
            {
                views = selectedViews;
            }
            else
            {
                if (!(active is ViewPlan || active is ViewSection) || active.IsTemplate)
                {
                    TaskDialog.Show(TITLE, "Hãy mở một view mặt bằng, mặt đứng hoặc mặt cắt rồi chạy lại lệnh.");
                    return Result.Cancelled;
                }
                views = new List<View> { active };

                // Cấu kiện đã chọn sẵn: chỉ Tag các cấu kiện đó
                List<ElementId> picked = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.Category != null && CATS.Any(c => c.Element == e.Category.BuiltInCategory))
                    .Select(e => e.Id)
                    .ToList();
                if (picked.Count > 0) limit = new HashSet<ElementId>(picked);
            }

            // 2. Hộp thoại tuỳ chọn
            AutoTagOptions opt = AutoTagOptions.Last;
            using (AutoTagForm form = new AutoTagForm(opt, views.Count, limit == null ? 0 : limit.Count))
            {
                RevitWindowOwner owner = new RevitWindowOwner(commandData.Application.MainWindowHandle);
                if (form.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;
            }

            // 3. Tìm family Tag cho từng loại được chọn
            List<Cat> enabled = new List<Cat>();
            Dictionary<Kind, ElementId> tagTypes = new Dictionary<Kind, ElementId>();
            List<string> missing = new List<string>();

            foreach (Cat c in CATS)
            {
                if (!IsEnabled(opt, c.Kind)) continue;

                ElementId tagType = FindTagType(doc, c.TagCategory);
                if (tagType == null || tagType == ElementId.InvalidElementId)
                {
                    missing.Add(c.Name);
                    continue;
                }
                enabled.Add(c);
                tagTypes[c.Kind] = tagType;
            }

            if (enabled.Count == 0)
            {
                TaskDialog.Show(TITLE, "Project chưa có family Tag cho: " + string.Join(", ", missing)
                    + ".\nHãy nạp family Tag (Insert > Load Family) rồi chạy lại.");
                return Result.Cancelled;
            }

            // 4. Tạo Tag
            Dictionary<Kind, int> done = new Dictionary<Kind, int>();
            foreach (Cat c in CATS) done[c.Kind] = 0;
            int alreadyTagged = 0, failed = 0;

            using (Transaction tx = new Transaction(doc, TITLE))
            {
                tx.Start();
                try
                {
                    foreach (View view in views)
                    {
                        HashSet<ElementId> tagged = opt.SkipTagged ? GetTaggedIds(doc, view) : new HashSet<ElementId>();
                        double gapFt = GAP_MM / 304.8 * view.Scale;
                        double beamFt = BEAM_OFFSET_MM / 304.8 * view.Scale;

                        foreach (Cat c in enabled)
                        {
                            IEnumerable<Element> items = new FilteredElementCollector(doc, view.Id)
                                .OfCategory(c.Element)
                                .WhereElementIsNotElementType()
                                .ToElements();

                            foreach (Element el in items)
                            {
                                if (limit != null && !limit.Contains(el.Id)) continue;

                                if (tagged.Contains(el.Id))
                                {
                                    alreadyTagged++;
                                    continue;
                                }

                                try
                                {
                                    XYZ pt = ComputePoint(el, c.Kind, view, gapFt, beamFt);
                                    if (pt == null)
                                    {
                                        failed++;
                                        continue;
                                    }

                                    IndependentTag tag = IndependentTag.Create(
                                        doc, view.Id, new Reference(el), opt.UseLeader,
                                        TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, pt);

                                    if (tag == null)
                                    {
                                        failed++;
                                        continue;
                                    }

                                    ElementId want = tagTypes[c.Kind];
                                    if (tag.GetTypeId() != want)
                                        tag.ChangeTypeId(want);

                                    done[c.Kind]++;
                                }
                                catch (Exception)
                                {
                                    failed++;
                                }
                            }
                        }
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

            // 5. Báo kết quả
            int total = done.Values.Sum();
            string msg = "Đã tạo " + total + " Tag trong " + views.Count + " view:";
            foreach (Cat c in enabled)
                msg += "\n  " + c.Name + ": " + done[c.Kind];
            if (alreadyTagged > 0)
                msg += "\nBỏ qua " + alreadyTagged + " cấu kiện đã có Tag.";
            if (failed > 0)
                msg += "\nKhông tạo được Tag cho " + failed + " cấu kiện.";
            if (missing.Count > 0)
                msg += "\nThiếu family Tag cho: " + string.Join(", ", missing) + ".";
            TaskDialog.Show(TITLE, msg);

            return Result.Succeeded;
        }

        private static bool IsEnabled(AutoTagOptions o, Kind k)
        {
            switch (k)
            {
                case Kind.Column: return o.Columns;
                case Kind.Beam: return o.Beams;
                case Kind.Foundation: return o.Foundations;
                default: return o.Floors;
            }
        }

        /// <summary>Family Tag mặc định của loại Tag; nếu chưa đặt mặc định thì lấy Tag đầu tiên trong project.</summary>
        private static ElementId FindTagType(Document doc, BuiltInCategory tagCategory)
        {
            try
            {
                ElementId def = doc.GetDefaultFamilyTypeId(new ElementId(tagCategory));
                if (def != null && def != ElementId.InvalidElementId && doc.GetElement(def) != null)
                    return def;
            }
            catch (Exception)
            {
                // bỏ qua, thử cách dưới
            }

            FamilySymbol fs = new FilteredElementCollector(doc)
                .OfCategory(tagCategory)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            return fs != null ? fs.Id : ElementId.InvalidElementId;
        }

        /// <summary>Id các cấu kiện đã có Tag trong view.</summary>
        private static HashSet<ElementId> GetTaggedIds(Document doc, View view)
        {
            HashSet<ElementId> set = new HashSet<ElementId>();
            foreach (IndependentTag t in new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
            {
                try
                {
                    foreach (ElementId id in t.GetTaggedLocalElementIds())
                        set.Add(id);
                }
                catch (Exception)
                {
                    // Tag không đọc được: bỏ qua
                }
            }
            return set;
        }

        /// <summary>Điểm đặt Tag theo hệ trục của view (Right/Up), tuỳ loại cấu kiện.</summary>
        private static XYZ ComputePoint(Element el, Kind kind, View view, double gapFt, double beamFt)
        {
            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;

            BoundingBoxXYZ bb = el.get_BoundingBox(view);
            if (bb == null) bb = el.get_BoundingBox(null);
            if (bb == null) return null;

            // Phạm vi cấu kiện theo trục Right/Up của view
            double rMin = double.MaxValue, rMax = double.MinValue;
            double uMin = double.MaxValue, uMax = double.MinValue;
            double[] xs = { bb.Min.X, bb.Max.X };
            double[] ys = { bb.Min.Y, bb.Max.Y };
            double[] zs = { bb.Min.Z, bb.Max.Z };
            foreach (double x in xs)
                foreach (double y in ys)
                    foreach (double z in zs)
                    {
                        XYZ p = new XYZ(x, y, z);
                        double r = p.DotProduct(right);
                        double u = p.DotProduct(up);
                        if (r < rMin) rMin = r;
                        if (r > rMax) rMax = r;
                        if (u < uMin) uMin = u;
                        if (u > uMax) uMax = u;
                    }

            XYZ center = (bb.Min + bb.Max) / 2.0;
            double cr = (rMin + rMax) / 2.0;
            double cu = (uMin + uMax) / 2.0;

            switch (kind)
            {
                case Kind.Column:
                    // góc trên phải
                    return center + right * ((rMax - cr) + gapFt) + up * ((uMax - cu) + gapFt);

                case Kind.Foundation:
                    // góc dưới trái
                    return center - right * ((cr - rMin) + gapFt) - up * ((cu - uMin) + gapFt);

                case Kind.Beam:
                    {
                        LocationCurve lc = el.Location as LocationCurve;
                        if (lc == null || lc.Curve == null) return center;

                        XYZ mid = lc.Curve.Evaluate(0.5, true);
                        XYZ d = lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0);
                        if (d.GetLength() < 1e-9) return mid;
                        d = d.Normalize();

                        bool horizontalOnScreen = Math.Abs(d.DotProduct(right)) >= Math.Abs(d.DotProduct(up));
                        return mid + (horizontalOnScreen ? up : right) * beamFt;
                    }

                default:
                    // sàn: tâm
                    return center;
            }
        }
    }
}

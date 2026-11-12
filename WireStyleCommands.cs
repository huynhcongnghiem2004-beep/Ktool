using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

// Cần thêm Reference: System.Windows.Forms, System.Drawing

namespace SDK_HD.Dim
{
    /// <summary>Bảng quy ước cỡ dây -> độ dày nét + màu. Sửa tại đây nếu muốn đổi quy ước.</summary>
    public static class WireStyles
    {
        public const string Prefix = "Dây ";

        /// <summary>Cỡ chữ (mm trên giấy) của con số ghi trên đường dây.</summary>
        public const double TextSizeMm = 2.5;

        /// <summary>Tên Line Pattern nét đứt dùng cho dây công tắc đèn.</summary>
        public const string DashPatternName = "KTool - Nét đứt dây";

        /// <summary>Lấy Line Pattern nét đứt (đoạn 3 mm, hở 1.5 mm); chưa có thì tạo. Gọi trong Transaction.</summary>
        public static ElementId GetOrCreateDashPatternId(Document doc)
        {
            LinePatternElement existing = LinePatternElement.GetLinePatternElementByName(doc, DashPatternName);
            if (existing != null) return existing.Id;

            LinePattern lp = new LinePattern(DashPatternName);
            lp.SetSegments(new List<LinePatternSegment>
            {
                new LinePatternSegment(LinePatternSegmentType.Dash, 3.0 / 304.8),
                new LinePatternSegment(LinePatternSegmentType.Space, 1.5 / 304.8)
            });
            return LinePatternElement.Create(doc, lp).Id;
        }

        public class Def
        {
            public string Size;      // cỡ dây (mm2) hoặc tên loại dây, hiện trong hộp thoại chọn
            public int Weight;       // Line Weight 1..16
            public byte R, G, B;     // màu
            public bool Dashed;      // true: nét đứt, false: nét liền
            public bool HasLabel;    // true: ghi con số Size lên đường dây
            private readonly string _styleName;

            public Def(string size, int weight, byte r, byte g, byte b,
                       string styleName = null, bool dashed = false, bool hasLabel = true)
            {
                Size = size; Weight = weight; R = r; G = g; B = b;
                _styleName = styleName; Dashed = dashed; HasLabel = hasLabel;
            }

            /// <summary>Tên Line Style, ví dụ "Dây 2.5" hoặc "Dây công tắc đèn".</summary>
            public string StyleName { get { return _styleName ?? Prefix + Size; } }

            /// <summary>Tên kiểu TextNote dùng ghi số cỡ dây, ví dụ "KTool - Dây 2.5".</summary>
            public string TextTypeName { get { return "KTool - " + StyleName; } }
        }

        public static readonly List<Def> Items = new List<Def>
        {
            new Def("1.5",  1,   0, 150,   0),  // xanh lá
            new Def("2.5",  2, 255,   0,   0),  // đỏ
            new Def("4.0",  3,   0,   0, 255),  // xanh dương
            new Def("6.0",  4, 255, 128,   0),  // cam
            new Def("10.0", 5, 153,   0, 204),  // tím
            new Def("16.0", 6, 128,  64,   0),  // nâu
            // Dây công tắc đèn: nét đứt, màu đỏ, nét số 3, không ghi số
            new Def("Công tắc đèn", 3, 255, 0, 0, "Dây công tắc đèn", true, false),
        };
    }

    // ------------------------------------------------------------------
    // Lệnh 1: Tạo (hoặc cập nhật) Line Style và kiểu text số cho từng cỡ dây
    // ------------------------------------------------------------------
    [Transaction(TransactionMode.Manual)]
    public class CreateWireStylesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                Categories cats = doc.Settings.Categories;
                Category linesCat = cats.get_Item(BuiltInCategory.OST_Lines);
                ElementId solidId = LinePatternElement.GetSolidPatternId();

                int created = 0, updated = 0, textCreated = 0, textUpdated = 0;

                using (Transaction t = new Transaction(doc, "KTool - Tạo Line Style dây điện"))
                {
                    t.Start();
                    ElementId dashId = WireStyles.GetOrCreateDashPatternId(doc);
                    foreach (WireStyles.Def d in WireStyles.Items)
                    {
                        // Line Style
                        Category sub;
                        if (linesCat.SubCategories.Contains(d.StyleName))
                        {
                            sub = linesCat.SubCategories.get_Item(d.StyleName);
                            updated++;
                        }
                        else
                        {
                            sub = cats.NewSubcategory(linesCat, d.StyleName);
                            created++;
                        }

                        sub.SetLineWeight(d.Weight, GraphicsStyleType.Projection);
                        sub.LineColor = new Color(d.R, d.G, d.B);
                        sub.SetLinePatternId(d.Dashed ? dashId : solidId, GraphicsStyleType.Projection);

                        // Kiểu text để ghi con số cỡ dây (cùng màu với Line Style)
                        if (d.HasLabel)
                        {
                            bool textIsNew;
                            TextNoteType tt = WireLabels.CreateOrUpdateTextType(doc, d, out textIsNew);
                            if (tt != null)
                            {
                                if (textIsNew) textCreated++; else textUpdated++;
                            }
                        }
                    }
                    t.Commit();
                }

                TaskDialog.Show("Tạo Line Style dây điện",
                    "Line Style: tạo mới " + created + ", cập nhật " + updated + Environment.NewLine +
                    "Kiểu text số cỡ dây: tạo mới " + textCreated + ", cập nhật " + textUpdated);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ------------------------------------------------------------------
    // Lệnh 2: Đổi cỡ dây (Line Style + con số) cho các Detail Line đã chọn
    // ------------------------------------------------------------------
    [Transaction(TransactionMode.Manual)]
    public class ChangeWireSizeCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 1. Detail Line: ưu tiên đối tượng chọn sẵn
                List<DetailCurve> curves = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .OfType<DetailCurve>()
                    .ToList();

                if (curves.Count == 0)
                {
                    IList<Reference> refs = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new DetailCurveFilter(),
                        "Chọn các Detail Line cần đổi cỡ dây, rồi bấm Finish");
                    curves = refs.Select(r => doc.GetElement(r)).OfType<DetailCurve>().ToList();
                }

                if (curves.Count == 0)
                {
                    TaskDialog.Show("Đổi cỡ dây", "Chưa chọn Detail Line nào.");
                    return Result.Cancelled;
                }

                // 2. Các cỡ dây đã có Line Style trong project
                Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                List<WireStyles.Def> available = WireStyles.Items
                    .Where(d => linesCat.SubCategories.Contains(d.StyleName))
                    .ToList();

                if (available.Count == 0)
                {
                    TaskDialog.Show("Đổi cỡ dây",
                        "Project chưa có Line Style dây điện. Hãy chạy lệnh \"Tạo Style dây điện\" trước.");
                    return Result.Cancelled;
                }

                // 3. Chọn cỡ dây
                using (WireSizeForm form = new WireSizeForm(available.Select(d => d.Size), null, true))
                {
                    if (form.ShowDialog() != WinForms.DialogResult.OK || form.SelectedSize == null)
                        return Result.Cancelled;

                    WireStyles.Def def = available.First(d => d.Size == form.SelectedSize);
                    Category sub = linesCat.SubCategories.get_Item(def.StyleName);
                    GraphicsStyle gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);

                    // Số cỡ dây cần có kiểu text; nếu thiếu thì chỉ đổi Line Style
                    // (loại dây không ghi số, ví dụ dây công tắc đèn, không cần kiểu text)
                    bool hasTextType = !def.HasLabel || WireLabels.FindTextType(doc, def.TextTypeName) != null;
                    bool addLabel = form.AddLabel && hasTextType && def.HasLabel;

                    // 4. Gán Line Style và cập nhật con số
                    using (Transaction t = new Transaction(doc, "KTool - Đổi cỡ dây"))
                    {
                        t.Start();
                        foreach (DetailCurve c in curves)
                        {
                            c.LineStyle = gs;

                            // Số cũ (nếu có) luôn được cập nhật cho khớp cỡ dây mới;
                            // số mới chỉ được tạo khi bật tùy chọn ghi số.
                            View v = doc.GetElement(c.OwnerViewId) as View;
                            if (v != null && hasTextType)
                                WireLabels.EnsureLabel(doc, v, c, def, addLabel);
                        }
                        t.Commit();
                    }

                    string report = "Đã đổi " + curves.Count + " đường sang " + def.StyleName + ".";
                    if (form.AddLabel && !hasTextType)
                        report += Environment.NewLine +
                                  "Chưa có kiểu text số cỡ dây. Hãy chạy lại lệnh \"Tạo Style dây điện\" để tạo.";
                    TaskDialog.Show("Đổi cỡ dây", report);
                }

                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private class DetailCurveFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) { return e is DetailCurve; }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }
    }

    /// <summary>Hộp thoại nhỏ để chọn cỡ dây và bật/tắt ghi số cỡ dây lên đường.</summary>
    internal class WireSizeForm : WinForms.Form
    {
        private readonly WinForms.ComboBox _combo = new WinForms.ComboBox();
        private readonly WinForms.CheckBox _chkLabel = new WinForms.CheckBox();

        public string SelectedSize { get { return _combo.SelectedItem as string; } }

        public bool AddLabel { get { return _chkLabel.Checked; } }

        public WireSizeForm(IEnumerable<string> sizes, string preselect = null, bool addLabel = true)
        {
            Text = "Chọn cỡ dây";
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Drawing.Size(250, 125);

            _combo.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
            _combo.SetBounds(15, 15, 220, 24);
            foreach (string s in sizes) _combo.Items.Add(s);
            if (preselect != null && _combo.Items.Contains(preselect)) _combo.SelectedItem = preselect;
            else if (_combo.Items.Count > 0) _combo.SelectedIndex = 0;

            _chkLabel.Text = "Ghi số cỡ dây lên đường";
            _chkLabel.Checked = addLabel;
            _chkLabel.SetBounds(15, 48, 220, 24);

            WinForms.Button ok = new WinForms.Button { Text = "OK", DialogResult = WinForms.DialogResult.OK };
            ok.SetBounds(50, 85, 70, 26);
            WinForms.Button cancel = new WinForms.Button { Text = "Hủy", DialogResult = WinForms.DialogResult.Cancel };
            cancel.SetBounds(130, 85, 70, 26);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(_combo);
            Controls.Add(_chkLabel);
            Controls.Add(ok);
            Controls.Add(cancel);
        }
    }
}

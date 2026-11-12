using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace SDK_HD.Dim
{
    // ======================================================================
    // 1. TẠO LINE STYLE
    // ======================================================================

    /// <summary>Tạo (hoặc cập nhật) Line Style và kiểu text ký hiệu cho một nhóm loại đường.</summary>
    public abstract class CreateStylesCommandBase : IExternalCommand
    {
        protected abstract IList<LineStyleDef> Defs { get; }
        protected abstract string Noun { get; }   // "dây điện" / "ống nước"

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                Categories cats = doc.Settings.Categories;
                Category linesCat = cats.get_Item(BuiltInCategory.OST_Lines);

                int created = 0, updated = 0, textCreated = 0, textUpdated = 0;

                using (Transaction t = new Transaction(doc, "KTool - Tạo Line Style " + Noun))
                {
                    t.Start();
                    foreach (LineStyleDef d in Defs)
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
                        sub.SetLinePatternId(
                            LineStyleCatalog.GetOrCreatePatternId(doc, d.Pattern),
                            GraphicsStyleType.Projection);

                        // Kiểu text để ghi ký hiệu (cùng màu với Line Style)
                        if (d.HasLabel)
                        {
                            bool textIsNew;
                            TextNoteType tt = LineLabels.CreateOrUpdateTextType(doc, d, out textIsNew);
                            if (tt != null)
                            {
                                if (textIsNew) textCreated++; else textUpdated++;
                            }
                        }
                    }
                    t.Commit();
                }

                TaskDialog.Show("Tạo Line Style " + Noun,
                    "Line Style: tạo mới " + created + ", cập nhật " + updated + Environment.NewLine +
                    "Kiểu text ký hiệu: tạo mới " + textCreated + ", cập nhật " + textUpdated);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class CreateWireStylesCommand : CreateStylesCommandBase
    {
        protected override IList<LineStyleDef> Defs { get { return LineStyleCatalog.Wires; } }
        protected override string Noun { get { return "dây điện"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class CreatePipeStylesCommand : CreateStylesCommandBase
    {
        protected override IList<LineStyleDef> Defs { get { return LineStyleCatalog.Pipes; } }
        protected override string Noun { get { return "ống nước"; } }
    }

    // ======================================================================
    // 2. VẼ DETAIL LINE THEO LOẠI ĐƯỜNG
    // ======================================================================

    /// <summary>
    /// Vẽ Detail Line (đường gấp khúc) theo loại đường.
    /// Mỗi lần bấm một điểm sẽ vẽ một đoạn nối từ điểm trước, đồng thời ghi ký hiệu giữa đoạn
    /// (nếu bật tùy chọn). Bấm Esc để kết thúc.
    ///  - FixedLabelCode = null: hiện hộp thoại để chọn loại đường (dùng cho panel Điện).
    ///  - FixedLabelCode = "CW", "HW", ...: vẽ ngay loại đó, không hỏi gì (dùng cho các nút ống nước).
    /// </summary>
    public abstract class DrawLineCommandBase : IExternalCommand
    {
        // Nhớ lựa chọn của lần chạy trước (theo từng nhóm: dây điện / ống nước)
        private static readonly Dictionary<string, string> LastKey = new Dictionary<string, string>();
        private static readonly Dictionary<string, bool> LastLabel = new Dictionary<string, bool>();

        protected abstract IList<LineStyleDef> Defs { get; }
        protected abstract string Noun { get; }             // "dây điện" / "ống nước"
        protected abstract string CreateButtonName { get; } // tên nút tạo style, dùng trong thông báo
        protected abstract string LabelOptionText { get; }  // chữ của ô chọn ghi ký hiệu

        /// <summary>Mã ký hiệu của loại đường cần vẽ ngay (CW, HW, ...). null = hỏi người dùng.</summary>
        protected virtual string FixedLabelCode { get { return null; } }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (!LineViewHelper.SupportsDetailLines(view))
            {
                TaskDialog.Show("Vẽ " + Noun,
                    "View hiện tại không vẽ được Detail Line. Hãy chạy lệnh trong view mặt bằng, mặt cắt, mặt đứng hoặc view chi tiết.");
                return Result.Cancelled;
            }

            try
            {
                Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

                // 1. Xác định loại đường cần vẽ
                LineStyleDef def;
                bool addLabel;

                if (FixedLabelCode != null)
                {
                    def = Defs.FirstOrDefault(d => d.LabelText == FixedLabelCode);
                    if (def == null || !linesCat.SubCategories.Contains(def.StyleName))
                    {
                        string styleName = def != null ? def.StyleName : FixedLabelCode;
                        TaskDialog.Show("Vẽ " + Noun,
                            "Project chưa có Line Style \"" + styleName + "\". Hãy chạy lệnh \"" +
                            CreateButtonName + "\" trước.");
                        return Result.Cancelled;
                    }
                    addLabel = true;
                }
                else
                {
                    List<LineStyleDef> available = Defs
                        .Where(d => linesCat.SubCategories.Contains(d.StyleName))
                        .ToList();

                    if (available.Count == 0)
                    {
                        TaskDialog.Show("Vẽ " + Noun,
                            "Project chưa có Line Style " + Noun + ". Hãy chạy lệnh \"" + CreateButtonName + "\" trước.");
                        return Result.Cancelled;
                    }

                    string lastKey;
                    LastKey.TryGetValue(Noun, out lastKey);
                    bool lastLabel;
                    if (!LastLabel.TryGetValue(Noun, out lastLabel)) lastLabel = true;

                    using (LineTypeForm form = new LineTypeForm(
                               "Vẽ " + Noun, available.Select(d => d.Key), lastKey, lastLabel, LabelOptionText))
                    {
                        if (form.ShowDialog() != WinForms.DialogResult.OK || form.SelectedKey == null)
                            return Result.Cancelled;
                        def = available.First(d => d.Key == form.SelectedKey);
                        addLabel = form.AddLabel;
                    }
                    LastKey[Noun] = def.Key;
                    LastLabel[Noun] = addLabel;
                }

                // Ký hiệu cần có kiểu text; nếu thiếu thì chỉ vẽ đường
                if (addLabel && def.HasLabel && LineLabels.FindTextType(doc, def.TextTypeName) == null)
                {
                    TaskDialog.Show("Vẽ " + Noun,
                        "Chưa có kiểu text ký hiệu nên sẽ vẽ đường không ghi ký hiệu." + Environment.NewLine +
                        "Hãy chạy lại lệnh \"" + CreateButtonName + "\" để tạo kiểu text.");
                    addLabel = false;
                }

                Category sub = linesCat.SubCategories.get_Item(def.StyleName);
                GraphicsStyle gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                double tol = uiapp.Application.ShortCurveTolerance;

                const ObjectSnapTypes snaps =
                    ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints |
                    ObjectSnapTypes.Intersections | ObjectSnapTypes.Nearest |
                    ObjectSnapTypes.Centers;

                // 2. Vòng lặp chọn điểm; gom tất cả vào một TransactionGroup để Ctrl+Z một lần
                int created = 0;
                XYZ prev = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "KTool - Vẽ " + def.Key))
                {
                    tg.Start();

                    while (true)
                    {
                        XYZ picked;
                        try
                        {
                            string prompt = def.Key + (prev == null
                                ? ": chọn điểm đầu (Esc để kết thúc)"
                                : ": chọn điểm kế tiếp (Esc để kết thúc)");
                            picked = LineViewHelper.PickPoint(uidoc, doc, view, snaps, prompt);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break;
                        }

                        XYZ p = LineViewHelper.ProjectToView(picked, view);

                        if (prev != null && prev.DistanceTo(p) > tol)
                        {
                            using (Transaction t = new Transaction(doc, "Vẽ đoạn"))
                            {
                                t.Start();
                                DetailCurve dc = doc.Create.NewDetailCurve(view, Line.CreateBound(prev, p));
                                dc.LineStyle = gs;
                                if (addLabel && def.HasLabel)
                                    LineLabels.EnsureLabel(doc, view, dc, def, true);
                                t.Commit();
                            }
                            created++;
                        }
                        prev = p;
                    }

                    if (created > 0) tg.Assimilate();
                    else tg.RollBack();
                }

                return created > 0 ? Result.Succeeded : Result.Cancelled;
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
    }

    // ---- Panel Điện: chọn cỡ dây trong hộp thoại ----

    [Transaction(TransactionMode.Manual)]
    public class DrawWireCommand : DrawLineCommandBase
    {
        protected override IList<LineStyleDef> Defs { get { return LineStyleCatalog.Wires; } }
        protected override string Noun { get { return "dây điện"; } }
        protected override string CreateButtonName { get { return "Tạo Style dây điện"; } }
        protected override string LabelOptionText { get { return "Ghi số cỡ dây lên đường"; } }
    }

    // ---- Panel Nước: mỗi hệ thống ống một lệnh, vẽ ngay không hỏi ----

    public abstract class DrawFixedPipeCommandBase : DrawLineCommandBase
    {
        protected override IList<LineStyleDef> Defs { get { return LineStyleCatalog.Pipes; } }
        protected override string Noun { get { return "ống nước"; } }
        protected override string CreateButtonName { get { return "Tạo Style ống nước"; } }
        protected override string LabelOptionText { get { return "Ghi ký hiệu ống lên đường"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawPipeCwCommand : DrawFixedPipeCommandBase
    {
        protected override string FixedLabelCode { get { return "CW"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawPipeCCommand : DrawFixedPipeCommandBase
    {
        protected override string FixedLabelCode { get { return "C"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawPipeHwCommand : DrawFixedPipeCommandBase
    {
        protected override string FixedLabelCode { get { return "HW"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawPipeWpCommand : DrawFixedPipeCommandBase
    {
        protected override string FixedLabelCode { get { return "WP"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawPipeRpCommand : DrawFixedPipeCommandBase
    {
        protected override string FixedLabelCode { get { return "RP"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawPipeVpCommand : DrawFixedPipeCommandBase
    {
        protected override string FixedLabelCode { get { return "VP"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawPipeSpCommand : DrawFixedPipeCommandBase
    {
        protected override string FixedLabelCode { get { return "SP"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class DrawPipeDpCommand : DrawFixedPipeCommandBase
    {
        protected override string FixedLabelCode { get { return "DP"; } }
    }

    // ======================================================================
    // 3. ĐỔI LOẠI ĐƯỜNG CHO DETAIL LINE ĐÃ VẼ
    // ======================================================================

    /// <summary>Đổi Line Style (và ký hiệu) cho các Detail Line đã chọn.</summary>
    public abstract class ChangeLineCommandBase : IExternalCommand
    {
        protected abstract IList<LineStyleDef> Defs { get; }
        protected abstract string Noun { get; }
        protected abstract string CreateButtonName { get; }
        protected abstract string LabelOptionText { get; }

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
                        "Chọn các Detail Line cần đổi loại, rồi bấm Finish");
                    curves = refs.Select(r => doc.GetElement(r)).OfType<DetailCurve>().ToList();
                }

                if (curves.Count == 0)
                {
                    TaskDialog.Show("Đổi loại " + Noun, "Chưa chọn Detail Line nào.");
                    return Result.Cancelled;
                }

                // 2. Các loại đã có Line Style trong project
                Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                List<LineStyleDef> available = Defs
                    .Where(d => linesCat.SubCategories.Contains(d.StyleName))
                    .ToList();

                if (available.Count == 0)
                {
                    TaskDialog.Show("Đổi loại " + Noun,
                        "Project chưa có Line Style " + Noun + ". Hãy chạy lệnh \"" + CreateButtonName + "\" trước.");
                    return Result.Cancelled;
                }

                // 3. Chọn loại
                using (LineTypeForm form = new LineTypeForm(
                           "Đổi loại " + Noun, available.Select(d => d.Key), null, true, LabelOptionText))
                {
                    if (form.ShowDialog() != WinForms.DialogResult.OK || form.SelectedKey == null)
                        return Result.Cancelled;

                    LineStyleDef def = available.First(d => d.Key == form.SelectedKey);
                    Category sub = linesCat.SubCategories.get_Item(def.StyleName);
                    GraphicsStyle gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);

                    // Loại không ghi ký hiệu thì không cần kiểu text
                    bool hasTextType = !def.HasLabel || LineLabels.FindTextType(doc, def.TextTypeName) != null;
                    bool addLabel = form.AddLabel && hasTextType && def.HasLabel;

                    // 4. Gán Line Style và cập nhật ký hiệu
                    using (Transaction t = new Transaction(doc, "KTool - Đổi loại " + Noun))
                    {
                        t.Start();
                        foreach (DetailCurve c in curves)
                        {
                            c.LineStyle = gs;

                            // Ký hiệu cũ (nếu có) luôn được cập nhật cho khớp loại mới;
                            // ký hiệu mới chỉ được tạo khi bật tùy chọn ghi ký hiệu.
                            View v = doc.GetElement(c.OwnerViewId) as View;
                            if (v != null && hasTextType)
                                LineLabels.EnsureLabel(doc, v, c, def, addLabel);
                        }
                        t.Commit();
                    }

                    string report = "Đã đổi " + curves.Count + " đường sang " + def.StyleName + ".";
                    if (form.AddLabel && !hasTextType)
                        report += Environment.NewLine +
                                  "Chưa có kiểu text ký hiệu. Hãy chạy lại lệnh \"" + CreateButtonName + "\" để tạo.";
                    TaskDialog.Show("Đổi loại " + Noun, report);
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

    [Transaction(TransactionMode.Manual)]
    public class ChangeWireSizeCommand : ChangeLineCommandBase
    {
        protected override IList<LineStyleDef> Defs { get { return LineStyleCatalog.Wires; } }
        protected override string Noun { get { return "dây điện"; } }
        protected override string CreateButtonName { get { return "Tạo Style dây điện"; } }
        protected override string LabelOptionText { get { return "Ghi số cỡ dây lên đường"; } }
    }

    [Transaction(TransactionMode.Manual)]
    public class ChangePipeTypeCommand : ChangeLineCommandBase
    {
        protected override IList<LineStyleDef> Defs { get { return LineStyleCatalog.Pipes; } }
        protected override string Noun { get { return "ống nước"; } }
        protected override string CreateButtonName { get { return "Tạo Style ống nước"; } }
        protected override string LabelOptionText { get { return "Ghi ký hiệu ống lên đường"; } }
    }

    // ======================================================================
    // 4. MỞ LỆNH DETAIL LINE MẶC ĐỊNH CỦA REVIT
    // ======================================================================

    /// <summary>
    /// Mở lệnh Detail Line mặc định (Annotate > Detail Line). Line Style ống/dây sau khi tạo
    /// sẽ có trong danh sách Line Style của thanh Modify | Place Detail Lines.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class NativeDetailLineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            RevitCommandId id = RevitCommandId.LookupPostableCommandId(PostableCommand.DetailLine);
            if (id != null && uiapp.CanPostCommand(id))
            {
                uiapp.PostCommand(id);
                return Result.Succeeded;
            }

            TaskDialog.Show("Detail Line", "Không mở được lệnh Detail Line trong view hiện tại.");
            return Result.Cancelled;
        }
    }

    // ======================================================================
    // TIỆN ÍCH DÙNG CHUNG
    // ======================================================================

    internal static class LineViewHelper
    {
        /// <summary>Chiếu điểm lên mặt phẳng của view để Detail Line nằm đúng trong view.</summary>
        public static XYZ ProjectToView(XYZ p, View view)
        {
            XYZ n = view.ViewDirection;
            double dist = (p - view.Origin).DotProduct(n);
            return p - dist * n;
        }

        public static bool SupportsDetailLines(View v)
        {
            switch (v.ViewType)
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

        /// <summary>
        /// Chọn một điểm trong view. View mặt đứng / mặt cắt / chi tiết thường chưa có Work Plane
        /// nên PickPoint báo lỗi "No work plane set in current view". Khi đó hàm đặt Work Plane tạm
        /// trùng mặt phẳng view, cho chọn điểm rồi huỷ (RollBack) để không để lại thay đổi nào.
        /// View mặt bằng không cần.
        /// </summary>
        public static XYZ PickPoint(
            UIDocument uidoc, Document doc, View view, ObjectSnapTypes snaps, string prompt)
        {
            TransactionGroup tg = null;

            if (!(view is ViewPlan) && view.SketchPlane == null)
            {
                tg = new TransactionGroup(doc, "Work Plane tạm");
                tg.Start();

                using (Transaction t = new Transaction(doc, "Đặt Work Plane"))
                {
                    t.Start();
                    Plane plane = Plane.CreateByNormalAndOrigin(view.ViewDirection, view.Origin);
                    view.SketchPlane = SketchPlane.Create(doc, plane);
                    t.Commit();
                }
            }

            try
            {
                return uidoc.Selection.PickPoint(snaps, prompt);
            }
            finally
            {
                if (tg != null) tg.RollBack();
            }
        }
    }

    /// <summary>Hộp thoại nhỏ để chọn loại đường và bật/tắt ghi ký hiệu lên đường.</summary>
    internal class LineTypeForm : WinForms.Form
    {
        private readonly WinForms.ComboBox _combo = new WinForms.ComboBox();
        private readonly WinForms.CheckBox _chkLabel = new WinForms.CheckBox();

        public string SelectedKey { get { return _combo.SelectedItem as string; } }

        public bool AddLabel { get { return _chkLabel.Checked; } }

        public LineTypeForm(string caption, IEnumerable<string> keys, string preselect,
                            bool addLabel, string labelCaption)
        {
            Text = caption;
            FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
            StartPosition = WinForms.FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Drawing.Size(290, 125);

            _combo.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
            _combo.SetBounds(15, 15, 260, 24);
            foreach (string s in keys) _combo.Items.Add(s);
            if (preselect != null && _combo.Items.Contains(preselect)) _combo.SelectedItem = preselect;
            else if (_combo.Items.Count > 0) _combo.SelectedIndex = 0;

            _chkLabel.Text = labelCaption;
            _chkLabel.Checked = addLabel;
            _chkLabel.SetBounds(15, 48, 260, 24);

            WinForms.Button ok = new WinForms.Button { Text = "OK", DialogResult = WinForms.DialogResult.OK };
            ok.SetBounds(70, 85, 70, 26);
            WinForms.Button cancel = new WinForms.Button { Text = "Hủy", DialogResult = WinForms.DialogResult.Cancel };
            cancel.SetBounds(150, 85, 70, 26);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(_combo);
            Controls.Add(_chkLabel);
            Controls.Add(ok);
            Controls.Add(cancel);
        }
    }
}

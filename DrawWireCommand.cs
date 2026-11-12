using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WinForms = System.Windows.Forms;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Vẽ Detail Line (đường gấp khúc) theo cỡ dây đã chọn.
    /// Mỗi lần bấm một điểm sẽ vẽ một đoạn nối từ điểm trước, đồng thời ghi con số cỡ dây
    /// giữa đoạn (nếu bật tùy chọn). Bấm Esc để kết thúc.
    /// Dùng Line Style "Dây xx" và kiểu text "KTool - Dây xx" do lệnh "Tạo Style dây điện" tạo ra.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DrawWireCommand : IExternalCommand
    {
        // Nhớ lựa chọn của lần chạy trước
        private static string _lastSize;
        private static bool _lastAddLabel = true;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            if (!SupportsDetailLines(view))
            {
                TaskDialog.Show("Vẽ dây điện",
                    "View hiện tại không vẽ được Detail Line. Hãy chạy lệnh trong view mặt bằng, mặt cắt, mặt đứng hoặc view chi tiết.");
                return Result.Cancelled;
            }

            try
            {
                // 1. Các cỡ dây đã có Line Style trong project
                Category linesCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                var available = WireStyles.Items
                    .Where(d => linesCat.SubCategories.Contains(d.StyleName))
                    .ToList();

                if (available.Count == 0)
                {
                    TaskDialog.Show("Vẽ dây điện",
                        "Project chưa có Line Style dây điện. Hãy chạy lệnh \"Tạo Style dây điện\" trước.");
                    return Result.Cancelled;
                }

                // 2. Chọn cỡ dây và tùy chọn ghi số
                WireStyles.Def def;
                bool addLabel;
                using (WireSizeForm form = new WireSizeForm(
                           available.Select(d => d.Size), _lastSize, _lastAddLabel))
                {
                    if (form.ShowDialog() != WinForms.DialogResult.OK || form.SelectedSize == null)
                        return Result.Cancelled;
                    def = available.First(d => d.Size == form.SelectedSize);
                    addLabel = form.AddLabel;
                }
                _lastSize = def.Size;
                _lastAddLabel = addLabel;

                // Số cỡ dây cần có kiểu text; nếu thiếu thì chỉ vẽ đường
                if (addLabel && def.HasLabel && WireLabels.FindTextType(doc, def.TextTypeName) == null)
                {
                    TaskDialog.Show("Vẽ dây điện",
                        "Chưa có kiểu text số cỡ dây nên sẽ vẽ đường không ghi số." + Environment.NewLine +
                        "Hãy chạy lại lệnh \"Tạo Style dây điện\" để tạo kiểu text.");
                    addLabel = false;
                }

                Category sub = linesCat.SubCategories.get_Item(def.StyleName);
                GraphicsStyle gs = sub.GetGraphicsStyle(GraphicsStyleType.Projection);
                double tol = uiapp.Application.ShortCurveTolerance;

                const ObjectSnapTypes snaps =
                    ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints |
                    ObjectSnapTypes.Intersections | ObjectSnapTypes.Nearest |
                    ObjectSnapTypes.Centers;

                // 3. Vòng lặp chọn điểm; gom tất cả vào một TransactionGroup để Ctrl+Z một lần
                int created = 0;
                XYZ prev = null;

                using (TransactionGroup tg = new TransactionGroup(doc, "KTool - Vẽ dây " + def.Size))
                {
                    tg.Start();

                    while (true)
                    {
                        XYZ picked;
                        try
                        {
                            string prompt = prev == null
                                ? "Dây " + def.Size + ": chọn điểm đầu (Esc để kết thúc)"
                                : "Dây " + def.Size + ": chọn điểm kế tiếp (Esc để kết thúc)";
                            picked = uidoc.Selection.PickPoint(snaps, prompt);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break;
                        }

                        XYZ p = ProjectToView(picked, view);

                        if (prev != null && prev.DistanceTo(p) > tol)
                        {
                            using (Transaction t = new Transaction(doc, "Vẽ đoạn dây"))
                            {
                                t.Start();
                                DetailCurve dc = doc.Create.NewDetailCurve(view, Line.CreateBound(prev, p));
                                dc.LineStyle = gs;
                                if (addLabel && def.HasLabel)
                                    WireLabels.EnsureLabel(doc, view, dc, def, true);
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

        /// <summary>Chiếu điểm lên mặt phẳng của view để Detail Line nằm đúng trong view.</summary>
        private static XYZ ProjectToView(XYZ p, View view)
        {
            XYZ n = view.ViewDirection;
            double dist = (p - view.Origin).DotProduct(n);
            return p - dist * n;
        }

        private static bool SupportsDetailLines(View v)
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
    }
}

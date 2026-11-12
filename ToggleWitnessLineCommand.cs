using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Đảo "Witness Line Control" của Dim:
    ///   Gap to Element  <->  Fixed to Dimension Line
    /// Thông số này nằm ở Type của Dim, nên lệnh KHÔNG sửa trực tiếp Type đang dùng
    /// (tránh làm đổi các Dim khác). Thay vào đó lệnh nhân bản Type với tên "<tên gốc> ~<giá trị>",
    /// đổi thông số trên bản nhân bản rồi gán cho các Dim đã chọn. Lần sau Type này được dùng lại.
    /// Chọn sẵn các Dim trước khi chạy lệnh, hoặc chạy lệnh rồi quét chọn Dim.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ToggleWitnessLineCommand : IExternalCommand
    {
        private const string TITLE = "Witness Line Control";
        private const string TAG = " ~";
        private const BuiltInParameter WLC = BuiltInParameter.DIM_WITNS_LINE_CNTRL;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // 1. Lấy Dim: ưu tiên Dim đang chọn, nếu không thì cho người dùng chọn
            List<Dimension> dims = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<Dimension>()
                .Where(d => d.View != null && d.View.Id == view.Id)
                .ToList();

            if (dims.Count == 0)
            {
                try
                {
                    IList<Reference> picked = uidoc.Selection.PickObjects(
                        ObjectType.Element,
                        new FlipDimCommand.DimensionFilter(),
                        "Chọn các đường Dim cần đảo Witness Line Control, rồi bấm Finish");

                    dims = picked
                        .Select(r => doc.GetElement(r))
                        .OfType<Dimension>()
                        .Where(d => d.View != null && d.View.Id == view.Id)
                        .ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (dims.Count == 0)
            {
                TaskDialog.Show(TITLE, "Chưa chọn đường Dim nào trong view hiện tại.");
                return Result.Cancelled;
            }

            // 2. Đảo thông số
            int done = 0, skipped = 0;
            Dictionary<string, DimensionType> cache = new Dictionary<string, DimensionType>();
            HashSet<string> resultNames = new HashSet<string>();

            using (Transaction tx = new Transaction(doc, TITLE))
            {
                tx.Start();
                try
                {
                    foreach (Dimension dim in dims)
                    {
                        DimensionType src = doc.GetElement(dim.GetTypeId()) as DimensionType;
                        if (src == null) { skipped++; continue; }

                        Parameter p = src.get_Parameter(WLC);
                        if (p == null || p.StorageType != StorageType.Integer) { skipped++; continue; }

                        int target = p.AsInteger() == 0 ? 1 : 0;
                        string key = src.Id.ToString() + ":" + target;

                        DimensionType dst;
                        if (!cache.TryGetValue(key, out dst))
                        {
                            dst = GetOrCreateType(doc, src, target);
                            cache[key] = dst;
                        }
                        if (dst == null) { skipped++; continue; }

                        dim.DimensionType = dst;
                        resultNames.Add(dst.Name);
                        done++;
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

            string msg = "Đã đảo " + done + " Dim.";
            if (resultNames.Count > 0)
                msg += "\nType đang dùng: " + string.Join("; ", resultNames);
            if (skipped > 0)
                msg += "\nBỏ qua " + skipped + " Dim (Type không có thông số Witness Line Control).";
            TaskDialog.Show(TITLE, msg);

            return Result.Succeeded;
        }

        /// <summary>Tìm hoặc tạo Type nhân bản có Witness Line Control = target.</summary>
        private static DimensionType GetOrCreateType(Document doc, DimensionType src, int target)
        {
            string baseName = StripTag(src.Name);
            string tmpName = baseName + TAG + "tmp" + Guid.NewGuid().ToString("N").Substring(0, 6);

            ElementType dup = src.Duplicate(tmpName);
            Parameter dp = dup.get_Parameter(WLC);
            if (dp == null || dp.IsReadOnly)
            {
                doc.Delete(dup.Id);
                return null;
            }
            dp.Set(target);

            string valueText = dp.AsValueString();
            if (string.IsNullOrEmpty(valueText)) valueText = "WLC" + target;
            string finalName = baseName + TAG + valueText;

            DimensionType existing = new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .FirstOrDefault(t => t.Name == finalName);

            if (existing != null)
            {
                doc.Delete(dup.Id);
                return existing;
            }

            dup.Name = finalName;
            return dup as DimensionType;
        }

        private static string StripTag(string name)
        {
            int i = name.LastIndexOf(TAG, StringComparison.Ordinal);
            return i > 0 ? name.Substring(0, i) : name;
        }
    }
}

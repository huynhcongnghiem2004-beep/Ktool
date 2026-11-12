using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Tag by Category cho các đường ống nước / dây điện KTool đã vẽ bằng Detail Line.
    /// Detail Line không Tag được, nên lệnh đặt lên mỗi đường một "đối tượng nền" là family
    /// Detail Item dạng line-based tên KTool_Line (nét ẩn), ghi nội dung vào Comments
    /// (ống: ký hiệu hệ thống; dây: tên Line Style), rồi Tag đối tượng nền đó bằng
    /// family Detail Item Tag mặc định (Tag by Category).
    /// Chọn sẵn các Detail Line để chỉ xử lý các đường đó, nếu không thì xử lý tất cả trong view.
    /// Chạy lại không tạo trùng đối tượng nền hay Tag.
    /// </summary>
    public abstract class TagLineCommandBase : IExternalCommand
    {
        private const string HOST_FAMILY = "KTool_Line";
        private const double TAG_OFFSET_MM = 4.0; // khoảng cách (giấy) từ đường tới Tag
        private const double SAME_TOL = 1e-3;     // ft: coi 2 điểm trùng nhau
        private static readonly string[] PIPE_CODES = { "CW", "C", "HW", "WP", "RP", "VP", "SP", "DP" };
        private const string WIRE_PREFIX = "Dây ";

        protected abstract string Title { get; }
        protected abstract bool IncludePipes { get; }
        protected abstract bool IncludeWires { get; }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document doc = uidoc.Document;
                View view = doc.ActiveView;

                if (!LineLabelHelper.SupportsDetailLines(view))
                {
                    TaskDialog.Show(Title, "View hiện tại không hỗ trợ Detail Line.");
                    return Result.Cancelled;
                }

                // Family nền và family Tag phải có sẵn trong project
                FamilySymbol host = FindHostSymbol(doc);
                if (host == null)
                {
                    TaskDialog.Show(Title,
                        "Project chưa có family nền \"" + HOST_FAMILY + "\" (Detail Item dạng line-based).\n" +
                        "Hãy tạo family theo hướng dẫn rồi nạp vào project (Insert > Load Family).");
                    return Result.Cancelled;
                }

                ElementId tagType = FindTagType(doc);
                if (tagType == null || tagType == ElementId.InvalidElementId)
                {
                    TaskDialog.Show(Title,
                        "Project chưa có family Detail Item Tag.\n" +
                        "Hãy nạp một family Tag cho Detail Items (hiển thị tham số Comments) rồi chạy lại.");
                    return Result.Cancelled;
                }

                // Line Style -> nội dung Tag
                Dictionary<string, string> pipeLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (IncludePipes)
                {
                    foreach (string code in PIPE_CODES)
                    {
                        WaterPipeDefinition def = WaterPipeDefinitions.Get(code);
                        if (def != null && !string.IsNullOrEmpty(def.StyleName))
                            pipeLabels[def.StyleName] = def.Code;
                    }
                }

                // Detail Line cần xử lý
                List<DetailCurve> lines = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .OfType<DetailCurve>()
                    .Where(d => d.OwnerViewId == view.Id)
                    .ToList();

                if (lines.Count == 0)
                {
                    lines = new FilteredElementCollector(doc, view.Id)
                        .OfCategory(BuiltInCategory.OST_Lines)
                        .WhereElementIsNotElementType()
                        .OfType<DetailCurve>()
                        .ToList();
                }

                if (lines.Count == 0)
                {
                    TaskDialog.Show(Title, "Không có Detail Line nào trong view này.");
                    return Result.Cancelled;
                }

                // Đối tượng nền và Tag đã có trong view
                List<FamilyInstance> hosts = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(i => i.Symbol != null &&
                                string.Equals(i.Symbol.FamilyName, HOST_FAMILY, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                HashSet<ElementId> tagged = GetTaggedIds(doc, view);

                XYZ right = view.RightDirection;
                XYZ up = view.UpDirection;
                double offsetFt = TAG_OFFSET_MM / 304.8 * view.Scale;

                int hostsCreated = 0, tagsCreated = 0, alreadyTagged = 0, unknown = 0, failed = 0;

                using (Transaction tx = new Transaction(doc, Title))
                {
                    tx.Start();
                    try
                    {
                        if (!host.IsActive) host.Activate();

                        foreach (DetailCurve dc in lines)
                        {
                            Line ln = dc.GeometryCurve as Line;
                            GraphicsStyle gs = dc.LineStyle as GraphicsStyle;
                            if (ln == null || gs == null)
                            {
                                unknown++;
                                continue;
                            }

                            string label = ResolveLabel(gs.Name, pipeLabels);
                            if (label == null)
                            {
                                unknown++;
                                continue;
                            }

                            XYZ p0 = ln.GetEndPoint(0);
                            XYZ p1 = ln.GetEndPoint(1);

                            try
                            {
                                // Đối tượng nền: dùng lại nếu đã có đúng vị trí, nếu không thì tạo mới
                                FamilyInstance inst = hosts.FirstOrDefault(h => SameSegment(h, p0, p1));
                                if (inst == null)
                                {
                                    inst = doc.Create.NewFamilyInstance(ln, host, view);
                                    hosts.Add(inst);
                                    hostsCreated++;
                                }

                                Parameter comments = inst.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (comments != null && !comments.IsReadOnly && comments.AsString() != label)
                                    comments.Set(label);

                                if (tagged.Contains(inst.Id))
                                {
                                    alreadyTagged++;
                                    continue;
                                }

                                // Điểm đặt Tag: giữa đoạn, lệch lên trên (đường nằm ngang) hoặc sang phải (đường đứng)
                                XYZ mid = (p0 + p1) / 2.0;
                                XYZ d = p1 - p0;
                                bool horizontalOnScreen =
                                    Math.Abs(d.DotProduct(right)) >= Math.Abs(d.DotProduct(up));
                                XYZ pt = mid + (horizontalOnScreen ? up : right) * offsetFt;

                                IndependentTag tag = IndependentTag.Create(
                                    doc, view.Id, new Reference(inst), false,
                                    TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, pt);

                                if (tag == null)
                                {
                                    failed++;
                                    continue;
                                }

                                if (tag.GetTypeId() != tagType)
                                    tag.ChangeTypeId(tagType);

                                tagged.Add(inst.Id);
                                tagsCreated++;
                            }
                            catch (Exception)
                            {
                                failed++;
                            }
                        }
                        tx.Commit();
                    }
                    catch
                    {
                        tx.RollBack();
                        throw;
                    }
                }

                string msg = "Đã tạo " + tagsCreated + " Tag (và " + hostsCreated + " đối tượng nền " + HOST_FAMILY + ").";
                if (alreadyTagged > 0) msg += "\nBỏ qua " + alreadyTagged + " đường đã có Tag.";
                if (unknown > 0) msg += "\nBỏ qua " + unknown + " đường không thuộc " + Scope() + ".";
                if (failed > 0) msg += "\nKhông xử lý được " + failed + " đường (kiểm tra family nền và family Tag).";
                TaskDialog.Show(Title, msg);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private string ResolveLabel(string styleName, Dictionary<string, string> pipeLabels)
        {
            string label;
            if (IncludePipes && pipeLabels.TryGetValue(styleName, out label))
                return label;

            if (IncludeWires && styleName.StartsWith(WIRE_PREFIX, StringComparison.OrdinalIgnoreCase))
                return styleName;

            return null;
        }

        private string Scope()
        {
            if (IncludePipes && IncludeWires) return "ống nước hoặc dây điện";
            return IncludePipes ? "ống nước" : "dây điện";
        }

        private static FamilySymbol FindHostSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => string.Equals(s.FamilyName, HOST_FAMILY, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Family Detail Item Tag mặc định; nếu chưa đặt mặc định thì lấy Tag đầu tiên trong project.</summary>
        private static ElementId FindTagType(Document doc)
        {
            BuiltInCategory cat = BuiltInCategory.OST_DetailComponentTags;
            try
            {
                ElementId def = doc.GetDefaultFamilyTypeId(new ElementId(cat));
                if (def != null && def != ElementId.InvalidElementId && doc.GetElement(def) != null)
                    return def;
            }
            catch (Exception)
            {
                // thử cách dưới
            }

            FamilySymbol fs = new FilteredElementCollector(doc)
                .OfCategory(cat)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();

            return fs != null ? fs.Id : ElementId.InvalidElementId;
        }

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
                    // bỏ qua Tag không đọc được
                }
            }
            return set;
        }

        /// <summary>Đối tượng nền có nằm đúng đoạn p0-p1 không (không phân biệt chiều).</summary>
        private static bool SameSegment(FamilyInstance inst, XYZ p0, XYZ p1)
        {
            LocationCurve lc = inst.Location as LocationCurve;
            if (lc == null) return false;

            Line ln = lc.Curve as Line;
            if (ln == null) return false;

            XYZ a = ln.GetEndPoint(0);
            XYZ b = ln.GetEndPoint(1);

            return (a.DistanceTo(p0) < SAME_TOL && b.DistanceTo(p1) < SAME_TOL)
                || (a.DistanceTo(p1) < SAME_TOL && b.DistanceTo(p0) < SAME_TOL);
        }
    }

    /// <summary>Tag by Category cho các đường ống nước.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TagPipeLinesCommand : TagLineCommandBase
    {
        protected override string Title { get { return "Tag ống nước"; } }
        protected override bool IncludePipes { get { return true; } }
        protected override bool IncludeWires { get { return false; } }
    }

    /// <summary>Tag by Category cho các đường dây điện.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TagWireLinesCommand : TagLineCommandBase
    {
        protected override string Title { get { return "Tag dây điện"; } }
        protected override bool IncludePipes { get { return false; } }
        protected override bool IncludeWires { get { return true; } }
    }
}

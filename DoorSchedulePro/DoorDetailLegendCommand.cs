using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DoorDetailLegend
{
    // Revit 2024, .NET Framework 4.8. Không phụ thuộc các class DoorSchedulePro cũ.
    //
    // Cách dùng: bấm nút, hộp thoại liệt kê TẤT CẢ cửa / cửa sổ trong model (cửa đang chọn sẵn
    // trong Revit được tích sẵn) và cho chọn loại khung Generic Annotation (Type của family T89).
    // Lệnh tạo MỘT Legend chi tiết cho MỖI Family Type khác nhau trong số cửa đã chọn.
    //
    // Về kích thước: Dimension gắn vào Legend Component sẽ HỎNG ("dimension references are or have
    // become invalid") khi đổi Component Type. Vì vậy với Family Type khác Legend nguồn, lệnh xóa
    // các Dimension cũ trong bản sao TRƯỚC khi đổi type, rồi tạo lại kích thước tổng thể (rộng, cao)
    // cho mặt đứng và mặt bằng từ hình học của Legend Component mới.
    [Transaction(TransactionMode.Manual)]
    public class DoorDetailLegendCommand : IExternalCommand
    {
        private const string T89FamilyName = "T89_THÔNG KÊ CỬA";
        private const string CopyNamePrefix = "Chi Tiết Cửa";
        private const int ConfirmAboveCount = 5; // hỏi lại nếu số Family Type vượt ngưỡng này

        // Giá trị integer của parameter "View Direction" trong Legend Component.
        private const int FrontDirection = -7; // mặt đứng
        private const int BackDirection = -6;
        private const int PlanDirection = -8;  // mặt bằng

        // Khoảng cách từ đường Dim tới hình, đơn vị feet nội bộ (~275 mm).
        private const double DimOffsetFeet = 0.9;

        private class DetailResult
        {
            public View View;
            public int Dimensions;
            public int ParametersWritten;
            public string Note;
            public ElementId UpperTagId;
            public ElementId LowerTagId;
            public List<string> ParamNotes = new List<string>();
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument ui = commandData.Application.ActiveUIDocument;
            if (ui == null) { message = "Chưa mở Project Revit."; return Result.Failed; }
            Document doc = ui.Document;

            try
            {
                // 1. Legend nguồn và Family Type hiện có trong đó.
                int sourceCount;
                View source = FindSource(doc, out sourceCount);
                ElementId oldTypeId = ElementId.InvalidElementId;

                // 2. Liệt kê tất cả cửa / cửa sổ và các loại khung T89 cho cửa sổ wizard.
                List<DoorRow> rows = CollectRows(doc, ui);
                if (rows.Count == 0)
                {
                    message = "Model chưa có cửa hoặc cửa sổ nào.";
                    return Result.Failed;
                }

                List<FamilySymbol> frames = GetFrameSymbols(doc);
                long sourceFrameId = -1;
                SetupOptions setup = BuildSetupOptions(doc, source, sourceFrameId);

                List<ElementId> createdViewIds = new List<ElementId>();

                // 3. Cửa sổ WPF modal. Runner chạy ngay trong sự kiện bấm nút nên vẫn nằm trong
                //    ngữ cảnh hợp lệ của Revit API (không cần ExternalEvent).
                DoorWizardWindow wizard = new DoorWizardWindow(rows, setup);
                new System.Windows.Interop.WindowInteropHelper(wizard).Owner = commandData.Application.MainWindowHandle;

                wizard.TagImageProvider = id => RenderTagImage(doc, source, id);
                wizard.TagParameterProvider = id => GetWritableTagParameters(doc, id);
                wizard.ParameterProvider = ids => GetParameterNames(doc, ids);
                wizard.PreviewValueProvider = (ids, name) =>
                {
                    FamilyInstance door = ids.Select(id => doc.GetElement(new ElementId(id)) as FamilyInstance)
                        .FirstOrDefault(d => d != null && d.Symbol != null);
                    if (door == null) return null;
                    if (string.Equals(name, "Count", StringComparison.OrdinalIgnoreCase))
                        return ids.Count(id => (doc.GetElement(new ElementId(id)) as FamilyInstance)?.Symbol?.Id.Value == door.Symbol.Id.Value).ToString();
                    Parameter parameter = FindParam(door, name) ?? FindParam(door.Symbol, name);
                    return parameter == null ? null : ValueText(parameter);
                };
                wizard.Runner = (req, sink) =>
                {
                    sink.Log("Dựng Legend theo thiết lập WPF; chỉ sử dụng phần tử khởi tạo từ: " + source.Name);
                    createdViewIds.AddRange(RunRequest(doc, source, oldTypeId, frames, req, sink));
                };
                wizard.ShowDialog();

                if (createdViewIds.Count == 0) return Result.Cancelled;

                // 4. Mở Legend đầu tiên SAU KHI đóng cửa sổ (đổi view khi đang modal không ổn định).
                try { ui.ActiveView = (View)doc.GetElement(createdViewIds[0]); }
                catch (Exception) { /* không mở được thì người dùng tự mở trong Project Browser */ }

                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        /// <summary>Tạo mỗi Family Type một Legend theo yêu cầu từ wizard. Trả về Id các Legend đã tạo.</summary>
        private static List<ElementId> RunRequest(Document doc, View source, ElementId oldTypeId,
                                                  List<FamilySymbol> frames, WizardRequest req, WizardSink sink)
        {
            List<ElementId> createdIds = new List<ElementId>();

            FamilySymbol frame = (req.FrameIndex >= 0 && req.FrameIndex < frames.Count) ? frames[req.FrameIndex] : null;

            List<FamilyInstance> doors = req.DoorIds
                .Select(id => doc.GetElement(new ElementId(id)) as FamilyInstance)
                .Where(d => d != null && d.Symbol != null)
                .ToList();

            // Gộp theo Family Type: nhiều cửa cùng type chỉ tạo một Legend.
            List<IGrouping<long, FamilyInstance>> groups = doors.GroupBy(d => d.Symbol.Id.Value).ToList();
            if (groups.Count == 0)
            {
                sink.Log("Không có cửa hợp lệ để tạo Legend.");
                return createdIds;
            }

            sink.Log("Đã chọn " + doors.Count + " cửa, " + groups.Count + " Family Type.");
            if (frame != null) sink.Log("Khung Generic Annotation: " + frame.Name);

            if (groups.Count > ConfirmAboveCount &&
                !sink.Confirm("Sẽ tạo " + groups.Count + " Legend (mỗi Family Type một Legend)." +
                              "\nĐã chọn " + doors.Count + " cửa. Tiếp tục?"))
            {
                sink.Log("Đã hủy theo yêu cầu.");
                return createdIds;
            }

            string prefix = string.IsNullOrWhiteSpace(req.NamePrefix) ? CopyNamePrefix : SafeName(req.NamePrefix);
            if (prefix.Length == 0) prefix = CopyNamePrefix;

            int done = 0, failed = 0;
            sink.Report(0, groups.Count);

            // Mỗi Family Type một Transaction riêng: lỗi ở type này không ảnh hưởng type khác.
            foreach (IGrouping<long, FamilyInstance> g in groups)
            {
                if (sink.StopRequested) break;

                FamilyInstance sample = g.First(); // giá trị parameter lấy từ cửa đầu tiên của type
                FamilySymbol symbol = sample.Symbol;
                string label = symbol.FamilyName + " : " + symbol.Name;
                sink.Log("Đang tạo: " + label + " ...");

                try
                {
                    DetailResult r;
                    using (TransactionGroup detailGroup = new TransactionGroup(doc, "Hoàn thiện chi tiết cửa"))
                    {
                        detailGroup.Start();
                        r = CreateDetail(doc, source, oldTypeId, symbol, frame, sample, prefix, req);
                        if (detailGroup.Assimilate() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Không hoàn tất được bộ chi tiết cửa.");
                    }
                    createdIds.Add(r.View.Id);

                    string line = "  OK: " + r.View.Name + " (" + r.Dimensions + " kích thước";
                    if (r.ParametersWritten > 0)
                        line += ", " + r.ParametersWritten + " giá trị parameter";
                    sink.Log(line + ")");
                    if (!string.IsNullOrEmpty(r.Note)) sink.Log("    " + r.Note);
                    foreach (string n in r.ParamNotes) sink.Log("    " + n);
                }
                catch (Exception ex)
                {
                    failed++;
                    sink.Log("  LỖI: " + label + " - " + ex.Message);
                }

                done++;
                sink.Report(done, groups.Count);
            }

            sink.Log("Đã tạo " + createdIds.Count + "/" + groups.Count + " Legend" +
                     (failed > 0 ? ", lỗi " + failed : string.Empty) + ".");
            if (req.PlaceOnSheet && !sink.StopRequested && createdIds.Count > 0)
            {
                try { DoorSheetPlacement.Place(doc, createdIds, req, sink.Log); }
                catch (Exception ex) { sink.Log("LỖI ĐẶT SHEET: " + ex.Message + " Các Legend đã tạo vẫn được giữ trong Project Browser."); }
            }
            return createdIds;
        }

        // ---------------------------------------------------------------- Thiết lập (bước 3 của wizard)

        /// <summary>Điền các ComboBox của tab "Thiết lập triển khai": loại Tag (Generic Annotation) và loại Dim.</summary>
        private static SetupOptions BuildSetupOptions(Document doc, View source, long sourceFrameId)
        {
            SetupOptions o = new SetupOptions();
            o.Sheets.Add(new ChoiceItem { Name = "(Tạo Sheet mới)", Id = -1 });
            o.Sheets.AddRange(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(v => !v.IsPlaceholder).OrderBy(v => v.SheetNumber).Select(v => new ChoiceItem { Name = v.SheetNumber + " - " + v.Name, Id = v.Id.Value }));
            o.TitleBlocks.AddRange(new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>().OrderBy(f => f.FamilyName).ThenBy(f => f.Name)
                .Select(f => new ChoiceItem { Name = f.FamilyName + " : " + f.Name, Id = f.Id.Value }));
            if (o.TitleBlocks.Count > 0) o.DefaultTitleBlockId = o.TitleBlocks[0].Id;
            var usedBlock = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilyInstance>().GroupBy(f=>f.Symbol.Id.Value).OrderByDescending(g=>g.Count()).FirstOrDefault();
            if(usedBlock!=null)o.DefaultTitleBlockId=usedBlock.Key;

            List<FamilySymbol> tags = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .Cast<FamilySymbol>()
                .OrderBy(t => t.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (FamilySymbol t in tags)
            {
                o.TagTypes.Add(new ChoiceItem { Name = t.FamilyName + " : " + t.Name, Id = t.Id.Value });
                // Mặc định: Type có chữ UPPER / LOWER trong tên (nếu có).
                if (o.DefaultUpperTagId < 0 && t.Name.IndexOf("UPPER", StringComparison.OrdinalIgnoreCase) >= 0)
                    o.DefaultUpperTagId = t.Id.Value;
                if (o.DefaultLowerTagId < 0 && t.Name.IndexOf("LOWER", StringComparison.OrdinalIgnoreCase) >= 0)
                    o.DefaultLowerTagId = t.Id.Value;
            }
            if (o.DefaultUpperTagId < 0) o.DefaultUpperTagId = sourceFrameId;

            foreach (DimensionType dt in new FilteredElementCollector(doc)
                         .OfClass(typeof(DimensionType)).Cast<DimensionType>()
                         .Where(d => d.StyleType == DimensionStyleType.Linear)
                         .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                o.DimTypes.Add(new ChoiceItem { Name = dt.Name, Id = dt.Id.Value });
            }

            if (o.DimTypes.Count > 0) o.DefaultDimTypeId = o.DimTypes[0].Id;

            return o;
        }

        // Xuất hình của instance thật trong drafting view tạm, không dùng thumbnail của family.
        // Rollback cả TransactionGroup để không để lại view, instance hay thay đổi IsActive.
        private static System.Windows.Media.ImageSource RenderTagImage(Document doc, View source, long id)
        {
            FamilySymbol symbol = doc.GetElement(new ElementId(id)) as FamilySymbol;
            if (symbol == null) throw new InvalidOperationException("Không tìm thấy loại Tag đã chọn.");
            ViewFamilyType draftingType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);
            if (draftingType == null) throw new InvalidOperationException("Project chưa có loại Drafting View để xuất hình Tag.");
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KToolTagPreview_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            try
            {
                using (TransactionGroup group = new TransactionGroup(doc, "Xem trước Tag (tạm)"))
                {
                    group.Start();
                    try
                    {
                        ElementId previewId;
                        using (Transaction transaction = new Transaction(doc, "Đặt Tag để xuất hình"))
                        {
                            transaction.Start();
                            ViewDrafting preview = ViewDrafting.Create(doc, draftingType.Id);
                            preview.Scale = source.Scale;
                            preview.DetailLevel = ViewDetailLevel.Fine;
                            preview.DisplayStyle = DisplayStyle.Wireframe;
                            if (!symbol.IsActive) symbol.Activate();
                            doc.Regenerate();
                            doc.Create.NewFamilyInstance(XYZ.Zero, symbol, preview);
                            doc.Regenerate();
                            previewId = preview.Id;
                            if (transaction.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Không tạo được hình xem trước Tag.");
                        }
                        ImageExportOptions options = new ImageExportOptions
                        {
                            FilePath = System.IO.Path.Combine(folder, "tag"),
                            ExportRange = ExportRange.SetOfViews,
                            ZoomType = ZoomFitType.FitToPage,
                            FitDirection = FitDirectionType.Horizontal,
                            PixelSize = 1200,
                            ImageResolution = ImageResolution.DPI_150,
                            HLRandWFViewsFileType = ImageFileType.PNG,
                            ShadowViewsFileType = ImageFileType.PNG
                        };
                        options.SetViewsAndSheets(new List<ElementId> { previewId });
                        doc.ExportImage(options);
                        string file = System.IO.Directory.GetFiles(folder, "*.png").FirstOrDefault();
                        if (file == null) throw new InvalidOperationException("Revit không xuất được hình Tag.");
                        var image = new System.Windows.Media.Imaging.BitmapImage();
                        image.BeginInit();
                        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        image.UriSource = new Uri(file, UriKind.Absolute);
                        image.EndInit();
                        image.Freeze();
                        return image;
                    }
                    finally { if (group.GetStatus() == TransactionStatus.Started) group.RollBack(); }
                }
            }
            finally
            {
                try
                {
                    foreach (string file in System.IO.Directory.GetFiles(folder)) System.IO.File.Delete(file);
                    System.IO.Directory.Delete(folder);
                }
                catch (System.IO.IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // ---------------------------------------------------------------- Parameter (bước 2 của wizard)

        /// <summary>Tên mọi parameter (instance + type) của các cửa đã chọn. Mỗi Family Type chỉ đọc một lần.</summary>
        private static List<string> GetParameterNames(Document doc, List<long> doorIds)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            HashSet<long> seenTypes = new HashSet<long>();
            foreach (long id in doorIds)
            {
                FamilyInstance fi = doc.GetElement(new ElementId(id)) as FamilyInstance;
                if (fi == null || fi.Symbol == null) continue;
                if (!seenTypes.Add(fi.Symbol.Id.Value)) continue;
                AddParameterNames(fi, names);
                AddParameterNames(fi.Symbol, names);
            }
            return names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void AddParameterNames(Element e, HashSet<string> names)
        {
            foreach (Parameter p in e.Parameters)
            {
                Definition def = p.Definition;
                if (def != null && !string.IsNullOrWhiteSpace(def.Name)) names.Add(def.Name);
            }
        }

        private static Parameter FindParam(Element e, string name)
        {
            foreach (Parameter p in e.Parameters)
            {
                Definition def = p.Definition;
                if (def != null && string.Equals(def.Name, name, StringComparison.CurrentCultureIgnoreCase)) return p;
            }
            return null;
        }

        /// <summary>
        /// Ghi giá trị parameter của cửa vào các khung T89 trong Legend. Cột "Việt hóa" là tên parameter đích.
        /// Chỉ ghi INSTANCE parameter của khung: parameter Type sẽ đổi mọi khung cùng Type nên bị bỏ qua.
        /// </summary>
        private static int WriteFrameParameters(Document doc, View view, FamilyInstance door,
                                                List<ParamMap> map, List<string> notes)
        {
            List<FamilyInstance> frames = GetTagInstances(doc, view);
            if (frames.Count == 0)
            {
                notes.Add("Legend không có khung / Tag để ghi parameter.");
                return 0;
            }

            int written = 0;
            foreach (ParamMap m in map)
            {
                Parameter src = FindParam(door, m.Name) ?? FindParam(door.Symbol, m.Name);
                if (src == null)
                {
                    notes.Add("Cửa không có parameter '" + m.Name + "'.");
                    continue;
                }

                string target = string.IsNullOrWhiteSpace(m.Label) ? m.Name : m.Label.Trim();
                bool any = false;
                foreach (FamilyInstance f in frames)
                {
                    Parameter dst = FindParam(f, target);
                    if (dst != null && CopyValue(src, dst)) any = true;
                }

                if (any) written++;
                else notes.Add("Khung / Tag không có instance parameter ghi được tên '" + target + "' (nguồn: " + m.Name + ").");
            }
            return written;
        }

        private static List<string> GetWritableTagParameters(Document doc, long typeId)
        {
            using (TransactionGroup group = new TransactionGroup(doc, "Đọc parameter Tag (tạm)"))
            {
                group.Start();
                try
                {
                    using (Transaction t = new Transaction(doc, "Đọc Tag"))
                    {
                        t.Start();
                        FamilySymbol symbol = doc.GetElement(new ElementId(typeId)) as FamilySymbol;
                        if (symbol == null) return new List<string>();
                        if (!symbol.IsActive) symbol.Activate();
                        ViewFamilyType vt = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>().First(x => x.ViewFamily == ViewFamily.Drafting);
                        ViewDrafting view = ViewDrafting.Create(doc, vt.Id);
                        FamilyInstance tag = doc.Create.NewFamilyInstance(XYZ.Zero, symbol, view);
                        var names = tag.Parameters.Cast<Parameter>().Where(p => !p.IsReadOnly &&
                            p.StorageType == StorageType.String).Select(p => p.Definition.Name)
                            .Distinct().OrderBy(n => n).ToList();
                        t.RollBack();
                        return names;
                    }
                }
                catch { return new List<string>(); }
                finally { group.RollBack(); }
            }
        }

        private static int WriteUpperFields(Document doc, FamilyInstance tag, FamilyInstance door,
                                             WizardRequest req, List<string> notes)
        {
            int written = 0;
            if (req.UpperFields == null) return 0;
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < req.UpperFields.Count; i++)
            {
                TagField field = req.UpperFields[i];
                Parameter resolved = ResolveUpperTarget(tag, field, i);
                if (field.Param == null && string.IsNullOrEmpty(field.Prefix))
                {
                    if (resolved != null) resolved.Set(string.Empty);
                    continue;
                }
                if (tag == null) throw new InvalidOperationException("Đã chọn Parameter " + (i+1) + " nhưng chưa chọn Tag trên.");
                Parameter dst = resolved;
                if (dst != null && !destinations.Add(dst.Definition.Name))
                    throw new InvalidOperationException("Các dòng Tag ghi trùng parameter đích: " + field.Target);
                string value = string.Empty;
                if (string.Equals(field.Param, "Count", StringComparison.OrdinalIgnoreCase))
                    value = req.DoorIds.Count(id => (doc.GetElement(new ElementId(id)) as FamilyInstance)?.Symbol.Id == door.Symbol.Id).ToString();
                else if (field.Param != null)
                {
                    Parameter src = FindParam(door, field.Param) ?? FindParam(door.Symbol, field.Param);
                    if (src == null) throw new InvalidOperationException("Cửa không có parameter: " + field.Param);
                    value = DoorValue(door, field.Param, req);
                }
                string finalValue = (field.Prefix ?? string.Empty) + value;
                if (dst != null && dst.AsString() != finalValue)
                {
                    dst.Set(finalValue);
                    doc.Regenerate();
                    dst = FindParam(doc.GetElement(tag.Id), dst.Definition.Name);
                    if (dst.AsString() != finalValue)
                        throw new InvalidOperationException("Không ghi được parameter Tag: " + dst.Definition.Name);
                }
                notes.Add("Parameter " + (i+1) + ": " + (field.Prefix ?? string.Empty) + value
                    + (dst == null ? " (hiển thị bằng TextNote cạnh Tag)" : " → " + dst.Definition.Name));
                written++;
            }
            return written;
        }

        private static string DoorValue(FamilyInstance door, string name, WizardRequest req)
        {
            if (string.Equals(name, "Count", StringComparison.OrdinalIgnoreCase))
                return req.DoorIds.Count(id => (door.Document.GetElement(new ElementId(id)) as FamilyInstance)?.Symbol.Id == door.Symbol.Id).ToString();
            Parameter instance = FindParam(door, name);
            string text = instance == null ? "" : ValueText(instance);
            return !string.IsNullOrWhiteSpace(text) ? text : ValueTextOrEmpty(FindParam(door.Symbol, name));
        }

        private static string ValueTextOrEmpty(Parameter p) { return p == null ? "" : ValueText(p); }

        private static Parameter ResolveUpperTarget(FamilyInstance tag, TagField field, int index)
        {
            if (tag == null) return null;
            string[] names = string.IsNullOrWhiteSpace(field.Target)
                ? new[] { "Parameter" + (index+1), "Parameter " + (index+1) }
                : new[] { field.Target };
            return names.Select(n => FindParam(tag,n)).FirstOrDefault(p => p != null &&
                !p.IsReadOnly && p.StorageType == StorageType.String);
        }

        private static void ShowUpperValues(Document doc, View view, FamilyInstance tag,
                                             FamilyInstance door, WizardRequest req, List<string> notes)
        {
            if (tag == null || req.UpperFields == null) return;
            var rows = req.UpperFields.Select((f,i) => new { Field=f, Index=i })
                .Where(x => ResolveUpperTarget(tag,x.Field,x.Index) == null &&
                    (x.Field.Param != null || !string.IsNullOrWhiteSpace(x.Field.Prefix)))
                .Select(x => (x.Field.Prefix ?? "") + (x.Field.Param == null ? "" : DoorValue(door,x.Field.Param,req))).ToList();
            if (rows.Count == 0) return;
            ElementId textType = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (textType == ElementId.InvalidElementId)
                throw new InvalidOperationException("Project chưa có TextNoteType để thể hiện giá trị Tag.");
            TextNoteType infoType = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>().FirstOrDefault(t => t.Name == "KTool_Door_Info_2.5mm");
            if (infoType == null)
            {
                infoType = (TextNoteType)((TextNoteType)doc.GetElement(textType)).Duplicate("KTool_Door_Info_2.5mm");
                infoType.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(2.5 / 304.8);
            }
            textType = infoType.Id;
            BoundingBoxXYZ box = tag.get_BoundingBox(view);
            if (box == null) throw new InvalidOperationException("Không xác định được vị trí Tag để thể hiện giá trị.");
            var corners = BoxCorners(box).ToList();
            double x = corners.Max(p => p.DotProduct(view.RightDirection)) + 3.0 / 304.8 * view.Scale;
            double y = corners.Max(p => p.DotProduct(view.UpDirection));
            TextNote.Create(doc, view.Id, view.RightDirection * x + view.UpDirection * y,
                string.Join("\n", rows), new TextNoteOptions(textType) { HorizontalAlignment = HorizontalTextAlignment.Left });
            notes.Add("Đã thể hiện " + rows.Count + " dòng giá trị bằng TextNote cạnh Tag; giá trị lấy từ cửa lúc triển khai.");
        }

        private static void DrawDetailFrame(Document doc, View view)
        {
            var points = new List<XYZ>();
            foreach (Element e in new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType())
            {
                if (e is CurveElement || (e is FamilyInstance anchor && anchor.Symbol.FamilyName.StartsWith("KTool_DoorDim_"))) continue;
                Dimension dimension = e as Dimension;
                if (dimension != null)
                {
                    points.AddRange(DimensionLayoutPoints(dimension));
                    continue;
                }
                if (!(e is TextNote) && !(e is FamilyInstance) &&
                    e.Category?.Id.Value != (long)BuiltInCategory.OST_LegendComponents) continue;
                BoundingBoxXYZ box = e.get_BoundingBox(view);
                if (box != null) points.AddRange(e.Category?.Id.Value == (long)BuiltInCategory.OST_LegendComponents ? ComponentPoints(e,view) : BoxCorners(box));
            }
            if (points.Count == 0) return;
            double gap = 5.0 / 304.8 * view.Scale;
            double x0 = points.Min(p => p.DotProduct(view.RightDirection)) - gap;
            double x1 = points.Max(p => p.DotProduct(view.RightDirection)) + gap;
            double y0 = points.Min(p => p.DotProduct(view.UpDirection)) - gap;
            double y1 = points.Max(p => p.DotProduct(view.UpDirection)) + gap;
            XYZ[] corners = { view.RightDirection*x0+view.UpDirection*y0, view.RightDirection*x1+view.UpDirection*y0,
                view.RightDirection*x1+view.UpDirection*y1, view.RightDirection*x0+view.UpDirection*y1 };
            for (int i=0; i<4; i++) { var line=doc.Create.NewDetailCurve(view, Line.CreateBound(corners[i], corners[(i+1)%4])); SetLayoutRole(line,"KTool_Door_Frame"); }
        }

        private static double DoorSize(FamilyInstance door, bool width)
        {
            BuiltInParameter bip = width ? BuiltInParameter.DOOR_WIDTH : BuiltInParameter.DOOR_HEIGHT;
            Parameter p = door.Symbol.get_Parameter(bip);
            if (p == null || p.StorageType != StorageType.Double || p.AsDouble() <= 0)
                p = door.get_Parameter(bip);
            if (p == null || p.StorageType != StorageType.Double || p.AsDouble() <= 0)
                throw new InvalidOperationException("Không đọc được " + (width ? "chiều rộng" : "chiều cao") + " cửa từ parameter.");
            return p.AsDouble();
        }

        private sealed class AnchorFamilyLoad : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool used, out bool overwrite) { overwrite = true; return true; }
            public bool OnSharedFamilyFound(Family shared, bool used, out FamilySource source, out bool overwrite)
            { source = FamilySource.Family; overwrite = true; return true; }
        }

        private static FamilySymbol EnsureAnchorSymbol(Document doc, double width, double height)
        {
            string key = "KTool_DoorDim_" + Math.Round(width * 304800).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "_" + Math.Round(height * 304800).ToString(System.Globalization.CultureInfo.InvariantCulture);
            FamilySymbol existing = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .FirstOrDefault(f => f.FamilyName == key);
            if (existing != null) return existing;
            string templateRoot = doc.Application.FamilyTemplatePath;
            string template = System.IO.Directory.Exists(templateRoot)
                ? System.IO.Directory.GetFiles(templateRoot, "*.rft", System.IO.SearchOption.AllDirectories)
                    .FirstOrDefault(f => string.Equals(System.IO.Path.GetFileName(f), "Metric Detail Item.rft", StringComparison.OrdinalIgnoreCase)) : null;
            if (template == null)
            {
                string standard = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Autodesk", "RVT 2024", "Family Templates", "English", "Metric Detail Item.rft");
                if (System.IO.File.Exists(standard)) template = standard;
            }
            if (template == null) throw new InvalidOperationException("Cần template Metric Detail Item.rft để tạo family neo Dim ẩn.");
            Document familyDoc = doc.Application.NewFamilyDocument(template);
            try
            {
                using (Transaction t = new Transaction(familyDoc, "Tạo Detail Item neo Dim"))
                {
                    t.Start();
                    View familyView = new FilteredElementCollector(familyDoc).OfClass(typeof(View)).Cast<View>()
                        .First(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan);
                    Action<string, XYZ, XYZ> plane = (name, a, b) => {
                        ReferencePlane rp = familyDoc.FamilyCreate.NewReferencePlane(a, b, XYZ.BasisZ, familyView);
                        rp.Name = name;
                        rp.get_Parameter(BuiltInParameter.ELEM_IS_REFERENCE).Set((int)FamilyInstanceReferenceType.StrongReference);
                    };
                    plane("KTool_Left", XYZ.Zero, new XYZ(0,height,0));
                    plane("KTool_Right", new XYZ(width,0,0), new XYZ(width,height,0));
                    plane("KTool_Bottom", XYZ.Zero, new XYZ(width,0,0));
                    plane("KTool_Top", new XYZ(0,height,0), new XYZ(width,height,0));
                    DetailCurve curve = familyDoc.FamilyCreate.NewDetailCurve(familyView,
                        Line.CreateBound(XYZ.Zero,new XYZ(width,0,0)));
                    GraphicsStyle invisible = curve.GetLineStyleIds().Select(id => familyDoc.GetElement(id) as GraphicsStyle)
                        .FirstOrDefault(g => g != null && (g.GraphicsStyleCategory.Id.Value == (long)BuiltInCategory.OST_InvisibleLines
                            || g.Name.IndexOf("invisible", StringComparison.OrdinalIgnoreCase) >= 0));
                    if (invisible == null) throw new InvalidOperationException("Template Detail Item không có Invisible Lines.");
                    curve.LineStyle = invisible;
                    familyDoc.FamilyManager.NewType("Anchor");
                    if (t.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Không tạo được family neo Dim.");
                }
                string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KTool_DoorDimFamilies");
                System.IO.Directory.CreateDirectory(folder);
                familyDoc.SaveAs(System.IO.Path.Combine(folder,key+".rfa"),new SaveAsOptions { OverwriteExistingFile = true });
                Family loaded = familyDoc.LoadFamily(doc, new AnchorFamilyLoad());
                if (loaded == null) throw new InvalidOperationException("Không nạp được Detail Item neo Dim.");
                return doc.GetElement(loaded.GetFamilySymbolIds().First()) as FamilySymbol;
            }
            finally { familyDoc.Close(false); }
        }

        private static int CreateAnchorDimensions(Document doc, View view, FamilyInstance door)
        {
            double width = DoorSize(door, true), height = DoorSize(door, false);
            double dimOffset = 6.0 / 304.8 * view.Scale;
            FamilySymbol anchorType = EnsureAnchorSymbol(doc, width, height);
            int count = 0;
            using (Transaction t = new Transaction(doc, "Dim cửa bằng Detail Item invisible"))
            {
                t.Start();
                if (!anchorType.IsActive) anchorType.Activate();
                foreach (Element component in GetLegendComponents(doc, view))
                {
                    int d = component.get_Parameter(BuiltInParameter.LEGEND_COMPONENT_VIEW).AsInteger();
                    if (d != FrontDirection && d != BackDirection && d != PlanDirection) continue;
                    BoundingBoxXYZ box = component.get_BoundingBox(view);
                    if (box == null) throw new InvalidOperationException("Không xác định được vị trí cửa để đặt Detail Item.");
                    var corners = ComponentPoints(component, view).ToList();
                    double center = (corners.Min(p => p.DotProduct(view.RightDirection)) + corners.Max(p => p.DotProduct(view.RightDirection))) / 2;
                    double bottom = corners.Min(p => p.DotProduct(view.UpDirection));
                    double left = center - width/2, right = center + width/2;
                    Func<double,double,XYZ> pt = (x,y) => view.RightDirection*x+view.UpDirection*y;
                    FamilyInstance anchor = PlaceAnnotationViaDrafting(doc, view, anchorType, pt(left,bottom));
                    doc.Regenerate();
                    Reference l = anchor.GetReferenceByName("KTool_Left"), r = anchor.GetReferenceByName("KTool_Right");
                    Reference low = anchor.GetReferenceByName("KTool_Bottom"), high = anchor.GetReferenceByName("KTool_Top");
                    if (l==null || r==null || low==null || high==null) throw new InvalidOperationException("Detail Item thiếu reference neo Dim.");
                    ReferenceArray horizontal = new ReferenceArray(); horizontal.Append(l); horizontal.Append(r);
                    Dimension w = doc.Create.NewDimension(view, Line.CreateBound(pt(left,bottom-dimOffset), pt(right,bottom-dimOffset)), horizontal);
                    doc.Regenerate();
                    if (w==null)
                        throw new InvalidOperationException("Dim rộng không khớp parameter cửa.");
                    count++;
                    if (d != PlanDirection)
                    {
                        ReferenceArray vertical = new ReferenceArray(); vertical.Append(low); vertical.Append(high);
                        Dimension h = doc.Create.NewDimension(view, Line.CreateBound(pt(right+dimOffset,bottom),pt(right+dimOffset,bottom+height)),vertical);
                        doc.Regenerate();
                        if(h==null)
                            throw new InvalidOperationException("Dim cao không khớp parameter cửa.");
                        count++;
                    }
                }
                if(t.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Revit không giữ được Dim Detail Item.");
            }
            var committed = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
            if(committed.Count!=count)
                throw new InvalidOperationException("Dim mất reference sau Commit; đã hủy bộ chi tiết.");
            return count;
        }

        private static bool CopyValue(Parameter src, Parameter dst)
        {
            if (dst.IsReadOnly) return false;
            try
            {
                switch (dst.StorageType)
                {
                    case StorageType.String:
                        return dst.Set(ValueText(src));

                    case StorageType.Double:
                        if (src.StorageType == StorageType.Double && SameDataType(src, dst))
                            return dst.Set(src.AsDouble());
                        string text = src.AsValueString();
                        return !string.IsNullOrEmpty(text) && dst.SetValueString(text);

                    case StorageType.Integer:
                        if (src.StorageType == StorageType.Integer) return dst.Set(src.AsInteger());
                        int iv;
                        return int.TryParse(ValueText(src), out iv) && dst.Set(iv);

                    case StorageType.ElementId:
                        return src.StorageType == StorageType.ElementId && dst.Set(src.AsElementId());

                    default:
                        return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool SameDataType(Parameter a, Parameter b)
        {
            try { return a.Definition.GetDataType().Equals(b.Definition.GetDataType()); }
            catch (Exception) { return false; }
        }

        private static string ValueText(Parameter p)
        {
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString() ?? string.Empty;
                case StorageType.Double:
                case StorageType.Integer:
                case StorageType.ElementId: return p.AsValueString() ?? string.Empty;
                default: return string.Empty;
            }
        }

        // ---------------------------------------------------------------- Liệt kê cửa

        /// <summary>Lấy mọi cửa đi và cửa sổ (FamilyInstance) trong model, sắp theo Family : Type.</summary>
        private static List<DoorRow> CollectRows(Document doc, UIDocument ui)
        {
            HashSet<long> preselected = new HashSet<long>(ui.Selection.GetElementIds().Select(i => i.Value));

            ElementMulticategoryFilter filter = new ElementMulticategoryFilter(
                new List<BuiltInCategory> { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows });

            List<DoorRow> rows = new List<DoorRow>();
            foreach (FamilyInstance fi in new FilteredElementCollector(doc)
                         .OfClass(typeof(FamilyInstance)).WherePasses(filter).Cast<FamilyInstance>())
            {
                if (fi.Symbol == null || fi.Category == null) continue;

                Parameter mark = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                Level level = doc.GetElement(fi.LevelId) as Level;

                rows.Add(new DoorRow
                {
                    Id = fi.Id.Value,
                    SymbolId = fi.Symbol.Id.Value,
                    Mark = mark != null ? (mark.AsString() ?? string.Empty) : string.Empty,
                    TypeName = fi.Symbol.FamilyName + " : " + fi.Symbol.Name,
                    Level = level != null ? level.Name : string.Empty,
                    Size = SizeText(fi),
                    IsWindow = fi.Category.Id.Value == (long)BuiltInCategory.OST_Windows,
                    Preselected = preselected.Contains(fi.Id.Value)
                });
            }

            return rows
                .OrderBy(r => r.TypeName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.Level, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.Mark, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static string SizeText(FamilyInstance fi)
        {
            double? w = ReadLengthMm(fi, "FAMILY_WIDTH_PARAM", "DOOR_WIDTH", "WINDOW_WIDTH");
            double? h = ReadLengthMm(fi, "FAMILY_HEIGHT_PARAM", "DOOR_HEIGHT", "WINDOW_HEIGHT");
            if (w == null || h == null) return string.Empty;
            return ((int)Math.Round(w.Value)).ToString() + " x " + ((int)Math.Round(h.Value)).ToString();
        }

        /// <summary>Thử lần lượt các BuiltInParameter (tra theo tên lúc chạy), lấy giá trị đầu tiên đọc được.</summary>
        private static double? ReadLengthMm(FamilyInstance fi, params string[] builtInNames)
        {
            foreach (string name in builtInNames)
            {
                BuiltInParameter bip;
                if (!Enum.TryParse<BuiltInParameter>(name, out bip)) continue;
                Parameter p = fi.Symbol.get_Parameter(bip) ?? fi.get_Parameter(bip);
                if (p == null || p.StorageType != StorageType.Double) continue;
                return UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
            }
            return null;
        }


        // ---------------------------------------------------------------- Khung Generic Annotation (T89)

        /// <summary>Các Type của family khung T89, sắp theo tên.</summary>
        private static List<FamilySymbol> GetFrameSymbols(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .Cast<FamilySymbol>()
                .Where(s => IsFrameFamily(s.FamilyName))
                .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Id loại khung T89 đang dùng trong Legend nguồn, -1 nếu không có.</summary>
        private static long GetSourceFrameId(Document doc, View source)
        {
            FamilyInstance fi = GetFrameInstances(doc, source).FirstOrDefault(x => x.Symbol != null);
            return fi != null ? fi.Symbol.Id.Value : -1;
        }

        private static List<FamilyInstance> GetFrameInstances(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(x => x.Symbol != null && IsFrameFamily(x.Symbol.FamilyName))
                .ToList();
        }

        /// <summary>Family khung T89: so theo tiền tố "T89" để không lệ thuộc cách gõ dấu của phần tên sau.</summary>
        private static bool IsFrameFamily(string familyName)
        {
            return !string.IsNullOrEmpty(familyName) &&
                   familyName.StartsWith("T89", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Mọi Generic Annotation (khung T89 hoặc Tag đã thay) trong view.</summary>
        private static List<FamilyInstance> GetTagInstances(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_GenericAnnotation)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(x => x.Symbol != null)
                .ToList();
        }

        /// <summary>Đổi mọi khung T89 trong view sang loại đã chọn.</summary>
        private static void ApplyFrame(Document doc, View view, FamilySymbol frame)
        {
            if (!frame.IsActive) frame.Activate();
            List<FamilyInstance> list = GetFrameInstances(doc, view)
                .Where(x => x.Symbol.Id.Value != frame.Id.Value).ToList();
            foreach (FamilyInstance fi in list) fi.ChangeTypeId(frame.Id);
        }

        // ---------------------------------------------------------------- Tạo Legend

        private static View FindSource(Document doc, out int count)
        {
            List<View> sources = new FilteredElementCollector(doc).OfClass(typeof(View))
                .Cast<View>().Where(v => !v.IsTemplate && v.ViewType == ViewType.Legend)
                .OrderBy(v => v.Id.Value)
                .Where(v => GetLegendComponents(doc, v).Any(IsUsableSeed)).ToList();
            count = sources.Count;
            if (count == 0)
                throw new InvalidOperationException("Model chưa có Legend Component. Hãy đặt một Legend Component bất kỳ vào một Legend rồi chạy lại; không cần Legend mẫu hay khung T89.");
            return sources[0];
        }

        private static bool IsUsableSeed(Element e)
        {
            if (e == null || !e.IsValidObject) return false;
            Parameter type = GetParam(e, "LEGEND_COMPONENT", "Component Type");
            Parameter direction = GetParam(e, "LEGEND_COMPONENT_VIEW", "View Direction");
            return type != null && !type.IsReadOnly && type.StorageType == StorageType.ElementId
                && direction != null && !direction.IsReadOnly && direction.StorageType == StorageType.Integer;
        }

        private static void ConfigureComponent(Document doc, View view, Element component,
                                               ElementId typeId, int direction, bool below)
        {
            if (component.Pinned) component.Pinned = false;
            Parameter type = GetParam(component, "LEGEND_COMPONENT", "Component Type");
            Parameter dir = GetParam(component, "LEGEND_COMPONENT_VIEW", "View Direction");
            if (type.AsElementId() != typeId) type.Set(typeId);
            if (dir.AsInteger() != direction) dir.Set(direction);
            if (type.AsElementId() != typeId || dir.AsInteger() != direction)
                throw new InvalidOperationException("Không đổi được loại cửa hoặc hướng của Legend Component.");
            doc.Regenerate();
            BoundingBoxXYZ box = component.get_BoundingBox(view);
            if (box == null) throw new InvalidOperationException("Loại cửa không có hình Legend ở hướng đã chọn.");
            var points = ComponentPoints(component, view).ToList();
            double center = (points.Min(x => x.DotProduct(view.RightDirection))
                + points.Max(x => x.DotProduct(view.RightDirection))) / 2;
            double edge = below ? points.Max(x => x.DotProduct(view.UpDirection))
                : points.Min(x => x.DotProduct(view.UpDirection));
            double target = below ? -14.0 / 304.8 * view.Scale : 0;
            ElementTransformUtils.MoveElement(doc, component.Id,
                -center * view.RightDirection + (target - edge) * view.UpDirection);
        }

        private static DetailResult CreateDetail(Document doc, View source, ElementId oldTypeId,
                                                 FamilySymbol symbol, FamilySymbol frame,
                                                 FamilyInstance sampleDoor, string prefix, WizardRequest req)
        {
            DetailResult result = new DetailResult();
            DimensionFailureHandler handler = new DimensionFailureHandler(doc);
            bool typeChanged = true;

            using (Transaction t = new Transaction(doc, "Triển khai chi tiết cửa: " + symbol.Name))
            {
                // Nếu lúc Commit Revit báo "dimension references invalid", chỉ xóa đúng các Dimension lỗi
                // thay vì bắt người dùng bấm Cancel. Lỗi khác thì rollback.
                t.Start();
                FailureHandlingOptions fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(handler);
                fho.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(fho);
                try
                {
                    if (!symbol.IsActive) symbol.Activate();

                    // Copy only a seed component into an empty duplicate: source annotations,
                    // dimensions, frames and layout never become part of the generated detail.
                    View copy = doc.GetElement(source.Duplicate(ViewDuplicateOption.WithDetailing)) as View;
                    if (copy == null) throw new InvalidOperationException("Không tạo được Legend trống.");
                    copy.Name = UniqueViewName(doc, BuildCopyName(prefix, symbol));
                    copy.ViewTemplateId = ElementId.InvalidElementId;
                    foreach (ElementId filter in copy.GetFilters().ToList()) copy.RemoveFilter(filter);
                    foreach (BuiltInCategory category in new[] { BuiltInCategory.OST_Dimensions,
                        BuiltInCategory.OST_DetailComponents, BuiltInCategory.OST_GenericAnnotation,
                        BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_Lines })
                    {
                        Category visibleCategory = Category.GetCategory(doc, category);
                        if (visibleCategory != null && copy.CanCategoryBeHidden(visibleCategory.Id))
                            copy.SetCategoryHidden(visibleCategory.Id, false);
                    }
                    doc.Regenerate();
                    // Work entirely in the duplicate. Keep its seed alive while removing
                    // inherited content; do not delete every component then reuse a wrapper.
                    foreach (Group group in new FilteredElementCollector(doc).OwnedByView(copy.Id)
                                 .OfClass(typeof(Group)).Cast<Group>().ToList())
                        if (group.IsValidObject) group.UngroupMembers();
                    doc.Regenerate();
                    Element front = GetLegendComponents(doc, copy).FirstOrDefault(IsUsableSeed);
                    if (front == null) throw new InvalidOperationException("Bản sao không có Legend Component hợp lệ để khởi tạo.");
                    ElementId frontId = front.Id;
                    var inherited = new FilteredElementCollector(doc).OwnedByView(copy.Id)
                        .WhereElementIsNotElementType()
                        .Where(e => e.Id != frontId && e.Id != copy.Id).Select(e => e.Id).ToList();
                    ElementId copyId = copy.Id;
                    foreach (ElementId id in inherited)
                    {
                        if (doc.GetElement(id) == null) continue;
                        using (SubTransaction cleanup = new SubTransaction(doc))
                        {
                            cleanup.Start();
                            try
                            {
                                ICollection<ElementId> removed = doc.Delete(id);
                                // Revit may include internal owner/dependency elements in this
                                // collector. Never let a cascading delete remove our view/seed.
                                if (removed.Contains(frontId) || removed.Contains(copyId)) cleanup.RollBack();
                                else cleanup.Commit();
                            }
                            catch { cleanup.RollBack(); }
                        }
                    }
                    doc.Regenerate();
                    copy = doc.GetElement(copyId) as View;
                    front = doc.GetElement(frontId);
                    if (!IsUsableSeed(front))
                        throw new InvalidOperationException("Phần tử khởi tạo bị xóa theo nội dung phụ thuộc của Legend. Hãy dùng một Legend Component độc lập, không nằm trong nhóm.");
                    if (req.ViewScale < 1 || req.ViewScale > 500) throw new InvalidOperationException("Tỷ lệ chi tiết từ 1 đến 500.");
                    copy.Scale = req.ViewScale;
                    ConfigureComponent(doc, copy, front, symbol.Id, req.ElevationBack ? BackDirection : FrontDirection, false);
                    if (req.CreatePlan)
                    {
                        var planIds = ElementTransformUtils.CopyElement(doc, front.Id, XYZ.Zero);
                        Element plan = planIds.Select(id => doc.GetElement(id)).First(IsUsableSeed);
                        ConfigureComponent(doc, copy, plan, symbol.Id, PlanDirection, true);
                        // Host length is editable only after switching to the plan direction.
                        double expectedLength = DoorSize(sampleDoor, true) + 300.0 / 304.8;
                        Parameter hostLength = GetParam(plan, "LEGEND_COMPONENT_LENGTH", "Host Length");
                        if (hostLength == null || hostLength.StorageType != StorageType.Double)
                            throw new InvalidOperationException("Legend Component mặt bằng không có Host Length dạng chiều dài.");
                        const double lengthTolerance = 0.1 / 304.8;
                        if (Math.Abs(hostLength.AsDouble() - expectedLength) > lengthTolerance)
                        {
                            if (hostLength.IsReadOnly)
                                throw new InvalidOperationException("Host Length mặt bằng đang chỉ đọc sau khi đổi sang hướng Plan.");
                            hostLength.Set(expectedLength);
                            doc.Regenerate();
                        }
                        if (Math.Abs(hostLength.AsDouble() - expectedLength) > lengthTolerance)
                            throw new InvalidOperationException("Host Length chưa khớp: cần " +
                                Math.Round(expectedLength * 304.8, 1) + " mm; hiện tại " +
                                Math.Round(hostLength.AsDouble() * 304.8, 1) + " mm.");
                        // Recenter using the bounding box after resizing the host wall.
                        ConfigureComponent(doc, copy, plan, symbol.Id, PlanDirection, true);
                    }

                    // Thay khung T89 bằng Tag trên / Tag dưới đã chọn ở tab Thiết lập.
                    List<FamilyInstance> layoutTags = ApplyTags(doc, copy, req, result.ParamNotes);
                    result.UpperTagId = layoutTags[0]?.Id;
                    result.LowerTagId = layoutTags[1]?.Id;

                    if (req.WriteParameters && req.Params != null && req.Params.Count > 0)
                        result.ParametersWritten += WriteFrameParameters(doc, copy, sampleDoor, req.Params, result.ParamNotes);

                    result.ParametersWritten += WriteUpperFields(doc, layoutTags[0], sampleDoor, req, result.ParamNotes);
                    if (layoutTags[1] != null)
                    {
                        Parameter labels = FindParam(layoutTags[1], "Parameter name");
                        Parameter values = FindParam(layoutTags[1], "Parameter value");
                        if (labels != null && values != null && !labels.IsReadOnly && !values.IsReadOnly &&
                            labels.StorageType == StorageType.String && values.StorageType == StorageType.String)
                        {
                            var fields = req.Params ?? new List<ParamMap>();
                            labels.Set(string.Join("\n", fields.Select(f => string.IsNullOrWhiteSpace(f.Label) ? f.Name : f.Label)));
                            values.Set(string.Join("\n", fields.Select(f => DoorValue(sampleDoor,f.Name,req))));
                            if(fields.Count==0){doc.Delete(layoutTags[1].Id);layoutTags[1]=null;result.LowerTagId=null;}
                        }
                    }

                    doc.Regenerate();
                    AlignTag(doc, copy, layoutTags[0], true);
                    AlignTag(doc, copy, layoutTags[1], false);

                    try { ResolveDoorType(doc, copy, symbol.Id, req.CreatePlan, false); }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException("Bản sao không hợp lệ nên đã hủy. " + ex.Message);
                    }

                    // Commit KHÔNG ném lỗi khi bộ xử lý lỗi chọn rollback, chỉ trả về trạng thái.
                    TransactionStatus status = t.Commit();
                    if (status != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit đã hủy thay đổi do lỗi lúc Commit (" + status + "). "
                            + string.Join(" | ", handler.Errors.Distinct()));
                    result.View = copy;
                }
                catch
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    throw;
                }
            }

            // Kích thước tạo SAU khi Legend đã Commit, trong Transaction riêng (xem AddDimensions).
            if (req.CreateDimensions)
            {
                string dimNote;
                AddDimensions(doc, result.View, sampleDoor, out dimNote);
                result.Note = dimNote;
            }

            // Đổi mọi Dimension trong Legend sang loại Dim đã chọn ở tab Thiết lập.
            if (req.DimTypeId > 0 && CountDimensions(doc, result.View) > 0 &&
                ApplyDimensionType(doc, result.View, req.DimTypeId) < 0)
                result.ParamNotes.Add("Không đổi được loại kích thước sang loại đã chọn.");

            // Đếm sau khi Commit: đã tính cả các Dimension lỗi bị bộ xử lý lỗi xóa đi.
            using (Transaction layout = new Transaction(doc, "Hoàn thiện bố cục chi tiết cửa"))
            {
                layout.Start();
                FamilyInstance upper = result.UpperTagId == null ? null : doc.GetElement(result.UpperTagId) as FamilyInstance;
                FamilyInstance lower = result.LowerTagId == null ? null : doc.GetElement(result.LowerTagId) as FamilyInstance;
                if(upper!=null) SetLayoutRole(upper,"KTool_Door_Upper");
                if(lower!=null) SetLayoutRole(lower,"KTool_Door_Lower");
                ReflowDetail(doc, result.View);
                ShowUpperValues(doc, result.View, upper, sampleDoor, req, result.ParamNotes);
                doc.Regenerate();
                if (req.CreateFrame) DrawDetailFrame(doc, result.View);
                if (layout.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Không hoàn tất được bố cục Tag và khung.");
            }
            result.Dimensions = CountDimensions(doc, result.View);
            if (handler.DeletedDimensions > 0)
                result.Note = (result.Note == null ? string.Empty : result.Note + " ") +
                              "Revit đã tự xóa " + handler.DeletedDimensions + " kích thước lỗi.";
            if (typeChanged && !req.CreateDimensions)
                result.Note = "Không tạo kích thước (đã tắt tùy chọn).";
            else if (typeChanged && result.Dimensions == 0 && string.IsNullOrEmpty(result.Note))
                result.Note = "Chưa có kích thước, hãy Dim thủ công.";
            return result;
        }


        // ---------------------------------------------------------------- Tag thay cho khung T89

        private static FamilySymbol GetTagSymbol(Document doc, long id)
        {
            return id <= 0 ? null : doc.GetElement(new ElementId(id)) as FamilySymbol;
        }

        /// <summary>Tâm của annotation trong tọa độ view (ưu tiên bounding box, dự phòng điểm chèn).</summary>
        private static XYZ TagCenter(FamilyInstance f, View view)
        {
            BoundingBoxXYZ bb = f.get_BoundingBox(view);
            if (bb != null)
            {
                XYZ a = bb.Transform.OfPoint(bb.Min);
                XYZ b = bb.Transform.OfPoint(bb.Max);
                return (a + b) * 0.5;
            }
            LocationPoint lp = f.Location as LocationPoint;
            return lp != null ? lp.Point : XYZ.Zero;
        }

        /// <summary>
        /// Thay khung T89 trong bản sao bằng Tag trên / Tag dưới đã chọn. Khung T89 xếp từ trên xuống dưới theo
        /// trục đứng của view: khung trên cùng thành Tag trên, khung dưới cùng thành Tag dưới. Legend chỉ có
        /// một khung thì khung đó thành Tag trên (hoặc Tag dưới nếu chỉ chọn Tag dưới). Tag nào chưa có khung
        /// tương ứng thì đặt mới phía trên / phía dưới hình, sau đó căn lại theo khung bao thực tế. Cả hai ô đều "(không dùng)"
        /// thì giữ nguyên khung T89.
        /// </summary>
        private static List<FamilyInstance> ApplyTags(Document doc, View view, WizardRequest req, List<string> notes)
        {
            FamilySymbol upper = GetTagSymbol(doc, req.UpperTagId);
            FamilySymbol lower = GetTagSymbol(doc, req.LowerTagId);
            if (upper == null && lower == null) return new List<FamilyInstance> { null, null };

            if (upper != null && !upper.IsActive) upper.Activate();
            if (lower != null && !lower.IsActive) lower.Activate();
            doc.Regenerate();

            XYZ up = view.UpDirection;
            XYZ right = view.RightDirection;
            List<FamilyInstance> frames = GetFrameInstances(doc, view)
                .OrderByDescending(f => TagCenter(f, view).DotProduct(up))
                .ThenBy(f => TagCenter(f, view).DotProduct(right))
                .ToList();

            FamilyInstance upperInst = null, lowerInst = null;
            if (upper != null && frames.Count > 0) upperInst = frames[0];
            if (lower != null)
            {
                if (frames.Count >= 2) lowerInst = frames[frames.Count - 1];
                else if (frames.Count == 1 && upper == null) lowerInst = frames[0];
            }
            if (frames.Count > 2)
                notes.Add("Legend có " + frames.Count + " khung T89, chỉ thay khung trên cùng và dưới cùng.");

            if (upper != null)
            {
                if (upperInst != null) upperInst = SetTagType(doc, view, upperInst, upper, "Tag trên", notes);
                else upperInst = PlaceMissingTag(doc, view, upper, true, "Tag trên", notes);
            }
            if (lower != null)
            {
                if (lowerInst != null) lowerInst = SetTagType(doc, view, lowerInst, lower, "Tag dưới", notes);
                else lowerInst = PlaceMissingTag(doc, view, lower, false, "Tag dưới", notes);
            }
            return new List<FamilyInstance> { upperInst, lowerInst };
        }

        /// <summary>Đổi một khung sang Type của Tag; nếu Revit không cho đổi trực tiếp thì đặt Tag mới tại chỗ rồi xóa khung.</summary>
        private static FamilyInstance SetTagType(Document doc, View view, FamilyInstance fi, FamilySymbol sym,
                                       string label, List<string> notes)
        {
            if (fi.Symbol != null && fi.Symbol.Id.Value == sym.Id.Value) return fi;

            if (fi.IsValidType(sym.Id))
            {
                ElementId replacementId = fi.ChangeTypeId(sym.Id);
                return replacementId != ElementId.InvalidElementId
                    ? doc.GetElement(replacementId) as FamilyInstance : fi;
            }

            try
            {
                LocationPoint lp = fi.Location as LocationPoint;
                XYZ p = lp != null ? lp.Point : TagCenter(fi, view);
                FamilyInstance replacement = PlaceAnnotationViaDrafting(doc, view, sym, p);
                doc.Delete(fi.Id);
                return replacement;
            }
            catch (Exception ex)
            {
                notes.Add(label + ": không thay được khung T89 bằng '" + sym.FamilyName + " : " + sym.Name + "' - " + ex.Message);
            }
            return null;
        }

        private static FamilyInstance PlaceAnnotationViaDrafting(Document doc, View target,
                                                                 FamilySymbol symbol, XYZ position)
        {
            ViewFamilyType draftingType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting);
            if (draftingType == null)
                throw new InvalidOperationException("Chưa có loại Drafting View để khởi tạo Tag.");
            ViewDrafting staging = ViewDrafting.Create(doc, draftingType.Id);
            staging.Scale = target.Scale;
            FamilyInstance original = doc.Create.NewFamilyInstance(XYZ.Zero, symbol, staging);
            doc.Regenerate();
            ICollection<ElementId> copied = ElementTransformUtils.CopyElements(staging,
                new[] { original.Id }, target, Transform.CreateTranslation(position), new CopyPasteOptions());
            doc.Regenerate();
            FamilyInstance placed = copied.Select(id => doc.GetElement(id)).OfType<FamilyInstance>()
                .FirstOrDefault(f => f.OwnerViewId == target.Id && f.Symbol.Id == symbol.Id);
            if (placed == null) throw new InvalidOperationException("Không sao chép được Tag vào Legend: " + symbol.Name);
            ElementId placedId = placed.Id;
            ICollection<ElementId> deleted = doc.Delete(staging.Id);
            if (deleted.Contains(placedId))
                throw new InvalidOperationException("Tag trong Legend còn phụ thuộc view tạm: " + symbol.Name);
            doc.Regenerate();
            return doc.GetElement(placedId) as FamilyInstance
                ?? throw new InvalidOperationException("Tag không còn hợp lệ sau khi dọn view tạm.");
        }

        /// <summary>Đặt Tag mới ở giữa, phía trên hoặc phía dưới các Legend Component (chừa chỗ cho đường Dim).</summary>
        private static FamilyInstance PlaceMissingTag(Document doc, View view, FamilySymbol sym, bool top,
                                            string label, List<string> notes)
        {
            double u0, u1, v0, v1;
            if (!GetContentBounds(doc, view, out u0, out u1, out v0, out v1))
            {
                notes.Add(label + ": Legend không có Legend Component nên không xác định được chỗ đặt.");
                return null;
            }

            double u = (u0 + u1) / 2;
            double v = top ? v1 + 0.5 : v0 - DimOffsetFeet - 1.1;
            XYZ p = view.RightDirection * u + view.UpDirection * v;

            try
            {
                return PlaceAnnotationViaDrafting(doc, view, sym, p);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(label + ": không đặt được Tag - " + ex.Message, ex);
            }
        }

        // Căn theo mép hình thực tế, không phụ thuộc điểm chèn của family annotation.
        // Khoảng hở 5 mm trên giấy, cộng khoảng chừa cho kích thước tổng thể.
        private static void AlignTag(Document doc, View view, FamilyInstance tag, bool top)
        {
            if (tag == null) return;
            double u0, u1, v0, v1;
            if (!GetContentBounds(doc, view, out u0, out u1, out v0, out v1))
                throw new InvalidOperationException("Không xác định được khung bao hình để bố trí Tag.");

            // Bao gồm Dim nguồn nếu Dim vươn ra ngoài hình.
            foreach (Dimension dim in new FilteredElementCollector(doc).OwnedByView(view.Id)
                         .OfClass(typeof(Dimension)).Cast<Dimension>())
            {
                foreach (XYZ point in DimensionLayoutPoints(dim))
                {
                    double v = point.DotProduct(view.UpDirection);
                    v0 = Math.Min(v0, v);
                    v1 = Math.Max(v1, v);
                }
            }
            BoundingBoxXYZ bb = tag.get_BoundingBox(view);
            if (bb == null)
                throw new InvalidOperationException("Không xác định được khung bao Tag để căn vị trí.");
            List<XYZ> corners = BoxCorners(bb).ToList();
            double left = corners.Min(p => p.DotProduct(view.RightDirection));
            double right = corners.Max(p => p.DotProduct(view.RightDirection));
            double bottom = corners.Min(p => p.DotProduct(view.UpDirection));
            double upper = corners.Max(p => p.DotProduct(view.UpDirection));
            double gap = 5.0 / 304.8 * view.Scale;
            XYZ delta = view.RightDirection * ((u0 + u1 - left - right) / 2)
                + view.UpDirection * (top ? v1 + gap - bottom : v0 - gap - upper);
            if (delta.GetLength() < 1e-6) return;
            bool pinned = tag.Pinned;
            try
            {
                if (pinned) tag.Pinned = false;
                ElementTransformUtils.MoveElement(doc, tag.Id, delta);
            }
            finally { if (pinned) tag.Pinned = true; }
        }

        private static readonly Guid LayoutSchemaId=new Guid("B45D1F13-A542-4C7A-A257-0D082CA3DC62");
        private static Autodesk.Revit.DB.ExtensibleStorage.Schema LayoutSchema()
        {
            var schema=Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(LayoutSchemaId);
            if(schema!=null)return schema;
            var builder=new Autodesk.Revit.DB.ExtensibleStorage.SchemaBuilder(LayoutSchemaId);
            builder.SetSchemaName("KToolDoorLayoutRole");
            builder.AddSimpleField("Role",typeof(string));
            builder.SetReadAccessLevel(Autodesk.Revit.DB.ExtensibleStorage.AccessLevel.Public);
            builder.SetWriteAccessLevel(Autodesk.Revit.DB.ExtensibleStorage.AccessLevel.Public);
            return builder.Finish();
        }
        private static void SetLayoutRole(Element element,string role)
        {
            var schema=LayoutSchema();var entity=new Autodesk.Revit.DB.ExtensibleStorage.Entity(schema);
            entity.Set<string>(schema.GetField("Role"),role);element.SetEntity(entity);
        }
        internal static string LayoutRole(Element element)
        {
            var schema=Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(LayoutSchemaId);if(schema==null)return "";
            var entity=element.GetEntity(schema);return entity.IsValid()?entity.Get<string>(schema.GetField("Role")):"";
        }

        internal static List<XYZ> ComponentPoints(Element component, View view)
        {
            var points = new List<XYZ>();
            Action<GeometryElement> read = null;
            read = geo => {
                if(geo==null)return;
                foreach(GeometryObject g in geo)
                {
                    if(g is GeometryInstance instance) read(instance.GetInstanceGeometry());
                    else if(g is Curve curve) points.AddRange(curve.Tessellate());
                    else if(g is Solid solid) foreach(Edge edge in solid.Edges) points.AddRange(edge.Tessellate());
                    else if(g is PolyLine poly) points.AddRange(poly.GetCoordinates());
                }
            };
            read(component.get_Geometry(new Options{View=view,IncludeNonVisibleObjects=false}));
            if(points.Count==0){var box=component.get_BoundingBox(view);if(box!=null)points.AddRange(BoxCorners(box));}
            if(points.Count==0)throw new InvalidOperationException("Không lấy được biên hình học Legend Component.");
            return points;
        }

        internal static void ReflowDetail(Document doc, View view)
        {
            doc.Regenerate();
            var components=GetLegendComponents(doc,view).ToList();
            var front=components.FirstOrDefault(e=>e.get_Parameter(BuiltInParameter.LEGEND_COMPONENT_VIEW).AsInteger()!=PlanDirection);
            var plan=components.FirstOrDefault(e=>e.get_Parameter(BuiltInParameter.LEGEND_COMPONENT_VIEW).AsInteger()==PlanDirection);
            if(front==null)return;
            var dims=new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
            var anchors=new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(f=>f.Symbol.FamilyName.StartsWith("KTool_DoorDim_")).ToList();
            var planAnchors=new HashSet<ElementId>();
            double frontBottom=ComponentPoints(front,view).Min(p=>p.DotProduct(view.UpDirection));
            if(plan!=null)
            {
                var pp=ComponentPoints(plan,view);double bottom=pp.Min(p=>p.DotProduct(view.UpDirection));
                double top=pp.Max(p=>p.DotProduct(view.UpDirection));
                foreach(var anchor in anchors){var lp=anchor.Location as LocationPoint;if(lp!=null&&Math.Abs(lp.Point.DotProduct(view.UpDirection)-bottom)<0.1/304.8)planAnchors.Add(anchor.Id);}
                double delta=frontBottom-14.0/304.8*view.Scale-top;
                var move=new List<ElementId>{plan.Id};move.AddRange(planAnchors);
                if(Math.Abs(delta)>1e-6)ElementTransformUtils.MoveElements(doc,move,view.UpDirection*delta);
                doc.Regenerate();
            }
            double offset=6.0/304.8*view.Scale;
            foreach(var dim in dims)
            {
                Line line=dim.Curve as Line;if(line==null)continue;
                bool horizontal=Math.Abs(line.Direction.DotProduct(view.RightDirection))>0.99;
                if(horizontal)
                {
                    bool onPlan=plan!=null&&dim.References.Cast<Reference>().Any(r=>planAnchors.Contains(r.ElementId));
                    var part=onPlan?plan:front;
                    double target=ComponentPoints(part,view).Min(p=>p.DotProduct(view.UpDirection))-offset;
                    ElementTransformUtils.MoveElement(doc,dim.Id,view.UpDirection*(target-dim.Origin.DotProduct(view.UpDirection)));
                }
                else if(Math.Abs(line.Direction.DotProduct(view.UpDirection))>0.99)
                {
                    double target=ComponentPoints(front,view).Max(p=>p.DotProduct(view.RightDirection))+offset;
                    ElementTransformUtils.MoveElement(doc,dim.Id,view.RightDirection*(target-dim.Origin.DotProduct(view.RightDirection)));
                }
            }
            doc.Regenerate();
            var tags=new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
            foreach(var tag in tags){string marker=LayoutRole(tag);
                if(marker=="KTool_Door_Upper"||marker=="KTool_Door_Lower"){
                    XYZ old=(tag.Location as LocationPoint)?.Point;AlignTag(doc,view,tag,marker=="KTool_Door_Upper");
                    if(marker=="KTool_Door_Upper"&&old!=null){XYZ shift=((LocationPoint)tag.Location).Point-old;
                        foreach(var note in new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(TextNote)))if(shift.GetLength()>1e-6)ElementTransformUtils.MoveElement(doc,note.Id,shift);}
                }}
            var frame=new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(CurveElement))
                .Where(e=>LayoutRole(e)=="KTool_Door_Frame").Select(e=>e.Id).ToList();
            if(frame.Count>0){doc.Delete(frame);doc.Regenerate();DrawDetailFrame(doc,view);}
            doc.Regenerate();
        }

        private static IEnumerable<XYZ> DimensionLayoutPoints(Dimension dimension)
        {
            // View-specific dimension bounding boxes may span Revit's unbounded
            // reference planes before the view is rendered. Use finite measured extents.
            Line line = dimension.Curve as Line;
            if (line == null || !dimension.Value.HasValue) return new XYZ[0];
            double half = dimension.Value.Value / 2;
            XYZ origin = dimension.Origin;
            return new[] { origin - line.Direction * half, origin + line.Direction * half };
        }

        private static IEnumerable<XYZ> BoxCorners(BoundingBoxXYZ box)
        {
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        yield return box.Transform.OfPoint(new XYZ(
                            x == 0 ? box.Min.X : box.Max.X,
                            y == 0 ? box.Min.Y : box.Max.Y,
                            z == 0 ? box.Min.Z : box.Max.Z));
        }

        private static bool GetContentBounds(Document doc, View view,
                                             out double u0, out double u1, out double v0, out double v1)
        {
            u0 = double.MaxValue; v0 = double.MaxValue;
            u1 = double.MinValue; v1 = double.MinValue;
            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;
            bool any = false;

            foreach (Element e in GetLegendComponents(doc, view))
            {
                BoundingBoxXYZ bb = e.get_BoundingBox(view);
                if (bb == null) continue;
                var points = ComponentPoints(e, view).ToList();
                u0 = Math.Min(u0, points.Min(p => p.DotProduct(right)));
                u1 = Math.Max(u1, points.Max(p => p.DotProduct(right)));
                v0 = Math.Min(v0, points.Min(p => p.DotProduct(up)));
                v1 = Math.Max(v1, points.Max(p => p.DotProduct(up)));
                any = true;
            }
            return any;
        }

        /// <summary>Xóa Legend Component mặt bằng khỏi bản sao (khi bỏ tích "Mặt bằng").</summary>
        private static void RemovePlanComponents(Document doc, View view)
        {
            List<ElementId> ids = new List<ElementId>();
            foreach (Element e in GetLegendComponents(doc, view))
            {
                Parameter dir = GetParam(e, "LEGEND_COMPONENT_VIEW", "View Direction");
                if (dir == null || dir.StorageType != StorageType.Integer) continue;
                if (dir.AsInteger() == PlanDirection) ids.Add(e.Id);
            }
            if (ids.Count > 0) doc.Delete(ids);
        }

        /// <summary>Đổi loại (DimensionType) của mọi Dimension trong view. Trả về số Dim đã đổi, -1 nếu lỗi.</summary>
        private static int ApplyDimensionType(Document doc, View view, long dimTypeId)
        {
            DimensionType dt = doc.GetElement(new ElementId(dimTypeId)) as DimensionType;
            if (dt == null) return -1;

            int changed = 0;
            using (Transaction t = new Transaction(doc, "Đổi loại kích thước chi tiết cửa"))
            {
                t.Start();
                try
                {
                    foreach (Dimension d in new FilteredElementCollector(doc, view.Id)
                                 .OfClass(typeof(Dimension)).Cast<Dimension>())
                    {
                        if (d.DimensionType != null && d.DimensionType.Id.Value == dt.Id.Value) continue;
                        d.DimensionType = dt;
                        changed++;
                    }
                    if (t.Commit() != TransactionStatus.Committed) return -1;
                }
                catch (Exception)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    return -1;
                }
            }
            return changed;
        }

        /// <summary>Đổi Component Type của mặt đứng và mặt bằng từ type cũ sang type mới.</summary>
        private static void ReplaceType(Document doc, View view, ElementId oldTypeId, ElementId newTypeId)
        {
            int changed = 0;
            foreach (Element e in GetLegendComponents(doc, view))
            {
                Parameter type = GetParam(e, "LEGEND_COMPONENT", "Component Type");
                Parameter dir = GetParam(e, "LEGEND_COMPONENT_VIEW", "View Direction");
                if (type == null || type.StorageType != StorageType.ElementId) continue;
                if (dir == null || dir.StorageType != StorageType.Integer) continue;
                int d = dir.AsInteger();
                if (d != FrontDirection && d != PlanDirection) continue;
                if (type.AsElementId().Value != oldTypeId.Value) continue;

                if (type.IsReadOnly || !type.Set(newTypeId))
                    throw new InvalidOperationException("Không đổi được Component Type của Legend Component.");
                changed++;
            }
            if (changed == 0)
                throw new InvalidOperationException("Không có Legend Component nào để đổi Family Type.");
        }

        private static int CountDimensions(Document doc, View view)
        {
            return new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(Dimension)).GetElementCount();
        }

        // ---------------------------------------------------------------- Tạo kích thước

        private class RefLine
        {
            public XYZ P0;
            public XYZ P1;
            public Reference Ref;
            public XYZ Dir;
        }

        /// <summary>Dim bằng reference thật trên Legend Component; không dựng line neo thay thế.</summary>
        private static int AddDimensions(Document doc, View view, FamilyInstance sampleDoor, out string note)
        {
            int count = CreateAnchorDimensions(doc, view, sampleDoor);
            note = "Dim bằng line ẩn, số đo thật theo Width/Height của cửa; không ghi đè chữ Dim. Cần chạy lại triển khai khi đổi kích thước cửa.";
            return count;
        }

        private static int RunDimensionPass(Document doc, View view, FamilyInstance sampleDoor, List<string> diag)
        {
            string pass = "[reference cửa]";
            DimensionFailureHandler handler = new DimensionFailureHandler(doc);

            using (Transaction t = new Transaction(doc, "Tạo kích thước chi tiết cửa"))
            {
                t.Start();
                FailureHandlingOptions fho = t.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(handler);
                fho.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(fho);
                try
                {
                    CreateDimensions(doc, view, sampleDoor, diag);
                    TransactionStatus status = t.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        diag.Add(pass + " Revit đã hủy lúc Commit (" + status + ")");
                        return 0;
                    }
                }
                catch (Exception ex)
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    diag.Add(pass + " lỗi - " + ex.Message);
                    return 0;
                }
            }

            if (handler.DeletedDimensions > 0)
                diag.Add(pass + " Revit báo Reference không hợp lệ, đã xóa " + handler.DeletedDimensions + " kích thước");
            // Revit có thể giữ Dimension nhưng reference không còn giải được sau Commit.
            List<ElementId> invalid = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Dimension)).Cast<Dimension>()
                .Where(d => !d.AreReferencesAvailable || (d.Value.HasValue && d.Value.Value <= 0))
                .Select(d => d.Id).ToList();
            if (invalid.Count > 0)
            {
                using (Transaction cleanup = new Transaction(doc, "Loại Dim không có reference cửa hợp lệ"))
                {
                    cleanup.Start();
                    doc.Delete(invalid);
                    if (cleanup.Commit() != TransactionStatus.Committed)
                        diag.Add("Không xóa được Dim không hợp lệ sau Commit.");
                }
                diag.Add("Đã loại " + invalid.Count + " Dim mất reference hoặc có giá trị không hợp lệ.");
            }
            return CountDimensions(doc, view);
        }

        /// <summary>Tạo Dim trong Transaction đang mở. Trả về số Dim đã tạo (chưa chắc còn hợp lệ sau Commit).</summary>
        private static int CreateDimensions(Document doc, View view, FamilyInstance sampleDoor, List<string> diag)
        {
            int created = 0, comps = 0;
            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;

            foreach (Element comp in GetLegendComponents(doc, view))
            {
                Parameter dir = GetParam(comp, "LEGEND_COMPONENT_VIEW", "View Direction");
                if (dir == null || dir.StorageType != StorageType.Integer) continue;
                int d = dir.AsInteger();
                if (d != FrontDirection && d != PlanDirection) continue;
                comps++;
                string tag = d == FrontDirection ? "Mặt đứng" : "Mặt bằng";

                List<RefLine> lines = new List<RefLine>();
                try
                {
                    Options opt = new Options { View = view, ComputeReferences = true };
                    GeometryElement ge = comp.get_Geometry(opt);
                    if (ge == null) diag.Add(tag + ": get_Geometry trả về null");
                    else CollectLines(ge, null, lines);
                    foreach (RefLine line in lines) line.Ref = LegendReference(doc, comp, line.Ref);
                }
                catch (Exception ex) { diag.Add(tag + ": lỗi đọc hình học - " + ex.Message); }

                int total = lines.Count;
                lines = lines.Where(l => l.Ref != null && l.P0.DistanceTo(l.P1) > 0.01).ToList();
                if (lines.Count == 0)
                {
                    diag.Add(tag + ": hình học có " + total + " đoạn thẳng, không đoạn nào có Reference");
                    continue;
                }

                foreach (RefLine l in lines) l.Dir = (l.P1 - l.P0).Normalize();
                created += DimensionExtents(doc, view, lines, right, up, tag, diag, sampleDoor);
                if (d == FrontDirection)
                    created += DimensionSillHeight(doc, view, lines, sampleDoor, diag);
            }

            if (comps == 0) diag.Add("Không tìm thấy Legend Component để Dim.");
            return created;
        }

        /// <summary>Duyệt hình học, gom các đoạn thẳng có Reference (đã đổi sang tọa độ view).</summary>
        private static void CollectLines(GeometryElement ge, Transform tr, List<RefLine> output)
        {
            foreach (GeometryObject go in ge)
            {
                Line line = go as Line;
                if (line != null) { AddLine(output, line, line.Reference, tr); continue; }

                GeometryInstance gi = go as GeometryInstance;
                if (gi != null)
                {
                    // Reference dùng để Dim lấy từ hình học của Symbol; tọa độ đổi bằng Transform của instance.
                    Transform t2 = tr == null ? gi.Transform : tr.Multiply(gi.Transform);
                    CollectLines(gi.GetSymbolGeometry(), t2, output);
                    continue;
                }

                Solid solid = go as Solid;
                if (solid != null)
                {
                    foreach (PlanarFace face in solid.Faces.OfType<PlanarFace>())
                    {
                        XYZ normal = tr == null ? face.FaceNormal : tr.OfVector(face.FaceNormal);
                        XYZ origin = tr == null ? face.Origin : tr.OfPoint(face.Origin);
                        // Legend view nằm trong XY; mặt đứng có cạnh cao theo Y, ngang theo X.
                        XYZ tangent = null;
                        if (Math.Abs(normal.DotProduct(XYZ.BasisX)) > 0.999) tangent = XYZ.BasisY;
                        else if (Math.Abs(normal.DotProduct(XYZ.BasisY)) > 0.999) tangent = XYZ.BasisX;
                        if (tangent != null)
                            output.Add(new RefLine { P0 = origin, P1 = origin + tangent, Ref = face.Reference });
                    }
                    foreach (Edge edge in solid.Edges)
                    {
                        Line el = edge.AsCurve() as Line;
                        if (el != null) AddLine(output, el, edge.Reference, tr);
                    }
                }
            }
        }

        private static void AddLine(List<RefLine> output, Line line, Reference reference, Transform tr)
        {
            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            if (tr != null) { p0 = tr.OfPoint(p0); p1 = tr.OfPoint(p1); }
            output.Add(new RefLine { P0 = p0, P1 = p1, Ref = reference });
        }

        private static int DimensionExtents(Document doc, View view, List<RefLine> lines, XYZ right, XYZ up,
                                            string tag, List<string> diag, FamilyInstance sampleDoor)
        {
            const double axisTol = 0.999;
            const double minSpan = 0.05; // feet, bỏ qua nếu hai cạnh gần như trùng nhau

            List<RefLine> vertical = lines.Where(l => Math.Abs(l.Dir.DotProduct(up)) > axisTol).ToList();
            List<RefLine> horizontal = lines.Where(l => Math.Abs(l.Dir.DotProduct(right)) > axisTol).ToList();

            double uMin = lines.Min(l => Math.Min(l.P0.DotProduct(right), l.P1.DotProduct(right)));
            double vMin = lines.Min(l => Math.Min(l.P0.DotProduct(up), l.P1.DotProduct(up)));

            int n = 0;

            // Select door jamb witnesses by actual family width, rather than wall extents.
            Parameter widthParam = sampleDoor.Symbol.get_Parameter(BuiltInParameter.DOOR_WIDTH)
                ?? sampleDoor.get_Parameter(BuiltInParameter.DOOR_WIDTH);
            double width = widthParam != null && widthParam.StorageType == StorageType.Double ? widthParam.AsDouble() : 0;
            int widthCount = 0;
            if (width > minSpan)
            {
                var pairs = (from a in vertical from b in vertical
                    let span = b.P0.DotProduct(right) - a.P0.DotProduct(right)
                    where span > minSpan && Math.Abs(span - width) < 2.0 / 304.8
                    orderby Math.Abs(span - width), a.P0.DotProduct(right)
                    select new { A = a, B = b, Span = span }).ToList();
                foreach (var pair in pairs)
                {
                    double v = vMin - DimOffsetFeet;
                    XYZ p1 = right * pair.A.P0.DotProduct(right) + up * v;
                    XYZ p2 = p1 + right * pair.Span;
                    using (SubTransaction attempt = new SubTransaction(doc))
                    {
                        attempt.Start();
                        try
                        {
                            ReferenceArray refs = new ReferenceArray();
                            refs.Append(pair.A.Ref); refs.Append(pair.B.Ref);
                            Dimension dim = doc.Create.NewDimension(view, Line.CreateBound(p1, p2), refs);
                            doc.Regenerate();
                            if (dim != null && dim.AreReferencesAvailable && dim.Value.HasValue &&
                                Math.Abs(dim.Value.Value - width) < 2.0 / 304.8)
                            { attempt.Commit(); widthCount = 1; break; }
                            attempt.RollBack();
                        }
                        catch { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); }
                    }
                }
            }
            n += widthCount;
            if (widthCount == 0) diag.Add(tag + ": chưa có cặp reference hợp lệ đúng chiều rộng cửa; không lấy mép tường làm Dim cửa.");

            // Kích thước đứng: giữa cạnh ngang thấp nhất và cao nhất, đặt phía trái hình.
            if (horizontal.Count >= 2)
            {
                RefLine a = horizontal.OrderBy(l => l.P0.DotProduct(up)).First();
                RefLine b = horizontal.OrderByDescending(l => l.P0.DotProduct(up)).First();
                if (b.P0.DotProduct(up) - a.P0.DotProduct(up) > minSpan)
                {
                    double u = uMin - DimOffsetFeet;
                    XYZ p1 = a.P0 + right * (u - a.P0.DotProduct(right));
                    XYZ p2 = p1 + up * (b.P0.DotProduct(up) - a.P0.DotProduct(up));
                    n += TryDimension(doc, view, p1, p2, a.Ref, b.Ref, tag, diag);
                }
            }

            if (n == 0)
                diag.Add(tag + ": có " + lines.Count + " đoạn có Reference (" + vertical.Count + " dọc, " +
                         horizontal.Count + " ngang) nhưng không Dim được");
            return n;
        }

        private static Reference LegendReference(Document doc, Element component, Reference reference)
        {
            if (reference == null) return null;
            if (reference.ElementId == component.Id) return reference;
            try
            {
                // Reference của symbol phải mang đường dẫn instance của Legend Component.
                string stable = component.UniqueId + ":0:INSTANCE:" + reference.ConvertToStableRepresentation(doc);
                Reference bound = Reference.ParseFromStableRepresentation(doc, stable);
                return bound.ElementId == component.Id && component.GetGeometryObjectFromReference(bound) != null
                    ? bound : null;
            }
            catch { return null; }
        }

        private static int DimensionSillHeight(Document doc, View view, List<RefLine> lines,
                                               FamilyInstance sampleDoor, List<string> diag)
        {
            Parameter parameter = sampleDoor.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
            if (parameter == null || parameter.StorageType != StorageType.Double)
            {
                diag.Add("Sill Height: cửa không có parameter Sill Height.");
                return 0;
            }
            double sill = parameter.AsDouble();
            const double tolerance = 1.0 / 304.8; // 1 mm
            if (sill <= tolerance)
            {
                diag.Add("Sill Height bằng 0: không tạo đoạn Dim có chiều dài bằng 0.");
                return 0;
            }
            XYZ right = view.RightDirection, up = view.UpDirection;
            List<RefLine> horizontal = lines.Where(l => Math.Abs(l.Dir.DotProduct(right)) > 0.999)
                .OrderBy(l => l.P0.DotProduct(up)).ToList();
            double left = lines.Min(l => Math.Min(l.P0.DotProduct(right), l.P1.DotProduct(right)));
            double rightEdge = lines.Max(l => Math.Max(l.P0.DotProduct(right), l.P1.DotProduct(right)));

            // Chỉ dùng đường chuẩn sàn đã có trong Legend, không tự dựng line hoặc ghi đè số Dim.
            // Đường chuẩn phải phủ bề ngang cửa và cách mép cửa đúng Sill Height của cửa mẫu.
            foreach (CurveElement element in new FilteredElementCollector(doc, view.Id)
                         .OfClass(typeof(CurveElement)).Cast<CurveElement>())
            {
                Line baseline = element.GeometryCurve as Line;
                if (baseline == null || !baseline.IsBound || Math.Abs(baseline.Direction.DotProduct(right)) < 0.999) continue;
                XYZ p0 = baseline.GetEndPoint(0), p1 = baseline.GetEndPoint(1);
                if (Math.Min(p0.DotProduct(right), p1.DotProduct(right)) > left + tolerance ||
                    Math.Max(p0.DotProduct(right), p1.DotProduct(right)) < rightEdge - tolerance) continue;
                RefLine bottom = horizontal.FirstOrDefault(l =>
                    Math.Abs(l.P0.DotProduct(up) - p0.DotProduct(up) - sill) <= tolerance);
                if (bottom == null) continue;
                XYZ a = p0 + right * (left - 2 * DimOffsetFeet - p0.DotProduct(right));
                XYZ b = a + up * (bottom.P0.DotProduct(up) - a.DotProduct(up));
                int count = TryDimension(doc, view, a, b, baseline.Reference, bottom.Ref, "Sill Height", diag);
                if (count > 0) return count;
            }
            diag.Add("Sill Height: chưa có reference chuẩn sàn hợp lệ cách mép cửa đúng " +
                Math.Round(sill * 304.8, 1) + " mm. Không dựng line phụ; cần chuẩn sàn trong Legend nguồn.");
            return 0;
        }

        private static int TryDimension(Document doc, View view, XYZ p1, XYZ p2, Reference r1, Reference r2,
                                        string tag, List<string> diag)
        {
            try
            {
                ReferenceArray refs = new ReferenceArray();
                refs.Append(r1);
                refs.Append(r2);
                Dimension dim = doc.Create.NewDimension(view, Line.CreateBound(p1, p2), refs);
                return dim != null ? 1 : 0;
            }
            catch (Exception ex)
            {
                string msg = ex.Message ?? string.Empty;
                int cut = msg.IndexOf('\n');
                if (cut > 0) msg = msg.Substring(0, cut);
                diag.Add(tag + ": NewDimension lỗi - " + msg);
                return 0;
            }
        }

        /// <summary>
        /// Khi Revit báo lỗi lúc Commit mà mọi phần tử gây lỗi đều là Dimension (ví dụ "dimension references
        /// are or have become invalid"), xóa đúng các Dimension đó rồi Commit tiếp, không hiện hộp thoại.
        /// Cảnh báo (Warning) bị xóa im lặng. Lỗi khác thì rollback.
        /// </summary>
        private class DimensionFailureHandler : IFailuresPreprocessor
        {
            private readonly Document _doc;
            public int DeletedDimensions;
            public readonly List<string> Errors = new List<string>();

            public DimensionFailureHandler(Document doc) { _doc = doc; }

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                List<ElementId> toDelete = new List<ElementId>();
                bool otherError = false;

                foreach (FailureMessageAccessor m in accessor.GetFailureMessages())
                {
                    if (m.GetSeverity() == FailureSeverity.Warning)
                    {
                        accessor.DeleteWarning(m);
                        continue;
                    }

                    List<ElementId> ids = m.GetFailingElementIds().ToList();
                    Errors.Add(m.GetDescriptionText() + " [" + string.Join(", ", ids.Select(i => i.Value)) + "]");
                    if (ids.Count > 0 && ids.All(i => _doc.GetElement(i) is Dimension)) toDelete.AddRange(ids);
                    else otherError = true;
                }

                if (otherError) return FailureProcessingResult.ProceedWithRollBack;
                if (toDelete.Count == 0) return FailureProcessingResult.Continue;

                List<ElementId> distinct = toDelete.Distinct().ToList();
                accessor.DeleteElements(distinct);
                DeletedDimensions += distinct.Count;
                return FailureProcessingResult.ProceedWithCommit;
            }
        }

        // ---------------------------------------------------------------- Kiểm tra Legend

        private static List<Element> GetLegendComponents(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.Id.Value == (long)BuiltInCategory.OST_LegendComponents)
                .ToList();
        }

        /// <summary>
        /// Kiểm tra Legend có mặt đứng + mặt bằng cùng một Family Type và có Generic Annotation T89.
        /// expected == null: tự xác định Family Type từ Legend. Ngược lại: bắt buộc đúng Family Type đó.
        /// </summary>
        private static ElementId ResolveDoorType(Document doc, View view, ElementId expected, bool requirePlan = true, bool requireFrame = true)
        {
            List<Element> components = GetLegendComponents(doc, view);

            HashSet<long> front = TypeIds(components, FrontDirection);
            front.UnionWith(TypeIds(components, BackDirection));
            HashSet<long> plan = TypeIds(components, PlanDirection);

            if (front.Count == 0 || (requirePlan && plan.Count == 0))
                throw new InvalidOperationException("Legend '" + view.Name + "' thiếu mặt đứng hoặc mặt bằng.");

            List<long> common = requirePlan
                ? front.Intersect(plan).OrderBy(x => x).ToList()
                : front.OrderBy(x => x).ToList();
            if (common.Count == 0)
                throw new InvalidOperationException("Legend '" + view.Name +
                    "': mặt đứng và mặt bằng không dùng chung một Family Type.");

            long chosen;
            if (expected == null) chosen = common[0];
            else
            {
                chosen = expected.Value;
                if (!common.Contains(chosen))
                    throw new InvalidOperationException("Legend '" + view.Name +
                        "' thiếu mặt đứng hoặc mặt bằng đúng Family Type (Id " + chosen + ").");
            }

            if (requireFrame && GetFrameInstances(doc, view).Count == 0)
                throw new InvalidOperationException("Legend '" + view.Name + "' thiếu Generic Annotation " + T89FamilyName);

            return new ElementId(chosen);
        }

        /// <summary>Tập Id Family Type của các Legend Component theo hướng nhìn.</summary>
        private static HashSet<long> TypeIds(IEnumerable<Element> components, int direction)
        {
            HashSet<long> ids = new HashSet<long>();
            foreach (Element e in components)
            {
                // Dùng BuiltInParameter trước (không phụ thuộc ngôn ngữ Revit), tên parameter là phương án dự phòng.
                Parameter type = GetParam(e, "LEGEND_COMPONENT", "Component Type");
                Parameter dir = GetParam(e, "LEGEND_COMPONENT_VIEW", "View Direction");
                if (type == null || type.StorageType != StorageType.ElementId) continue;
                if (dir == null || dir.StorageType != StorageType.Integer) continue;
                if (dir.AsInteger() != direction) continue;
                ElementId id = type.AsElementId();
                if (id != null && id.Value != ElementId.InvalidElementId.Value) ids.Add(id.Value);
            }
            return ids;
        }

        private static Parameter GetParam(Element e, string builtInName, string fallbackName)
        {
            // Tra enum bằng tên lúc chạy: nếu tên không tồn tại trong API thì rơi về LookupParameter.
            BuiltInParameter bip;
            if (Enum.TryParse<BuiltInParameter>(builtInName, out bip))
            {
                Parameter p = e.get_Parameter(bip);
                if (p != null) return p;
            }
            return e.LookupParameter(fallbackName);
        }

        // ---------------------------------------------------------------- Đặt tên

        private static string BuildCopyName(string prefix, FamilySymbol symbol)
        {
            Parameter typeMark = symbol.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_MARK);
            string mark = typeMark == null ? null : typeMark.AsString();
            // Type Mark rỗng: dùng tên Type để vẫn tạo được Legend có tên nhận diện.
            if (string.IsNullOrWhiteSpace(mark)) mark = symbol.Name;
            return SafeName(prefix.Trim() + " " + mark.Trim());
        }

        private static string SafeName(string s)
        {
            // Revit không cho các ký tự này trong tên view.
            char[] bad = { '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~' };
            foreach (char c in bad) s = s.Replace(c, '_');
            return s.Trim();
        }

        private static string UniqueViewName(Document doc, string root)
        {
            HashSet<string> names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View))
                .Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(root)) return root;
            for (int i = 2; i < 100000; i++)
                if (!names.Contains(root + "_" + i)) return root + "_" + i;
            throw new InvalidOperationException("Không tìm được tên Legend mới.");
        }
    }
}

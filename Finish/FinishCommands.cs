using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.Dim.Finish
{
    [Transaction(TransactionMode.Manual)]
    public class FinishFloorCommand : IExternalCommand { public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { return FinishRunner.Execute(d, FinishKind.Floor, ref m); } }
    [Transaction(TransactionMode.Manual)]
    public class FinishCeilingCommand : IExternalCommand { public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { return FinishRunner.Execute(d, FinishKind.Ceiling, ref m); } }
    [Transaction(TransactionMode.Manual)]
    public class FinishWallCommand : IExternalCommand { public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { return FinishRunner.Execute(d, FinishKind.Wall, ref m); } }
    [Transaction(TransactionMode.Manual)]
    public class FinishThresholdCommand : IExternalCommand { public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { return FinishRunner.Execute(d, FinishKind.Threshold, ref m); } }
    [Transaction(TransactionMode.Manual)]
    public class FinishTileCommand : IExternalCommand { public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { return FinishRunner.Execute(d, FinishKind.Tile, ref m); } }

    [Transaction(TransactionMode.Manual)]
    public class LintelCommand : IExternalCommand { public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { return FinishRunner.Execute(d, FinishKind.Lintel, ref m); } }
    [Transaction(TransactionMode.Manual)]
    public class PlasterCommand : IExternalCommand { public Result Execute(ExternalCommandData d, ref string m, ElementSet e) { return FinishRunner.Execute(d, FinishKind.Plaster, ref m); } }

    internal static class FinishRunner
    {
        private static readonly Guid TagGuid = new Guid("173bdb25-3ab4-40c7-a982-91d75f248612");
        private const double Mm = FinishGeometry.Mm;
        public static Result Execute(ExternalCommandData data, FinishKind kind, ref string message)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) { message = "Hãy mở Project Revit."; return Result.Failed; }
            Document doc = ui.Document;
            try
            {
                List<FinishChoice> types = Choices(doc, kind);
                if (types.Count == 0)
                {
                    TaskDialog.Show("KTool - Hoàn thiện", kind == FinishKind.Threshold
                        ? "Chưa có Family ngạch phù hợp. Hãy load Family Generic Model / Specialty Equipment / Furniture dạng đặt theo Level, theo đường hoặc có host."
                        : kind == FinishKind.Lintel ? "Chưa có Family dầm Structural Framing. Hãy load Family dầm lanh tô trước khi chạy lệnh."
                        : "Chưa có loại cấu kiện phù hợp. Hãy tạo/load Type cần dùng trước khi chạy lệnh.");
                    return Result.Cancelled;
                }
                FinishSettings settings;
                if (kind == FinishKind.Wall)
                {
                    var roomData = WallRooms(ui);
                    var window = new FinishWallWindow(WallRows(doc, types), roomData);
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                    if (window.ShowDialog() != true) return Result.Cancelled;
                    settings = window.Settings;
                    if (window.Mode == WallRoomMode.Pick)
                    {
                        var allowed = new HashSet<long>(roomData.Select(r => r.Id));
                        settings.Sources = ui.Selection.PickObjects(ObjectType.Element, new WallRoomFilter(allowed),
                            "Chọn các Room cần tạo tường hoàn thiện rồi bấm Finish (Esc để hủy).")
                            .Select(r => r.ElementId.Value).Distinct().ToList();
                        if (settings.Sources.Count == 0) return Result.Cancelled;
                    }
                }
                else
                {
                    var window = new FinishWindow(kind, types, Sources(ui, kind));
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                    if (window.ShowDialog() != true) return Result.Cancelled;
                    settings = window.Settings;
                }
                Reference face = null;
                if (kind == FinishKind.Tile)
                    face = ui.Selection.PickObject(ObjectType.Face, new FinishFaceFilter(), "Chọn mặt phẳng sàn hoặc tường để chia viên gạch 3D.");
                string report = null;
                var progress = new SDK_HD.Dim.JoinProgressWindow();
                new System.Windows.Interop.WindowInteropHelper(progress).Owner = data.Application.MainWindowHandle;
                progress.Execute(update => report = kind == FinishKind.Tile ? CreateTiles(doc, face, settings, update) : CreateSources(doc, kind, settings, update));
                TaskDialog.Show("KTool - " + FinishWindow.Names[(int)kind], report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (System.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        private sealed class WallRoomFilter : ISelectionFilter
        {
            private readonly HashSet<long> allowed;
            internal WallRoomFilter(HashSet<long> ids) { allowed = ids; }
            public bool AllowElement(Element element) => element is Room && allowed.Contains(element.Id.Value);
            public bool AllowReference(Reference reference, XYZ point) => false;
        }
        private static List<WallRoomData> WallRooms(UIDocument ui)
        {
            var doc = ui.Document;
            var visible = FilteredElementCollector.IsViewValidForElementIteration(doc, ui.ActiveView.Id)
                ? new HashSet<long>(new FilteredElementCollector(doc, ui.ActiveView.Id).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Select(r => r.Id.Value))
                : new HashSet<long>();
            var result = new List<WallRoomData>();
            foreach (Room room in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(r => r.Area > 1e-6 && r.Location != null))
            {
                var item = new WallRoomData { Id = room.Id.Value, Visible = visible.Contains(room.Id.Value) };
                item.Parameters["Room.Number"] = room.Number; item.Parameters["Room.Name"] = room.Name;
                item.Parameters["Level"] = doc.GetElement(room.LevelId)?.Name ?? "";
                var phase = room.get_Parameter(BuiltInParameter.ROOM_PHASE);
                item.Parameters["Phase"] = phase == null ? "" : doc.GetElement(phase.AsElementId())?.Name ?? "";
                foreach (var parameterGroup in room.Parameters.Cast<Parameter>().Where(p => p.Definition != null && p.StorageType != StorageType.None).GroupBy(p => p.Definition.Name))
                foreach (Parameter parameter in parameterGroup)
                {
                    string key = parameter.Definition.Name + (parameterGroup.Count() > 1 ? " [" + parameter.Id.Value + "]" : "");
                    item.Parameters[key] = parameter.StorageType == StorageType.String ? parameter.AsString() ?? "" : parameter.AsValueString() ??
                        (parameter.StorageType == StorageType.Integer ? parameter.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : parameter.StorageType == StorageType.ElementId ? doc.GetElement(parameter.AsElementId())?.Name ?? "" : "");
                }
                result.Add(item);
            }
            return result;
        }
        private static List<WallTypeRow> WallRows(Document doc, List<FinishChoice> choices)
        {
            return choices.Select(choice =>
            {
                var type = (WallType)doc.GetElement(new ElementId(choice.Id));
                var drawing = new System.Windows.Media.DrawingGroup();
                using (var dc = drawing.Open())
                {
                    dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new System.Windows.Rect(0, 0, 70, 34));
                    using (var structure = type.GetCompoundStructure())
                    {
                        double x = 0;
                        var layers = structure == null ? null : structure.GetLayers();
                        if (layers == null || layers.Count == 0)
                            dc.DrawRectangle(System.Windows.Media.Brushes.LightSteelBlue, null, new System.Windows.Rect(0, 8, 70, 18));
                        else foreach (var layer in layers)
                        {
                            double width = 70 * layer.Width / type.Width;
                            if (width <= 0) continue;
                            var material = doc.GetElement(layer.MaterialId) as Material;
                            var color = material?.Color;
                            var brush = color == null || !color.IsValid ? System.Windows.Media.Brushes.LightSteelBlue
                                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(color.Red, color.Green, color.Blue));
                            dc.DrawRectangle(brush, new System.Windows.Media.Pen(System.Windows.Media.Brushes.SlateGray, .35), new System.Windows.Rect(x, 8, width, 18));
                            x += width;
                        }
                    }
                    dc.DrawRectangle(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.SlateGray, .7), new System.Windows.Rect(0, 8, 70, 18));
                }
                var image = new System.Windows.Media.DrawingImage(drawing); image.Freeze();
                return new WallTypeRow { Choice = choice, Preview = image,
                    Description = "Basic Wall · " + (type.Width / Mm).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " mm" };
            }).ToList();
        }
        private static List<FinishChoice> Choices(Document doc, FinishKind kind)
        {
            IEnumerable<Element> items;
            if (kind == FinishKind.Floor) items = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().Where(t => !t.IsFoundationSlab);
            else if (kind == FinishKind.Ceiling) items = new FilteredElementCollector(doc).OfClass(typeof(CeilingType));
            else if (kind == FinishKind.Wall || kind == FinishKind.Plaster) items = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().Where(t => t.Kind == WallKind.Basic && t.Width > 0);
            else if (kind == FinishKind.Lintel) items = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilySymbol>()
                .Where(s => s.Family.FamilyPlacementType == FamilyPlacementType.CurveDrivenStructural || s.Family.FamilyPlacementType == FamilyPlacementType.CurveBased);
            else if (kind == FinishKind.Tile) items = new FilteredElementCollector(doc).OfClass(typeof(Material));
            else items = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(s => s.Category != null && new[] { (long)BuiltInCategory.OST_GenericModel, (long)BuiltInCategory.OST_SpecialityEquipment, (long)BuiltInCategory.OST_Furniture }.Contains(s.Category.Id.Value))
                .Where(s => s.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased || s.Family.FamilyPlacementType == FamilyPlacementType.CurveBased ||
                    s.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased || s.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted);
            var result = items.Select(e => new FinishChoice { Id = e.Id.Value, Name = e is FamilySymbol ? ((FamilySymbol)e).FamilyName + " : " + e.Name : e.Name })
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (kind == FinishKind.Tile) result.Insert(0, new FinishChoice { Id = -1, Name = "Theo Category (không gán vật liệu riêng)" });
            return result;
        }
        private static List<FinishSource> Sources(UIDocument ui, FinishKind kind)
        {
            if (kind == FinishKind.Tile) return new List<FinishSource>();
            Document doc = ui.Document;
            var selected = new HashSet<long>(ui.Selection.GetElementIds().Select(id => id.Value));
            IEnumerable<Element> elements = kind == FinishKind.Lintel
                ? new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                    .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows })).WhereElementIsNotElementType()
                : kind == FinishKind.Threshold
                ? new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType()
                : new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>().Where(r => r.Area > 1e-6 && r.Location != null);
            return elements.Select(e =>
            {
                var room = e as Room;
                Level level = doc.GetElement(e.LevelId) as Level;
                Parameter roomPhase = room == null ? null : room.get_Parameter(BuiltInParameter.ROOM_PHASE);
                Element phase = doc.GetElement(roomPhase != null ? roomPhase.AsElementId() : e.CreatedPhaseId);
                return new FinishSource { Id = e.Id.Value, Name = room != null ? room.Number + " — " + room.Name : ((FamilyInstance)e).Symbol.FamilyName + " : " + ((FamilyInstance)e).Symbol.Name + " • Id " + e.Id.Value,
                    Level = (level == null ? "Không rõ Level" : level.Name) + " • " + (phase == null ? "" : phase.Name), Checked = selected.Contains(e.Id.Value) };
            }).OrderBy(s => s.Level).ThenBy(s => s.Name).ToList();
        }
        private class FinishFaceFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) { return e is Floor || e is Wall; }
            public bool AllowReference(Reference reference, XYZ point) { return reference.LinkedElementId == ElementId.InvalidElementId; }
        }
        private static Schema TagSchema()
        {
            Schema schema = Schema.Lookup(TagGuid);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(TagGuid); builder.SetSchemaName("KToolRoomFinish");
            builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("Key", typeof(string)); builder.AddSimpleField("Source", typeof(string)); builder.AddSimpleField("Kind", typeof(string));
            return builder.Finish();
        }
        private static void Tag(Element element, string key, Element source, FinishKind kind)
        {
            Schema schema = TagSchema(); var entity = new Entity(schema);
            entity.Set(schema.GetField("Key"), key); entity.Set(schema.GetField("Source"), source.UniqueId); entity.Set(schema.GetField("Kind"), kind.ToString()); element.SetEntity(entity);
        }
        private static Dictionary<string, List<ElementId>> Existing(Document doc)
        {
            var result = new Dictionary<string, List<ElementId>>();
            Schema schema = Schema.Lookup(TagGuid); if (schema == null) return result;
            foreach (Element element in new FilteredElementCollector(doc).WherePasses(new ExtensibleStorageFilter(TagGuid)).WhereElementIsNotElementType())
            {
                Entity entity = element.GetEntity(schema); if (!entity.IsValid()) continue;
                string key = entity.Get<string>(schema.GetField("Key"));
                if (!result.ContainsKey(key)) result[key] = new List<ElementId>(); result[key].Add(element.Id);
            }
            return result;
        }
        private static string CreateSources(Document doc, FinishKind kind, FinishSettings settings, Action<string> update)
        {
            var existing = Existing(doc); int succeeded = 0, skipped = 0, failed = 0, count = 0;
            var notes = new List<string>();
            // Snapshot biên Room trước khi tạo tường hoàn thiện để tránh thay đổi kết quả Room boundary giữa các Room.
            var boundaries = new Dictionary<long, IList<IList<BoundarySegment>>>();
            if (kind == FinishKind.Wall || kind == FinishKind.Plaster)
                foreach (long id in settings.Sources)
                {
                    var room = doc.GetElement(new ElementId(id)) as Room;
                    if (room != null) boundaries[id] = room.GetBoundarySegments(new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish });
                }
            using (var group = new TransactionGroup(doc, "KTool - " + FinishWindow.Names[(int)kind]))
            {
                group.Start();
                foreach (long id in settings.Sources)
                {
                    update("Đang tạo " + FinishWindow.Names[(int)kind] + " • " + (succeeded + skipped + failed + 1) + "/" + settings.Sources.Count);
                    Element source = doc.GetElement(new ElementId(id));
                    if (source == null) { failed++; continue; }
                    string key = kind + ":" + source.UniqueId;
                    List<ElementId> old;
                    if (existing.TryGetValue(key, out old) && !settings.Replace) { skipped++; continue; }
                    var failures = new FinishFailures();
                    using (var transaction = new Transaction(doc, "Hoàn thiện: " + source.Id.Value))
                    {
                        transaction.Start();
                        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                        try
                        {
                            if (old != null && old.Count > 0) doc.Delete(old);
                            var created = new List<Element>();
                            Room room = source as Room;
                            if (kind == FinishKind.Floor || kind == FinishKind.Ceiling)
                            {
                                var loops = FinishGeometry.RoomLoops(room);
                                double offset = FinishGeometry.BaseOffset(room) + settings.OffsetMm * Mm;
                                if (kind == FinishKind.Floor)
                                {
                                    Floor floor = Floor.Create(doc, loops, new ElementId(settings.TypeId), room.LevelId);
                                    floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(offset); created.Add(floor);
                                }
                                else
                                {
                                    Ceiling ceiling = Ceiling.Create(doc, loops, new ElementId(settings.TypeId), room.LevelId);
                                    ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM).Set(offset + FinishGeometry.Height(room, settings)); created.Add(ceiling);
                                }
                            }
                            else if (kind == FinishKind.Wall || kind == FinishKind.Plaster)
                            {
                                IList<IList<BoundarySegment>> segments = boundaries.ContainsKey(id) ? boundaries[id] : null;
                                if (segments == null) throw new InvalidOperationException("Room không có biên kín.");
                                WallType type = (WallType)doc.GetElement(new ElementId(settings.TypeId));
                                int ignored = 0;
                                foreach (BoundarySegment segment in segments.SelectMany(loop => loop))
                                {
                                    update("Room " + room.Number + " • Đang dựng mặt tường, giữ lỗ cửa");
                                    Wall host = doc.GetElement(segment.ElementId) as Wall;
                                    if (host == null) { ignored++; continue; }
                                    created.AddRange(FinishGeometry.WallSegment(doc, room, host, segment.GetCurve(), type, settings));
                                }
                                if (created.Count == 0) throw new InvalidOperationException("Không có đoạn biên tường xử lý được trong model hiện tại.");
                                if (ignored > 0) notes.Add("Room " + room.Number + ": " + ignored + " biên Room Separation, cột hoặc link không tạo tường.");
                            }
                            else if (kind == FinishKind.Lintel) created.Add(CreateLintel(doc, (FamilyInstance)source, settings));
                            else created.Add(CreateThreshold(doc, (FamilyInstance)source, settings));
                            foreach (Element element in created)
                            {
                                Parameter roomPhase = room == null ? null : room.get_Parameter(BuiltInParameter.ROOM_PHASE);
                                ElementId phase = roomPhase == null ? source.CreatedPhaseId : roomPhase.AsElementId();
                                if (element.HasPhases() && phase != ElementId.InvalidElementId && element.IsPhaseCreatedValid(phase)) element.CreatedPhaseId = phase;
                                Tag(element, key, source, kind);
                                Parameter comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                if (comments != null && !comments.IsReadOnly)
                                    comments.Set("KTool • " + FinishWindow.Names[(int)kind] + " • " + (room == null ? "Cửa Id " + source.Id.Value : "Room " + room.Number + " - " + room.Name));
                            }
                            if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException(string.Join("; ", failures.Errors));
                            succeeded++; count += created.Count;
                            if (failures.Warnings.Count > 0) notes.Add("Id " + id + ": " + string.Join("; ", failures.Warnings.Take(2)));
                        }
                        catch (System.OperationCanceledException) { throw; }
                        catch (Autodesk.Revit.Exceptions.RegenerationFailedException) { throw; }
                        catch (Exception ex)
                        {
                            if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
                            failed++; notes.Add("Id " + id + ": " + ex.Message);
                        }
                    }
                }
                if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu được lượt hoàn thiện.");
            }
            return "Nguồn thành công: " + succeeded + "\nCấu kiện đã tạo: " + count + "\nBỏ qua đã có: " + skipped + "\nNguồn lỗi (đã rollback): " + failed +
                (notes.Count == 0 ? "" : "\n\nChi tiết:\n" + string.Join("\n", notes.Take(18)));
        }

        private static FamilyInstance CreateLintel(Document doc, FamilyInstance opening, FinishSettings settings)
        {
            Wall host = opening.Host as Wall;
            LocationPoint placement = opening.Location as LocationPoint;
            if (host == null || placement == null) throw new InvalidOperationException("Cửa cần có tường chủ và điểm đặt trong model hiện tại.");
            CurveLoop cut = FinishGeometry.DoorCutout(doc, opening);
            XYZ along = new XYZ(opening.HandOrientation.X, opening.HandOrientation.Y, 0);
            if (along.GetLength() < 1e-6) throw new InvalidOperationException("Không xác định được phương ngang cửa.");
            along = along.Normalize();
            // Bổ sung cực trị của cung để đo cả lỗ cửa vòm, không chỉ các điểm tessellation.
            var points = cut.SelectMany(c => c.Tessellate()).ToList();
            foreach (Curve curve in cut)
            {
                Arc arc = curve as Arc;
                Ellipse ellipse = curve as Ellipse;
                if (arc == null && ellipse == null) continue;
                XYZ xAxis = arc != null ? arc.XDirection * arc.Radius : ellipse.XDirection * ellipse.RadiusX;
                XYZ yAxis = arc != null ? arc.YDirection * arc.Radius : ellipse.YDirection * ellipse.RadiusY;
                foreach (XYZ direction in new[] { along, XYZ.BasisZ })
                {
                    double angle = Math.Atan2(yAxis.DotProduct(direction), xAxis.DotProduct(direction));
                    for (int opposite = 0; opposite < 2; opposite++)
                    {
                        double parameter = angle + opposite * Math.PI;
                        double startParameter = curve.IsBound ? curve.GetEndParameter(0) : 0;
                        double endParameter = curve.IsBound ? curve.GetEndParameter(1) : 2 * Math.PI;
                        parameter += Math.Ceiling((startParameter - parameter) / (2 * Math.PI)) * 2 * Math.PI;
                        if (parameter <= endParameter + 1e-9) points.Add(curve.Evaluate(parameter, false));
                    }
                }
            }
            double left = points.Min(p => (p - placement.Point).DotProduct(along));
            double right = points.Max(p => (p - placement.Point).DotProduct(along));
            double head = points.Max(p => p.Z);
            if (right - left < 10 * Mm) throw new InvalidOperationException("Bề rộng lỗ cửa không hợp lệ.");
            double bearing = settings.BearingMm * Mm, bottom = head + settings.OffsetMm * Mm;
            XYZ start = placement.Point + along * (left - bearing);
            XYZ end = placement.Point + along * (right + bearing);
            start = new XYZ(start.X, start.Y, bottom); end = new XYZ(end.X, end.Y, bottom);
            Level level = doc.GetElement(opening.LevelId) as Level ?? doc.GetElement(host.LevelId) as Level;
            if (level == null) throw new InvalidOperationException("Không xác định được Level của cửa.");
            FamilySymbol symbol = (FamilySymbol)doc.GetElement(new ElementId(settings.TypeId));
            if (!symbol.IsActive) symbol.Activate(); doc.Regenerate();
            FamilyInstance beam = doc.Create.NewFamilyInstance(Line.CreateBound(start, end), symbol, level, StructuralType.Beam);
            StructuralFramingUtils.DisallowJoinAtEnd(beam, 0); StructuralFramingUtils.DisallowJoinAtEnd(beam, 1);
            doc.Regenerate();
            double actualBottom = BeamBottom(beam);
            ElementTransformUtils.MoveElement(doc, beam.Id, XYZ.BasisZ * (bottom - actualBottom));
            doc.Regenerate();
            if (Math.Abs(BeamBottom(beam) - bottom) > Mm)
                throw new InvalidOperationException("Family dầm không giữ được cao độ đáy lanh tô yêu cầu; đã hoàn tác cửa này.");
            return beam;
        }
        private static double BeamBottom(FamilyInstance beam)
        {
            var solids = FinishGeometry.Solids(beam.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })).ToList();
            if (solids.Count == 0) throw new InvalidOperationException("Family dầm không có solid 3D để căn đáy lanh tô.");
            double bottom = double.MaxValue;
            foreach (Solid solid in solids)
            {
                BoundingBoxXYZ box = solid.GetBoundingBox();
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                    bottom = Math.Min(bottom, box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                        y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)).Z);
            }
            return bottom;
        }

        private static FamilyInstance CreateThreshold(Document doc, FamilyInstance door, FinishSettings settings)
        {
            Wall host = door.Host as Wall;
            if (host == null) throw new InvalidOperationException("Cửa không có tường chủ.");
            var outline = FinishGeometry.DoorCutout(doc, door);
            var points = outline.SelectMany(c => c.Tessellate()).ToList();
            XYZ along = new XYZ(door.HandOrientation.X, door.HandOrientation.Y, 0).Normalize();
            XYZ original = ((LocationPoint)door.Location).Point;
            double sillZ = points.Min(p => p.Z);
            // Đo tại chân lỗ cửa thay vì bounding box chứa cả cánh và phụ kiện.
            var foot = points.Where(p => p.Z <= sillZ + 2 * Mm).ToList();
            if (foot.Count < 2) throw new InvalidOperationException("Không xác định được hai mép chân cửa.");
            double left = foot.Min(p => (p - original).DotProduct(along)), right = foot.Max(p => (p - original).DotProduct(along));
            double width = right - left, depth = host.Width + 2 * settings.OverhangMm * Mm;
            if (width < 10 * Mm) throw new InvalidOperationException("Biên chân cửa quá nhỏ.");
            XYZ center = original + along * ((left + right) / 2);
            center = new XYZ(center.X, center.Y, sillZ + settings.OffsetMm * Mm);
            Level level = doc.GetElement(door.LevelId) as Level;
            if (level == null) throw new InvalidOperationException("Cửa không có Level hợp lệ.");
            FamilySymbol symbol = (FamilySymbol)doc.GetElement(new ElementId(settings.TypeId));
            if (!symbol.IsActive) symbol.Activate(); doc.Regenerate();
            FamilySymbol specific = symbol;
            FamilyInstance threshold;
            if (specific.Family.FamilyPlacementType == FamilyPlacementType.CurveBased)
                threshold = doc.Create.NewFamilyInstance(Line.CreateBound(center - along * width / 2, center + along * width / 2), specific, level, StructuralType.NonStructural);
            else if (specific.Family.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased)
            {
                XYZ projected;
                Reference hostFace = FloorHostFace(doc, center, out projected);
                threshold = doc.Create.NewFamilyInstance(hostFace, projected, along, specific);
                Parameter freeOffset = threshold.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                if (Math.Abs(center.Z - projected.Z) > Mm)
                {
                    if (freeOffset == null || freeOffset.IsReadOnly) throw new InvalidOperationException("Family face-based không cho chỉnh cao độ so với mặt sàn host.");
                    freeOffset.Set(center.Z - projected.Z);
                }
            }
            else if (specific.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBasedHosted)
            {
                Parameter behavior = specific.Family.get_Parameter(BuiltInParameter.FAMILY_HOSTING_BEHAVIOR);
                Element familyHost = host;
                if (behavior == null || behavior.AsInteger() != 1)
                {
                    XYZ projected;
                    Reference floorFace = FloorHostFace(doc, center, out projected);
                    familyHost = doc.GetElement(floorFace.ElementId);
                }
                threshold = doc.Create.NewFamilyInstance(center, specific, familyHost, level, StructuralType.NonStructural);
                XYZ axis = threshold.GetTransform().BasisX;
                double rotation = Math.Atan2(along.Y, along.X) - Math.Atan2(axis.Y, axis.X);
                if (!(familyHost is Wall) && Math.Abs(rotation) > 1e-6)
                    ElementTransformUtils.RotateElement(doc, threshold.Id, Line.CreateBound(center, center + XYZ.BasisZ), rotation);
            }
            else
            {
                threshold = doc.Create.NewFamilyInstance(center, specific, level, StructuralType.NonStructural);
                // Trục X của Family tương ứng bề rộng ngạch.
                ElementTransformUtils.RotateElement(doc, threshold.Id, Line.CreateBound(center, center + XYZ.BasisZ), Math.Atan2(along.Y, along.X));
            }
            bool curveBased = specific.Family.FamilyPlacementType == FamilyPlacementType.CurveBased;
            if ((threshold.LookupParameter(settings.WidthParam) == null && specific.LookupParameter(settings.WidthParam) != null) ||
                (threshold.LookupParameter(settings.DepthParam) == null && specific.LookupParameter(settings.DepthParam) != null))
            {
                // Chỉ nhân Type khi phải thay kích thước Type, tái dùng đúng Type do KTool đã tạo.
                string dimensionKey = "ThresholdType:" + symbol.UniqueId + ":" + settings.WidthParam + ":" + width.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + ":" + settings.DepthParam + ":" + depth.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                Schema schema = TagSchema();
                FamilySymbol cached = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .FirstOrDefault(s => { Entity entity = s.GetEntity(schema); return entity.IsValid() && entity.Get<string>(schema.GetField("Key")) == dimensionKey; });
                if (cached == null)
                {
                    specific = (FamilySymbol)symbol.Duplicate(symbol.Name + "_KTool_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Tag(specific, dimensionKey, symbol, FinishKind.Threshold);
                }
                else specific = cached;
                if (!specific.IsActive) specific.Activate();
                threshold.Symbol = specific;
            }
            SetLength(threshold, specific, settings.WidthParam, width, curveBased);
            SetLength(threshold, specific, settings.DepthParam, depth, false);
            doc.Regenerate();
            BoundingBoxXYZ bounds = threshold.get_BoundingBox(null);
            if (bounds != null)
            {
                XYZ actualCenter = bounds.Transform.OfPoint((bounds.Min + bounds.Max) / 2);
                ElementTransformUtils.MoveElement(doc, threshold.Id, new XYZ(center.X - actualCenter.X, center.Y - actualCenter.Y, 0));
            }
            return threshold;
        }
        private static void SetLength(FamilyInstance instance, FamilySymbol type, string name, double value, bool optional)
        {
            Parameter p = instance.LookupParameter(name) ?? type.LookupParameter(name);
            if (p == null && optional) return;
            if (p != null && p.StorageType == StorageType.Double && p.Definition.GetDataType().Equals(SpecTypeId.Length) && Math.Abs(p.AsDouble() - value) < 0.01 * Mm) return;
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double || !p.Definition.GetDataType().Equals(SpecTypeId.Length))
                throw new InvalidOperationException("Family không có parameter Length ghi được: " + name + ". Hãy nhập đúng tên parameter.");
            if (!p.Set(value)) throw new InvalidOperationException("Không đặt được parameter " + name);
        }

        private static Reference FloorHostFace(Document doc, XYZ point, out XYZ projected)
        {
            Reference best = null; XYZ bestPoint = null; double distance = 1000 * Mm;
            foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
            {
                BoundingBoxXYZ bounds = floor.get_BoundingBox(null);
                if (bounds == null || point.X < bounds.Min.X - Mm || point.X > bounds.Max.X + Mm || point.Y < bounds.Min.Y - Mm || point.Y > bounds.Max.Y + Mm) continue;
                foreach (Reference reference in HostObjectUtils.GetTopFaces(floor))
                {
                    PlanarFace face = floor.GetGeometryObjectFromReference(reference) as PlanarFace;
                    if (face == null || face.FaceNormal.Z < 0.9999) continue;
                    IntersectionResult projection = face.Project(point);
                    if (projection == null || !face.IsInside(projection.UVPoint) || projection.Distance >= distance) continue;
                    distance = projection.Distance; best = reference; bestPoint = projection.XYZPoint;
                }
            }
            if (best == null) throw new InvalidOperationException("Family cần host nhưng chưa có mặt trên sàn phẳng tại chân cửa (trong 1000 mm). Hãy tạo sàn host hoặc dùng Family đặt tự do.");
            projected = bestPoint; return best;
        }

        private static string CreateTiles(Document doc, Reference reference, FinishSettings settings, Action<string> update)
        {
            Element source = doc.GetElement(reference.ElementId);
            XYZ normal, origin;
            var loops = FinishGeometry.FaceLoops(source, reference, out normal, out origin);
            XYZ u = Math.Abs(normal.Z) > 0.99 ? XYZ.BasisX - normal * normal.DotProduct(XYZ.BasisX) : XYZ.BasisZ.CrossProduct(normal);
            u = u.Normalize(); XYZ v = normal.CrossProduct(u).Normalize();
            double angle = settings.AngleDegrees * Math.PI / 180;
            XYZ rotated = u * Math.Cos(angle) + v * Math.Sin(angle);
            v = -u * Math.Sin(angle) + v * Math.Cos(angle); u = rotated;
            double thickness = settings.TileThicknessMm * Mm;
            var options = new SolidOptions(new ElementId(settings.TypeId), ElementId.InvalidElementId);
            Solid panel = GeometryCreationUtilities.CreateExtrusionGeometry(loops, normal, thickness, options);
            // Khung bao solid luôn bao hết cung cong; tessellation biên có thể bỏ sót cực trị.
            BoundingBoxXYZ box = panel.GetBoundingBox();
            var points = new List<XYZ>();
            for (int bx = 0; bx < 2; bx++) for (int by = 0; by < 2; by++) for (int bz = 0; bz < 2; bz++)
                points.Add(box.Transform.OfPoint(new XYZ(bx == 0 ? box.Min.X : box.Max.X, by == 0 ? box.Min.Y : box.Max.Y, bz == 0 ? box.Min.Z : box.Max.Z)));
            double minU = points.Min(p => (p - origin).DotProduct(u)), maxU = points.Max(p => (p - origin).DotProduct(u));
            double minV = points.Min(p => (p - origin).DotProduct(v)), maxV = points.Max(p => (p - origin).DotProduct(v));
            double width = settings.TileWidthMm * Mm, length = settings.TileLengthMm * Mm, grout = settings.GroutMm * Mm;
            double stepU = width + grout, stepV = length + grout;
            double gridU = minU + settings.GridXmm * Mm, gridV = minV + settings.GridYmm * Mm;
            // Dịch nguyên bước để luôn phủ kín mặt, kể cả khi offset lưới lớn.
            gridU -= Math.Ceiling((gridU - minU) / stepU) * stepU;
            gridV -= Math.Ceiling((gridV - minV) / stepV) * stepV;
            long columns = (long)Math.Ceiling((maxU - gridU) / stepU), rows = (long)Math.Ceiling((maxV - gridV) / stepV);
            if (columns <= 0 || rows <= 0 || columns > 20000 || rows > 20000 || columns * rows > 20000)
                throw new InvalidOperationException("Lưới vượt 20.000 viên trên một mặt. Hãy tăng kích thước viên hoặc chọn mặt nhỏ hơn.");
            string stable = reference.ConvertToStableRepresentation(doc);
            string key;
            using (var hash = SHA256.Create()) key = "Tile:" + source.UniqueId + ":" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(stable))).Replace("-", "");
            List<ElementId> old;
            Existing(doc).TryGetValue(key, out old);
            if (old != null && old.Count > 0 && !settings.Replace) return "Mặt đã có gạch do KTool tạo. Chọn Thay thế nếu muốn chia lại.";
            int created = 0, clippedCount = 0, failed = 0;
            var failures = new FinishFailures();
            using (var transaction = new Transaction(doc, "KTool - Viên gạch ốp lát 3D"))
            {
                transaction.Start(); transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                if (old != null && old.Count > 0) doc.Delete(old);
                for (long y = 0; y < rows; y++) for (long x = 0; x < columns; x++)
                {
                    update("Đang chia viên " + (y * columns + x + 1) + "/" + (rows * columns) + " • Đã tạo " + created);
                    XYZ a = origin + u * (gridU + x * stepU) + v * (gridV + y * stepV);
                    CurveLoop tileLoop = FinishGeometry.Polygon(a, a + u * width, a + u * width + v * length, a + v * length);
                    Solid blank = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { tileLoop }, normal, thickness, options);
                    Solid tile;
                    try { tile = BooleanOperationsUtils.ExecuteBooleanOperation(blank, panel, BooleanOperationsType.Intersect); }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException) { failed++; continue; }
                    if (tile == null || tile.Volume < 1e-9) continue;
                    DirectShape shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                    shape.ApplicationId = "KTool.Finish"; shape.ApplicationDataId = key + ":" + x + ":" + y;
                    shape.Name = "KTool Gạch " + settings.TileWidthMm + "x" + settings.TileLengthMm + "x" + settings.TileThicknessMm;
                    shape.SetShape(new List<GeometryObject> { tile }); Tag(shape, key, source, FinishKind.Tile);
                    var comments = shape.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (comments != null && !comments.IsReadOnly) comments.Set("KTool • Gạch " + settings.TileWidthMm + "x" + settings.TileLengthMm + " mm • Ron " + settings.GroutMm + " mm • Nguồn " + source.Id.Value);
                    if (tile.Volume < blank.Volume - 1e-8) clippedCount++;
                    created++;
                }
                if (created == 0) throw new InvalidOperationException("Không tạo được viên hợp lệ; lượt tạo đã được hoàn tác.");
                if (failed > 0) throw new InvalidOperationException("Boolean thất bại ở " + failed + " ô. Đã hoàn tác cả mặt để không để lại mặt lát thiếu viên.");
                if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException(string.Join("; ", failures.Errors));
            }
            return "Đã tạo " + created + " viên gạch 3D.\nViên biên / viên cắt theo lỗ: " + clippedCount + "\nKhe ron: " + settings.GroutMm + " mm.\nCategory: Generic Models.";
        }
        private class FinishFailures : IFailuresPreprocessor
        {
            public List<string> Errors = new List<string>(), Warnings = new List<string>();
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (var failure in accessor.GetFailureMessages())
                    if (failure.GetSeverity() == FailureSeverity.Warning) { Warnings.Add(failure.GetDescriptionText()); accessor.DeleteWarning(failure); }
                    else Errors.Add(failure.GetDescriptionText());
                return Errors.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
            }
        }
    }
}

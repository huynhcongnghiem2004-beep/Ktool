using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.ExportLayout;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace KTool.DwgMerge
{
    public sealed class MergeCommand
    {
        [CommandMethod("KTOOLDWGMERGE", CommandFlags.Session)]
        public void Merge()
        {
            var editor = Application.DocumentManager.MdiActiveDocument.Editor;
            var prompt = editor.GetString(new PromptStringOptions("\nManifest path: ") { AllowSpaces = true });
            if (prompt.Status != PromptStatus.OK) return;
            string manifest = prompt.StringResult.Trim().Trim('"'); XElement job = null;
            var opened = new List<Autodesk.AutoCAD.ApplicationServices.Document>();
            try
            {
                using (var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) job = XElement.Load(reader);
                string output = (string)job.Attribute("output"), progress = (string)job.Attribute("progress");
                bool loose = (bool?)job.Attribute("looseEntities") ?? false;
                var sheetHandles = new List<List<Handle>>();
                int columns = (int)job.Attribute("columns"); double gap = (double)job.Attribute("gapMm");
                var sheets = job.Elements("Sheet").ToList();
                if (sheets.Count == 0 || columns < 1 || columns > 50 || gap < 0 || !Path.IsPathRooted(output)) throw new System.Exception("Invalid merge manifest.");
                using (var merged = new Database(true, true))
                {
                    merged.Insunits = UnitsValue.Millimeters; merged.TileMode = true;
                    double cursorX = 0, cursorTop = 0, rowHeight = 0; int completed = 0;
                    foreach (var sheet in sheets)
                    {
                        string file = (string)sheet.Attribute("file"), label = (string)sheet.Attribute("name");
                        File.WriteAllText(progress, "Đang gom " + (completed + 1) + "/" + sheets.Count + ": " + label);
                        var docs = Application.DocumentManager;
                        var source = docs.Cast<Autodesk.AutoCAD.ApplicationServices.Document>().FirstOrDefault(d => string.Equals(Path.GetFullPath(d.Name), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase));
                        if (source == null) { source = docs.Open(file, false); opened.Add(source); }
                        docs.MdiActiveDocument = source;
                        using (source.LockDocument())
                        {
                            var prior = HostApplicationServices.WorkingDatabase; HostApplicationServices.WorkingDatabase = source.Database;
                            try
                            {
                                BindReferences(source.Database);
                                ObjectId layoutId; string layoutName; double unitFactor;
                                using (var tx = source.Database.TransactionManager.StartTransaction())
                                {
                                    var dictionary = (DBDictionary)tx.GetObject(source.Database.LayoutDictionaryId, OpenMode.ForRead);
                                    var layoutList = new List<Layout>();
                                    foreach (DBDictionaryEntry entry in dictionary)
                                    { var candidate = (Layout)tx.GetObject(entry.Value, OpenMode.ForRead); if (!candidate.ModelType) layoutList.Add(candidate); }
                                    var layouts = layoutList.OrderBy(l => l.TabOrder).ToList();
                                    var selected = layouts.FirstOrDefault(l => ((BlockTableRecord)tx.GetObject(l.BlockTableRecordId, OpenMode.ForRead)).Cast<ObjectId>()
                                        .Any(id => !(tx.GetObject(id, OpenMode.ForRead) is Viewport viewport) || viewport.Number > 1));
                                    if (selected == null) throw new System.Exception(label + ": không có Layout chứa nội dung sheet.");
                                    layoutId = selected.ObjectId; layoutName = selected.LayoutName; unitFactor = selected.PlotPaperUnits == PlotPaperUnit.Inches ? 25.4 : 1;
                                }
                                LayoutManager.Current.CurrentLayout = layoutName; source.Database.TileMode = false;
                                using (var flat = Engine.Instance().ExportLayout(layoutId))
                                {
                                    if (flat == null) throw new System.Exception(label + ": ExportLayout thất bại: " + Engine.Instance().EngineStatus);
                                    var ids = new ObjectIdCollection(); Extents3d? extents = null;
                                    using (var tx = flat.TransactionManager.StartTransaction())
                                    {
                                        var table = (BlockTable)tx.GetObject(flat.BlockTableId, OpenMode.ForRead);
                                        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                                        foreach (ObjectId id in model)
                                        {
                                            var entity = tx.GetObject(id, OpenMode.ForRead) as Entity; if (entity == null) continue; ids.Add(id);
                                            try { var bounds = entity.GeometricExtents; if (extents.HasValue) { var combined = extents.Value; combined.AddExtents(bounds); extents = combined; } else extents = bounds; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                                        }
                                    }
                                    if (!extents.HasValue || ids.Count == 0) throw new System.Exception(label + ": Model sau chuyển rỗng.");
                                    double width = (extents.Value.MaxPoint.X - extents.Value.MinPoint.X) * unitFactor, height = (extents.Value.MaxPoint.Y - extents.Value.MinPoint.Y) * unitFactor;
                                    if (width <= 0 || height <= 0) throw new System.Exception(label + ": kích thước sheet không hợp lệ.");
                                    ObjectId blockId;
                                    using (var tx = merged.TransactionManager.StartTransaction())
                                    {
                                        var table = (BlockTable)tx.GetObject(merged.BlockTableId, OpenMode.ForWrite);
                                        var block = new BlockTableRecord { Name = "KTOOL_SHEET_" + (completed + 1).ToString("D4"), Origin = Point3d.Origin };
                                        blockId = table.Add(block); tx.AddNewlyCreatedDBObject(block, true); tx.Commit();
                                    }
                                    using (var mapping = new IdMapping()) flat.WblockCloneObjects(ids, blockId, mapping, DuplicateRecordCloning.MangleName, false);
                                    if (completed % columns == 0 && completed > 0) { cursorX = 0; cursorTop -= rowHeight + gap; rowHeight = 0; }
                                    using (var tx = merged.TransactionManager.StartTransaction())
                                    {
                                        var table = (BlockTable)tx.GetObject(merged.BlockTableId, OpenMode.ForRead);
                                        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                                        var position = new Point3d(cursorX - extents.Value.MinPoint.X * unitFactor, cursorTop - extents.Value.MaxPoint.Y * unitFactor, -extents.Value.MinPoint.Z * unitFactor);
                                        var reference = new BlockReference(position, blockId) { ScaleFactors = new Scale3d(unitFactor) };
                                        sheetHandles.Add(PlaceSheet(model,reference,tx,loose)); tx.Commit();
                                    }
                                    cursorX += width + gap; rowHeight = Math.Max(rowHeight, height); completed++;
                                }
                            }
                            finally { HostApplicationServices.WorkingDatabase = prior; }
                        }
                    }
                    merged.UpdateExt(true); merged.SaveAs(output, DwgVersion.AC1032);
                    if (!File.Exists(output) || completed != sheets.Count) throw new System.Exception("Không tạo đủ nội dung các sheet.");
                    using (var verify = new Database(false, true))
                    {
                        verify.ReadDwgFile(output, FileOpenMode.OpenForReadAndAllShare, false, null);
                        using (var tx = verify.TransactionManager.StartTransaction())
                        {
                            var table = (BlockTable)tx.GetObject(verify.BlockTableId, OpenMode.ForRead);
                            var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                            if(sheetHandles.Count!=completed)throw new System.Exception("Thiếu nhóm đối tượng Sheet.");
                            var bounds = new List<Extents3d>();
                            foreach(var handles in sheetHandles)
                            {
                                Extents3d? combined=null;
                                foreach(var handle in handles)
                                {
                                    var id=verify.GetObjectId(false,handle,0);
                                    var entity=tx.GetObject(id,OpenMode.ForRead) as Entity;
                                    if(entity==null||entity.IsErased)throw new System.Exception("Thiếu đối tượng Sheet sau khi lưu.");
                                    if(loose && entity is BlockReference br && ((BlockTableRecord)tx.GetObject(br.BlockTableRecord,OpenMode.ForRead)).Name.StartsWith("KTOOL_SHEET_"))throw new System.Exception("Còn Block bao Sheet trong Model.");
                                    try{var e=entity.GeometricExtents;if(combined.HasValue){var c=combined.Value;c.AddExtents(e);combined=c;}else combined=e;}catch(Autodesk.AutoCAD.Runtime.Exception){}
                                }
                                if(!combined.HasValue)throw new System.Exception("Sheet rỗng sau khi lưu.");
                                var extent=combined.Value;
                                if(bounds.Any(b=>extent.MinPoint.X<b.MaxPoint.X-.001 && extent.MaxPoint.X>b.MinPoint.X+.001 && extent.MinPoint.Y<b.MaxPoint.Y-.001 && extent.MaxPoint.Y>b.MinPoint.Y+.001))throw new System.Exception("Các Sheet chồng lấn sau khi lưu.");
                                bounds.Add(extent);
                            }
                            foreach (ObjectId id in table)
                                if (((BlockTableRecord)tx.GetObject(id, OpenMode.ForRead)).IsFromExternalReference) throw new System.Exception("DWG chung vẫn còn Xref chưa bind.");
                        }
                    }
                    new XElement("Success", new XAttribute("count", completed), new XAttribute("output", output)).Save((string)job.Attribute("result"));
                    File.WriteAllText(progress, "Đã gom " + completed + " sheet vào Model.");
                }
            }
            catch (System.Exception ex)
            {
                if (job != null) File.WriteAllText((string)job.Attribute("failure"), ex.ToString());
                editor.WriteMessage("\nKTOOL_MERGE_FAIL " + ex.Message + "\n");
            }
            finally
            {
                foreach (var document in opened) { try { document.CloseAndDiscard(); } catch { } }
            }
        }
        private static List<Handle> PlaceSheet(BlockTableRecord model, BlockReference reference, Transaction tx, bool loose)
        {
            model.AppendEntity(reference);tx.AddNewlyCreatedDBObject(reference,true);
            if(!loose)return new List<Handle>{reference.Handle};
            var handles=new List<Handle>();
            using(var exploded=new DBObjectCollection())
            {
                reference.Explode(exploded); // Exactly one level; nested symbol blocks remain blocks.
                try
                {
                    foreach(DBObject obj in exploded)
                    {
                        var entity=obj as Entity;
                        if(entity==null)continue;
                        model.AppendEntity(entity);tx.AddNewlyCreatedDBObject(entity,true);handles.Add(entity.Handle);
                    }
                }
                finally{foreach(DBObject obj in exploded)if(obj.ObjectId.IsNull)obj.Dispose();}
            }
            if(handles.Count==0)throw new System.Exception("Không tách được Block bao Sheet; đã hủy gom.");
            reference.Erase();return handles;
        }
        private static void BindReferences(Database database)
        {
            database.ResolveXrefs(false, false);
            var ids = new ObjectIdCollection();
            using (var tx = database.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tx.GetObject(database.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in table)
                {
                    var record = (BlockTableRecord)tx.GetObject(id, OpenMode.ForRead);
                    if (record.IsFromExternalReference)
                    {
                        if (record.IsUnloaded || !record.IsResolved) throw new System.Exception("Xref không tải được: " + record.Name);
                        ids.Add(id);
                    }
                }
            }
            if (ids.Count > 0) database.BindXrefs(ids, false);
        }
#if KTOOL_SELF_TEST
        [CommandMethod("KTOOLDWGSELFTEST", CommandFlags.Modal)]
        public void SelfTest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument; var db = doc.Database;
            string status = Path.Combine(Path.GetDirectoryName(typeof(MergeCommand).Assembly.Location), "selftest-result.txt");
            File.WriteAllText(status, "START");
            try
            {
                var layoutId = LayoutManager.Current.CreateLayout("KTOOL_TEST");
                LayoutManager.Current.CurrentLayout = "KTOOL_TEST"; db.TileMode = false;
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    var table = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    var rectangle = Rect(0, 0, 100, 100); model.AppendEntity(rectangle); tx.AddNewlyCreatedDBObject(rectangle, true);
                    var text = new DBText { TextString = "MODEL_TEST", Position = new Point3d(20, 50, 0), Height = 5 }; model.AppendEntity(text); tx.AddNewlyCreatedDBObject(text, true);
                    var layout = (Layout)tx.GetObject(layoutId, OpenMode.ForRead);
                    var paper = (BlockTableRecord)tx.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
                    var border = Rect(0, 0, 220, 160); paper.AppendEntity(border); tx.AddNewlyCreatedDBObject(border, true);
                    var title = new DBText { TextString = "SHEET_TEST", Position = new Point3d(10, 10, 0), Height = 4 }; paper.AppendEntity(title); tx.AddNewlyCreatedDBObject(title, true);
                    var viewport = new Viewport { CenterPoint = new Point3d(110, 85, 0), Width = 180, Height = 120, ViewCenter = new Point2d(50, 50), ViewHeight = 120, ViewDirection = Vector3d.ZAxis, ViewTarget = Point3d.Origin };
                    paper.AppendEntity(viewport); tx.AddNewlyCreatedDBObject(viewport, true); viewport.On = true; tx.Commit();
                }
                LayoutManager.Current.CurrentLayout = "KTOOL_TEST"; db.TileMode = false;
                db.SaveAs(Path.Combine(Path.GetDirectoryName(typeof(MergeCommand).Assembly.Location), "selftest-source.dwg"), DwgVersion.AC1032);
                using (var flat = Engine.Instance().ExportLayout(layoutId))
                {
                    if (flat == null) throw new System.Exception("ExportLayout returned null: " + Engine.Instance().EngineStatus);
                    using (var tx = flat.TransactionManager.StartTransaction())
                    {
                        var table = (BlockTable)tx.GetObject(flat.BlockTableId, OpenMode.ForRead);
                        var model = (BlockTableRecord)tx.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                        int count = model.Cast<ObjectId>().Count();
                        if (count < 3) throw new System.Exception("Flattened model is missing geometry: " + count);
                        doc.Editor.WriteMessage("\nKTOOL_SELFTEST_PASS entities=" + count + " status=" + Engine.Instance().EngineStatus + "\n");
                        File.WriteAllText(status, "PASS entities=" + count);
                    }
                }
            }
            catch (System.Exception ex) { File.WriteAllText(status, "FAIL " + ex); doc.Editor.WriteMessage("\nKTOOL_SELFTEST_FAIL " + ex + "\n"); }
        }
        private static Polyline Rect(double x, double y, double w, double h)
        { var p = new Polyline(); p.AddVertexAt(0, new Point2d(x, y), 0, 0, 0); p.AddVertexAt(1, new Point2d(x + w, y), 0, 0, 0); p.AddVertexAt(2, new Point2d(x + w, y + h), 0, 0, 0); p.AddVertexAt(3, new Point2d(x, y + h), 0, 0, 0); p.Closed = true; return p; }
#endif
    }
}

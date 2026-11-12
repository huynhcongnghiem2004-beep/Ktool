using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.Dim
{
    internal sealed class StairSolidItem
    {
        public string Key { get; private set; }
        public Solid Geometry { get; private set; }
        public StairSolidItem(string key, Solid geometry)
        {
            Key = key; Geometry = geometry;
        }
    }

    internal static class StairSlabFamilyWriter
    {
        // Stable per-Stair family name. Repeated execution reloads same family and
        // reuses its instance, instead of creating additional copies.
        public static void CreateOrUpdate(Autodesk.Revit.ApplicationServices.Application app,
            Document project, Stairs stair, IList<StairSolidItem> solids,
            string templatePath, string materialName)
        {
            if (solids == null || solids.Count == 0) throw new ArgumentException("Danh sách bản thang rỗng.");
            string familyName = "KTool_StairBTCT_" + SafeName(stair.UniqueId);
            string tempDir = Path.Combine(Path.GetTempPath(), "KTool_StairSlabPro");
            Directory.CreateDirectory(tempDir);
            string tempPath = Path.Combine(tempDir, familyName + ".rfa");
            // Revit Family geometry must be LOCAL to the family insertion point.
            XYZ position = stair.get_BoundingBox(null).Min;
            Transform toLocal = Transform.CreateTranslation(position.Negate());
            Document familyDoc = null;
            try
            {
                familyDoc = app.NewFamilyDocument(templatePath);
                if (familyDoc == null || !familyDoc.IsFamilyDocument)
                    throw new InvalidOperationException("Không tạo được Generic Model Family từ template.");
                using (Transaction tx = new Transaction(familyDoc, "Dựng bản thang BTCT"))
                {
                    tx.Start();
                    foreach (var part in solids)
                    {
                        // FreeFormElement is editable in the Family Editor.
                        Solid local = SolidUtils.CreateTransformed(part.Geometry, toLocal);
                        FreeFormElement element = FreeFormElement.Create(familyDoc, local);
                        var comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (comments != null && !comments.IsReadOnly) comments.Set(part.Key);
                    }
                    tx.Commit();
                }
                var save = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 };
                familyDoc.SaveAs(tempPath, save);
            }
            finally
            {
                if (familyDoc != null) familyDoc.Close(false);
            }

            using (Transaction tx = new Transaction(project, "KTool - Tạo/cập nhật Family bản thang"))
            {
                tx.Start();
                Family family;
                project.LoadFamily(tempPath, new OverwriteFamilyOptions(), out family);
                if (family == null)
                    family = new FilteredElementCollector(project).OfClass(typeof(Family))
                        .Cast<Family>().FirstOrDefault(x => x.Name == familyName);
                if (family == null)
                    throw new InvalidOperationException("Không thể tải Family. Kiểm tra Generic Model template.");
                ElementId typeId = family.GetFamilySymbolIds().FirstOrDefault();
                var symbol = project.GetElement(typeId) as FamilySymbol;
                if (symbol == null) throw new InvalidOperationException("Family không có type hợp lệ.");
                if (!symbol.IsActive) symbol.Activate();

                FamilyInstance instance = new FilteredElementCollector(project)
                    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .FirstOrDefault(x => x.Symbol != null && x.Symbol.Family.Id == family.Id);
                if (instance == null)
                {
                    project.Create.NewFamilyInstance(position, symbol, StructuralType.NonStructural);
                }
                else
                {
                    // Same family is reloaded, geometry updates automatically; move insertion origin.
                    XYZ oldPoint = (instance.Location as LocationPoint)?.Point;
                    if (oldPoint != null && oldPoint.DistanceTo(position) > 1e-7)
                        ElementTransformUtils.MoveElement(project, instance.Id, position - oldPoint);
                }
                tx.Commit();
            }
        }

        private static string SafeName(string uniqueId)
        {
            // Revit UniqueId contains alphanumerics and hyphens; stable across sessions.
            return new string(uniqueId.Where(char.IsLetterOrDigit).ToArray());
        }

        private sealed class OverwriteFamilyOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}

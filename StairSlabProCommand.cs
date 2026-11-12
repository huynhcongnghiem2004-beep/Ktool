using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using TextBox = System.Windows.Controls.TextBox;
using Panel = System.Windows.Controls.Panel;

namespace SDK_HD.Dim
{
    /// <summary>
    /// Builds slabs below native straight runs and horizontal landings.
    /// Thickness/clearance are vertical below the existing concrete soffit.
    /// Supports DirectShape and loadable Generic Model Family output.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class StairSlabProCommand : IExternalCommand
    {
        private const string AppId = "KTool.StairSlabPro.v2";
        private const double FtPerMm = 1.0 / 304.8;

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument uidoc = data.Application.ActiveUIDocument;
            if (uidoc == null) { message = "Không có tài liệu Revit đang mở."; return Result.Failed; }
            Document doc = uidoc.Document;
            try
            {
                Stairs stairs = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id)).OfType<Stairs>().FirstOrDefault();
                if (stairs == null)
                {
                    Reference pick = uidoc.Selection.PickObject(ObjectType.Element,
                        new StairOnlyFilter(), "Chọn cầu thang Stair cần dựng bản bê tông");
                    stairs = doc.GetElement(pick.ElementId) as Stairs;
                }
                if (stairs == null) return Result.Cancelled;
                var settings = new StairSlabOptionsWindow();
                if (settings.ShowDialog() != true) return Result.Cancelled;

                var solids = new List<StairSolidItem>();
                var warnings = new List<string>();
                // Solids calculated outside transaction; they are not written to document until commit.
                ElementId materialId = FindConcreteMaterial(doc, settings.MaterialName);
                foreach (ElementId id in stairs.GetStairsRuns())
                {
                    var run = doc.GetElement(id) as StairsRun;
                    if (run == null) continue;
                    Solid slab;
                    string reason;
                    if (BuildRunSolid(run, settings.ThicknessMm * FtPerMm,
                        settings.ClearanceMm * FtPerMm, materialId, out slab, out reason))
                        solids.Add(new StairSolidItem("RUN|" + run.UniqueId, slab));
                    else warnings.Add("Vế " + id.Value + ": " + reason);
                }
                if (settings.IncludeLanding)
                {
                    foreach (ElementId id in stairs.GetStairsLandings())
                    {
                        var landing = doc.GetElement(id) as StairsLanding;
                        if (landing == null) continue;
                        Solid slab;
                        string reason;
                        if (BuildLandingSolid(landing, settings.ThicknessMm * FtPerMm,
                            settings.ClearanceMm * FtPerMm, materialId, out slab, out reason))
                            solids.Add(new StairSolidItem("LANDING|" + landing.UniqueId, slab));
                        else warnings.Add("Chiếu nghỉ " + id.Value + ": " + reason);
                    }
                }
                if (solids.Count == 0)
                {
                    TaskDialog.Show("KTool StairSlabPro", "Không dựng được bản BTCT.\n" + string.Join("\n", warnings));
                    return Result.Cancelled;
                }

                if (settings.CreateFamily)
                {
                    if (string.IsNullOrWhiteSpace(settings.TemplatePath) ||
                        !System.IO.File.Exists(settings.TemplatePath))
                    {
                        TaskDialog.Show("KTool", "Chọn template Generic Model .rft có thật trên máy.");
                        return Result.Cancelled;
                    }
                    StairSlabFamilyWriter.CreateOrUpdate(data.Application.Application, doc,
                        stairs, solids, settings.TemplatePath, settings.MaterialName);
                }
                else
                {
                    using (Transaction tx = new Transaction(doc, "KTool - Tạo/cập nhật bản thang BTCT"))
                    {
                        tx.Start();
                        UpdateDirectShapes(doc, stairs, solids);
                        if (tx.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Không lưu được bản thang vào mô hình.");
                    }
                }
                TaskDialog.Show("KTool StairSlabPro", "Đã xử lý " + solids.Count +
                    " cấu kiện. Chế độ: " + (settings.CreateFamily ? "Generic Model Family" : "DirectShape") +
                    (warnings.Count == 0 ? "" : "\n\nCác phần chưa hỗ trợ:\n" + string.Join("\n", warnings.Take(12))) +
                    "\n\nLưu ý: Cần kiểm tra mặt cắt và khả năng chịu lực trước thi công.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.ToString(); return Result.Failed; }
        }

        private static void UpdateDirectShapes(Document doc, Stairs stairs, List<StairSolidItem> items)
        {
            string prefix = stairs.UniqueId + "|";
            var old = new FilteredElementCollector(doc).OfClass(typeof(DirectShape))
                .Cast<DirectShape>()
                .Where(s => s.ApplicationId == AppId && s.ApplicationDataId != null &&
                    s.ApplicationDataId.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(s => s.ApplicationDataId, s => s);
            var used = new HashSet<string>();
            foreach (var item in items)
            {
                string key = prefix + item.Key;
                DirectShape shape;
                if (!old.TryGetValue(key, out shape))
                    shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                shape.ApplicationId = AppId;
                shape.ApplicationDataId = key;
                shape.Name = "KTool_BTCT_" + item.Key.Split('|')[0];
                shape.SetShape(new List<GeometryObject> { item.Geometry });
                used.Add(key);
            }
            foreach (var stale in old)
                if (!used.Contains(stale.Key)) doc.Delete(stale.Value.Id);
        }

        private static bool BuildLandingSolid(StairsLanding landing, double thickness,
            double clearance, ElementId materialId, out Solid result, out string reason)
        {
            return StairSlabNativeSoffit.TryBuild(landing, thickness, clearance, materialId, out result, out reason);
        }

        private static void GatherTopFaces(GeometryElement ge, IList<PlanarFace> output)
        {
            foreach (GeometryObject obj in ge)
            {
                var instance = obj as GeometryInstance;
                if (instance != null) { GatherTopFaces(instance.GetInstanceGeometry(), output); continue; }
                var solid = obj as Solid;
                if (solid == null || solid.Volume <= 1e-8) continue;
                foreach (Face f in solid.Faces)
                {
                    var planar = f as PlanarFace;
                    if (planar != null && planar.FaceNormal.Z > 0.999) output.Add(planar);
                }
            }
        }

        private static bool BuildRunSolid(StairsRun run, double thickness, double clearance,
            ElementId material, out Solid result, out string reason)
        {
            if (run.StairsRunStyle == StairsRunStyle.Sketched)
            {
                result = null;
                reason = "Vế Sketched có bậc quạt cần đổi cấu tạo Stair trước. Dùng lệnh Chiếu nghỉ phẳng để tạo phương án ba vế và hai chiếu nghỉ đi lại ngang.";
                return false;
            }
            return StairSlabNativeSoffit.TryBuild(run, thickness, clearance, material, out result, out reason);
        }

        private static void CollectGeometry(GeometryElement geometry, Transform tr,
            IList<XYZ> treads, IList<XYZ> verts)
        {
            foreach (GeometryObject go in geometry)
            {
                GeometryInstance gi = go as GeometryInstance;
                if (gi != null)
                {
                    CollectGeometry(gi.GetSymbolGeometry(), tr.Multiply(gi.Transform), treads, verts);
                    continue;
                }
                Solid solid = go as Solid;
                if (solid == null || solid.Faces.Size == 0 || solid.Volume < 1e-6) continue;
                foreach (Face face in solid.Faces)
                {
                    Mesh mesh = face.Triangulate();
                    foreach (XYZ vertex in mesh.Vertices)
                        verts.Add(tr.OfPoint(vertex));
                    PlanarFace pf = face as PlanarFace;
                    if (pf != null && tr.OfVector(pf.FaceNormal).Normalize().Z > 0.99
                        && face.Area > 0.05)
                    {
                        BoundingBoxUV bb = face.GetBoundingBox();
                        UV center = new UV((bb.Min.U + bb.Max.U) * 0.5, (bb.Min.V + bb.Max.V) * 0.5);
                        if (face.IsInside(center))
                            treads.Add(tr.OfPoint(face.Evaluate(center)));
                    }
                }
            }
        }

        private static ElementId FindConcreteMaterial(Document doc, string name)
        {
            Material m = new FilteredElementCollector(doc).OfClass(typeof(Material))
                .Cast<Material>().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (m == null) m = new FilteredElementCollector(doc).OfClass(typeof(Material))
                .Cast<Material>().FirstOrDefault(x => x.Name.IndexOf("concrete", StringComparison.OrdinalIgnoreCase) >= 0
                    || x.Name.IndexOf("bê tông", StringComparison.OrdinalIgnoreCase) >= 0);
            return m == null ? ElementId.InvalidElementId : m.Id;
        }

        private class StairOnlyFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) { return element is Stairs; }
            public bool AllowReference(Reference reference, XYZ point) { return false; }
        }
    }

    internal class StairSlabOptionsWindow : Window
    {
        private readonly TextBox thickness, clearance, material, template;
        private readonly CheckBox landing, family;
        public double ThicknessMm { get; private set; }
        public double ClearanceMm { get; private set; }
        public string MaterialName { get; private set; }
        public string TemplatePath { get; private set; }
        public bool IncludeLanding { get { return landing.IsChecked == true; } }
        public bool CreateFamily { get { return family.IsChecked == true; } }

        public StairSlabOptionsWindow()
        {
            Title = "KTool - Bản bê tông cầu thang";
            Width = 475; Height = 460;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new StackPanel { Margin = new Thickness(20) };
            root.Children.Add(new TextBlock { Text = "Bản bê tông dưới bậc Stair",
                FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0,0,0,12) });
            root.Children.Add(new TextBlock { Text = "Chiều dày bản đo đứng (mm)" });
            thickness = AddInput(root, "100");
            root.Children.Add(new TextBlock { Text = "Khoảng hở đứng (mm)" });
            clearance = AddInput(root, "50");
            landing = new CheckBox { Content = "Tạo bản đáy dưới chiếu nghỉ hiện có", IsChecked = true,
                Margin = new Thickness(0, 0, 0, 10) };
            root.Children.Add(landing);
            root.Children.Add(new TextBlock { Text = "Vật liệu trong Project (DirectShape)" });
            material = AddInput(root, "NPD_BTCT");
            family = new CheckBox { Content = "Tạo/cập nhật Family Generic Model (RFA)",
                Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(family);
            root.Children.Add(new TextBlock { Text = "Generic Model template (.rft)" });
            var templateRow = new DockPanel { Margin = new Thickness(0, 4, 0, 10) };
            var browse = new Button { Content = "Chọn...", Width = 72, Margin = new Thickness(6,0,0,0) };
            DockPanel.SetDock(browse, Dock.Right);
            templateRow.Children.Add(browse);
            template = new TextBox { Height = 27, Text = "" };
            templateRow.Children.Add(template);
            browse.Click += (s,e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Revit Family Template (*.rft)|*.rft" };
                if (dlg.ShowDialog() == true) template.Text = dlg.FileName;
            };
            root.Children.Add(templateRow);
            root.Children.Add(new TextBlock { Text = "Chiều dày và khoảng hở đều đo đứng dưới đáy Stair.\n" +
                "Đổi bậc quạt bằng lệnh Chiếu nghỉ phẳng trước.",
                TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
            var ok = new Button { Content = "Tạo / cập nhật", Width = 125, Height = 32 };
            var cancel = new Button { Content = "Hủy", Width = 75, Height = 32,
                Margin = new Thickness(8,0,0,0) };
            ok.Click += (sender,e) =>
            {
                double t, c;
                if (!Parse(thickness.Text, out t) || !Parse(clearance.Text, out c) ||
                    t < 50 || t > 500 || c < 0 || c > 200)
                { MessageBox.Show("Chiều dày 50–500 mm; khoảng hở 0–200 mm.", "KTool"); return; }
                if (CreateFamily && !System.IO.File.Exists(template.Text))
                { MessageBox.Show("Vui lòng chọn file Generic Model .rft.", "KTool"); return; }
                ThicknessMm = t; ClearanceMm = c;
                MaterialName = material.Text.Trim(); TemplatePath = template.Text.Trim();
                DialogResult = true;
            };
            cancel.Click += (sender,e) => { DialogResult = false; };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            root.Children.Add(buttons); Content = root;
        }
        private static TextBox AddInput(Panel p, string value)
        {
            var tb = new TextBox { Text = value, Height = 27,
                Margin = new Thickness(0, 4, 0, 10) }; p.Children.Add(tb); return tb;
        }
        private static bool Parse(string text, out double value)
        {
            return double.TryParse(text.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}



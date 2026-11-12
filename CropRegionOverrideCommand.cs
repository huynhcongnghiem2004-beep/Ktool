using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ComboBox = System.Windows.Controls.ComboBox;
using Point = System.Windows.Point;

namespace SDK_HD.Dim
{
    internal sealed class CropRegionStylePanel : StackPanel
    {
        private readonly ComboBox pattern = new ComboBox { DisplayMemberPath = "Name" }, weight = new ComboBox();
        private readonly Button color = new Button { Content = "Chọn màu đường Crop", Height = 32 };
        private readonly CheckBox visible = new CheckBox { Content = "Hiện đường Crop", IsChecked = true, Margin = new Thickness(0, 10, 0, 8) };
        private byte red, green, blue;
        internal event EventHandler StyleChanged;
        private sealed class Pattern { public string Name { get; set; } internal ElementId Id; internal double[] Dashes; }
        internal ImageSource Preview
        {
            get
            {
                var drawing = new DrawingGroup();
                using (var dc = drawing.Open())
                {
                    dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 360, 190));
                    var gray = new Pen(Brushes.LightSlateGray, 1);
                    dc.DrawRectangle(null, gray, new Rect(110, 55, 140, 80));
                    for (int i = 0; i < 5; i++) dc.DrawLine(gray, new Point(130, 65 + i * 12), new Point(230, 65 + i * 12));
                    double thickness = 1 + (weight.SelectedItem is int value ? value : 1) * .25;
                    var pen = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue)), thickness);
                    var selected = pattern.SelectedItem as Pattern;
                    if (selected?.Dashes != null && selected.Dashes.Length > 0)
                    { pen.DashStyle = new DashStyle(selected.Dashes.Select(v => v / thickness), 0); pen.DashCap = PenLineCap.Round; }
                    if (visible.IsChecked == true) dc.DrawRectangle(null, pen, new Rect(45, 25, 270, 140));
                }
                var image = new DrawingImage(drawing); image.Freeze(); return image;
            }
        }
        internal CropRegionStylePanel(Document doc)
        {
            Children.Add(new TextBlock { Text = "OVERRIDE CROP VIEW REGION", FontWeight = FontWeights.Bold, Foreground = Brushes.OliveDrab, Margin = new Thickness(0, 12, 0, 8) });
            Children.Add(color); color.Foreground = Brushes.Black; color.Background = Brushes.White;
            color.Click += (s, e) =>
            {
                using (var dialog = new System.Windows.Forms.ColorDialog { Color = System.Drawing.Color.FromArgb(red, green, blue), FullOpen = true })
                {
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    red = dialog.Color.R; green = dialog.Color.G; blue = dialog.Color.B;
                    color.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
                    color.Foreground = red * .299 + green * .587 + blue * .114 < 128 ? Brushes.White : Brushes.Black;
                    color.Content = "Màu Crop  #" + red.ToString("X2") + green.ToString("X2") + blue.ToString("X2");
                    StyleChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            Children.Add(new TextBlock { Text = "Kiểu nét", Margin = new Thickness(0, 8, 0, 4) });
            pattern.Items.Add(new Pattern { Name = "Solid", Id = LinePatternElement.GetSolidPatternId() });
            foreach (var p in new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>().OrderBy(p => p.Name))
            using (var linePattern = p.GetLinePattern())
                pattern.Items.Add(new Pattern { Name = p.Name, Id = p.Id, Dashes = linePattern.GetSegments().Select(s => s.Type == LinePatternSegmentType.Dot ? .1 : Math.Max(.1, s.Length * 304.8 * 96 / 25.4)).ToArray() });
            pattern.SelectedIndex = 0; pattern.MinHeight = 30; Children.Add(pattern);
            Children.Add(new TextBlock { Text = "Line Weight (1–16, theo bảng nét Revit)", Margin = new Thickness(0, 8, 0, 4) });
            weight.ItemsSource = Enumerable.Range(1, 16).ToList(); weight.SelectedIndex = 0; weight.MinHeight = 30; Children.Add(weight); Children.Add(visible);
            pattern.SelectionChanged += (s, e) => StyleChanged?.Invoke(this, EventArgs.Empty);
            weight.SelectionChanged += (s, e) => StyleChanged?.Invoke(this, EventArgs.Empty);
            visible.Checked += (s, e) => StyleChanged?.Invoke(this, EventArgs.Empty);
            visible.Unchecked += (s, e) => StyleChanged?.Invoke(this, EventArgs.Empty);
        }
        private static List<ElementId> ResolveCropElements(Document doc, View view)
        {
            // Crop elements reference their owning view through ID_PARAM. A view
            // collector/visibility difference is not reliable for internal elements.
            var provider = new ParameterValueProvider(new ElementId(BuiltInParameter.ID_PARAM));
            var rule = new FilterElementIdRule(provider, new FilterNumericEquals(), view.Id);
            var candidates = new FilteredElementCollector(doc).WherePasses(new ElementParameterFilter(rule))
                .WhereElementIsNotElementType().Where(e => e.Id != view.Id).ToList();
            if (candidates.Count == 1) return new List<ElementId> { candidates[0].Id };
            var modelCrop = new[] { BuiltInCategory.OST_CropBoundary, BuiltInCategory.OST_CropBoundarySpecial };
            var explicitCrop = candidates.Where(e => e.Category != null && modelCrop.Any(c => e.Category.Id == new ElementId(c))).ToList();
            if (explicitCrop.Count > 0) return explicitCrop.Select(e => e.Id).ToList();
            // Accept explicit crop boundary children only, never a generic viewer,
            // a different view, or an annotation-crop boundary as a fallback.
            var children = view.GetDependentElements(null).Select(doc.GetElement)
                .Where(e => e != null && e.Id != view.Id && e.Category != null && modelCrop.Any(c => e.Category.Id == new ElementId(c)))
                .Select(e => e.Id).Distinct().ToList();
            if (children.Count > 0) return children;
            string detail = string.Join("; ", candidates.Take(8).Select(e => "Id " + e.Id + ", " + e.GetType().Name + ", category " + (e.Category?.Name ?? "không có")));
            throw new InvalidOperationException("Chưa xác định được Crop liên kết với View Id " + view.Id
                + ". Số ứng viên ID_PARAM: " + candidates.Count + (detail.Length == 0 ? "" : ". " + detail));
        }
        internal void Apply(Document doc, View view)
        {
            using (var item = new SubTransaction(doc))
            {
                item.Start();
                try
                {
                    view.CropBoxVisible = true; doc.Regenerate();
                    var ids = ResolveCropElements(doc, view);
                    foreach (var id in ids)
                    using (var graphics = view.GetElementOverrides(id))
                    {
                        graphics.SetProjectionLineColor(new Autodesk.Revit.DB.Color(red, green, blue));
                        graphics.SetProjectionLinePatternId(((Pattern)pattern.SelectedItem).Id);
                        graphics.SetProjectionLineWeight((int)weight.SelectedItem);
                        view.SetElementOverrides(id, graphics);
                    }
                    view.CropBoxVisible = visible.IsChecked == true; doc.Regenerate();
                    if (item.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu Override Crop.");
                }
                catch { if (item.GetStatus() == TransactionStatus.Started) item.RollBack(); throw; }
            }
        }
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class CropRegionOverrideCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument; if (ui == null) return Result.Cancelled;
            var doc = ui.Document;
            var views = ui.Selection.GetElementIds().Select(doc.GetElement).Select(e => e is Viewport port ? doc.GetElement(port.ViewId) as View : e as View)
                .Where(v => v != null && !v.IsTemplate && (v is ViewPlan || v is ViewSection)).GroupBy(v => v.Id).Select(g => g.First()).ToList();
            if (views.Count == 0 && (ui.ActiveView is ViewPlan || ui.ActiveView is ViewSection)) views.Add(ui.ActiveView);
            var initial = new HashSet<long>(views.Select(v => v.Id.Value));
            var sheetViews = new HashSet<long>(); var sheets = new Dictionary<long, string>();
            foreach (Viewport port in new FilteredElementCollector(doc).OfClass(typeof(Viewport)))
            {
                var sheet = doc.GetElement(port.SheetId) as ViewSheet;
                if (sheet != null) sheets[port.ViewId.Value] = sheet.SheetNumber;
                if (port.SheetId == ui.ActiveView.Id) sheetViews.Add(port.ViewId.Value);
            }
            if (initial.Count == 0 && ui.ActiveView is ViewSheet) initial.UnionWith(sheetViews);
            var eligible = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && (v is ViewPlan || v is ViewSection)).OrderBy(v => v.Name).ToList();
            var rows = eligible.Select(v => new CropViewRow { Id = v.Id.Value, Name = v.Name, Kind = v.ViewType.ToString(),
                Sheet = sheets.TryGetValue(v.Id.Value, out var sheetNumber) ? "Sheet " + sheetNumber : "Chưa lên sheet",
                Initial = initial.Contains(v.Id.Value), Checked = initial.Contains(v.Id.Value), ActiveSheet = sheetViews.Contains(v.Id.Value), CropActive = v.CropBoxActive }).ToList();
            if (rows.Count == 0) { TaskDialog.Show("Override Crop View Region", "Dự án chưa có mặt bằng/mặt cắt/mặt đứng để xử lý."); return Result.Cancelled; }
            var panel = new CropRegionStylePanel(doc);
            var dialog = new CropRegionOverrideWindow(rows, panel);
            new System.Windows.Interop.WindowInteropHelper(dialog).Owner = data.Application.MainWindowHandle;
            if (dialog.ShowDialog() != true) return Result.Cancelled;
            views = dialog.SelectedIds.Select(id => doc.GetElement(new ElementId(id)) as View).Where(v => v != null).ToList();
            var errors = new List<string>(); int done = 0;
            using (var tx = new Transaction(doc, "Override Crop View Region"))
            {
                try
                {
                    tx.Start();
                    foreach (var view in views) { try { panel.Apply(doc, view); done++; } catch (Exception ex) { errors.Add(view.Name + ": " + ex.Message); } }
                    if (tx.Commit() != TransactionStatus.Committed) { message = "Revit không lưu Override Crop."; return Result.Failed; }
                }
                catch (Exception ex) { if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack(); message = ex.Message; return Result.Failed; }
            }
            new TaskDialog("Override Crop View Region") { MainInstruction = "Đã cập nhật " + done + "/" + views.Count + " view", ExpandedContent = string.Join("\n", errors), CommonButtons = TaskDialogCommonButtons.Close }.Show();
            return done > 0 ? Result.Succeeded : Result.Cancelled;
        }
    }
}



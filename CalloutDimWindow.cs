using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using W = System.Windows;
using Grid = System.Windows.Controls.Grid;
using Control = System.Windows.Controls.Control;
using Color = System.Windows.Media.Color;

namespace SDK_HD.Dim
{
    internal sealed class CalloutDimWindow : Window
    {
        private readonly TextBox name = new TextBox();
        private readonly TextBox scale = new TextBox();
        private readonly TextBox gap = new TextBox { Text = "8" };
        private readonly ComboBox viewTypes = new ComboBox { DisplayMemberPath = "Name" };
        private readonly ComboBox dimTypes = new ComboBox { DisplayMemberPath = "Name" };
        private readonly CheckBox[,] sides = new CheckBox[3, 4];
        private readonly Canvas preview = new Canvas { Width = 460, Height = 380, Background = Brushes.White };
        private readonly Brush[] colors = { Brushes.DodgerBlue, Brushes.DarkOrange, Brushes.MediumSeaGreen };
        private readonly CheckBox floorPlan = new CheckBox { Content = "Mặt bằng lát sàn", IsChecked = true };
        private readonly CheckBox ceilingPlan = new CheckBox { Content = "Mặt bằng trần (Reflected Ceiling Plan)", IsChecked = true };
        private readonly Dictionary<string, ComboBox> templates = new Dictionary<string, ComboBox>();
        private sealed class TemplateChoice
        {
            public string Name { get; set; }
            internal ElementId Id = ElementId.InvalidElementId;
        }
        internal ElementId TemplateFor(string key) => ((TemplateChoice)templates[key.Length == 1 ? "Mặt đứng" : key].SelectedItem).Id;
        internal bool CreateCeilingPlan => ceilingPlan.IsChecked == true;
        private readonly CheckBox elevations = new CheckBox { Content = "Tạo mặt đứng nội thất", IsChecked = true };
        private readonly CheckBox sheet = new CheckBox { Content = "Xếp lên sheet theo mẫu (chưa có khung tên)", IsChecked = true };
        private readonly CheckBox[] elevationSides = new CheckBox[4];
        private readonly TextBox height = new TextBox { Text = "2700" };
        private readonly CheckBox adjustBottom = new CheckBox { Content = "Chỉnh cạnh dưới crop mặt đứng", IsChecked = true };
        private readonly TextBox bottomOffset = new TextBox { Text = "0" };
        private readonly CheckBox centerTitles = new CheckBox { Content = "Căn giữa Title với từng viewport trên sheet", IsChecked = true };
        private readonly CheckBox fitCrop = new CheckBox { Content = "Fit Crop Grid / Level cho các view", IsChecked = true };
        private readonly TextBox[] fitOffsets = new TextBox[4];
        internal bool AdjustBottom => adjustBottom.IsChecked == true;
        internal double BottomOffsetMm { get; private set; }
        internal bool CenterTitles => centerTitles.IsChecked == true;
        internal bool FitCrop => fitCrop.IsChecked == true;
        internal FitCropSettings DatumFitSettings { get; private set; }
        internal bool CreateFloorPlan => floorPlan.IsChecked == true;
        internal bool CreateElevations => elevations.IsChecked == true;
        internal bool CreateSheet => sheet.IsChecked == true;
        internal double ElevationHeightMm { get; private set; } = 2700;
        internal bool IsElevationEnabled(int side) => elevationSides[side].IsChecked == true;
        internal ViewFamilyType CalloutType => (ViewFamilyType)viewTypes.SelectedItem;
        internal DimensionType DimType => (DimensionType)dimTypes.SelectedItem;
        internal string ViewName => name.Text.Trim();
        internal int ScaleValue { get; private set; }
        internal double GapMm { get; private set; }
        // Side order: left, right, top, bottom.
        internal bool IsSideEnabled(int layer, int side) => sides[layer, side].IsChecked == true;

        internal CalloutDimWindow(List<ViewFamilyType> types, List<DimensionType> dims, View parent, bool create)
        {
            Title = "KTool | Triển khai nhà WC";
            Width = 1030; Height = 720; MinWidth = 980; MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(240, 243, 248));
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            heading.Children.Add(new TextBlock { Text = "TRIỂN KHAI NHÀ WC", FontSize = 24, FontWeight = FontWeights.Bold });
            heading.Children.Add(new TextBlock { Text = "Lát sàn • Thiết bị • Trần • Elevation A/B/C/D • Template riêng.", Foreground = Brushes.SlateGray });
            root.Children.Add(heading);
            var body = new Grid(); Grid.SetRow(body, 1); root.Children.Add(body);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            var panel = new StackPanel { Margin = new Thickness(0, 0, 24, 0) }; body.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            name.Text = create ? "WC - " + parent.Name : parent.Name; name.IsEnabled = create;
            viewTypes.ItemsSource = types; viewTypes.SelectedItem = types.FirstOrDefault(t => t.Id == parent.GetTypeId()) ?? types.FirstOrDefault();
            viewTypes.IsEnabled = create;
            dimTypes.ItemsSource = dims; dimTypes.SelectedItem = dims.FirstOrDefault(d => d.Id == parent.Document.GetDefaultElementTypeId(ElementTypeGroup.LinearDimensionType)) ?? dims.First();
            scale.Text = (create ? 20 : parent.Scale).ToString(CultureInfo.InvariantCulture);
            AddField(panel, "Tên mặt bằng WC", name); AddField(panel, "Loại view chi tiết", viewTypes);
            AddField(panel, "Tỷ lệ 1 /", scale); AddField(panel, "Kiểu Dim", dimTypes);
            AddField(panel, "Khoảng lớp (mm trên giấy)", gap);
            var outputs = new StackPanel { Margin = new Thickness(0, 8, 0, 8), IsEnabled = create };
            outputs.Children.Add(new TextBlock { Text = "BỘ VIEW THEO MẪU", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            outputs.Children.Add(new TextBlock { Text = "✓ Mặt bằng định vị thiết bị (luôn tạo)" });
            outputs.Children.Add(floorPlan); outputs.Children.Add(ceilingPlan); outputs.Children.Add(elevations);
            outputs.Children.Add(sheet);
            var elevationRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 8) };
            string[] directionLabels = { "A · Trái", "B · Trên", "C · Phải", "D · Dưới" };
            for (int i = 0; i < 4; i++)
            {
                elevationSides[i] = new CheckBox { Content = directionLabels[i], IsChecked = i < 3, Margin = new Thickness(0, 0, 12, 0) };
                elevationRow.Children.Add(elevationSides[i]);
            }
            outputs.Children.Add(elevationRow);
            outputs.Children.Add(new TextBlock { Text = "Hướng nhìn theo mặt bằng nguồn. Chiều cao crop (mm):" });
            height.MinHeight = 28; outputs.Children.Add(height); panel.Children.Add(outputs);
            var templatePanel = new StackPanel { IsEnabled = create };
            templatePanel.Children.Add(new TextBlock { Text = "VIEW TEMPLATE RIÊNG CHO TỪNG VIEW", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 8) });
            var allTemplates = new FilteredElementCollector(parent.Document).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate).OrderBy(v => v.Name).ToList();
            foreach (string key in new[] { "Thiết bị", "Lát sàn", "Trần", "Mặt đứng" })
            {
                var combo = new ComboBox { DisplayMemberPath = "Name" };
                combo.Items.Add(new TemplateChoice { Name = "Không gán template" });
                var kind = key == "Trần" ? ViewType.CeilingPlan : key == "Mặt đứng" ? ViewType.Elevation : ViewType.FloorPlan;
                foreach (var template in allTemplates.Where(t => t.ViewType == kind)) combo.Items.Add(new TemplateChoice { Name = template.Name, Id = template.Id });
                combo.SelectedIndex = 0; templates.Add(key, combo);
                AddField(templatePanel, key == "Mặt đứng" ? "Mặt đứng A / B / C / D" : "Mặt bằng " + key, combo);
            }
            templatePanel.Children.Add(new TextBlock { Text = "Tỷ lệ do template quản lý sẽ được ưu tiên. Template cần bật Dimensions và không cắt mất Dim ngoài Crop.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray });
            panel.Children.Add(templatePanel);
            var finishing = new StackPanel { Margin = new Thickness(0, 12, 0, 12) };
            finishing.Children.Add(new TextBlock { Text = "CROP / TITLE / FIT CROP", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
            finishing.Children.Add(adjustBottom);
            AddField(finishing, "Cạnh dưới so cốt tầng (mm)", bottomOffset);
            finishing.Children.Add(new TextBlock { Text = "Âm: hạ xuống dưới cốt tầng; dương: nâng lên. Cạnh trên giữ nguyên.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray });
            centerTitles.IsEnabled = create; finishing.Children.Add(centerTitles); finishing.Children.Add(fitCrop);
            var storedFit = FitCropSettings.Load();
            double[] offsets = { storedFit.HorizontalTailMm, storedFit.HorizontalBubbleMm, storedFit.VerticalTailMm, storedFit.VerticalBubbleMm };
            string[] offsetNames = { "Ngang · Tail (mm model)", "Ngang · Bubble (mm model)", "Đứng · Tail (mm model)", "Đứng · Bubble (mm model)" };
            for (int i = 0; i < 4; i++)
            { fitOffsets[i] = new TextBox { Text = offsets[i].ToString(CultureInfo.InvariantCulture) }; AddField(finishing, offsetNames[i], fitOffsets[i]); }
            panel.Children.Add(finishing);
            panel.Children.Add(new TextBlock { Text = "BỐ CỤC TỪNG LỚP", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 16, 0, 8) });
            var table = new Grid();
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            for (int s = 0; s < 4; s++) table.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 4; r++) table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            string[] labels = { "Trái", "Phải", "Trên", "Dưới" };
            for (int s = 0; s < 4; s++) Put(table, new TextBlock { Text = labels[s], HorizontalAlignment = HorizontalAlignment.Center }, 0, s + 1);
            string[] layers = { "1 · Tâm thiết bị", "2 · Thông thủy / cửa", "3 · Tổng phủ bì" };
            for (int l = 0; l < 3; l++)
            {
                Put(table, new TextBlock { Text = layers[l], Foreground = colors[l], FontWeight = FontWeights.SemiBold }, l + 1, 0);
                for (int s = 0; s < 4; s++)
                {
                    var check = new CheckBox { IsChecked = s == 0 || s == 3, HorizontalAlignment = HorizontalAlignment.Center };
                    sides[l, s] = check; Put(table, check, l + 1, s + 1);
                    check.Checked += (o, e) => DrawPreview(); check.Unchecked += (o, e) => DrawPreview();
                }
            }
            panel.Children.Add(table);
            panel.Children.Add(new TextBlock { Text = "Có thể chọn nhiều cạnh cho mỗi lớp. Bỏ chọn cả 4 cạnh để tắt lớp đó.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 10, 0, 0) });
            var rightPanel = new StackPanel(); Grid.SetColumn(rightPanel, 1); body.Children.Add(rightPanel);
            rightPanel.Children.Add(new TextBlock { Text = "XEM TRƯỚC BỐ CỤC", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) });
            rightPanel.Children.Add(new Border { BorderBrush = Brushes.LightSlateGray, BorderThickness = new Thickness(1), Child = preview });
            rightPanel.Children.Add(new TextBlock { Text = "Sơ đồ minh họa bố cục Dim.\nChọn điểm trong Room WC đã đặt trong model.\nMốc Dim: mặt hoàn thiện biên Room và reference tâm thiết bị.\nRon gạch và thiết bị lấy từ model, không dựng từ ảnh.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 12, 0, 0) });
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            Grid.SetRow(footer, 2); root.Children.Add(footer);
            var cancel = new Button { Content = "Hủy", Width = 90, Height = 36, IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
            var ok = new Button { Content = create ? "Chọn Room WC" : "Tạo lại Dim", Width = 170, Height = 36, IsDefault = true };
            footer.Children.Add(cancel); footer.Children.Add(ok);
            ok.Click += (o, e) =>
            {
                int value; double distance;
                if ((create && string.IsNullOrWhiteSpace(ViewName)) || !int.TryParse(scale.Text, out value) || value < 1 || value > 24000)
                { MessageBox.Show(this, "Nhập tên mặt bằng WC và tỷ lệ nguyên từ 1 đến 24000.", Title); return; }
                if (!double.TryParse(gap.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out distance) || double.IsNaN(distance) || distance < 3 || distance > 50)
                { MessageBox.Show(this, "Khoảng lớp phải từ 3 đến 50 mm giấy.", Title); return; }
                if (!sides.Cast<CheckBox>().Any(c => c.IsChecked == true))
                { MessageBox.Show(this, "Chọn ít nhất một vị trí đặt Dim.", Title); return; }
                double heightValue;
                if (!double.TryParse(height.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out heightValue)
                    || double.IsNaN(heightValue) || heightValue < 1000 || heightValue > 10000)
                { MessageBox.Show(this, "Chiều cao crop mặt đứng phải từ 1000 đến 10000 mm.", Title); return; }
                if (create && CreateElevations && !elevationSides.Any(c => c.IsChecked == true))
                { MessageBox.Show(this, "Chọn ít nhất một hướng mặt đứng.", Title); return; }
                ElevationHeightMm = heightValue;
                double bottom;
                if (AdjustBottom && (!FitCropSettings.TryParseLength(bottomOffset.Text, out bottom) || double.IsNaN(bottom) || double.IsInfinity(bottom) || bottom < -10000 || bottom >= heightValue))
                { MessageBox.Show(this, "Cạnh dưới phải từ -10000 mm đến nhỏ hơn cao độ cạnh trên.", Title); return; }
                BottomOffsetMm = AdjustBottom ? ParseLength(bottomOffset.Text) : 0;
                var values = new double[4];
                for (int i = 0; i < 4; i++)
                {
                    if (FitCrop && (!FitCropSettings.TryParseLength(fitOffsets[i].Text, out values[i]) || double.IsNaN(values[i]) || double.IsInfinity(values[i]) || Math.Abs(values[i]) > 100000))
                    { MessageBox.Show(this, "Offset Fit Crop phải là độ dài hợp lệ trong khoảng ±100000 mm.", Title); return; }
                }
                DatumFitSettings = new FitCropSettings { HorizontalTailMm = values[0], HorizontalBubbleMm = values[1], VerticalTailMm = values[2], VerticalBubbleMm = values[3] };
                ScaleValue = value; GapMm = distance;
                SavePreferences(parent.Document, create);
                DialogResult = true;
            };
            RestorePreferences(parent.Document, create);
            DrawPreview();
        }

        private Dictionary<string, TextBox> TextSettings() => new Dictionary<string, TextBox>
        { { "Scale", scale }, { "Gap", gap }, { "Height", height }, { "Bottom", bottomOffset },
          { "Fit0", fitOffsets[0] }, { "Fit1", fitOffsets[1] }, { "Fit2", fitOffsets[2] }, { "Fit3", fitOffsets[3] } };
        private Dictionary<string, CheckBox> CheckSettings(bool create)
        {
            var result = new Dictionary<string, CheckBox> { { "AdjustBottom", adjustBottom }, { "FitCrop", fitCrop } };
            if (create)
            {
                result.Add("FloorPlan", floorPlan); result.Add("CeilingPlan", ceilingPlan); result.Add("Elevations", elevations);
                result.Add("Sheet", sheet); result.Add("CenterTitles", centerTitles);
                for (int i = 0; i < 4; i++) result.Add("Elevation" + i, elevationSides[i]);
            }
            for (int l = 0; l < 3; l++) for (int s = 0; s < 4; s++) result.Add("Side" + l + s, sides[l, s]);
            return result;
        }
        private void RestorePreferences(Document doc, bool create)
        {
            var settings = WcUserSettings.Load();
            foreach (var item in TextSettings()) if (create || item.Key != "Scale") item.Value.Text = settings.Get(item.Key, item.Value.Text);
            foreach (var item in CheckSettings(create))
            { bool value; if (bool.TryParse(settings.Get(item.Key, ""), out value)) item.Value.IsChecked = value; }
            if (create) name.Text = settings.Get("ViewName", name.Text);
            RestoreElement(settings, "DimType", dimTypes, doc);
            if (create)
            {
                RestoreElement(settings, "CalloutType", viewTypes, doc);
                foreach (var item in templates)
                {
                    string id = settings.Get("TemplateId." + item.Key, ""), label = settings.Get("TemplateName." + item.Key, "");
                    var choices = item.Value.Items.Cast<TemplateChoice>().ToList();
                    var matching = choices.FirstOrDefault(c => c.Id != ElementId.InvalidElementId && doc.GetElement(c.Id)?.UniqueId == id)
                        ?? choices.FirstOrDefault(c => c.Name == label);
                    if (matching != null) item.Value.SelectedItem = matching;
                }
            }
        }
        private static void RestoreElement(WcUserSettings settings, string key, ComboBox combo, Document doc)
        {
            string id = settings.Get(key + ".Id", ""), label = settings.Get(key + ".Name", "");
            var items = combo.Items.Cast<Element>().ToList();
            var matching = items.FirstOrDefault(e => e.UniqueId == id) ?? items.FirstOrDefault(e => e.Name == label);
            if (matching != null) combo.SelectedItem = matching;
        }
        private void SavePreferences(Document doc, bool create)
        {
            var settings = WcUserSettings.Load();
            foreach (var item in TextSettings()) if (create || item.Key != "Scale") settings.Set(item.Key, item.Value.Text);
            foreach (var item in CheckSettings(create)) settings.Set(item.Key, (item.Value.IsChecked == true).ToString());
            foreach (var pair in new[] { new { Key = "DimType", Combo = dimTypes }, new { Key = "CalloutType", Combo = viewTypes } })
            {
                if (pair.Key == "CalloutType" && !create) continue;
                if (pair.Combo.SelectedItem is Element element) { settings.Set(pair.Key + ".Id", element.UniqueId); settings.Set(pair.Key + ".Name", element.Name); }
            }
            if (create)
            {
                settings.Set("ViewName", name.Text);
                foreach (var item in templates)
                {
                    var choice = (TemplateChoice)item.Value.SelectedItem;
                    settings.Set("TemplateId." + item.Key, doc.GetElement(choice.Id)?.UniqueId ?? ""); settings.Set("TemplateName." + item.Key, choice.Name);
                }
            }
            try { settings.Save(); }
            catch (Exception ex) { MessageBox.Show(this, "Có thể tiếp tục tạo view, nhưng chưa lưu được thiết lập: " + ex.Message, Title); }
        }

        private static double ParseLength(string text) { double value; FitCropSettings.TryParseLength(text, out value); return value; }
        private static void AddField(StackPanel panel, string label, Control control)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            control.MinHeight = 30; Grid.SetColumn(control, 1); row.Children.Add(control); panel.Children.Add(row);
        }
        private static void Put(Grid grid, UIElement item, int row, int col)
        { Grid.SetRow(item, row); Grid.SetColumn(item, col); grid.Children.Add(item); }
        private void Line(double x1, double y1, double x2, double y2, Brush color, double width = 1)
        { preview.Children.Add(new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = color, StrokeThickness = width }); }
        private void Label(string text, double x, double y, Brush color)
        { var label = new TextBlock { Text = text, Foreground = color, FontSize = 11 }; Canvas.SetLeft(label, x); Canvas.SetTop(label, y); preview.Children.Add(label); }
        private void DrawPreview()
        {
            preview.Children.Clear();
            Line(125, 105, 335, 105, Brushes.SlateGray, 3); Line(335, 105, 335, 275, Brushes.SlateGray, 3);
            Line(335, 275, 125, 275, Brushes.SlateGray, 3); Line(125, 275, 125, 105, Brushes.SlateGray, 3);
            Label("MẶT BẰNG WC", 182, 175, Brushes.SlateGray);
            for (int l = 0; l < 3; l++) for (int s = 0; s < 4; s++)
            {
                if (sides[l, s] == null || !IsSideEnabled(l, s)) continue;
                double offset = 22 * (l + 1); Brush color = colors[l];
                bool horizontal = s >= 2; double baseline = s == 0 ? 125 - offset : s == 1 ? 335 + offset : s == 2 ? 105 - offset : 275 + offset;
                double start = horizontal ? 125 : 105, end = horizontal ? 335 : 275;
                if (horizontal) Line(start, baseline, end, baseline, color); else Line(baseline, start, baseline, end, color);
                double[] ticks = l == 2 ? new[] { start, end } : new[] { start, start + (end - start) * .4, end };
                foreach (double tick in ticks)
                {
                    if (horizontal) { Line(tick, baseline - 6, tick, baseline + 6, color); Line(tick - 3, baseline + 3, tick + 3, baseline - 3, color, 2); }
                    else { Line(baseline - 6, tick, baseline + 6, tick, color); Line(baseline - 3, tick + 3, baseline + 3, tick - 3, color, 2); }
                }
                Label("L" + (l + 1), horizontal ? 224 : baseline - 18, horizontal ? baseline - 17 : 182, color);
            }
        }
    }
}






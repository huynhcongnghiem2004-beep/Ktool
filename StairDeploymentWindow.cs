using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Grid = System.Windows.Controls.Grid;

namespace SDK_HD.Dim
{
    internal sealed class StairDeploymentWindow : Window
    {
        private readonly TextBox name = new TextBox { Text = "CT-01" };
        private readonly TextBox scale = new TextBox { Text = "20" }, margin = new TextBox { Text = "300" }, gap = new TextBox { Text = "8" };
        private readonly ComboBox planTemplate = new ComboBox(), sectionTemplate = new ComboBox(), dimType = new ComboBox { DisplayMemberPath = "Name" };
        private readonly List<CheckBox> levels = new List<CheckBox>();
        private readonly CheckBox plans = new CheckBox { Content = "Mặt bằng các tầng đã chọn", IsChecked = true };
        private readonly CheckBox longitudinal = new CheckBox { Content = "Mặt cắt dọc vế thang", IsChecked = true };
        private readonly CheckBox transverse = new CheckBox { Content = "Mặt cắt ngang vế thang", IsChecked = true };
        private readonly CheckBox sheet = new CheckBox { Content = "Tạo sheet và bố trí các view", IsChecked = true };
        private readonly CheckBox dimensions = new CheckBox { Content = "Bật Dim triển khai: 3 lớp mặt cắt và Dim bậc", IsChecked = true };
        private readonly CheckBox fit = new CheckBox { Content = "Fit Crop Grid / Level (theo cài đặt hiện có)", IsChecked = true };
        private readonly CheckBox titles = new CheckBox { Content = "Căn giữa Title với viewport", IsChecked = true };
        private readonly CheckBox onlyRisers = new CheckBox { Content = "Chuỗi Dim bậc: chỉ lấy cổ bậc, tách riêng từng vế", IsChecked = true };
        internal bool OnlyRisers => onlyRisers.IsChecked == true;
        private readonly CheckBox planDimensions = new CheckBox { Content = "Bật 3 lớp Dim mặt bằng: chi tiết / ô thang / tổng", IsChecked = true };
        private readonly CheckBox stepNumbers = new CheckBox { Content = "Đánh số bậc bằng NumberSystem Revit", IsChecked = true };
        private readonly CheckBox stairPath = new CheckBox { Content = "Mũi tên và chữ LÊN / XUỐNG bằng Stairs Path", IsChecked = true };
        private readonly ComboBox pathType = new ComboBox { DisplayMemberPath = "Name" };
        internal bool PlanDimensions => dimensions.IsChecked == true && planDimensions.IsChecked == true;
        internal bool StepNumbers => stepNumbers.IsChecked == true;
        internal bool StairPath => stairPath.IsChecked == true;
        internal ElementId PathType => (pathType.SelectedItem as StairsPathType)?.Id ?? ElementId.InvalidElementId;
        private readonly CheckBox overrideCrop = new CheckBox { Content = "Override màu / nét / độ dày đường Crop", Margin = new Thickness(0, 14, 0, 0) };
        internal CropRegionStylePanel CropStyle { get; private set; }
        internal bool OverrideCrop => overrideCrop.IsChecked == true;
        private sealed class Choice { public string Name { get; set; } internal ElementId Id; }
        internal string Prefix => name.Text.Trim();
        internal bool Plans => plans.IsChecked == true;
        internal bool Longitudinal => longitudinal.IsChecked == true;
        internal bool Transverse => transverse.IsChecked == true;
        internal bool Sheet => sheet.IsChecked == true;
        internal bool Dimensions => dimensions.IsChecked == true;
        internal bool Fit => fit.IsChecked == true;
        internal bool Titles => titles.IsChecked == true;
        internal int Scale { get; private set; }
        internal double CropMargin { get; private set; }
        internal double Gap { get; private set; }
        internal List<ElementId> Levels => levels.Where(c => c.IsChecked == true).Select(c => (ElementId)c.Tag).ToList();
        internal ElementId PlanTemplate => ((Choice)planTemplate.SelectedItem).Id;
        internal ElementId SectionTemplate => ((Choice)sectionTemplate.SelectedItem).Id;
        internal DimensionType DimType => dimType.SelectedItem as DimensionType;
        internal StairDeploymentWindow(Document doc, List<Level> candidates, IList<DimensionType> dimensionsList)
        {
            Title = "KTool | Triển khai cầu thang bộ | " + StairDeploymentCommand.BuildTag; Width = 860; Height = 760; MinWidth = 790; MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 246, 252));
            var root = new Grid { Margin = new Thickness(22) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock { Text = "TRIỂN KHAI CẦU THANG BỘ", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Brushes.RoyalBlue, Margin = new Thickness(0, 0, 0, 18) });
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(body, 1); root.Children.Add(body);
            var left = new StackPanel { Margin = new Thickness(0, 0, 20, 0) }; body.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            AddField(left, "Mã cầu thang / tiền tố view", name); AddField(left, "Tỷ lệ 1 /", scale); AddField(left, "Biên crop (mm model)", margin); AddField(left, "Khoảng lớp Dim (mm giấy)", gap);
            Templates(doc, planTemplate, ViewType.FloorPlan); Templates(doc, sectionTemplate, ViewType.Section);
            AddField(left, "Template mặt bằng", planTemplate); AddField(left, "Template cắt dọc / ngang", sectionTemplate);
            dimType.ItemsSource = dimensionsList; dimType.SelectedItem = dimensionsList.FirstOrDefault(d => d.Id == doc.GetDefaultElementTypeId(ElementTypeGroup.LinearDimensionType)) ?? dimensionsList.FirstOrDefault();
            AddField(left, "Kiểu Dim", dimType);
            left.Children.Add(overrideCrop);
            CropStyle = new CropRegionStylePanel(doc) { IsEnabled = false }; left.Children.Add(CropStyle);
            overrideCrop.Checked += (s, e) => CropStyle.IsEnabled = true;
            overrideCrop.Unchecked += (s, e) => CropStyle.IsEnabled = false;
            var right = new StackPanel(); var scroller = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroller, 1); body.Children.Add(scroller);
            right.Children.Add(new TextBlock { Text = "BỘ VIEW", FontWeight = FontWeights.Bold });
            foreach (var c in new[] { plans, longitudinal, transverse, sheet, dimensions, onlyRisers, fit, titles }) { c.Margin = new Thickness(0, 9, 0, 0); right.Children.Add(c); }
            dimensions.Checked += (s, e) => onlyRisers.IsEnabled = true;
            dimensions.Unchecked += (s, e) => onlyRisers.IsEnabled = false;
            foreach (var c in new[] { planDimensions, stepNumbers, stairPath }) { c.Margin = new Thickness(0, 9, 0, 0); right.Children.Add(c); }
            pathType.ItemsSource = new FilteredElementCollector(doc).OfClass(typeof(StairsPathType)).Cast<StairsPathType>().OrderBy(t => t.Name).ToList(); pathType.SelectedIndex = pathType.Items.Count > 0 ? 0 : -1;
            AddField(right, "Loại Stairs Path", pathType);
            right.Children.Add(new TextBlock { Text = "TẦNG TRIỂN KHAI", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 20, 0, 8) });
            foreach (var level in candidates)
            { var check = new CheckBox { Content = level.Name, Tag = level.Id, IsChecked = true, Margin = new Thickness(0, 6, 0, 6) }; levels.Add(check); right.Children.Add(check); }
            right.Children.Add(new TextBlock { Text = "Chọn Stairs trong model hiện tại. Hướng dọc lấy theo đường chạy của vế dài nhất. Dim chỉ dùng mặt phẳng thật; mặt cong/xiên hoặc thiếu reference sẽ được báo. Sheet chưa có khung tên.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 18, 0, 0) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; Grid.SetRow(buttons, 2); root.Children.Add(buttons);
            buttons.Children.Add(new Button { Content = "Hủy", IsCancel = true, Width = 90, Height = 38, Margin = new Thickness(0, 0, 12, 0) });
            var ok = new Button { Content = "Tạo bộ triển khai", IsDefault = true, Width = 170, Height = 38, Background = Brushes.RoyalBlue, Foreground = Brushes.White }; buttons.Children.Add(ok);
            ok.Click += (s, e) =>
            {
                int scaleValue; double marginValue, gapValue;
                if (Prefix.Length == 0 || !int.TryParse(scale.Text, out scaleValue) || scaleValue < 1 || scaleValue > 24000
                    || !Length(margin.Text, out marginValue) || marginValue < 0 || marginValue > 10000 || !Length(gap.Text, out gapValue) || gapValue < 3 || gapValue > 50)
                { MessageBox.Show(this, "Nhập mã thang, tỷ lệ 1–24000, biên crop 0–10000 mm và khoảng Dim 3–50 mm giấy.", Title); return; }
                if ((!Plans && !Longitudinal && !Transverse) || (Plans && Levels.Count == 0) || (Dimensions && DimType == null))
                { MessageBox.Show(this, "Chọn ít nhất một view, tầng mặt bằng và kiểu Dim phù hợp.", Title); return; }
                Scale = scaleValue; CropMargin = marginValue / 304.8; Gap = gapValue;
                DialogResult = true;
            };
        }
        private static bool Length(string value, out double number) => double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number);
        private static void AddField(StackPanel panel, string label, System.Windows.Controls.Control control)
        { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) }); control.MinHeight = 32; panel.Children.Add(control); }
        private static void Templates(Document doc, ComboBox combo, ViewType kind)
        {
            combo.DisplayMemberPath = "Name"; combo.Items.Add(new Choice { Name = "Không gán Template", Id = ElementId.InvalidElementId });
            foreach (View template in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate && v.ViewType == kind).OrderBy(v => v.Name)) combo.Items.Add(new Choice { Name = template.Name, Id = template.Id });
            combo.SelectedIndex = 0;
        }
    }
}


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SDK_HD.Dim.Finish
{
    public enum WallRoomMode { Pick, Visible, Parameter }
    public sealed class WallRoomData
    {
        public long Id;
        public bool Visible;
        public readonly Dictionary<string, string> Parameters = new Dictionary<string, string>();
    }
    public sealed class WallTypeRow
    {
        public FinishChoice Choice { get; set; }
        public string Name => Choice.Name;
        public ImageSource Preview { get; set; }
        public string Description { get; set; }
    }
    public sealed class FinishWallWindow : Window
    {
        private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(25, 109, 210));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(102, 119, 141));
        private readonly ListBox types = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.White, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox search = new TextBox();
        private readonly TextBox offset = new TextBox { Text = "0" }, height = new TextBox { Text = "2700" };
        private readonly RadioButton pick = new RadioButton { Content = "Pick chọn Room cần tạo", IsChecked = true, GroupName = "RoomMode" };
        private readonly RadioButton visible = new RadioButton { Content = "Toàn bộ Room hiển thị trên View", GroupName = "RoomMode" };
        private readonly RadioButton parameter = new RadioButton { Content = "Tạo theo Parameter Room", GroupName = "RoomMode" };
        private readonly CheckBox replace = new CheckBox { Content = "Thay thế tường hoàn thiện KTool đã tạo" };
        private readonly StackPanel rulesPanel = new StackPanel();
        private readonly TextBlock count = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap };
        private readonly List<WallTypeRow> allTypes;
        private readonly List<WallRoomData> rooms;
        private readonly List<FilterRow> rules = new List<FilterRow>();
        private readonly Button add;
        public WallRoomMode Mode => parameter.IsChecked == true ? WallRoomMode.Parameter : visible.IsChecked == true ? WallRoomMode.Visible : WallRoomMode.Pick;
        public FinishSettings Settings { get; private set; }
        private sealed class FilterRow
        {
            internal ComboBox Parameter, Value;
            internal Grid Container;
        }

        public FinishWallWindow(List<WallTypeRow> wallTypes, List<WallRoomData> roomData)
        {
            allTypes = wallTypes; rooms = roomData;
            Title = "KTool | Tạo tường hoàn thiện cho Room";
            Width = 1030; Height = 790; MinWidth = 860; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            Background = new SolidColorBrush(Color.FromRgb(241, 246, 252));
            Foreground = new SolidColorBrush(Color.FromRgb(27, 44, 66));
            var root = new Grid { Margin = new Thickness(20) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            header.Children.Add(new TextBlock { Text = "TƯỜNG HOÀN THIỆN", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = Blue });
            header.Children.Add(new TextBlock { Text = "Chọn loại tường và phạm vi Room • Giữ lỗ cửa theo tường chủ", Foreground = Muted, Margin = new Thickness(0, 5, 0, 0) });
            root.Children.Add(header);
            var body = new Grid(); Grid.SetRow(body, 1); root.Children.Add(body);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new Grid(); left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); left.RowDefinitions.Add(new RowDefinition()); left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.Children.Add(Card(left));
            search.Margin = new Thickness(12); search.Height = 34; search.Padding = new Thickness(8, 4, 8, 4); search.ToolTip = "Tìm theo tên Wall Type";
            search.TextChanged += (s, e) => RefreshTypes(); left.Children.Add(search);
            var columns = new Grid { Background = new SolidColorBrush(Color.FromRgb(229, 239, 251)), Margin = new Thickness(0, 0, 0, 1) };
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) }); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            Put(columns, Text("Mẫu lớp", true), 0); Put(columns, Text("Type Name", true), 1); Put(columns, Text("Chọn", true), 2);
            Grid.SetRow(columns, 1); left.Children.Add(columns);
            types.ItemTemplate = TypeTemplate();
            var containerStyle = new Style(typeof(ListBoxItem));
            containerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            containerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            containerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            types.ItemContainerStyle = containerStyle;
            Grid.SetRow(types, 2); left.Children.Add(types);
            var typeHint = new TextBlock { Text = "Chọn 1 Wall Type • Hình minh họa các lớp vật liệu", Foreground = Muted, Margin = new Thickness(12), FontSize = 11 };
            Grid.SetRow(typeHint, 3); left.Children.Add(typeHint);

            var right = new Grid(); Grid.SetColumn(right, 2); body.Children.Add(right);
            right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var options = new StackPanel();
            var optionsScroll = new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; right.Children.Add(optionsScroll);
            var modes = new StackPanel(); modes.Children.Add(new Border { Background = Blue, Padding = new Thickness(14, 10, 14, 10), Child = new TextBlock { Text = "LỰA CHỌN CÁCH TẠO", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold } });
            foreach (var mode in new[] { pick, visible, parameter })
            { mode.Margin = new Thickness(16, 12, 12, 12); modes.Children.Add(mode); mode.Checked += (s, e) => RefreshMode(); }
            options.Children.Add(Card(modes));
            var dimensions = new Grid { Margin = new Thickness(0, 14, 0, 14) }; dimensions.ColumnDefinitions.Add(new ColumnDefinition()); dimensions.ColumnDefinitions.Add(new ColumnDefinition());
            Put(dimensions, Field("Base offset (mm)", offset), 0); Put(dimensions, Field("Wall Height (mm)", height), 1); options.Children.Add(dimensions);
            options.Children.Add(new TextBlock { Text = "Height = 0: lên đỉnh Room. Offset tính từ đáy Room.", Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 0, 0, 12) });
            var filters = new StackPanel();
            var filterHeader = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(229, 239, 251)), LastChildFill = true };
            var clear = Button("Xóa lọc", () => { rules.Clear(); rulesPanel.Children.Clear(); RefreshCount(); });
            DockPanel.SetDock(clear, Dock.Right); filterHeader.Children.Add(clear);
            filterHeader.Children.Add(new TextBlock { Text = "PARAMETER ROOM  =  GIÁ TRỊ", FontWeight = FontWeights.SemiBold, Margin = new Thickness(12), VerticalAlignment = VerticalAlignment.Center });
            filters.Children.Add(filterHeader); filters.Children.Add(rulesPanel);
            filters.Children.Add(new TextBlock { Text = "Các điều kiện kết hợp AND • So khớp giá trị hiển thị", Foreground = Muted, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap, FontSize = 11 });
            options.Children.Add(Card(filters));
            replace.Margin = new Thickness(0, 14, 0, 12); options.Children.Add(replace);
            options.Children.Add(new TextBlock { Text = "Chỉ thay kết quả của công cụ tường hoàn thiện. Room đã có kết quả sẽ được bỏ qua nếu không bật Thay thế.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
            count.Margin = new Thickness(0, 14, 0, 10); options.Children.Add(count);
            var bottom = new Grid { Margin = new Thickness(0, 16, 0, 0) }; Grid.SetRow(bottom, 1); right.Children.Add(bottom);
            bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition());
            add = Button("+ Thêm Parameter", AddRule); add.Height = 42; Put(bottom, add, 0);
            var create = Button("Bắt đầu tạo", Accept); create.Background = Blue; create.Foreground = Brushes.White; create.FontWeight = FontWeights.SemiBold; create.Height = 42; Put(bottom, create, 1);
            RefreshTypes(); AddRule(); RefreshMode();
        }
        private static Border Card(UIElement content) => new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(214, 226, 241)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = content };
        private static TextBlock Text(string value, bool bold = false) => new TextBlock { Text = value, Margin = new Thickness(10), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Muted };
        private static void Put(Grid grid, UIElement item, int column) { Grid.SetColumn(item, column); grid.Children.Add(item); }
        private static StackPanel Field(string label, TextBox input)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; panel.Children.Add(new TextBlock { Text = label, Foreground = Muted, Margin = new Thickness(0, 0, 0, 6) });
            input.Height = 34; input.Padding = new Thickness(8, 4, 8, 4); panel.Children.Add(input); return panel;
        }
        private static Button Button(string label, Action action)
        {
            var button = new Button { Content = label, Background = new SolidColorBrush(Color.FromRgb(231, 241, 255)), Foreground = Blue,
                BorderBrush = new SolidColorBrush(Color.FromRgb(196, 217, 245)), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(4), Cursor = System.Windows.Input.Cursors.Hand };
            button.Click += (s, e) => action(); return button;
        }
        private static DataTemplate TypeTemplate()
        {
            var template = new DataTemplate(typeof(WallTypeRow));
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(228, 235, 244))); border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            var dock = new FrameworkElementFactory(typeof(DockPanel)); dock.SetValue(FrameworkElement.MinHeightProperty, 76.0); dock.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 10, 0));
            var image = new FrameworkElementFactory(typeof(Image)); image.SetValue(FrameworkElement.WidthProperty, 65.0); image.SetValue(FrameworkElement.HeightProperty, 38.0); image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 14, 0)); image.SetBinding(Image.SourceProperty, new Binding("Preview")); dock.AppendChild(image);
            var check = new FrameworkElementFactory(typeof(CheckBox)); check.SetValue(DockPanel.DockProperty, Dock.Right); check.SetValue(UIElement.IsHitTestVisibleProperty, false); check.SetValue(UIElement.FocusableProperty, false); check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); check.SetValue(FrameworkElement.MarginProperty, new Thickness(12));
            check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding("IsSelected") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1), Mode = BindingMode.OneWay }); dock.AppendChild(check);
            var text = new FrameworkElementFactory(typeof(StackPanel)); text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var description = new FrameworkElementFactory(typeof(TextBlock)); description.SetBinding(TextBlock.TextProperty, new Binding("Description")); description.SetValue(TextBlock.ForegroundProperty, Blue); description.SetValue(TextBlock.FontSizeProperty, 11.0); text.AppendChild(description);
            var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new Binding("Name")); name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); name.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0)); text.AppendChild(name); dock.AppendChild(text);
            border.AppendChild(dock); template.VisualTree = border; return template;
        }
        private void RefreshTypes()
        {
            var selected = types.SelectedItem;
            types.ItemsSource = allTypes.Where(t => t.Name.IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            types.SelectedItem = selected;
            if (types.SelectedIndex < 0 && types.Items.Count > 0) types.SelectedIndex = 0;
        }
        private void AddRule()
        {
            var row = new FilterRow { Container = new Grid { Margin = new Thickness(10, 8, 10, 0) }, Parameter = new ComboBox { MinHeight = 32, Margin = new Thickness(0, 0, 8, 0) }, Value = new ComboBox { IsEditable = true, MinHeight = 32 } };
            row.Container.ColumnDefinitions.Add(new ColumnDefinition()); row.Container.ColumnDefinitions.Add(new ColumnDefinition()); row.Container.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Parameter.ItemsSource = rooms.SelectMany(r => r.Parameters.Keys).Distinct().OrderBy(n => n).ToList();
            Put(row.Container, row.Parameter, 0); Put(row.Container, row.Value, 1); Put(row.Container, Button("×", () => { rules.Remove(row); rulesPanel.Children.Remove(row.Container); RefreshCount(); }), 2);
            row.Parameter.SelectionChanged += (s, e) =>
            {
                string key = row.Parameter.SelectedItem as string;
                row.Value.ItemsSource = rooms.Where(r => key != null && r.Parameters.ContainsKey(key)).Select(r => r.Parameters[key]).Distinct().OrderBy(v => v).ToList();
                row.Value.SelectedIndex = row.Value.Items.Count > 0 ? 0 : -1; RefreshCount();
            };
            row.Value.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((s, e) => RefreshCount()));
            row.Value.SelectionChanged += (s, e) => RefreshCount();
            rules.Add(row); rulesPanel.Children.Add(row.Container); if (row.Parameter.Items.Count > 0) row.Parameter.SelectedIndex = 0;
            RefreshMode();
        }
        private IEnumerable<WallRoomData> Matching()
        {
            return rooms.Where(r => Mode == WallRoomMode.Visible ? r.Visible : rules.Count > 0 && rules.All(rule =>
                rule.Parameter.SelectedItem is string key && r.Parameters.TryGetValue(key, out var value) && string.Equals(value.Trim(), rule.Value.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)));
        }
        private void RefreshMode()
        {
            rulesPanel.IsEnabled = Mode == WallRoomMode.Parameter;
            if (add != null) add.IsEnabled = Mode == WallRoomMode.Parameter;
            RefreshCount();
        }
        private void RefreshCount()
        {
            count.Text = Mode == WallRoomMode.Pick ? "Sau khi bấm Bắt đầu tạo: chọn các Room trong model rồi bấm Finish. Esc để hủy."
                : "Số Room phù hợp: " + Matching().Count() + (Mode == WallRoomMode.Parameter ? " (toàn dự án, theo bộ lọc)" : " (view hiện tại)");
        }
        private void Accept()
        {
            double baseOffset, wallHeight;
            if (!(types.SelectedItem is WallTypeRow selected)) { MessageBox.Show(this, "Chọn một loại tường Basic Wall.", Title); return; }
            if (!double.TryParse(offset.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out baseOffset) || double.IsNaN(baseOffset) || double.IsInfinity(baseOffset)
                || !double.TryParse(height.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out wallHeight) || double.IsNaN(wallHeight) || double.IsInfinity(wallHeight) || wallHeight < 0)
            { MessageBox.Show(this, "Nhập offset hợp lệ và chiều cao không âm (mm).", Title); return; }
            if (Mode == WallRoomMode.Parameter && (rules.Count == 0 || rules.Any(r => r.Parameter.SelectedItem == null)))
            { MessageBox.Show(this, "Thêm ít nhất một điều kiện Parameter Room.", Title); return; }
            var ids = Mode == WallRoomMode.Pick ? new List<long>() : Matching().Select(r => r.Id).ToList();
            if (Mode != WallRoomMode.Pick && ids.Count == 0) { MessageBox.Show(this, "Không có Room phù hợp với phạm vi đã chọn.", Title); return; }
            Settings = new FinishSettings { TypeId = selected.Choice.Id, OffsetMm = baseOffset, HeightMm = wallHeight, Replace = replace.IsChecked == true, Sources = ids };
            DialogResult = true;
        }
    }
}

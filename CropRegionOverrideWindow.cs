using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SDK_HD.Dim
{
    internal sealed class CropViewRow
    {
        internal long Id;
        internal string Name, Kind, Sheet;
        internal bool Initial, ActiveSheet, Checked, CropActive;
    }
    internal sealed class CropRegionOverrideWindow : Window
    {
        private readonly List<CropViewRow> rows;
        private readonly StackPanel list = new StackPanel();
        private readonly TextBox search = new TextBox { Height = 33, Padding = new Thickness(8, 4, 8, 4) };
        private readonly ComboBox scope = new ComboBox { MinHeight = 32 };
        private readonly TextBlock count = new TextBlock(), status = new TextBlock();
        internal List<long> SelectedIds => rows.Where(r => r.Checked).Select(r => r.Id).ToList();
        internal CropRegionOverrideWindow(List<CropViewRow> views, CropRegionStylePanel style)
        {
            rows = views;
            Title = "KTool | Override Crop View Region"; Width = 1040; Height = 780; MinWidth = 900; MinHeight = 660;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            Background = Brush(244, 247, 235); Foreground = Brush(30, 48, 70);
            var root = new Grid { Margin = new Thickness(22) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            heading.Children.Add(new TextBlock { Text = "OVERRIDE CROP VIEW REGION", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = Brush(88, 117, 43) });
            heading.Children.Add(new TextBlock { Text = "Chọn view • Thiết lập đường Crop • Áp dụng hàng loạt", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 5, 0, 0) }); root.Children.Add(heading);
            var body = new Grid(); Grid.SetRow(body, 1); root.Children.Add(body);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new DockPanel(); body.Children.Add(Card(left));
            var tools = new StackPanel { Margin = new Thickness(14) }; DockPanel.SetDock(tools, Dock.Top); left.Children.Add(tools);
            tools.Children.Add(new TextBlock { Text = "DANH SÁCH VIEW", FontWeight = FontWeights.Bold, Foreground = Brush(88, 117, 43) });
            scope.Items.Add("View hiện tại / đang chọn"); scope.Items.Add("View trên sheet đang mở"); scope.Items.Add("Tất cả view trong dự án"); scope.SelectedIndex = 0;
            scope.Margin = new Thickness(0, 10, 0, 8); tools.Children.Add(scope);
            tools.Children.Add(new TextBlock { Text = "Tìm tên view, loại view hoặc số sheet", Foreground = Brushes.SlateGray, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) }); tools.Children.Add(search);
            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            actions.Children.Add(Button("Chọn kết quả lọc", () => { foreach (var r in Filtered()) r.Checked = true; Draw(); }));
            actions.Children.Add(Button("Bỏ chọn tất cả", () => { foreach (var r in rows) r.Checked = false; Draw(); })); tools.Children.Add(actions);
            var footer = new StackPanel { Margin = new Thickness(14, 8, 14, 12) }; DockPanel.SetDock(footer, Dock.Bottom); left.Children.Add(footer);
            count.FontWeight = FontWeights.SemiBold; footer.Children.Add(count);
            footer.Children.Add(new TextBlock { Text = "Danh sách lọc không làm mất các view đã tích chọn.", FontSize = 11, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            left.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var right = new StackPanel { Margin = new Thickness(16) };
            var rightCard = Card(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Grid.SetColumn(rightCard, 2); body.Children.Add(rightCard);
            right.Children.Add(style);
            right.Children.Add(new TextBlock { Text = "XEM TRƯỚC ĐƯỜNG CROP", FontWeight = FontWeights.Bold, Foreground = Brush(88, 117, 43), Margin = new Thickness(0, 14, 0, 8) });
            var preview = new Image { Height = 170, Stretch = Stretch.Uniform, Source = style.Preview };
            right.Children.Add(new Border { Background = Brush(248, 250, 243), BorderBrush = Brush(214, 225, 197), BorderThickness = new Thickness(1), Padding = new Thickness(12), Child = preview });
            right.Children.Add(new TextBlock { Text = "Minh họa kiểu nét. Độ dày in thực tế theo Line Weights của Revit và tỷ lệ view.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) });
            style.StyleChanged += (s, e) => preview.Source = style.Preview;
            var bottom = new DockPanel { Margin = new Thickness(0, 18, 0, 0) }; Grid.SetRow(bottom, 2); root.Children.Add(bottom);
            var apply = Button("Áp dụng Override", () => { if (SelectedIds.Count == 0) { MessageBox.Show(this, "Chọn ít nhất một view.", Title); return; } DialogResult = true; });
            apply.Width = 180; apply.Height = 40; apply.Background = Brush(88, 117, 43); apply.Foreground = Brushes.White; apply.FontWeight = FontWeights.SemiBold; DockPanel.SetDock(apply, Dock.Right); bottom.Children.Add(apply);
            var cancel = Button("Đóng", () => Close()); cancel.Width = 90; cancel.Height = 40; DockPanel.SetDock(cancel, Dock.Right); bottom.Children.Add(cancel);
            status.Text = "Chỉ đổi đồ họa đường Crop • Có thể Undo cả lượt"; status.VerticalAlignment = VerticalAlignment.Center; status.Foreground = Brushes.SlateGray; status.TextWrapping = TextWrapping.Wrap; bottom.Children.Add(status);
            search.TextChanged += (s, e) => Draw(); scope.SelectionChanged += (s, e) => Draw();
            if (!rows.Any(r => r.Initial)) scope.SelectedIndex = 2;
            Draw();
        }
        private IEnumerable<CropViewRow> Filtered()
        {
            string text = search.Text.Trim();
            return rows.Where(r => (scope.SelectedIndex == 2 || (scope.SelectedIndex == 1 ? r.ActiveSheet : r.Initial))
                && (r.Name + " " + r.Kind + " " + r.Sheet).IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }
        private void Draw()
        {
            list.Children.Clear();
            foreach (var row in Filtered())
            {
                var content = new StackPanel(); content.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(new TextBlock { Text = row.Kind + "  •  " + row.Sheet + (row.CropActive ? "" : "  •  Crop View tắt"), Foreground = Brushes.SlateGray, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
                var check = new CheckBox { Content = content, IsChecked = row.Checked, Margin = new Thickness(14, 10, 14, 10), VerticalContentAlignment = VerticalAlignment.Center };
                check.Checked += (s, e) => { row.Checked = true; Count(); }; check.Unchecked += (s, e) => { row.Checked = false; Count(); };
                list.Children.Add(new Border { BorderBrush = Brush(230, 236, 220), BorderThickness = new Thickness(0, 0, 0, 1), Child = check });
            }
            Count();
        }
        private void Count() { count.Text = "Đã chọn " + rows.Count(r => r.Checked) + " view  •  Đang hiển thị " + Filtered().Count(); }
        private static SolidColorBrush Brush(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        private static Border Card(UIElement content) => new Border { Child = content, Background = Brushes.White, BorderBrush = Brush(214, 225, 197), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        private static Button Button(string text, Action click)
        { var button = new Button { Content = text, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(3), Background = Brush(232, 240, 213), Foreground = Brush(88, 117, 43), BorderBrush = Brush(190, 211, 166) }; button.Click += (s, e) => click(); return button; }
    }
}


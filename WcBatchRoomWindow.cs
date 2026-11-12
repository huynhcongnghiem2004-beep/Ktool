using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Grid = System.Windows.Controls.Grid;

namespace SDK_HD.Dim
{
    internal sealed class WcBatchRoomWindow : Window
    {
        private sealed class Row { internal Room Room; internal CheckBox Check; internal string Search; }
        private readonly List<Row> _rows = new List<Row>();
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBox _search = new TextBox { Height = 30, Margin = new Thickness(0, 8, 0, 8), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly TextBlock _count = new TextBlock { Foreground = Brush("#526174"), VerticalAlignment = VerticalAlignment.Center };
        internal List<Room> SelectedRooms => _rows.Where(r => r.Check.IsChecked == true).Select(r => r.Room).ToList();

        internal WcBatchRoomWindow(List<Room> rooms)
        {
            Title = "KTool | Triển khai hàng loạt WC"; Width = 760; Height = 680; MinWidth = 620; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#F4F6F9"); FontFamily = new FontFamily("Segoe UI");
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var heading = new StackPanel();
            heading.Children.Add(new TextBlock { Text = "TRIỂN KHAI HÀNG LOẠT WC", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brush("#17334F") });
            heading.Children.Add(new TextBlock { Text = "Chọn nhiều Room ở các tầng. KTool sẽ dùng một cấu hình và tạo bộ view riêng cho từng Room.", Foreground = Brush("#526174"), Margin = new Thickness(0, 3, 0, 0) });
            root.Children.Add(heading);
            _search.TextChanged += (s, e) => FilterRows();
            Grid.SetRow(_search, 1); root.Children.Add(_search);
            var tools = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var selectAll = new Button { Content = "Chọn tất cả đang hiện", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(9, 4, 9, 4), Background = Brush("#8B6248"), Foreground = Brushes.White, BorderBrush = Brush("#8B6248") };
            var clear = new Button { Content = "Bỏ chọn", Padding = new Thickness(9, 4, 9, 4) };
            selectAll.Click += (s, e) => { foreach (var row in _rows.Where(r => MatchesFilter(r))) row.Check.IsChecked = true; UpdateCount(); };
            clear.Click += (s, e) => { foreach (var row in _rows) row.Check.IsChecked = false; UpdateCount(); };
            tools.Children.Add(selectAll); tools.Children.Add(clear); DockPanel.SetDock(_count, Dock.Right); tools.Children.Add(_count);
            Grid.SetRow(tools, 2); root.Children.Add(tools);
            foreach (Room room in rooms)
            {
                Level level = room.Document.GetElement(room.LevelId) as Level;
                string levelName = level == null ? "(không rõ tầng)" : level.Name;
                string text = room.Number + "  ·  " + room.Name + "   |   " + levelName + "   |   " + (room.Area * 0.092903).ToString("0.##") + " m²";
                var check = new CheckBox { Content = text, Margin = new Thickness(8, 7, 8, 7), Foreground = Brush("#24354A"), VerticalContentAlignment = VerticalAlignment.Center };
                var border = new Border { BorderBrush = Brush("#D7DEE8"), BorderThickness = new Thickness(0, 0, 0, 1), Child = check };
                _list.Children.Add(border);
                var row = new Row { Room = room, Check = check, Search = (room.Number + " " + room.Name + " " + levelName).ToLowerInvariant() };
                _rows.Add(row); check.Checked += (s, e) => UpdateCount(); check.Unchecked += (s, e) => UpdateCount();
            }
            var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.White, BorderBrush = Brush("#C8D2DE"), BorderThickness = new Thickness(1) };
            Grid.SetRow(scroll, 3); root.Children.Add(scroll);
            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var cancel = new Button { Content = "Hủy", Width = 92, Height = 36, Margin = new Thickness(5), IsCancel = true };
            var proceed = new Button { Content = "Tiếp tục cấu hình", Width = 160, Height = 36, Margin = new Thickness(5), Background = Brush("#17334F"), Foreground = Brushes.White, BorderBrush = Brush("#17334F"), FontWeight = FontWeights.SemiBold, IsDefault = true };
            proceed.Click += (s, e) => { if (SelectedRooms.Count == 0) { MessageBox.Show(this, "Chọn ít nhất một Room.", Title); return; } DialogResult = true; };
            DockPanel.SetDock(proceed, Dock.Right); DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(proceed); footer.Children.Add(cancel);
            footer.Children.Add(new TextBlock { Text = "Cấu hình sẽ mở ở bước tiếp theo.", Foreground = Brush("#526174"), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(footer, 4); root.Children.Add(footer);
            UpdateCount();
        }

        private bool MatchesFilter(Row row) => string.IsNullOrWhiteSpace(_search.Text) || row.Search.Contains(_search.Text.Trim().ToLowerInvariant());
        private void FilterRows() { foreach (var row in _rows) ((UIElement)((Border)row.Check.Parent).Parent).Visibility = MatchesFilter(row) ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; UpdateCount(); }
        private void UpdateCount() => _count.Text = _rows.Count(r => r.Check.IsChecked == true) + " / " + _rows.Count(r => MatchesFilter(r)) + " Room được chọn / đang hiện";
        private static SolidColorBrush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }
}


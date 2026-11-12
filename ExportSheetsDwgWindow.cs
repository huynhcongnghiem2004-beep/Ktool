using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SDK_HD.Dim
{
    internal sealed class DwgSheetRow
    {
        internal long Id;
        internal string Number, Name, Prefix;
        internal bool Selected = true;
        internal readonly Dictionary<string, string> Groups = new Dictionary<string, string>();
    }
    internal sealed class DwgSheetSet { public string Name { get; set; } internal HashSet<long> Ids; }
    internal sealed class ExportSheetsDwgWindow : Window
    {
        private readonly List<DwgSheetRow> rows;
        private readonly TreeView tree = new TreeView { BorderThickness = new Thickness(0) };
        private readonly ComboBox sets = new ComboBox { DisplayMemberPath = "Name", MinHeight = 30 }, setups = new ComboBox { MinHeight = 30 }, groupBy = new ComboBox { MinHeight = 30 };
        private readonly TextBox search = new TextBox { MinHeight = 30, Padding = new Thickness(8, 4, 8, 4) }, columns = new TextBox { Text = "3", Width = 60 }, gap = new TextBox { Text = "50", Width = 80 };
        private readonly TextBlock count = new TextBlock { Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap };
        private static readonly Brush Avocado = new SolidColorBrush(Color.FromRgb(88, 117, 43));
        private readonly CheckBox loose = new CheckBox { Content = "Xuất thành đối tượng rời (chỉ tách Block bao Sheet)", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
        internal bool LooseEntities => loose.IsChecked == true;
        internal string SetupName => setups.SelectedIndex == 0 ? null : setups.SelectedItem as string;
        internal int Columns { get; private set; }
        internal double GapMm { get; private set; }
        internal List<DwgSheetRow> Selected => rows.Where(r => r.Selected).OrderBy(r => r.Number, StringComparer.CurrentCultureIgnoreCase).ToList();
        internal ExportSheetsDwgWindow(List<DwgSheetRow> sheets, List<DwgSheetSet> savedSets, IList<string> exportSetups)
        {
            rows = sheets;
            Title = "KTool | Xuất tất cả Sheet vào một DWG"; Width = 760; Height = 820; MinWidth = 660; MinHeight = 620;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(244, 247, 235));
            var root = new Grid { Margin = new Thickness(20) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; root.Children.Add(top);
            top.Children.Add(new TextBlock { Text = "XUẤT SHEET → MỘT FILE AUTOCAD", FontSize = 23, Foreground = Avocado, FontWeight = FontWeights.Bold });
            top.Children.Add(new TextBlock { Text = "Tất cả sheet đã chọn được xếp trong Model • Đơn vị khổ giấy: mm", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap });
            sets.Items.Add(new DwgSheetSet { Name = "Chọn thủ công / Tất cả sheet" }); foreach (var set in savedSets) sets.Items.Add(set); sets.SelectedIndex = 0;
            AddField(top, "Bộ sheet in đã lưu", sets);
            groupBy.Items.Add("Tiền tố Sheet Number"); foreach (var key in rows.SelectMany(r => r.Groups.Keys).Distinct().OrderBy(k => k)) groupBy.Items.Add(key); groupBy.SelectedIndex = 0;
            AddField(top, "Nhóm theo", groupBy); AddField(top, "Tìm sheet", search);
            var card = new Border { Background = Brushes.White, BorderBrush = Brushes.DarkSeaGreen, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(8), Child = tree }; Grid.SetRow(card, 1); root.Children.Add(card);
            var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(bottom, 2); root.Children.Add(bottom);
            bottom.Children.Add(count);
            setups.Items.Add("Thiết lập DWG mặc định"); foreach (var setup in exportSetups) setups.Items.Add(setup); setups.SelectedIndex = 0; AddField(bottom, "Thiết lập xuất DWG", setups);
            var arrangement = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            arrangement.Children.Add(new TextBlock { Text = "Số sheet mỗi hàng", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }); arrangement.Children.Add(columns);
            arrangement.Children.Add(new TextBlock { Text = "Khe giữa sheet (mm)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 8, 0) }); arrangement.Children.Add(gap); bottom.Children.Add(arrangement); bottom.Children.Add(loose);
            bottom.Children.Add(new TextBlock { Text = "Dùng AutoCAD 2023 trên máy để chuyển Layout sang Model và gom DWG. Hình học trong Model theo bố cục/tỷ lệ in trên sheet, không phải toàn bộ model 1:1.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, FontSize = 11, Margin = new Thickness(0, 10, 0, 8) });
            var export = new Button { Content = "Xuất một file AutoCAD", MinHeight = 40, Foreground = Brushes.White, Background = Avocado, FontWeight = FontWeights.SemiBold };
            export.Click += (s, e) =>
            {
                int cols; double distance;
                if (Selected.Count == 0 || !int.TryParse(columns.Text, out cols) || cols < 1 || cols > 50 || !FitCropSettings.TryParseLength(gap.Text, out distance) || double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0 || distance > 10000)
                { MessageBox.Show(this, "Chọn sheet, số cột 1–50 và khe từ 0–10000 mm.", Title); return; }
                Columns = cols; GapMm = distance; DialogResult = true;
            };
            bottom.Children.Add(export);
            sets.SelectionChanged += (s, e) => { var set = sets.SelectedItem as DwgSheetSet; if (set?.Ids != null) foreach (var row in rows) row.Selected = set.Ids.Contains(row.Id); Draw(); };
            groupBy.SelectionChanged += (s, e) => Draw(); search.TextChanged += (s, e) => Draw(); Draw();
        }
        private void Draw()
        {
            tree.Items.Clear(); string query = search.Text.Trim(), key = groupBy.SelectedItem as string;
            var filtered = rows.Where(r => (r.Number + " " + r.Name).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            var all = new CheckBox { Content = "CHỌN TẤT CẢ TRONG PHẠM VI (" + filtered.Count + ")", IsThreeState = true, IsChecked = State(filtered), Foreground = Avocado, FontWeight = FontWeights.SemiBold };
            var root = new TreeViewItem { Header = all, IsExpanded = true }; tree.Items.Add(root);
            all.Click += (s, e) => { foreach (var r in filtered) r.Selected = all.IsChecked != false; ManualSelection(); Draw(); };
            foreach (var group in filtered.GroupBy(r => groupBy.SelectedIndex == 0 ? r.Prefix : r.Groups.TryGetValue(key, out var value) && value.Length > 0 ? value : "(Chưa gán)"))
            {
                var members = group.ToList(); var check = new CheckBox { Content = group.Key + " (" + members.Count + ")", IsThreeState = true, IsChecked = State(members), Foreground = Avocado, FontWeight = FontWeights.SemiBold };
                var node = new TreeViewItem { Header = check, IsExpanded = query.Length > 0, Margin = new Thickness(0, 6, 0, 6) }; root.Items.Add(node);
                check.Click += (s, e) => { foreach (var r in members) r.Selected = check.IsChecked != false; ManualSelection(); Draw(); };
                foreach (var row in members)
                {
                    var item = new CheckBox { Content = row.Number + " — " + row.Name, IsChecked = row.Selected, Margin = new Thickness(0, 4, 0, 4) };
                    item.Click += (s, e) => { row.Selected = item.IsChecked == true; ManualSelection(); check.IsChecked = State(members); all.IsChecked = State(filtered); UpdateCount(); };
                    node.Items.Add(new TreeViewItem { Header = item });
                }
            }
            UpdateCount();
        }
        private void UpdateCount() { count.Text = "Đã chọn " + rows.Count(r => r.Selected) + " / " + rows.Count + " sheet (kể cả đang ẩn do tìm kiếm)."; }
        private void ManualSelection() { if (sets.SelectedIndex != 0) sets.SelectedIndex = 0; }
        private static bool? State(List<DwgSheetRow> rows) => rows.Count == 0 || rows.All(r => !r.Selected) ? false : rows.All(r => r.Selected) ? (bool?)true : null;
        private static void AddField(StackPanel panel, string label, System.Windows.Controls.Control input)
        { var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(input, 1); grid.Children.Add(input); panel.Children.Add(grid); }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SDK_HD.Dim
{
    internal sealed class PlaceViewRow { internal long Id; internal string Name, Group; internal bool Selected, Initial, Repeatable; }
    internal sealed class PlaceSheetRow { internal long Id; internal string Name; internal bool Selected, Initial; }
    internal sealed class PlacementRow { internal long PlacementId, ViewId, SheetId; internal string Name; internal bool Selected, Remove; }
    internal sealed class PlacementRequest { internal long ViewId, SheetId; internal string Name; internal bool Selected; }
    internal sealed class PlaceViewsWindow : Window
    {
        internal readonly List<PlacementRequest> Additions = new List<PlacementRequest>();
        internal readonly List<PlacementRow> Existing;
        private readonly List<PlaceViewRow> views;
        private readonly List<PlaceSheetRow> sheets;
        private readonly TreeView viewTree = new TreeView(), sheetTree = new TreeView();
        private readonly TextBox viewSearch = new TextBox(), sheetSearch = new TextBox();
        private readonly TextBox paperMargin = new TextBox { Text = "15" }, reserve = new TextBox { Text = "60" }, gap = new TextBox { Text = "10" };
        private readonly CheckBox titles = new CheckBox { Content = "Căn giữa Title viewport", IsChecked = true };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
        private readonly RadioButton preselectedViews = new RadioButton { Content = "View đã chọn trước", GroupName = "ViewScope" };
        private readonly RadioButton preselectedSheets = new RadioButton { Content = "Sheet đã chọn trước", GroupName = "SheetScope" };
        private static readonly Brush Avocado = new SolidColorBrush(Color.FromRgb(88, 117, 43));
        internal double PaperMargin { get; private set; }
        internal double RightReserve { get; private set; }
        internal double Gap { get; private set; }
        internal bool CenterTitles => titles.IsChecked == true;
        internal PlaceViewsWindow(List<PlaceViewRow> viewRows, List<PlaceSheetRow> sheetRows, List<PlacementRow> placements)
        {
            views = viewRows; sheets = sheetRows; Existing = placements;
            Title = "KTool | Đặt View vào Sheet"; Width = 1170; Height = 820; MinWidth = 980; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Segoe UI"); FontSize = 13; Background = new SolidColorBrush(Color.FromRgb(244, 247, 235));
            var root = new Grid { Margin = new Thickness(20) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock { Text = "ĐẶT VIEW VÀO SHEET", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = Avocado, Margin = new Thickness(0, 0, 0, 18) });
            var body = new Grid(); Grid.SetRow(body, 1); root.Children.Add(body); body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) }); body.ColumnDefinitions.Add(new ColumnDefinition());
            body.Children.Add(Pane("CHỌN VIEW MUỐN ĐẶT", viewSearch, viewTree, preselectedViews, "Tất cả Views", "ViewScope"));
            var right = Pane("CHỌN SHEET ĐÍCH", sheetSearch, sheetTree, preselectedSheets, "Tất cả Sheets", "SheetScope"); Grid.SetColumn(right, 2); body.Children.Add(right);
            var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) }; Grid.SetColumn(arrows, 1); body.Children.Add(arrows);
            arrows.Children.Add(Button("Đặt →", QueueAdd)); arrows.Children.Add(Button("← Gỡ", QueueRemove));
            arrows.Children.Add(new TextBlock { Text = "Chọn nhiều sheet cho Legend / Schedule. View thường chỉ chọn một sheet đích mỗi lượt.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, FontSize = 11, Margin = new Thickness(3, 12, 3, 0) });
            var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(bottom, 2); root.Children.Add(bottom);
            var options = new WrapPanel(); AddNumber(options, "Lề giấy (mm)", paperMargin); AddNumber(options, "Chừa khung tên bên phải (mm)", reserve); AddNumber(options, "Khe giữa view (mm)", gap); titles.VerticalAlignment = VerticalAlignment.Center; titles.Margin = new Thickness(10, 0, 0, 0); options.Children.Add(titles); bottom.Children.Add(options);
            var actions = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var apply = Button("Áp dụng kế hoạch", Accept); apply.Background = Avocado; apply.Foreground = Brushes.White; apply.Width = 180; DockPanel.SetDock(apply, Dock.Right); actions.Children.Add(apply);
            var cancel = Button("Hủy", () => Close()); cancel.Width = 80; DockPanel.SetDock(cancel, Dock.Right); actions.Children.Add(cancel); actions.Children.Add(status); bottom.Children.Add(actions);
            viewSearch.TextChanged += (s, e) => DrawViews(); sheetSearch.TextChanged += (s, e) => DrawSheets();
            preselectedViews.Checked += (s, e) => DrawViews(); preselectedViews.Unchecked += (s, e) => DrawViews();
            preselectedSheets.Checked += (s, e) => DrawSheets(); preselectedSheets.Unchecked += (s, e) => DrawSheets();
            DrawViews(); DrawSheets(); UpdateStatus();
        }
        private static Border Pane(string title, TextBox search, TreeView tree, RadioButton preselected, string allText, string groupName)
        {
            var dock = new DockPanel(); var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head);
            head.Children.Add(new Border { Background = Avocado, Padding = new Thickness(12), Child = new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold } });
            head.Children.Add(new TextBlock { Text = "Tìm theo tên / số", Foreground = Brushes.SlateGray, FontSize = 11, Margin = new Thickness(12, 10, 12, 3) }); search.Margin = new Thickness(12, 0, 12, 10); search.Height = 32; search.Padding = new Thickness(8, 4, 8, 4); head.Children.Add(search);
            tree.BorderThickness = new Thickness(0); tree.Margin = new Thickness(6); dock.Children.Add(tree);
            var scopes = new WrapPanel { Margin = new Thickness(12, 8, 12, 10) };
            var all = new RadioButton { Content = allText, GroupName = groupName, IsChecked = true, Margin = new Thickness(0, 0, 18, 0) };
            scopes.Children.Add(all); scopes.Children.Add(preselected); DockPanel.SetDock(scopes, Dock.Bottom);
            dock.Children.Remove(tree); dock.Children.Add(scopes); dock.Children.Add(tree);
            return new Border { Child = dock, Background = Brushes.White, BorderBrush = Brushes.DarkSeaGreen, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5) };
        }
        private void DrawViews()
        {
            viewTree.Items.Clear(); string query = viewSearch.Text.Trim();
            var filtered = views.Where(v => (preselectedViews.IsChecked != true || v.Initial) && (v.Name + " " + v.Group).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            var rootCheck = new CheckBox { Content = "TẤT CẢ VIEWS TRONG PHẠM VI (" + filtered.Count + ")", Foreground = Avocado, FontWeight = FontWeights.SemiBold, IsThreeState = true, IsChecked = SelectionState(filtered.Select(v => v.Selected)) };
            var root = new TreeViewItem { Header = rootCheck, IsExpanded = true };
            rootCheck.Click += (s, e) => { foreach (var v in filtered) v.Selected = rootCheck.IsChecked != false; DrawViews(); };
            foreach (var group in filtered.GroupBy(v => v.Group))
            {
                var groupRows = group.ToList(); var all = new CheckBox { Content = group.Key + " (" + groupRows.Count + ")", Foreground = Avocado, FontWeight = FontWeights.SemiBold, IsThreeState = true, IsChecked = SelectionState(groupRows.Select(v => v.Selected)) };
                var node = new TreeViewItem { Header = all, IsExpanded = true, Margin = new Thickness(0, 5, 0, 5) };
                all.Click += (s, e) => { foreach (var v in groupRows) v.Selected = all.IsChecked != false; DrawViews(); };
                foreach (var view in groupRows)
                {
                    var check = new CheckBox { Content = view.Name, IsChecked = view.Selected, Margin = new Thickness(0, 4, 0, 4) };
                    check.Click += (s, e) => { view.Selected = check.IsChecked == true; all.IsChecked = SelectionState(groupRows.Select(v => v.Selected)); rootCheck.IsChecked = SelectionState(filtered.Select(v => v.Selected)); UpdateStatus(); }; node.Items.Add(new TreeViewItem { Header = check });
                }
                root.Items.Add(node);
            }
            viewTree.Items.Add(root);
            UpdateStatus();
        }
        private void DrawSheets()
        {
            sheetTree.Items.Clear(); string query = sheetSearch.Text.Trim();
            foreach (var sheet in sheets.Where(s => (preselectedSheets.IsChecked != true || s.Initial) && s.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0))
            {
                var existing = Existing.Where(p => p.SheetId == sheet.Id).ToList(); var pending = Additions.Where(p => p.SheetId == sheet.Id).ToList();
                bool occupied = existing.Any(p => !p.Remove) || pending.Count > 0;
                var select = new CheckBox { Content = sheet.Name + " (" + (existing.Count(p => !p.Remove) + pending.Count) + ")", IsChecked = sheet.Selected, Foreground = occupied ? Avocado : Brushes.SlateGray, FontWeight = occupied ? FontWeights.SemiBold : FontWeights.Normal };
                select.Checked += (s, e) => { sheet.Selected = true; UpdateStatus(); }; select.Unchecked += (s, e) => { sheet.Selected = false; UpdateStatus(); };
                var parent = new TreeViewItem { Header = select, IsExpanded = pending.Count > 0 || query.Length > 0, Margin = new Thickness(0, 5, 0, 5) };
                foreach (var placement in existing)
                {
                    var check = new CheckBox { Content = (placement.Remove ? "[Sẽ gỡ] " : "") + placement.Name, IsChecked = placement.Selected, Foreground = placement.Remove ? Brushes.DarkOrange : Brushes.SlateGray, Margin = new Thickness(0, 3, 0, 3) };
                    check.Checked += (s, e) => placement.Selected = true; check.Unchecked += (s, e) => placement.Selected = false; parent.Items.Add(new TreeViewItem { Header = check });
                }
                foreach (var placement in pending)
                {
                    var check = new CheckBox { Content = "[Sẽ đặt] " + placement.Name, IsChecked = placement.Selected, Foreground = Avocado, Margin = new Thickness(0, 3, 0, 3) };
                    check.Checked += (s, e) => placement.Selected = true; check.Unchecked += (s, e) => placement.Selected = false; parent.Items.Add(new TreeViewItem { Header = check });
                }
                sheetTree.Items.Add(parent);
            }
            UpdateStatus();
        }
        private void QueueAdd()
        {
            var chosenViews = views.Where(v => v.Selected).ToList(); var targets = sheets.Where(s => s.Selected).ToList();
            if (targets.Count == 0 || chosenViews.Count == 0) { status.Text = "Tích view bên trái và sheet đích bên phải."; return; }
            if (targets.Count > 1 && chosenViews.Any(v => !v.Repeatable)) { status.Text = "View thường không đặt được lên nhiều sheet. Chọn một sheet hoặc chỉ chọn Legend / Schedule."; return; }
            if (chosenViews.Any(v => !v.Repeatable && (Additions.Any(p => p.ViewId == v.Id && p.SheetId != targets[0].Id) || Existing.Any(p => p.ViewId == v.Id && p.SheetId != targets[0].Id && !p.Remove))))
            { status.Text = "Có view đã nằm trên sheet khác. Gỡ vị trí cũ trước khi chuyển; không tự sao chép view."; return; }
            foreach (var view in chosenViews)
            {
                foreach (var target in targets)
                    if (!Additions.Any(p => p.ViewId == view.Id && p.SheetId == target.Id) && !Existing.Any(p => p.ViewId == view.Id && p.SheetId == target.Id && !p.Remove))
                        Additions.Add(new PlacementRequest { ViewId = view.Id, SheetId = target.Id, Name = view.Name });
                view.Selected = false;
            }
            DrawViews(); DrawSheets(); UpdateStatus();
        }
        private static bool? SelectionState(IEnumerable<bool> values)
        { var list = values.ToList(); return list.Count == 0 || list.All(v => !v) ? false : list.All(v => v) ? (bool?)true : null; }
        private void QueueRemove()
        {
            foreach (var pending in Additions.Where(p => p.Selected).ToList()) Additions.Remove(pending);
            foreach (var placement in Existing.Where(p => p.Selected)) { placement.Remove = !placement.Remove; placement.Selected = false; }
            DrawSheets(); UpdateStatus();
        }
        private void UpdateStatus() { status.Text = "Đã chọn " + views.Count(v => v.Selected) + " view / " + sheets.Count(s => s.Selected) + " sheet (kể cả đang ẩn do lọc). Sẽ đặt " + Additions.Count + "; gỡ " + Existing.Count(p => p.Remove) + ". Gỡ chỉ bỏ khỏi sheet."; }
        private void Accept()
        {
            double m, r, g;
            if (!FitCropSettings.TryParseLength(paperMargin.Text, out m) || !FitCropSettings.TryParseLength(reserve.Text, out r) || !FitCropSettings.TryParseLength(gap.Text, out g)
                || new[] { m, r, g }.Any(v => double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v > 500)) { status.Text = "Nhập lề / vùng khung tên / khe view từ 0 đến 500 mm giấy."; return; }
            if (Additions.Count == 0 && !Existing.Any(p => p.Remove)) { status.Text = "Chưa có kế hoạch. Dùng nút Đặt hoặc Gỡ trước."; return; }
            PaperMargin = m / 304.8; RightReserve = r / 304.8; Gap = g / 304.8; DialogResult = true;
        }
        private static void AddNumber(WrapPanel panel, string label, TextBox box)
        { var stack = new StackPanel { Margin = new Thickness(0, 0, 14, 0) }; stack.Children.Add(new TextBlock { Text = label, Foreground = Brushes.SlateGray, FontSize = 11 }); box.Width = 110; box.Height = 28; stack.Children.Add(box); panel.Children.Add(stack); }
        private static Button Button(string text, Action action)
        { var button = new Button { Content = text, MinHeight = 36, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(3), Background = new SolidColorBrush(Color.FromRgb(228, 237, 210)), Foreground = Avocado, BorderBrush = Brushes.DarkSeaGreen }; button.Click += (s, e) => action(); return button; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public sealed class FileCleanupCommand : IExternalCommand
    {
        public sealed class Row
        {
            public ElementId Id;
            public Definition Definition;
            public string Name;
            public string Detail;
            public bool Selected { get; set; }
        }

        private sealed class Group
        {
            public string Name;
            public List<Row> Rows;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            List<Group> groups = ReadGroups(doc);
            var window = new CleanupWindow(groups, doc.Title);
            new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
            if (window.ShowDialog() != true) return Result.Cancelled;

            List<Row> selected = window.SelectedRows;
            if (selected.Count == 0) return Result.Cancelled;
            var confirm = new TaskDialog("KTool - Dọn dẹp file")
            {
                MainInstruction = "Xóa " + selected.Count + " mục đã chọn?",
                MainContent = "Một số mục có thể có phần tử phụ thuộc. Revit sẽ xử lý các phụ thuộc theo quy tắc của model.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int removed = 0;
            var errors = new List<string>();
            using (Transaction tx = new Transaction(doc, "KTool - Dọn dẹp file"))
            {
                tx.Start();
                foreach (Row row in selected)
                {
                    try
                    {
                        if (row.Definition != null)
                        {
                            if (doc.ParameterBindings.Remove(row.Definition)) removed++;
                            else errors.Add(row.Name + ": không thể gỡ parameter binding.");
                        }
                        else if (row.Id != null && doc.GetElement(row.Id) != null)
                        {
                            ICollection<ElementId> deleted = doc.Delete(row.Id);
                            removed += deleted.Count;
                        }
                    }
                    catch (Exception ex) { errors.Add(row.Name + ": " + ex.Message); }
                }
                if (removed == 0) tx.RollBack();
                else if (tx.Commit() != TransactionStatus.Committed)
                {
                    message = "Revit không lưu được thao tác dọn dẹp.";
                    return Result.Failed;
                }
            }
            new TaskDialog("KTool - Dọn dẹp file")
            {
                MainInstruction = "Đã xóa " + removed + " phần tử",
                MainContent = errors.Count == 0 ? "Có thể Undo lượt dọn dẹp trong Revit." : errors.Count + " mục chưa xóa được.",
                ExpandedContent = string.Join("\n", errors),
                CommonButtons = TaskDialogCommonButtons.Close
            }.Show();
            return removed > 0 ? Result.Succeeded : Result.Cancelled;
        }

        private static List<Group> ReadGroups(Document doc)
        {
            List<View> allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
            var groups = new List<Group>();
            groups.Add(Make("Views", allViews.Where(v => !v.IsTemplate && !(v is ViewSheet) && !(v is ViewSchedule))
                .Select(v => ElementRow(v, v.ViewType.ToString()))));
            groups.Add(Make("Sheets", allViews.OfType<ViewSheet>().Select(v => ElementRow(v, v.SheetNumber))));
            groups.Add(Make("View Templates", allViews.Where(v => v.IsTemplate).Select(v => ElementRow(v, v.ViewType.ToString()))));
            groups.Add(Make("Schedules", allViews.OfType<ViewSchedule>().Where(v => !v.IsTitleblockRevisionSchedule).Select(v => ElementRow(v, "Schedule"))));
            groups.Add(Make("Rooms", new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                .OfType<SpatialElement>().Select(e => ElementRow(e, (e.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "") + "  " + (e.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Room")))));
            groups.Add(Make("Line Patterns", new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                .Where(p => p.Id != LinePatternElement.GetSolidPatternId()).Select(p => ElementRow(p, "Line Pattern"))));
            var parameterRows = new List<Row>();
            DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                Definition def = iterator.Key as Definition;
                if (def != null) parameterRows.Add(new Row { Definition = def, Name = def.Name, Detail = "Project Parameter" });
            }
            groups.Add(Make("Parameters", parameterRows));
            groups.Add(Make("Filters", new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>()
                .Select(f => ElementRow(f, "View Filter"))));
            groups.Add(Make("Imported", new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
                .Select(i => ElementRow(i, i.IsLinked ? "Linked CAD" : "Imported CAD"))));
            return groups;
        }

        private static Row ElementRow(Element e, string detail) => new Row { Id = e.Id, Name = string.IsNullOrWhiteSpace(e.Name) ? "(Không có tên)" : e.Name, Detail = detail + " · ID " + e.Id.Value };
        private static Row RoomRow(Document doc, SpatialElement room)
        {
            string number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
            string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "Room";
            string level = doc.GetElement(room.LevelId)?.Name ?? "Không có Level";
            string area = room.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsValueString() ?? "Chưa tính diện tích";
            return new Row { Id = room.Id, Name = (number.Length == 0 ? "(Chưa đánh số)" : number) + " — " + name, Detail = level + " · " + area + " · ID " + room.Id.Value };
        }
        private static Group Make(string name, IEnumerable<Row> rows) => new Group { Name = name, Rows = rows.OrderBy(r => r.Name).ToList() };

        internal static ImageSource CreateIcon()
        {
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                var navy = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 48, 82));
                var pale = new SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 235, 248));
                var pen = new Pen(navy, 1.8);
                dc.DrawRectangle(pale, pen, new Rect(5, 4, 22, 24));
                dc.DrawLine(pen, new System.Windows.Point(10, 11), new System.Windows.Point(22, 11));
                dc.DrawLine(pen, new System.Windows.Point(10, 16), new System.Windows.Point(22, 16));
                dc.DrawLine(pen, new System.Windows.Point(10, 21), new System.Windows.Point(19, 21));
            }
            var image = new DrawingImage(group); image.Freeze(); return image;
        }

        private sealed class CleanupWindow : Window
        {
            private readonly List<Group> _groups;
            private readonly ListBox _items = new ListBox();
            private readonly TextBox _search = new TextBox();
            private readonly CheckBox _all = new CheckBox { Content = "Chọn tất cả mục", Margin = new Thickness(8, 5, 8, 5) };
            private readonly TextBlock _count = new TextBlock { Margin = new Thickness(4, 4, 4, 4), Foreground = Brush("#42536A") };
            private readonly Dictionary<string, Button> _nav = new Dictionary<string, Button>();
            private Group _active;
            private bool _updatingAll;
            private readonly Brush _navy = Brush("#183052");
            private readonly Brush _accent = Brush("#244C7A");
            private readonly Brush _light = Brush("#E4EDF7");

            internal List<Row> SelectedRows => _active == null ? new List<Row>() : _active.Rows.Where(r => r.Selected).ToList();
            internal CleanupWindow(List<Group> groups, string documentTitle)
            {
                _groups = groups;
                Title = "KTool - Dọn dẹp file"; Width = 920; Height = 650; MinWidth = 760; MinHeight = 520;
                WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#F5F7FA");
                var root = new Grid { Margin = new Thickness(10) };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var left = new Grid { Margin = new Thickness(0, 0, 10, 0) };
                left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var heading = new TextBlock { Text = "DỌN DẸP FILE REVIT  ·  " + System.IO.Path.GetFileNameWithoutExtension(documentTitle), Background = _navy, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold, Padding = new Thickness(12, 10, 8, 10) };
                left.Children.Add(heading);
                _search.Margin = new Thickness(12, 10, 12, 8); _search.Height = 34; _search.Padding = new Thickness(8, 6, 8, 4);
                _search.ToolTip = "Tìm kiếm trong nhóm đang chọn"; _search.TextChanged += (s, e) => RefreshItems();
                Grid.SetRow(_search, 1); left.Children.Add(_search);
                var allPanel = new Border { Background = _light, Margin = new Thickness(12, 0, 12, 4), Child = _all };
                _all.Checked += (s, e) => SetVisible(true); _all.Unchecked += (s, e) => SetVisible(false);
                Grid.SetRow(allPanel, 2); left.Children.Add(allPanel);
                _items.Margin = new Thickness(12, 0, 12, 6); _items.BorderBrush = _accent; _items.BorderThickness = new Thickness(1);
                Grid.SetRow(_items, 3); left.Children.Add(_items);
                Grid.SetRow(_count, 4); left.Children.Add(_count);
                Grid.SetColumn(left, 0); Grid.SetRow(left, 0); root.Children.Add(left);
                var side = new StackPanel { Background = Brush("#E8EEF5"), Margin = new Thickness(0, 0, 0, 8) };
                foreach (Group g in _groups)
                {
                    var b = new Button { Content = g.Name + " (" + g.Rows.Count + ")", Tag = g, Height = 40, Margin = new Thickness(6, 5, 6, 0), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = _navy, FontWeight = FontWeights.Medium };
                    b.Click += (s, e) => SelectGroup((Group)((Button)s).Tag); _nav[g.Name] = b; side.Children.Add(b);
                }
                Grid.SetColumn(side, 1); Grid.SetRow(side, 0); root.Children.Add(side);
                var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 2, 0) };
                var cancel = new Button { Content = "Đóng", Width = 90, Height = 34, Margin = new Thickness(4), IsCancel = true };
                var delete = new Button { Content = "Xóa mục đã chọn", Width = 150, Height = 36, Margin = new Thickness(4), Background = _navy, Foreground = Brushes.White, BorderBrush = _navy, FontWeight = FontWeights.SemiBold, IsDefault = true };
                delete.Click += (s, e) => { if (SelectedRows.Count == 0) { MessageBox.Show(this, "Hãy chọn ít nhất một mục cần xóa.", "KTool - Dọn dẹp file"); return; } DialogResult = true; };
                DockPanel.SetDock(delete, Dock.Right); footer.Children.Add(delete); DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel);
                var hint = new TextBlock { Text = "Chọn các mục cần xóa. Lượt xóa có thể Undo trong Revit.", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#42536A") }; footer.Children.Add(hint);
                Grid.SetColumnSpan(footer, 2); Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
                SelectGroup(_groups.FirstOrDefault());
            }
            private void SelectGroup(Group group)
            {
                _active = group;
                foreach (var pair in _nav) { bool active = group != null && pair.Key == group.Name; pair.Value.Background = active ? _accent : Brushes.Transparent; pair.Value.Foreground = active ? Brushes.White : _navy; }
                _updatingAll = true; _all.IsChecked = false; _updatingAll = false; _search.Text = ""; RefreshItems();
            }
            private void RefreshItems()
            {
                if (_active == null) return;
                string q = _search.Text.Trim();
                _items.ItemsSource = null;
                _items.Items.Clear();
                foreach (Row row in _active.Rows.Where(r => string.IsNullOrEmpty(q) || (r.Name + " " + r.Detail).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var line = new Grid { Margin = new Thickness(4, 2, 6, 2) };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                    var check = new CheckBox { IsChecked = row.Selected, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
                    check.Checked += (sender, args) => { row.Selected = true; RefreshCount(); };
                    check.Unchecked += (sender, args) => { row.Selected = false; RefreshCount(); };
                    var name = new TextBlock { Text = string.IsNullOrWhiteSpace(row.Name) ? "(Không có tên)" : row.Name, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brush("#1D2C3D"), FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = row.Name };
                    var detail = new TextBlock { Text = row.Detail ?? "", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 2, 0),
                        Foreground = Brush("#66758A"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = row.Detail };
                    Grid.SetColumn(name, 1); Grid.SetColumn(detail, 2);
                    line.Children.Add(check); line.Children.Add(name); line.Children.Add(detail);
                    _items.Items.Add(new ListBoxItem { Content = line, Padding = new Thickness(2), HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderThickness = new Thickness(0) });
                }
                RefreshCount();
            }            private void RefreshCount()
            {
                if (_active == null) { _count.Text = ""; return; }
                _count.Text = _active.Rows.Count + " mục trong nhóm · " + _active.Rows.Count(r => r.Selected) + " đã chọn";
            }
            private void SetVisible(bool value)
            {
                if (_active == null || !IsLoaded || _updatingAll) return;
                string q = _search.Text.Trim();
                foreach (Row r in _active.Rows.Where(r => string.IsNullOrEmpty(q) || (r.Name + " " + r.Detail).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)) r.Selected = value;
                RefreshItems();
            }
            private static SolidColorBrush Brush(string value) => new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
        }
    }
}

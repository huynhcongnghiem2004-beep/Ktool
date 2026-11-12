using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Grid = System.Windows.Controls.Grid;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.Dim
{
    internal sealed class BrowserRow
    {
        internal long Id;
        internal string Name, Group, Sheet;
        internal bool IsSheet, Open, Active, Placed;
    }
    [Transaction(TransactionMode.Manual)]
    public sealed class ViewSheetBrowserCommand : IExternalCommand
    {
        private static ViewSheetBrowserWindow window;
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (data.Application.ActiveUIDocument == null) return Result.Cancelled;
            if (window != null)
            {
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate(); window.RefreshViews(); return Result.Succeeded;
            }
            var handler = new ViewSheetBrowserHandler(); var external = ExternalEvent.Create(handler);
            window = new ViewSheetBrowserWindow(handler, external); handler.Window = window;
            new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
            handler.Read(data.Application); window.Closed += (s, e) => { window = null; external.Dispose(); };
            window.Show(); return Result.Succeeded;
        }
        internal static ImageSource CreateIcon()
        {
            var drawing = new DrawingGroup(); using (var dc = drawing.Open())
            {
                var pen = new Pen(Brushes.OliveDrab, 1.5);
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 32, 32));
                dc.DrawRectangle(Brushes.Honeydew, pen, new Rect(8, 3, 21, 26));
                for (int i = 0; i < 3; i++) { dc.DrawRectangle(Brushes.OliveDrab, null, new Rect(12, 8 + i * 6, 3, 3)); dc.DrawLine(pen, new System.Windows.Point(18, 10 + i * 6), new System.Windows.Point(25, 10 + i * 6)); }
                dc.DrawLine(pen, new System.Windows.Point(3, 6), new System.Windows.Point(3, 26));
                for (int i = 0; i < 3; i++) dc.DrawLine(pen, new System.Windows.Point(3, 9 + i * 7), new System.Windows.Point(7, 9 + i * 7));
            }
            var icon = new DrawingImage(drawing); icon.Freeze(); return icon;
        }
    }
    internal sealed class ViewSheetBrowserHandler : IExternalEventHandler
    {
        internal ViewSheetBrowserWindow Window;
        internal long? RequestedId;
        internal Document Document;
        public string GetName() => "KTool - Duyệt View/Sheet";
        internal void Read(UIApplication app)
        {
            var ui = app.ActiveUIDocument;
            if (ui == null) { Window.SetRows(new List<BrowserRow>(), "Không có tài liệu đang mở"); Document = null; return; }
            Document = ui.Document;
            var open = new HashSet<ElementId>(ui.GetOpenUIViews().Select(v => v.ViewId));
            var sheets = new Dictionary<ElementId, List<string>>();
            foreach (Viewport port in new FilteredElementCollector(Document).OfClass(typeof(Viewport)))
            {
                var sheet = Document.GetElement(port.SheetId) as ViewSheet; if (sheet == null) continue;
                if (!sheets.ContainsKey(port.ViewId)) sheets[port.ViewId] = new List<string>();
                sheets[port.ViewId].Add(sheet.SheetNumber);
            }
            foreach (ScheduleSheetInstance instance in new FilteredElementCollector(Document).OfClass(typeof(ScheduleSheetInstance)))
            {
                var sheet = Document.GetElement(instance.OwnerViewId) as ViewSheet; if (sheet == null) continue;
                if (!sheets.ContainsKey(instance.ScheduleId)) sheets[instance.ScheduleId] = new List<string>();
                sheets[instance.ScheduleId].Add(sheet.SheetNumber);
            }
            var rows = new List<BrowserRow>();
            foreach (View view in new FilteredElementCollector(Document).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && Supported(v)))
            {
                var sheet = view as ViewSheet; string placed = sheets.TryGetValue(view.Id, out var list) ? string.Join(", ", list.Distinct().OrderBy(n => n)) : "";
                if (sheet != null && sheet.IsPlaceholder) continue;
                rows.Add(new BrowserRow { Id = view.Id.Value, Name = sheet == null ? view.Name : sheet.SheetNumber + " — " + sheet.Name,
                    Group = sheet != null ? "Sheets" : Group(view.ViewType), Sheet = placed, IsSheet = sheet != null,
                    Open = open.Contains(view.Id), Active = ui.ActiveView.Id == view.Id, Placed = placed.Length > 0 });
            }
            Window.SetRows(rows.OrderBy(r => r.Group).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), Document.Title);
        }
        private static bool Supported(View v) => !(v is ViewSchedule schedule && schedule.IsTitleblockRevisionSchedule)
            && (v is ViewPlan || v is ViewSection || v is View3D || v is ViewSheet || v.ViewType == ViewType.Legend || v.ViewType == ViewType.DraftingView || v.ViewType == ViewType.Schedule);
        private static string Group(ViewType type)
        {
            switch (type)
            {
                case ViewType.FloorPlan: return "Floor Plans";
                case ViewType.CeilingPlan: return "Ceiling Plans";
                case ViewType.EngineeringPlan: return "Structural Plans";
                case ViewType.Elevation: return "Elevations";
                case ViewType.Section: return "Sections";
                case ViewType.ThreeD: return "3D Views";
                case ViewType.Legend: return "Legends";
                case ViewType.DraftingView: return "Drafting Views";
                case ViewType.Schedule: return "Schedules";
                case ViewType.AreaPlan: return "Area Plans";
                default: return type.ToString();
            }
        }
        public void Execute(UIApplication app)
        {
            try
            {
                if (Window == null || !Window.IsVisible) return;
                var id = RequestedId; RequestedId = null;
                if (id.HasValue)
                {
                    var ui = app.ActiveUIDocument;
                    // Revit may return a new managed Document wrapper for the same
                    // native document between an ExternalCommand and ExternalEvent.
                    // Document.Equals compares native identity; != compares wrappers.
                    if (ui == null || Document == null || !Document.IsValidObject || !ui.Document.Equals(Document))
                    { Read(app); Window.Status("Tài liệu đã đổi. Danh sách vừa được làm mới; chọn lại view."); return; }
                    var view = ui.Document.GetElement(new ElementId(id.Value)) as View;
                    if (view == null) { Read(app); Window.Status("View đã bị xóa; danh sách đã cập nhật."); return; }
                    ui.ActiveView = view;
                    Window.Status("Đã mở: " + view.Name);
                }
                Read(app);
                if (!id.HasValue) Window.Status("Đã làm mới danh sách view và sheet.");
            }
            catch (Exception ex) { Window?.Status("Không mở được view: " + ex.Message); }
        }
    }
    internal sealed class ViewSheetBrowserWindow : Window
    {
        private static readonly Brush Avocado = new SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 117, 43));
        private readonly ViewSheetBrowserHandler handler;
        private readonly ExternalEvent external;
        private readonly TextBox search = new TextBox { MinHeight = 32, Padding = new Thickness(8, 4, 8, 4) };
        private readonly CheckBox placed = new CheckBox { Content = "Đã lên sheet", Margin = new Thickness(0, 8, 14, 8) };
        private readonly CheckBox opened = new CheckBox { Content = "Đang mở", Margin = new Thickness(0, 8, 0, 8) };
        private readonly TreeView tree = new TreeView { BorderThickness = new Thickness(0), Background = Brushes.White };
        private readonly TextBlock count = new TextBlock(), status = new TextBlock(), document = new TextBlock();
        private List<BrowserRow> rows = new List<BrowserRow>();
        private readonly HashSet<string> expanded = new HashSet<string>();
        private bool initialized;
        internal ViewSheetBrowserWindow(ViewSheetBrowserHandler handler, ExternalEvent external)
        {
            this.handler = handler; this.external = external;
            Title = "KTool | Duyệt View/Sheet"; Width = 460; Height = 820; MinWidth = 350; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 247, 235)); Foreground = Brushes.DarkSlateGray;
            var root = new Grid { Margin = new Thickness(16) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new StackPanel(); root.Children.Add(top);
            top.Children.Add(new TextBlock { Text = "DUYỆT VIEW / SHEET", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Avocado });
            document.Foreground = Brushes.SlateGray; document.Margin = new Thickness(0, 5, 0, 12); document.TextTrimming = TextTrimming.CharacterEllipsis; top.Children.Add(document);
            top.Children.Add(search); search.ToolTip = "Tìm tên view, số/tên sheet hoặc nhóm view";
            var filters = new WrapPanel(); filters.Children.Add(placed); filters.Children.Add(opened); top.Children.Add(filters);
            var buttons = new WrapPanel(); buttons.Children.Add(Button("Làm mới", RefreshViews));
            buttons.Children.Add(Button("Mở rộng", () => { foreach (var g in rows.Select(r => r.Group).Distinct()) expanded.Add(g); Draw(); }));
            buttons.Children.Add(Button("Thu gọn", () => { expanded.Clear(); Draw(); })); top.Children.Add(buttons);
            var legend = new TextBlock { Text = "● View hiện tại    ■ Đã lên sheet    □ Chưa lên sheet", Foreground = Brushes.SlateGray, FontSize = 11, Margin = new Thickness(0, 8, 0, 10), TextWrapping = TextWrapping.Wrap }; top.Children.Add(legend);
            var card = new Border { Background = Brushes.White, BorderBrush = Brushes.DarkSeaGreen, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(5), Child = tree };
            Grid.SetRow(card, 1); root.Children.Add(card);
            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
            count.Foreground = Avocado; count.FontWeight = FontWeights.SemiBold; footer.Children.Add(count);
            var open = Button("Mở View / Sheet đã chọn", OpenSelected); open.Height = 36; open.Background = Avocado; open.Foreground = Brushes.White; footer.Children.Add(open);
            status.Text = "Nhấp đúp để mở • Cửa sổ giữ mở khi chuyển view"; status.TextWrapping = TextWrapping.Wrap; status.FontSize = 11; status.Foreground = Brushes.SlateGray; status.Margin = new Thickness(0, 6, 0, 0); footer.Children.Add(status);
            search.TextChanged += (s, e) => Draw(); placed.Checked += (s, e) => Draw(); placed.Unchecked += (s, e) => Draw(); opened.Checked += (s, e) => Draw(); opened.Unchecked += (s, e) => Draw();
            tree.KeyDown += (s, e) => { if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; } };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.F5) { RefreshViews(); e.Handled = true; } };
        }
        internal void SetRows(List<BrowserRow> values, string name)
        {
            rows = values; document.Text = name;
            if (!initialized) { foreach (var group in rows.Select(r => r.Group).Distinct()) expanded.Add(group); initialized = true; }
            Draw();
        }
        internal void Status(string text) { status.Text = text; }
        internal void RefreshViews() { Request(null); }
        private void Request(long? id)
        {
            if (external.IsPending) { Status("Yêu cầu trước đang xử lý. Hãy thử lại sau."); return; }
            handler.RequestedId = id; Raise();
        }
        private void Raise()
        {
            var request = external.Raise(); if (request != ExternalEventRequest.Accepted) Status("Yêu cầu trước đang xử lý. Hãy thử lại sau.");
        }
        private void OpenSelected()
        {
            if (!((tree.SelectedItem as TreeViewItem)?.Tag is BrowserRow row)) { Status("Chọn một view hoặc sheet trong cây."); return; }
            Request(row.Id);
        }
        private void Draw()
        {
            long? selectedId = ((tree.SelectedItem as TreeViewItem)?.Tag as BrowserRow)?.Id;
            tree.Items.Clear(); string q = search.Text.Trim();
            var filtered = rows.Where(r => (placed.IsChecked != true || r.Placed || r.IsSheet) && (opened.IsChecked != true || r.Open)
                && (r.Name + " " + r.Group + " " + r.Sheet).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            foreach (var group in filtered.GroupBy(r => r.Group))
            {
                string key = group.Key;
                var parent = new TreeViewItem { Header = key + " (" + group.Count() + ")", Foreground = Avocado, FontWeight = FontWeights.SemiBold,
                    IsExpanded = q.Length > 0 || expanded.Contains(key), Margin = new Thickness(0, 4, 0, 4) };
                parent.Expanded += (s, e) => { if (ReferenceEquals(e.OriginalSource, parent)) expanded.Add(key); };
                parent.Collapsed += (s, e) => { if (ReferenceEquals(e.OriginalSource, parent)) expanded.Remove(key); };
                foreach (var row in group)
                {
                    var text = new TextBlock { Text = (row.Active ? "● " : row.IsSheet || row.Placed ? "■ " : "□ ") + row.Name,
                        Foreground = row.Active ? Brushes.DarkGreen : row.IsSheet || row.Placed ? Avocado : Brushes.SlateGray, FontWeight = row.Active ? FontWeights.Bold : FontWeights.Normal };
                    var child = new TreeViewItem { Header = text, Tag = row, Padding = new Thickness(0, 3, 0, 3), ToolTip = row.Name + (row.Sheet.Length == 0 ? "" : "\nSheet: " + row.Sheet) + (row.Open ? "\nĐang mở" : "") };
                    child.MouseDoubleClick += (s, e) => { e.Handled = true; Request(row.Id); };
                    if (row.Id == selectedId) child.IsSelected = true;
                    parent.Items.Add(child);
                }
                tree.Items.Add(parent);
            }
            count.Text = filtered.Count + " / " + rows.Count + " view và sheet";
        }
        private static Button Button(string text, Action click)
        { var button = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(2), Foreground = Avocado, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 240, 213)), BorderBrush = Brushes.DarkSeaGreen }; button.Click += (s, e) => click(); return button; }
    }
}

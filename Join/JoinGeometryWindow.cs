using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Serialization;
namespace SDK_HD.Dim
{
    public class JoinRule
    {
        public int Priority { get; set; }
        public int Target { get; set; }
    }

    public class JoinCategory
    {
        public string Name { get; set; }
        public int Id { get; set; }
    }

    public class JoinRequest
    {
        public List<JoinRule> Rules = new List<JoinRule>();
        public int Scope; // 0 view, 1 project, 2 selection, 3 rectangle
        public int Action; // 0 join, 1 unjoin, 2 selected cuts others, 3 others cut selected
    }

    public static class JoinRuleCatalog
    {
        public static List<JoinCategory> Categories()
        {
            return new List<JoinCategory>
            {
                Category("Structural Floors / Sàn", (int)Autodesk.Revit.DB.BuiltInCategory.OST_Floors),
                Category("Structural Framing / Dầm", (int)Autodesk.Revit.DB.BuiltInCategory.OST_StructuralFraming),
                Category("Structural Columns / Cột kết cấu", (int)Autodesk.Revit.DB.BuiltInCategory.OST_StructuralColumns),
                Category("Walls / Tường", (int)Autodesk.Revit.DB.BuiltInCategory.OST_Walls),
                Category("Structural Foundations / Móng", (int)Autodesk.Revit.DB.BuiltInCategory.OST_StructuralFoundation),
                Category("Columns / Cột kiến trúc", (int)Autodesk.Revit.DB.BuiltInCategory.OST_Columns),
                Category("Roofs / Mái", (int)Autodesk.Revit.DB.BuiltInCategory.OST_Roofs),
                Category("Ceilings / Trần", (int)Autodesk.Revit.DB.BuiltInCategory.OST_Ceilings),
                Category("Generic Models / Mô hình chung", (int)Autodesk.Revit.DB.BuiltInCategory.OST_GenericModel)
            };
        }
        private static JoinCategory Category(string name, int id)
        { return new JoinCategory { Name = name, Id = (int)id }; }

        public static string ValidateRules(IEnumerable<JoinRule> rules)
        {
            var allowed = new HashSet<int>(Categories().Select(c => c.Id));
            var seen = new HashSet<string>();
            if (rules == null || !rules.Any()) return "Hãy thêm ít nhất một quy tắc.";
            foreach (JoinRule rule in rules)
            {
                if (rule == null || !allowed.Contains(rule.Priority) || !allowed.Contains(rule.Target))
                    return "Quy tắc chứa nhóm đối tượng không được hỗ trợ.";
                string key = Math.Min(rule.Priority, rule.Target) + ":" + Math.Max(rule.Priority, rule.Target);
                if (!seen.Add(key)) return "Có quy tắc trùng cặp hoặc ngược chiều nhau. Hãy giữ một chiều ưu tiên cho mỗi cặp.";
            }
            return null;
        }

    }
    public class JoinProgressWindow : Window
    {
        private readonly TextBlock _status = new TextBlock { Text = "Đang chuẩn bị…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        private bool _stop, _done;
        private DateTime _lastUpdate = DateTime.MinValue;
        public JoinProgressWindow()
        {
            Title = "KTool - Đang Join/Unjoin"; Width = 460; Height = 160;
            ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var panel = new StackPanel(); panel.Children.Add(_status);
            var cancel = new Button { Content = "Dừng và hoàn tác lượt chạy", Margin = new Thickness(12), Height = 30 };
            cancel.Click += delegate { _stop = true; _status.Text = "Đang hoàn tác…"; };
            panel.Children.Add(cancel); Content = panel;
            Closing += (sender, e) => { if (!_done) { _stop = true; e.Cancel = true; } };
        }
        public void Execute(Action<Action<string>> work)
        {
            Exception error = null;
            ContentRendered += delegate
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { work(Update); }
                    catch (Exception ex) { error = ex; }
                    finally { _done = true; Close(); }
                }));
            };
            ShowDialog();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        private void Update(string text)
        {
            if ((DateTime.UtcNow - _lastUpdate).TotalMilliseconds >= 100)
            {
                _status.Text = text; _lastUpdate = DateTime.UtcNow;
                Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
            }
            if (_stop) throw new System.OperationCanceledException();
        }
    }

    public class JoinGeometryWindow : Window
    {
        public static ImageSource CreateRibbonIcon(int size)
        {
            var drawing = new DrawingVisual();
            using (DrawingContext context = drawing.RenderOpen())
            {
                context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(255, 202, 0)),
                    new Pen(Brushes.DimGray, 1), new Rect(2, 4, 18, 16), 3, 3);
                context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(70, 90, 110)),
                    new Pen(Brushes.White, 1), new Rect(12, 13, 18, 16), 3, 3);
                context.DrawLine(new Pen(Brushes.White, 2), new Point(14, 17), new Point(18, 17));
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size,
                96.0 * size / 32, 96.0 * size / 32, PixelFormats.Pbgra32);
            bitmap.Render(drawing); bitmap.Freeze(); return bitmap;
        }
        private readonly StackPanel _rows = new StackPanel();
        private readonly List<JoinRule> _rules = new List<JoinRule>();
        private readonly List<JoinCategory> _categories = JoinRuleCatalog.Categories();
        private readonly RadioButton[] _scope = new RadioButton[4];
        private readonly Brush _yellow = new SolidColorBrush(Color.FromRgb(255, 202, 0));
        public JoinRequest Request { get; private set; }

        public JoinGeometryWindow(JoinRequest previous)
        {
            Title = "KTool - Join / Unjoin tự động"; Width = 680; Height = 660;
            MinWidth = 640; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
            var root = new Grid { Margin = new Thickness(12) }; Content = root;
            foreach (GridLength height in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
                root.RowDefinitions.Add(new RowDefinition { Height = height });
            var header = new Grid { Background = Brushes.WhiteSmoke, Height = 38 };
            Columns(header);
            Add(header, new TextBlock { Text = "Đối tượng ưu tiên cắt", VerticalAlignment = VerticalAlignment.Center }, 0);
            Add(header, new TextBlock { Text = "Đảo chiều", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }, 1);
            Add(header, new TextBlock { Text = "Đối tượng bị cắt", VerticalAlignment = VerticalAlignment.Center }, 2);
            Add(header, Button("+", () => AddRule(), "Thêm quy tắc"), 3); root.Children.Add(header);
            var scroll = new ScrollViewer { Content = _rows, Background = Brushes.WhiteSmoke, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1); root.Children.Add(scroll);
            var tools = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
            tools.Children.Add(Button("Cắt đối tượng khác", () => Accept(2), "Đối tượng được chọn cắt đối tượng khác theo các cặp nhóm trong bảng"));
            tools.Children.Add(Button("Cắt đối tượng được chọn", () => Accept(3), "Đối tượng khác cắt đối tượng được chọn theo các cặp nhóm trong bảng"));
            tools.Children.Add(Button("Nhập", Import, "Nhập quy tắc từ XML"));
            tools.Children.Add(Button("Xuất", Export, "Xuất quy tắc ra XML"));
            tools.Children.Add(Button("+", () => AddRule(), "Thêm quy tắc"));
            tools.Children.Add(Button("Xóa hết", () => { _rules.Clear(); DrawRows(); }, "Xóa các quy tắc trong bảng"));
            Grid.SetRow(tools, 2); root.Children.Add(tools);
            var scopePanel = new StackPanel();
            scopePanel.Children.Add(new Border { Background = _yellow, Padding = new Thickness(10), Child = new TextBlock { Text = "Phạm vi áp dụng", FontWeight = FontWeights.SemiBold } });
            var choices = new Grid { Margin = new Thickness(8) };
            choices.ColumnDefinitions.Add(new ColumnDefinition()); choices.ColumnDefinitions.Add(new ColumnDefinition());
            choices.RowDefinitions.Add(new RowDefinition()); choices.RowDefinitions.Add(new RowDefinition());
            string[] labels = { "Đối tượng trong view hiện tại", "Toàn bộ đối tượng dự án", "Các đối tượng đã chọn", "Quét chọn vùng đối tượng" };
            for (int i = 0; i < 4; i++)
            {
                _scope[i] = new RadioButton { Content = labels[i], GroupName = "Scope", Margin = new Thickness(3, 6, 3, 6) };
                Grid.SetRow(_scope[i], i / 2); Grid.SetColumn(_scope[i], i % 2); choices.Children.Add(_scope[i]);
            }
            _scope[previous == null ? 0 : Math.Max(0, Math.Min(3, previous.Scope))].IsChecked = true;
            scopePanel.Children.Add(choices); Grid.SetRow(scopePanel, 3); root.Children.Add(scopePanel);
            var actions = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
            var unjoin = Button("Unjoin", () => Accept(1), "Gỡ Join các cặp theo quy tắc"); unjoin.Height = 42; unjoin.Background = _yellow;
            var join = Button("Join / Switch Join", () => Accept(0), "Join và bảo đảm cột bên trái là đối tượng cắt"); join.Height = 42; join.Background = _yellow;
            Add(actions, unjoin, 0); Add(actions, join, 1); Grid.SetRow(actions, 4); root.Children.Add(actions);
            if (previous != null) _rules.AddRange(previous.Rules.Select(r => new JoinRule { Priority = r.Priority, Target = r.Target }));
            else _rules.Add(new JoinRule { Priority = _categories[0].Id, Target = _categories[1].Id });
            DrawRows();
        }
        private Button Button(string text, Action action, string hint)
        {
            var button = new Button { Content = text, ToolTip = hint, Margin = new Thickness(2), Padding = new Thickness(8, 5, 8, 5), Background = Brushes.White, BorderBrush = _yellow };
            button.Click += delegate { action(); }; return button;
        }
        private static void Columns(Grid grid)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        }
        private static void Add(Grid grid, UIElement element, int column)
        { Grid.SetColumn(element, column); grid.Children.Add(element); }
        private void AddRule()
        { _rules.Add(new JoinRule { Priority = _categories[0].Id, Target = _categories[1].Id }); DrawRows(); }
        private void DrawRows()
        {
            _rows.Children.Clear();
            foreach (JoinRule rule in _rules)
            {
                var row = new Grid { Margin = new Thickness(2, 4, 2, 4) }; Columns(row);
                var first = new ComboBox { ItemsSource = _categories, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = rule.Priority, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
                var second = new ComboBox { ItemsSource = _categories, DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = rule.Target, Margin = new Thickness(2), VerticalContentAlignment = VerticalAlignment.Center };
                first.SelectionChanged += delegate { if (first.SelectedValue != null) rule.Priority = (int)first.SelectedValue; };
                second.SelectionChanged += delegate { if (second.SelectedValue != null) rule.Target = (int)second.SelectedValue; };
                Add(row, first, 0); Add(row, second, 2);
                Add(row, Button("↔", () => { int id = rule.Priority; rule.Priority = rule.Target; rule.Target = id; DrawRows(); }, "Đảo đối tượng ưu tiên và đối tượng bị cắt"), 1);
                Add(row, Button("×", () => { _rules.Remove(rule); DrawRows(); }, "Xóa quy tắc này"), 3);
                _rows.Children.Add(row);
            }
        }
        private void Accept(int action)
        {
            string error = JoinRuleCatalog.ValidateRules(_rules);
            if (error != null) { MessageBox.Show(this, error, Title); return; }
            Request = new JoinRequest { Action = action, Scope = Array.FindIndex(_scope, r => r.IsChecked == true), Rules = _rules.Select(r => new JoinRule { Priority = r.Priority, Target = r.Target }).ToList() };
            DialogResult = true;
        }
        private void Import()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Quy tắc Join (*.xml)|*.xml" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                List<JoinRule> rules;
                var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
                using (var reader = System.Xml.XmlReader.Create(dialog.FileName, settings))
                    rules = (List<JoinRule>)new XmlSerializer(typeof(List<JoinRule>)).Deserialize(reader);
                string error = JoinRuleCatalog.ValidateRules(rules);
                if (error != null) throw new InvalidOperationException(error);
                _rules.Clear(); _rules.AddRange(rules); DrawRows();
            }
            catch (Exception ex) { MessageBox.Show(this, "Không nhập được quy tắc: " + ex.Message, Title); }
        }
        private void Export()
        {
            string error = JoinRuleCatalog.ValidateRules(_rules);
            if (error != null) { MessageBox.Show(this, error, Title); return; }
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Quy tắc Join (*.xml)|*.xml", FileName = "KTool-JoinRules.xml" };
            if (dialog.ShowDialog(this) != true) return;
            try { using (var stream = File.Create(dialog.FileName)) new XmlSerializer(typeof(List<JoinRule>)).Serialize(stream, _rules); }
            catch (Exception ex) { MessageBox.Show(this, "Không xuất được quy tắc: " + ex.Message, Title); }
        }
    }
}

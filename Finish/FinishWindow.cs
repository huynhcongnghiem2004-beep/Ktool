using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SDK_HD.Dim.Finish
{
    public enum FinishKind { Floor, Ceiling, Wall, Threshold, Tile, Lintel, Plaster }
    public class FinishChoice
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public override string ToString() { return Name; }
    }
    public class FinishSource
    {
        public long Id;
        public string Name;
        public string Level;
        public bool Checked;
    }
    public class FinishSettings
    {
        public long TypeId;
        public double OffsetMm;
        public double HeightMm;
        public bool Replace;
        public double OverhangMm;
        public string WidthParam = "Width", DepthParam = "Depth";
        public double TileWidthMm = 600, TileLengthMm = 600, TileThicknessMm = 10, GroutMm = 2, AngleDegrees;
        public double GridXmm, GridYmm;
        public double BearingMm = 200;
        public List<long> Sources = new List<long>();
    }
    public class FinishWindow : Window
    {
        public static readonly string[] Names = { "Sàn hoàn thiện theo Room", "Trần hoàn thiện theo Room", "Tường hoàn thiện theo Room", "Ngạch cửa theo Family", "Viên gạch ốp lát 3D", "Tạo lanh tô", "Tạo vữa trát theo Room" };
        private readonly FinishKind _kind;
        private readonly List<FinishSource> _sources;
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBox _search = new TextBox();
        private readonly ComboBox _types = new ComboBox { DisplayMemberPath = "Name", MinHeight = 30 };
        private readonly Dictionary<string, TextBox> _numbers = new Dictionary<string, TextBox>();
        private readonly CheckBox _replace = new CheckBox { Content = "Thay thế kết quả KTool đã tạo cho nguồn", Margin = new Thickness(0, 12, 0, 8) };
        private TextBox _widthParam, _depthParam;
        public FinishSettings Settings { get; private set; }
        public FinishWindow(FinishKind kind, List<FinishChoice> types, List<FinishSource> sources)
        {
            _kind = kind; _sources = sources ?? new List<FinishSource>();
            Title = "KTool - " + Names[(int)kind]; Width = 850; Height = 710; MinWidth = 760; MinHeight = 580;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.White;
            FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
            var root = new Grid { Margin = new Thickness(14) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 202, 0)), Padding = new Thickness(12),
                Child = new TextBlock { Text = Names[(int)kind], FontSize = 17, FontWeight = FontWeights.SemiBold } };
            root.Children.Add(heading);
            var body = new Grid { Margin = new Thickness(0, 12, 0, 12) }; Grid.SetRow(body, 1); root.Children.Add(body);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(kind == FinishKind.Tile ? 0 : 1.2, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new DockPanel { Margin = new Thickness(0, 0, 14, 0), ClipToBounds = true };
            var tools = new StackPanel(); DockPanel.SetDock(tools, Dock.Top);
            tools.Children.Add(new TextBlock { Text = kind == FinishKind.Lintel ? "Chọn cửa đi / cửa sổ trong model" : kind == FinishKind.Threshold ? "Chọn cửa đi trong model" : "Chọn Room đã đặt và có diện tích", FontWeight = FontWeights.SemiBold });
            _search.Margin = new Thickness(0, 8, 0, 6); _search.MinHeight = 28; _search.ToolTip = "Tìm theo tên, số, Level hoặc Phase";
            _search.TextChanged += delegate { DrawSources(); }; tools.Children.Add(_search);
            var select = new WrapPanel();
            select.Children.Add(MakeButton("Chọn kết quả lọc", () => { foreach (var item in Filtered()) item.Checked = true; DrawSources(); }));
            select.Children.Add(MakeButton("Bỏ chọn tất cả", () => { foreach (var item in _sources) item.Checked = false; DrawSources(); }));
            tools.Children.Add(select); left.Children.Add(tools);
            left.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); body.Children.Add(left);
            var options = new StackPanel { Margin = new Thickness(4) };
            var optionsScroll = new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(optionsScroll, 1); body.Children.Add(optionsScroll);
            options.Children.Add(Label(kind == FinishKind.Tile ? "Vật liệu viên gạch" : kind == FinishKind.Lintel ? "Family / Type dầm lanh tô (Structural Framing)" : kind == FinishKind.Plaster ? "Wall Type vữa trát (Basic Wall)" : kind == FinishKind.Threshold ? "Family / Type ngạch cửa" : "Loại cấu kiện hoàn thiện"));
            _types.ItemsSource = types; _types.SelectedIndex = types.Count > 0 ? 0 : -1; options.Children.Add(_types);
            if (kind != FinishKind.Tile)
                Number(options, "Offset", kind == FinishKind.Floor ? "Cao độ mặt trên so với đáy Room (mm)" : kind == FinishKind.Ceiling ? "Bù cao độ trần (mm)" : (kind == FinishKind.Wall || kind == FinishKind.Plaster) ? "Cao độ chân tường so với đáy Room (mm)" : kind == FinishKind.Lintel ? "Khe hở đáy lanh tô trên đỉnh lỗ cửa (mm)" : "Bù cao độ điểm đặt so với chân cửa (mm)", "0");
            if (kind == FinishKind.Ceiling || kind == FinishKind.Wall || kind == FinishKind.Plaster)
                Number(options, "Height", (kind == FinishKind.Wall || kind == FinishKind.Plaster) ? "Chiều cao tường (mm; 0 = lên đỉnh Room)" : "Chiều cao trần từ đáy Room (mm; 0 = theo Room)", "0");
            if (kind == FinishKind.Wall)
                options.Children.Add(Note("Tường Basic Wall đặt phía trong Room. Trừ lỗ cửa đi/cửa sổ theo biên cắt thực của Family trên tường chủ. Đoạn không xử lý được sẽ được báo trong log."));
            if (kind == FinishKind.Lintel)
            {
                Number(options, "Bearing", "Chiều dài gác mỗi đầu (mm)", "200");
                options.Children.Add(Note("Đặt dầm ngang phía trên lỗ cửa thực của cửa đi/cửa sổ. Đáy dầm được căn trên đỉnh lỗ cửa theo khe hở đã nhập. Tiết diện lấy từ Family Type đã chọn; hai đầu dầm không tự Join để giữ chiều dài gác."));
            }
            if (kind == FinishKind.Plaster)
                options.Children.Add(Note("Tạo lớp vữa bằng Wall Type riêng theo mặt trong Room, giữ lỗ cửa đi/cửa sổ của tường chủ. Bề dày và vật liệu lấy từ Wall Type vữa đã chọn. Kết quả vữa trát được quản lý độc lập với tường hoàn thiện; tránh chạy cả hai lớp trùng vị trí."));
            if (kind == FinishKind.Threshold)
            {
                Number(options, "Overhang", "Nhô khỏi mỗi mặt tường (mm)", "0");
                _widthParam = Text(options, "Tên parameter bề rộng cửa (instance hoặc type)", "Width");
                _depthParam = Text(options, "Tên parameter chiều sâu ngạch (instance hoặc type)", "Depth");
                options.Children.Add(Note("Hỗ trợ Family theo Level, theo đường, wall-host và face/floor-host. Family cần sàn host sẽ tìm mặt sàn tại chân cửa. Bề rộng lấy từ lỗ cắt cửa; chiều sâu bằng dày tường cộng phần nhô. Parameter Type dùng trên Type sao chép riêng, không đổi Type gốc. Trục X Family là bề rộng."));
            }
            if (kind == FinishKind.Tile)
            {
                Number(options, "TileWidth", "Rộng viên gạch (mm)", "600"); Number(options, "TileLength", "Dài / cao viên gạch (mm)", "600");
                Number(options, "Thickness", "Dày viên gạch (mm)", "10"); Number(options, "Grout", "Rộng khe ron (mm)", "2");
                Number(options, "Angle", "Góc xoay lưới gạch (độ)", "0");
                Number(options, "GridX", "Dịch lưới ngang (mm)", "0"); Number(options, "GridY", "Dịch lưới dọc (mm)", "0");
                options.Children.Add(Note("Sau khi bấm Tạo, chọn một mặt phẳng của sàn hoặc tường trong model hiện tại. Từng viên là Generic Model 3D có vật liệu; viên biên và lỗ rỗng được cắt theo mặt đã chọn. Chiều dày đi theo pháp tuyến ra ngoài mặt."));
            }
            options.Children.Add(_replace);
            options.Children.Add(Note("Nếu không chọn Thay thế, nguồn đã có kết quả KTool sẽ được bỏ qua để tránh tạo trùng. Chỉ các kết quả do công cụ này tạo mới được thay thế."));
            if (kind == FinishKind.Floor || kind == FinishKind.Ceiling)
                options.Children.Add(Note("Giữ các vòng biên Room, kể cả lỗ cột và giếng. Không tự mở rộng vào hốc cửa; phần ngạch cửa tạo bằng lệnh riêng."));
            var bottom = new DockPanel(); Grid.SetRow(bottom, 2); root.Children.Add(bottom);
            var create = MakeButton(kind == FinishKind.Tile ? "Chọn mặt và tạo gạch" : kind == FinishKind.Lintel ? "Tạo lanh tô" : kind == FinishKind.Plaster ? "Tạo vữa trát" : "Tạo hoàn thiện", Accept); create.Background = new SolidColorBrush(Color.FromRgb(255, 202, 0)); create.Height = 38;
            DockPanel.SetDock(create, Dock.Right); bottom.Children.Add(create);
            var cancel = MakeButton("Đóng", () => Close()); cancel.Height = 38; DockPanel.SetDock(cancel, Dock.Right); bottom.Children.Add(cancel);
            bottom.Children.Add(new TextBlock { Text = "Đơn vị nhập: mm • Có thể Undo toàn bộ lượt tạo", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            DrawSources();
        }
        private IEnumerable<FinishSource> Filtered()
        { string q = _search.Text.Trim(); return _sources.Where(s => (s.Name + " " + s.Level).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0); }
        private void DrawSources()
        {
            _list.Children.Clear();
            foreach (var item in Filtered())
            {
                var check = new CheckBox { Content = item.Name + "\n" + item.Level, IsChecked = item.Checked, Margin = new Thickness(3, 6, 3, 6) };
                check.Checked += delegate { item.Checked = true; }; check.Unchecked += delegate { item.Checked = false; }; _list.Children.Add(check);
            }
        }
        private static TextBlock Label(string text) { return new TextBlock { Text = text, Margin = new Thickness(0, 9, 0, 4), TextWrapping = TextWrapping.Wrap }; }
        private static TextBlock Note(string text) { return new TextBlock { Text = text, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 2), FontSize = 11 }; }
        private TextBox Text(StackPanel panel, string label, string value)
        { panel.Children.Add(Label(label)); var input = new TextBox { Text = value, MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center }; panel.Children.Add(input); return input; }
        private void Number(StackPanel panel, string key, string label, string value) { _numbers[key] = Text(panel, label, value); }
        private static Button MakeButton(string label, Action action)
        { var b = new Button { Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(2), BorderBrush = Brushes.Goldenrod, Background = Brushes.White }; b.Click += delegate { action(); }; return b; }
        private double Value(string key)
        {
            if (!_numbers.ContainsKey(key)) return 0;
            string text = _numbers[key].Text.Trim().Replace(',', '.');
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException("Thông số không hợp lệ: " + _numbers[key].Text);
            return value;
        }
        private void Accept()
        {
            try
            {
                var type = _types.SelectedItem as FinishChoice;
                if (type == null) throw new InvalidOperationException("Chưa có loại cấu kiện hoặc vật liệu phù hợp trong dự án.");
                Settings = new FinishSettings { TypeId = type.Id, OffsetMm = Value("Offset"), HeightMm = Value("Height"), Replace = _replace.IsChecked == true,
                    OverhangMm = Value("Overhang"), BearingMm = Value("Bearing"), WidthParam = _widthParam == null ? "Width" : _widthParam.Text.Trim(), DepthParam = _depthParam == null ? "Depth" : _depthParam.Text.Trim(),
                    Sources = _sources.Where(s => s.Checked).Select(s => s.Id).ToList() };
                if (_kind != FinishKind.Tile && Settings.Sources.Count == 0) throw new InvalidOperationException("Hãy chọn ít nhất một " + ((_kind == FinishKind.Threshold || _kind == FinishKind.Lintel) ? "cửa." : "Room."));
                if (Settings.HeightMm < 0 || Settings.OverhangMm < 0 || Settings.BearingMm < 0) throw new InvalidOperationException("Chiều cao, phần nhô và chiều dài gác không được âm.");
                if (_kind == FinishKind.Lintel && Settings.OffsetMm < 0) throw new InvalidOperationException("Khe hở lanh tô không được âm để tránh đặt dầm che lỗ cửa.");
                if (_kind == FinishKind.Threshold && (Settings.WidthParam.Length == 0 || Settings.DepthParam.Length == 0 || Settings.WidthParam == Settings.DepthParam))
                    throw new InvalidOperationException("Nhập hai parameter kích thước khác nhau cho rộng và sâu.");
                if (_kind == FinishKind.Tile)
                {
                    Settings.TileWidthMm = Value("TileWidth"); Settings.TileLengthMm = Value("TileLength"); Settings.TileThicknessMm = Value("Thickness"); Settings.GroutMm = Value("Grout");
                    Settings.AngleDegrees = Value("Angle"); Settings.GridXmm = Value("GridX"); Settings.GridYmm = Value("GridY");
                    if (Settings.TileWidthMm < 10 || Settings.TileLengthMm < 10 || Settings.TileThicknessMm < 1 || Settings.GroutMm < 0)
                        throw new InvalidOperationException("Gạch phải rộng/dài tối thiểu 10 mm, dày tối thiểu 1 mm và khe ron không âm.");
                }
                DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }
        public static ImageSource CreateToolIcon(int size, bool lintel)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                if (lintel)
                {
                    dc.DrawRectangle(Brushes.Goldenrod, new Pen(Brushes.DimGray, 1), new Rect(1, 4, 30, 7));
                    dc.DrawRectangle(Brushes.SlateGray, null, new Rect(4, 12, 6, 18));
                    dc.DrawRectangle(Brushes.SlateGray, null, new Rect(22, 12, 6, 18));
                }
                else
                {
                    dc.DrawRectangle(Brushes.SlateGray, null, new Rect(3, 3, 22, 27));
                    dc.DrawRectangle(Brushes.Gold, null, new Rect(5, 4, 5, 25));
                    dc.DrawLine(new Pen(Brushes.White, 3), new Point(14, 8), new Point(22, 8));
                    dc.DrawLine(new Pen(Brushes.White, 3), new Point(14, 15), new Point(22, 15));
                    dc.DrawLine(new Pen(Brushes.White, 3), new Point(14, 22), new Point(22, 22));
                }
            }
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96.0 * size / 32, 96.0 * size / 32, PixelFormats.Pbgra32);
            image.Render(visual); image.Freeze(); return image;
        }

        public static ImageSource CreateRibbonIcon(int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Goldenrod, null, new Rect(2, 2, 28, 28));
                for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++)
                    dc.DrawRectangle(x == y ? Brushes.White : Brushes.SlateGray, null, new Rect(4 + x * 9, 4 + y * 9, 7, 7));
            }
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96.0 * size / 32, 96.0 * size / 32, PixelFormats.Pbgra32);
            image.Render(visual); image.Freeze(); return image;
        }
    }
}

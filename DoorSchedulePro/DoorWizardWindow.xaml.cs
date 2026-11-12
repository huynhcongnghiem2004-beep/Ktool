using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

// File này cố ý KHÔNG dùng kiểu của Revit API: Autodesk.Revit.DB.Grid trùng System.Windows.Controls.Grid.
// Mọi việc liên quan Revit nằm trong DoorDetailLegendCommand, cửa sổ chỉ nhận dữ liệu và hai callback.
namespace DoorDetailLegend
{
    /// <summary>Một cửa trong danh sách: dữ liệu thuần, do lệnh điền từ model.</summary>
    public class DoorRow
    {
        public long Id;          // ElementId của cửa
        public long SymbolId;    // ElementId của Family Type
        public string Mark;
        public string TypeName;  // "Family : Type"
        public string Level;
        public string Size;      // "rộng x cao" (mm), có thể rỗng
        public bool IsWindow;
        public bool Preselected; // đang được chọn sẵn trong Revit
    }

    /// <summary>Một parameter đã chọn: Name là tên trên cửa, Label là cột "Việt hóa" (tên parameter đích trên khung T89).</summary>
    public class ParamMap
    {
        public string Name;
        public string Label;
    }

    /// <summary>Yêu cầu tạo, gửi từ cửa sổ sang lệnh khi bấm "Bắt đầu tạo".</summary>
    public class WizardRequest
    {
        public List<long> DoorIds;
        public List<ParamMap> Params;
        public int FrameIndex;          // -1: giữ nguyên loại khung T89 của Legend nguồn
        public string NamePrefix;       // rỗng: dùng tiền tố mặc định của lệnh
        public bool CreateDimensions;
        public bool WriteParameters;

        // Tab "Thiết lập triển khai"
        public long UpperTagId = -1;        // Tag trên: Type Generic Annotation thay cho khung T89 trên cùng, -1 = giữ nguyên
        public long LowerTagId = -1;        // Tag dưới: thay cho khung T89 dưới cùng (hoặc đặt mới), -1 = không dùng
        public List<TagField> UpperFields;  // 3 dòng của Tag trên (tiền tố + parameter)
        public bool ElevationBack;          // false: Elevation: Front, true: Elevation: Back
        public bool CreatePlan = true;      // Mặt bằng
        public bool CreateFrame;            // Tạo khung
        public bool PlaceOnSheet;
        public long TargetSheetId = -1;
        public long TitleBlockId = -1;
        public string SheetPrefix = "CT-CUA", SheetName = "CHI TIẾT CỬA";
        public bool OverflowSheets = true;
        public bool AutoFitSheet = true;
        public double MarginMm = 20, RightReserveMm = 50, GapMm = 10;
        public int ViewScale = 20;
        public long DimTypeId = -1;         // loại Dim, -1 nếu không chọn / tắt Dim
    }

    /// <summary>Một dòng của Tag trên: tiền tố + tên parameter (null nếu không dùng, "Count" = số lượng cửa cùng Type).</summary>
    public class TagField
    {
        public string Prefix;
        public string Param;
        public string Target;
    }

    /// <summary>Một lựa chọn trong ComboBox: tên hiển thị và ElementId.</summary>
    public class ChoiceItem
    {
        public string Name { get; set; }
        public long Id { get; set; }
        public System.Windows.Media.ImageSource PreviewImage { get; set; }
        public override string ToString() { return Name; }
    }

    /// <summary>Dữ liệu lệnh cung cấp để điền các ComboBox ở tab "Thiết lập triển khai".</summary>
    public class SetupOptions
    {
        public List<ChoiceItem> TagTypes = new List<ChoiceItem>();
        public List<ChoiceItem> DimTypes = new List<ChoiceItem>();
        public List<ChoiceItem> Sheets = new List<ChoiceItem>();
        public List<ChoiceItem> TitleBlocks = new List<ChoiceItem>();
        public long DefaultTitleBlockId = -1;
        public long DefaultSheetId = -1;
        public long DefaultUpperTagId = -1;
        public long DefaultLowerTagId = -1;
        public long DefaultDimTypeId = -1;
    }

    /// <summary>Cầu nối để lệnh báo tiến độ, ghi log và kiểm tra nút Dừng.</summary>
    public class WizardSink
    {
        private readonly DoorWizardWindow _window;
        public WizardSink(DoorWizardWindow window) { _window = window; }
        public bool StopRequested { get { return _window.StopRequested; } }
        public void Report(int done, int total) { _window.ReportProgress(done, total); }
        public void Log(string line) { _window.AppendLog(line); }
        public bool Confirm(string text) { return _window.Confirm(text); }
    }

    public class DoorItem : INotifyPropertyChanged
    {
        private bool _checked;
        private readonly Action _changed;

        public DoorItem(DoorRow row, Action changed)
        {
            Row = row;
            _checked = row.Preselected;
            _changed = changed;
        }

        public DoorRow Row { get; private set; }
        public string Mark { get { return Row.Mark; } }
        public string TypeName { get { return Row.TypeName; } }
        public string Level { get { return Row.Level; } }
        public string Size { get { return Row.Size; } }
        public string Kind { get { return Row.IsWindow ? "Cửa sổ" : "Cửa đi"; } }

        public bool IsChecked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                var h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs("IsChecked"));
                if (_changed != null) _changed();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class ParamItem : INotifyPropertyChanged
    {
        private string _label;

        public ParamItem(string name) { Name = name; _label = name; }

        public string Name { get; private set; }

        public string Label
        {
            get { return _label; }
            set
            {
                if (_label == value) return;
                _label = value;
                var h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs("Label"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class DoorWizardWindow : Window
    {
        private readonly ObservableCollection<DoorItem> _doors;
        private readonly ICollectionView _doorView;
        private readonly ObservableCollection<string> _leftParams = new ObservableCollection<string>();
        private readonly ICollectionView _leftView;
        private readonly ObservableCollection<ParamItem> _chosen = new ObservableCollection<ParamItem>();
        private string _loadedSignature;
        private bool _running;
        private bool _bulk; // true khi đang tích/bỏ tích hàng loạt: chỉ cập nhật tóm tắt một lần ở cuối

        private const string NoneText = "(không dùng)";
        private const string CountText = "Count"; // giá trị đặc biệt: số cửa cùng Family Type
        private List<string> _allParamNames = new List<string>();
        private bool _tagParamDefaulted;

        /// <summary>Lệnh cung cấp: trả danh sách tên parameter của các cửa đã chọn.</summary>
        public Func<List<long>, List<string>> ParameterProvider;
        public Func<List<long>, string, string> PreviewValueProvider;
        public Func<long, System.Windows.Media.ImageSource> TagImageProvider;
        public Func<long, List<string>> TagParameterProvider;
        private bool _loadingTagImage;

        /// <summary>Lệnh cung cấp: thực hiện tạo Legend, báo tiến độ qua WizardSink.</summary>
        public Action<WizardRequest, WizardSink> Runner;

        public bool StopRequested { get; private set; }

        public DoorWizardWindow(List<DoorRow> rows, SetupOptions setup)
        {
            InitializeComponent();

            _doors = new ObservableCollection<DoorItem>(rows.Select(r => new DoorItem(r, OnDoorCheckChanged)));
            _doorView = CollectionViewSource.GetDefaultView(_doors);
            _doorView.Filter = DoorFilter;
            lvDoors.ItemsSource = _doorView;

            _leftView = CollectionViewSource.GetDefaultView(_leftParams);
            _leftView.Filter = ParamFilter;
            lbParams.ItemsSource = _leftView;

            dgParams.ItemsSource = _chosen;
            _chosen.CollectionChanged += delegate { UpdateSummary(); UpdatePreview(); };
            foreach (ComboBox box in new[] { cboUpperTag, cboLowerTag, cboParam1, cboParam2, cboParam3, cboDimType })
                box.SelectionChanged += delegate { UpdatePreview(); };
            foreach (TextBox box in new[] { txtPre1, txtPre2, txtPre3 })
                box.TextChanged += delegate { UpdatePreview(); };

            if (setup == null) setup = new SetupOptions();
            FillChoice(cboUpperTag, setup.TagTypes, setup.DefaultUpperTagId, true);
            FillChoice(cboLowerTag, setup.TagTypes, setup.DefaultLowerTagId, true);
            FillChoice(cboDimType, setup.DimTypes, setup.DefaultDimTypeId, false);
            FillTagParamCombos();
            FillChoice(cboSheet, setup.Sheets, setup.DefaultSheetId, false);
            FillChoice(cboTitleBlock, setup.TitleBlocks, setup.DefaultTitleBlockId, false);

            cboUpperTag.SelectionChanged += delegate { RefreshTagTargets(); };
            Loaded += delegate { RefreshTagTargets(); UpdatePreview(); };
            Closing += OnClosing;
            rbDoors.IsChecked = true;
            ShowLog(false);
            UpdatePreview();
            UpdateSummary();
        }

        // ---------------------------------------------------------------- Điều hướng bước

        private void OnStepChecked(object sender, RoutedEventArgs e)
        {
            if (panelDoors == null || panelParams == null || panelSetup == null) return;
            panelDoors.Visibility = rbDoors.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            panelParams.Visibility = rbParams.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            panelSetup.Visibility = rbSetup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (rbParams.IsChecked == true || rbSetup.IsChecked == true) RefreshParameters();
        }

        private void OnGear(object sender, RoutedEventArgs e)
        {
            rbSetup.IsChecked = true;
        }

        // ---------------------------------------------------------------- Bước 1: cửa

        private bool DoorFilter(object o)
        {
            DoorItem d = (DoorItem)o;
            if (chkDoor != null && chkWindow != null)
            {
                if (d.Row.IsWindow && chkWindow.IsChecked != true) return false;
                if (!d.Row.IsWindow && chkDoor.IsChecked != true) return false;
            }

            string q = txtSearchDoor == null ? string.Empty : (txtSearchDoor.Text ?? string.Empty).Trim().ToLowerInvariant();
            if (q.Length == 0) return true;
            string hay = (d.Mark + " " + d.TypeName + " " + d.Level + " " + d.Size + " " + d.Kind).ToLowerInvariant();
            return hay.Contains(q);
        }

        private void OnDoorSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            hintDoor.Visibility = txtSearchDoor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _doorView.Refresh();
        }

        private void OnDoorFilterChanged(object sender, RoutedEventArgs e)
        {
            if (_doorView != null) _doorView.Refresh();
        }

        private void OnDoorCheckChanged()
        {
            if (!_bulk) UpdateSummary();
        }

        private void OnDoorDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Chỉ đổi trạng thái khi bấm đúng vào một dòng; bỏ qua ô tích (đã tự đổi) và tiêu đề cột.
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && !(node is System.Windows.Controls.ListViewItem))
            {
                if (node is System.Windows.Controls.CheckBox) return;
                node = ParentOf(node);
            }
            System.Windows.Controls.ListViewItem row = node as System.Windows.Controls.ListViewItem;
            DoorItem d = row == null ? null : row.Content as DoorItem;
            if (d != null) d.IsChecked = !d.IsChecked;
        }

        private static DependencyObject ParentOf(DependencyObject o)
        {
            if (o is System.Windows.Media.Visual || o is System.Windows.Media.Media3D.Visual3D)
                return System.Windows.Media.VisualTreeHelper.GetParent(o);
            return LogicalTreeHelper.GetParent(o);
        }

        private void SetAllDoors(IEnumerable<DoorItem> items, bool value)
        {
            _bulk = true;
            try { foreach (DoorItem d in items.ToList()) d.IsChecked = value; }
            finally { _bulk = false; }
            UpdateSummary();
        }

        private void OnSelectAllDoors(object sender, RoutedEventArgs e)
        {
            SetAllDoors(_doorView.Cast<DoorItem>(), true);
        }

        private void OnClearDoors(object sender, RoutedEventArgs e)
        {
            SetAllDoors(_doors, false);
        }

        // ---------------------------------------------------------------- Bước 2: parameters

        private List<long> CheckedDoorIds()
        {
            return _doors.Where(d => d.IsChecked).Select(d => d.Row.Id).ToList();
        }

        /// <summary>Nạp lại danh sách parameter khi tập cửa đã chọn thay đổi từ lần nạp trước.</summary>
        private void RefreshParameters()
        {
            List<long> ids = CheckedDoorIds();
            string signature = string.Join(",", ids.OrderBy(i => i));
            if (signature == _loadedSignature || ParameterProvider == null) return;

            List<string> names;
            try { names = ParameterProvider(ids); }
            catch (Exception ex)
            {
                AppendLog("Không đọc được danh sách parameter: " + ex.Message);
                return;
            }

            HashSet<string> chosenNames = new HashSet<string>(_chosen.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            _leftParams.Clear();
            foreach (string n in names)
                if (!chosenNames.Contains(n)) _leftParams.Add(n);
            _allParamNames = names;
            FillTagParamCombos();
            _loadedSignature = signature;
        }

        private bool ParamFilter(object o)
        {
            string q = txtSearchParam == null ? string.Empty : (txtSearchParam.Text ?? string.Empty).Trim();
            if (q.Length == 0) return true;
            return ((string)o).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void OnParamSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            hintParam.Visibility = txtSearchParam.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _leftView.Refresh();
        }

        private void OnParamDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AddSelectedParams();
        }

        private void OnAddParams(object sender, RoutedEventArgs e)
        {
            AddSelectedParams();
        }

        private void AddSelectedParams()
        {
            foreach (string n in lbParams.SelectedItems.Cast<string>().ToList())
            {
                _leftParams.Remove(n);
                _chosen.Add(new ParamItem(n));
                _chosen.Last().PropertyChanged += delegate { UpdatePreview(); };
            }
        }

        private void OnRemoveParams(object sender, RoutedEventArgs e)
        {
            foreach (ParamItem p in dgParams.SelectedItems.Cast<ParamItem>().ToList())
            {
                _chosen.Remove(p);
                int index = 0;
                while (index < _leftParams.Count &&
                       string.Compare(_leftParams[index], p.Name, StringComparison.CurrentCultureIgnoreCase) < 0)
                    index++;
                _leftParams.Insert(index, p.Name);
            }
        }

        private void OnMoveUp(object sender, RoutedEventArgs e)
        {
            List<ParamItem> sel = dgParams.SelectedItems.Cast<ParamItem>().OrderBy(p => _chosen.IndexOf(p)).ToList();
            HashSet<ParamItem> set = new HashSet<ParamItem>(sel);
            foreach (ParamItem p in sel)
            {
                int i = _chosen.IndexOf(p);
                if (i > 0 && !set.Contains(_chosen[i - 1])) _chosen.Move(i, i - 1);
            }
        }

        private void OnMoveDown(object sender, RoutedEventArgs e)
        {
            List<ParamItem> sel = dgParams.SelectedItems.Cast<ParamItem>().OrderByDescending(p => _chosen.IndexOf(p)).ToList();
            HashSet<ParamItem> set = new HashSet<ParamItem>(sel);
            foreach (ParamItem p in sel)
            {
                int i = _chosen.IndexOf(p);
                if (i >= 0 && i < _chosen.Count - 1 && !set.Contains(_chosen[i + 1])) _chosen.Move(i, i + 1);
            }
        }

        // ---------------------------------------------------------------- Bước 3: thiết lập

        /// <summary>Điền ComboBox. allowNone: thêm mục "(không dùng)" (Id = -1) lên đầu và chọn nó khi không có mặc định.</summary>
        private static void FillChoice(ComboBox box, List<ChoiceItem> items, long defaultId, bool allowNone)
        {
            box.Items.Clear();
            if (allowNone) box.Items.Add(new ChoiceItem { Name = NoneText, Id = -1 });

            if (items == null || items.Count == 0)
            {
                if (!allowNone)
                {
                    box.Items.Add(new ChoiceItem { Name = "(không tìm thấy loại nào)", Id = -1 });
                    box.IsEnabled = false;
                }
                box.SelectedIndex = 0;
                return;
            }

            foreach (ChoiceItem c in items) box.Items.Add(c);
            int idx = box.Items.Cast<ChoiceItem>().ToList().FindIndex(c => c.Id == defaultId);
            box.SelectedIndex = idx >= 0 ? idx : 0;
        }

        /// <summary>Điền lại 3 ô chọn Parameter của Tag trên, giữ lựa chọn cũ nếu còn tồn tại.</summary>
        private void FillTagParamCombos()
        {
            ComboBox[] boxes = { cboParam1, cboParam2, cboParam3 };
            bool hasCount = _allParamNames.Any(n => string.Equals(n, CountText, StringComparison.OrdinalIgnoreCase));

            foreach (ComboBox b in boxes)
            {
                string keep = b.SelectedItem as string;
                b.Items.Clear();
                b.Items.Add(NoneText);
                foreach (string n in _allParamNames) b.Items.Add(n);
                if (!hasCount) b.Items.Add(CountText);
                int idx = keep == null ? -1 : b.Items.IndexOf(keep);
                b.SelectedIndex = idx >= 0 ? idx : 0;
            }

            // Lần đầu có danh sách parameter: dòng 1 mặc định là Type Mark (hoặc Mark).
            if (!_tagParamDefaulted && _allParamNames.Count > 0)
            {
                _tagParamDefaulted = true;
                int m = cboParam1.Items.IndexOf("Type Mark");
                if (m < 0) m = cboParam1.Items.IndexOf("Mark");
                if (m >= 0) cboParam1.SelectedIndex = m;
            }
        }

        private void RefreshTagTargets()
        {
            ChoiceItem tag = cboUpperTag.SelectedItem as ChoiceItem;
            var names = tag != null && tag.Id > 0 && TagParameterProvider != null
                ? TagParameterProvider(tag.Id) : new List<string>();
            ComboBox[] boxes = { cboTarget1, cboTarget2, cboTarget3 };
            for (int i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                box.ItemsSource = names;
                string[] aliases = { "Parameter " + (i+1), "Parameter" + (i+1),
                    "Text " + (i+1), "Text" + (i+1), "Label " + (i+1),
                    i == 0 ? "Type Mark" : i == 1 ? "Description" : "Count" };
                box.SelectedItem = aliases.Select(a => names.FirstOrDefault(n =>
                    string.Equals(a, n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(n => n != null);
                if (box.SelectedItem == null) box.Text = string.Empty;
            }
        }

        private List<TagField> ReadUpperFields()
        {
            TextBox[] pre = { txtPre1, txtPre2, txtPre3 };
            ComboBox[] par = { cboParam1, cboParam2, cboParam3 };
            ComboBox[] targets = { cboTarget1, cboTarget2, cboTarget3 };
            List<TagField> list = new List<TagField>();
            for (int i = 0; i < 3; i++)
            {
                string p = par[i].SelectedItem as string;
                list.Add(new TagField
                {
                    Prefix = pre[i].Text ?? string.Empty,
                    Target = targets[i].Text,
                    Param = (p == null || p == NoneText) ? null : p
                });
            }
            return list;
        }

        private void OnPreviewChanged(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        /// <summary>Hình minh họa phản ánh các tùy chọn: mặt bằng, kích thước, khung, hướng mặt đứng.</summary>
        private void UpdatePreview()
        {
            if (cvPlan == null || dimPlan == null || dimElev == null || cvElev == null || rectFrame == null ||
                chkPlan == null || chkDims == null || chkFrame == null || rbBack == null) return;

            bool dims = chkDims.IsChecked == true;
            cvPlan.Visibility = chkPlan.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            dimPlan.Visibility = dims ? Visibility.Visible : Visibility.Collapsed;
            dimElev.Visibility = dims ? Visibility.Visible : Visibility.Collapsed;
            rectFrame.Visibility = chkFrame.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            System.Windows.Media.ScaleTransform flip = cvElev.RenderTransform as System.Windows.Media.ScaleTransform;
            if (flip != null) flip.ScaleX = rbBack.IsChecked == true ? -1 : 1;
            if (_doors == null || previewUpper == null || previewLower == null) return;
            DoorItem sample = _doors.FirstOrDefault(d => d.IsChecked);
            if (sample != null && previewWidth != null && previewHeight != null)
            {
                string[] size = (sample.Size ?? "").Replace("×", "x").Split('x');
                previewWidth.Text = size.Length == 2 ? size[0].Trim() : "Rộng";
                previewHeight.Text = size.Length == 2 ? size[1].Trim() : "Cao";
            }
            UpdateTagPreview(cboUpperTag, previewUpper, previewUpperName, previewUpperImage);
            UpdateTagPreview(cboLowerTag, previewLower, previewLowerName, previewLowerImage);
            // Không vẽ bảng giả đè lên hình Family đã xuất từ Revit.
            previewUpperFields.Visibility = Visibility.Visible;
            previewLowerFields.Visibility = Visibility.Collapsed;
            TextBlock[] fields = { previewField1, previewField2, previewField3 };
            List<TagField> settings = ReadUpperFields();
            previewUpperFields.Visibility = settings.Any(f => f.Param != null || !string.IsNullOrWhiteSpace(f.Prefix))
                ? Visibility.Visible : Visibility.Collapsed;
            for (int i = 0; i < fields.Length; i++)
            {
                TagField field = settings[i];
                fields[i].Text = field.Prefix + (field.Param == null ? string.Empty : PreviewValue(field.Param));
                fields[i].ToolTip = fields[i].Text;
            }
            previewLowerRows.Children.Clear();
            if (_chosen.Count == 0)
                previewLowerRows.Children.Add(new TextBlock { Text = "Chọn parameter ở bước 2", FontSize = 10, Margin = new Thickness(4) });
            foreach (ParamItem item in _chosen)
            {
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                TextBlock label = new TextBlock { Text = item.Label, FontSize = 10, Margin = new Thickness(3, 1, 3, 1), TextTrimming = TextTrimming.CharacterEllipsis };
                TextBlock value = new TextBlock { Text = PreviewValue(item.Name), FontSize = 10, Margin = new Thickness(3, 1, 3, 1), TextTrimming = TextTrimming.CharacterEllipsis };
                label.ToolTip = label.Text; value.ToolTip = value.Text;
                Grid.SetColumn(value, 1); row.Children.Add(label); row.Children.Add(value);
                previewLowerRows.Children.Add(row);
            }
        }

        private string PreviewValue(string name)
        {
            List<long> ids = CheckedDoorIds();
            if (ids.Count == 0) return "[" + name + "]";
            try
            {
                string value = PreviewValueProvider == null ? null : PreviewValueProvider(ids, name);
                return string.IsNullOrEmpty(value) ? "[" + name + "]" : value;
            }
            catch { return "[" + name + "]"; }
        }

        private void UpdateTagPreview(ComboBox box, StackPanel host, TextBlock name, Image image)
        {
            ChoiceItem choice = box.SelectedItem as ChoiceItem;
            host.Visibility = choice != null && choice.Id > 0 ? Visibility.Visible : Visibility.Collapsed;
            name.Text = choice == null ? string.Empty : choice.Name;
            name.ToolTip = name.Text;
            string error = null;
            if (choice != null && choice.Id > 0 && choice.PreviewImage == null && TagImageProvider != null && !_loadingTagImage)
            {
                _loadingTagImage = true;
                try { choice.PreviewImage = TagImageProvider(choice.Id); }
                catch (Exception ex) { error = ex.Message; }
                finally { _loadingTagImage = false; }
            }
            // Luôn gán lại Source: chọn loại khác không được giữ hình cũ.
            image.Source = choice == null ? null : choice.PreviewImage;
            image.Visibility = image.Source == null ? Visibility.Collapsed : Visibility.Visible;
            if (choice != null && choice.Id > 0 && image.Source == null)
                name.Text = "Chưa xuất được hình Tag • " + (error ?? "Đang tải");
            name.ToolTip = choice == null ? null : choice.Name + (error == null ? "" : "\n" + error);
            host.ToolTip = "Hình instance thật của Family Tag đã chọn, do Revit xuất. Giá trị nhãn là giá trị mặc định của Family.";
            if (choice != null && choice.Id > 0 && image.Source != null)
                image.ToolTip = choice.Name;
        }

        /// <summary>Khung bên trái hiện hình minh họa hoặc log (log tự hiện khi bắt đầu tạo).</summary>
        private void ShowLog(bool show)
        {
            txtLog.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            previewHost.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            btnLog.Content = show ? "Minh họa" : "Log";
        }

        private void OnToggleLog(object sender, RoutedEventArgs e)
        {
            ShowLog(txtLog.Visibility != Visibility.Visible);
        }

        // ---------------------------------------------------------------- Chạy

        private static bool ReadMillimeters(string text, out double value)
        {
            return double.TryParse((text ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) && value >= 0 &&
                !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private void OnStart(object sender, RoutedEventArgs e)
        {
            if (_running || Runner == null) return;

            List<long> ids = CheckedDoorIds();
            if (ids.Count == 0)
            {
                MessageBox.Show(this, "Hãy tích chọn ít nhất một cửa ở bước 'Chọn cửa triển khai'.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ChoiceItem upper = cboUpperTag.SelectedItem as ChoiceItem;
            ChoiceItem lower = cboLowerTag.SelectedItem as ChoiceItem;
            ChoiceItem dim = cboDimType.SelectedItem as ChoiceItem;
            bool dims = chkDims.IsChecked == true;

            int scale;
            double margin, reserve, gap;
            if (!int.TryParse(txtViewScale.Text, out scale) || scale < 1 || scale > 500 ||
                !ReadMillimeters(txtSheetMargin.Text, out margin) || !ReadMillimeters(txtSheetReserve.Text, out reserve) ||
                !ReadMillimeters(txtSheetGap.Text, out gap))
            { MessageBox.Show(this,"Tỷ lệ từ 1–500; lề, vùng khung tên và khoảng cách là số mm không âm.",Title); return; }
            var targetSheet = cboSheet.SelectedItem as ChoiceItem;
            var titleBlock = cboTitleBlock.SelectedItem as ChoiceItem;
            if(chkPlaceSheet.IsChecked==true && (targetSheet==null || targetSheet.Id<=0) && (titleBlock==null || titleBlock.Id<=0))
            { MessageBox.Show(this,"Hãy chọn loại khung tên để tạo Sheet.",Title); return; }

            WizardRequest req = new WizardRequest
            {
                DoorIds = ids,
                PlaceOnSheet = chkPlaceSheet.IsChecked == true,
                TargetSheetId = targetSheet == null ? -1 : targetSheet.Id,
                TitleBlockId = titleBlock == null ? -1 : titleBlock.Id,
                SheetPrefix = txtSheetPrefix.Text, SheetName = txtSheetName.Text,
                MarginMm = margin, RightReserveMm = reserve, GapMm = gap,
                ViewScale = scale, OverflowSheets = chkOverflowSheets.IsChecked == true,
                AutoFitSheet = chkAutoFitSheet.IsChecked == true,
                Params = _chosen.Select(p => new ParamMap { Name = p.Name, Label = p.Label }).ToList(),
                FrameIndex = -1,
                NamePrefix = string.Empty,
                CreateDimensions = dims,
                WriteParameters = true,
                UpperTagId = upper != null ? upper.Id : -1,
                LowerTagId = lower != null ? lower.Id : -1,
                UpperFields = ReadUpperFields(),
                ElevationBack = rbBack.IsChecked == true,
                CreatePlan = chkPlan.IsChecked == true,
                CreateFrame = chkFrame.IsChecked == true,
                DimTypeId = dims && dim != null ? dim.Id : -1
            };

            rbSetup.IsChecked = true; // để thấy log
            txtLog.Clear();
            ShowLog(true);
            StopRequested = false;
            SetRunning(true);
            ReportProgress(0, 0);

            try { Runner(req, new WizardSink(this)); }
            catch (Exception ex) { AppendLog("Lỗi: " + ex.Message); }
            finally
            {
                AppendLog(StopRequested ? "Đã dừng." : "Hoàn tất.");
                SetRunning(false);
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            StopRequested = true;
            AppendLog("Đang dừng sau Legend hiện tại...");
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_running)
            {
                StopRequested = true;
                e.Cancel = true; // đợi Legend hiện tại xong rồi mới đóng được
            }
        }

        private void SetRunning(bool running)
        {
            _running = running;
            btnStop.IsEnabled = running;
            btnGear.IsEnabled = !running;
            UpdateSummary();
        }

        public bool Confirm(string text)
        {
            return MessageBox.Show(this, text, Title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                   == MessageBoxResult.Yes;
        }

        public void ReportProgress(int done, int total)
        {
            pbar.Maximum = Math.Max(total, 1);
            pbar.Value = Math.Min(done, pbar.Maximum);
            int pct = total <= 0 ? 0 : (int)Math.Round(100.0 * done / total);
            lblPercent.Text = "(" + done + "/" + total + ") " + pct + "%";
            FlushUi();
        }

        public void AppendLog(string line)
        {
            txtLog.AppendText(line + Environment.NewLine);
            txtLog.ScrollToEnd();
            FlushUi();
        }

        /// <summary>Cho giao diện vẽ lại và nhận bấm nút Dừng giữa các bước xử lý.</summary>
        private void FlushUi()
        {
            Dispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
        }

        private void UpdateSummary()
        {
            if (lblSummary == null) return;
            int n = _doors.Count(d => d.IsChecked);
            lblSummary.Text = "Cửa đã chọn: " + n + "  -  Parameters: " + _chosen.Count;
            btnStart.IsEnabled = !_running && n > 0;
            UpdatePreview();
        }
    }
}

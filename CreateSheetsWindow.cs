using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace SDK_HD.Dim
{
    internal sealed class SheetTitleChoice
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }
    internal sealed class SheetCreateRow : INotifyPropertyChanged
    {
        public string Number { get; set; }
        public string Name { get; set; }
        public SheetTitleChoice TitleBlock { get; set; }
        public bool Selected { get; set; }
        private string error;
        public string Error { get => error; set { error = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error))); } }
        public event PropertyChangedEventHandler PropertyChanged;
    }
    internal sealed class CreateSheetsWindow : Window
    {
        internal readonly ObservableCollection<SheetCreateRow> Rows = new ObservableCollection<SheetCreateRow>();
        private readonly List<SheetTitleChoice> titleBlocks;
        private readonly HashSet<string> existing;
        private readonly DataGrid table;
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
        private static readonly Brush Avocado = new SolidColorBrush(Color.FromRgb(88, 117, 43));
        internal CreateSheetsWindow(List<SheetTitleChoice> titles, IEnumerable<string> used)
        {
            titleBlocks = titles; existing = new HashSet<string>(used, StringComparer.OrdinalIgnoreCase);
            Title = "KTool | Tạo Sheet"; Width = 1050; Height = 730; MinWidth = 860; MinHeight = 540;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            Background = new SolidColorBrush(Color.FromRgb(244, 247, 235));
            var root = new Grid { Margin = new Thickness(20) }; Content = root;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            top.Children.Add(new TextBlock { Text = "TẠO SHEET", FontSize = 25, FontWeight = FontWeights.Bold, Foreground = Avocado });
            top.Children.Add(new TextBlock { Text = "Nhập danh sách • Chọn khung tên từng sheet • Nhập / xuất Excel (.xlsx)", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 5, 0, 0) }); root.Children.Add(top);
            table = new DataGrid { ItemsSource = Rows, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, HeadersVisibility = DataGridHeadersVisibility.Column,
                Background = Brushes.White, BorderBrush = Brushes.DarkSeaGreen, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(226, 234, 214)), RowHeight = 38, SelectionMode = DataGridSelectionMode.Extended };
            var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)); headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(228, 237, 210)))); headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, Avocado)); headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10))); headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); table.ColumnHeaderStyle = headerStyle;
            var selectAll = new CheckBox { ToolTip = "Chọn tất cả để xóa dòng" }; selectAll.Click += (s, e) => { Commit(); foreach (var row in Rows) row.Selected = selectAll.IsChecked == true; table.Items.Refresh(); };
            table.Columns.Add(new DataGridTextColumn { Header = "Sheet Number", Binding = new Binding("Number") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(160) });
            table.Columns.Add(new DataGridTextColumn { Header = "Sheet Name", Binding = new Binding("Name") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 250 });
            table.Columns.Add(new DataGridComboBoxColumn { Header = "Title Blocks", ItemsSource = titleBlocks, DisplayMemberPath = "Name", SelectedItemBinding = new Binding("TitleBlock"), Width = new DataGridLength(300) });
            table.Columns.Add(new DataGridCheckBoxColumn { Header = selectAll, Binding = new Binding("Selected"), Width = 45 });
            table.Columns.Add(new DataGridTextColumn { Header = "Kiểm tra", Binding = new Binding("Error"), IsReadOnly = true, Width = 160 });
            Grid.SetRow(table, 1); root.Children.Add(table);
            var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; Grid.SetRow(bottom, 2); root.Children.Add(bottom);
            status.Text = "Tạo tất cả dòng trong bảng. Ô chọn dùng để xóa dòng, không xóa sheet có sẵn trong model."; bottom.Children.Add(status);
            var buttons = new UniformGrid { Columns = 5, Margin = new Thickness(0, 10, 0, 0) }; bottom.Children.Add(buttons);
            buttons.Children.Add(Button("Xuất mẫu Excel", Export)); buttons.Children.Add(Button("Nhập từ Excel", Import)); buttons.Children.Add(Button("+ Thêm Sheet", Add));
            buttons.Children.Add(Button("Xóa dòng đã chọn", () => { Commit(); foreach (var row in Rows.Where(r => r.Selected).ToList()) Rows.Remove(row); Status(); }));
            var create = Button("Tạo Sheets", Accept); create.Background = Avocado; create.Foreground = Brushes.White; buttons.Children.Add(create);
            Add();
        }
        private void Commit() { table.CommitEdit(DataGridEditingUnit.Cell, true); table.CommitEdit(DataGridEditingUnit.Row, true); }
        private void Add()
        {
            Commit(); string last = Rows.LastOrDefault()?.Number ?? "A-1"; var match = Regex.Match(last, @"^(.*?)(\d+)$");
            string prefix = match.Success ? match.Groups[1].Value : last + "-"; int digits = match.Success ? Math.Min(12, match.Groups[2].Value.Length) : 3;
            long value; if (!long.TryParse(match.Groups[2].Value, out value) || value >= long.MaxValue - 10000) value = -1;
            if (Rows.Count == 0) { prefix = "A"; digits = 3; value = -1; }
            string number; var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase); used.UnionWith(Rows.Select(r => r.Number ?? ""));
            do { value++; number = prefix + value.ToString("D" + digits); } while (used.Contains(number));
            Rows.Add(new SheetCreateRow { Number = number, Name = "", TitleBlock = Rows.LastOrDefault()?.TitleBlock ?? titleBlocks.First() }); Status();
        }
        private void Status() { status.Text = Rows.Count + " sheet trong bảng • Ô chọn chỉ dùng để xóa dòng. Nhập tên sheet trước khi tạo."; }
        private void Export()
        {
            Commit(); var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = "Mau_Tao_Sheets.xlsx" }; if (dialog.ShowDialog(this) != true) return;
            try { SheetTableWorkbook.Write(dialog.FileName, Rows.Select(r => new[] { r.Number, r.Name, r.TitleBlock?.Name ?? "" })); status.Text = "Đã xuất: " + dialog.FileName; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }
        private void Import()
        {
            var dialog = new OpenFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx" }; if (dialog.ShowDialog(this) != true) return;
            try
            {
                var data = SheetTableWorkbook.Read(dialog.FileName); var imported = new List<SheetCreateRow>();
                foreach (var row in data)
                {
                    var title = titleBlocks.FirstOrDefault(t => string.Equals(t.Name, row[2], StringComparison.CurrentCultureIgnoreCase));
                    if (title == null) throw new InvalidOperationException("Không tìm thấy Title Block: " + row[2] + ". Dùng đúng tên trong danh sách của dự án.");
                    imported.Add(new SheetCreateRow { Number = row[0], Name = row[1], TitleBlock = title });
                }
                if (imported.Count == 0) throw new InvalidOperationException("Excel chưa có dòng sheet.");
                // Append so importing never silently discards edited rows.
                foreach (var row in imported) Rows.Add(row); Status();
                status.Text = "Đã nhập thêm " + imported.Count + " dòng. Xóa dòng trống hoặc trùng trước khi tạo.";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Title); }
        }
        private void Accept()
        {
            Commit(); if (Rows.Count == 0) { MessageBox.Show(this, "Thêm ít nhất một sheet.", Title); return; }
            var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase); bool failed = false;
            foreach (var row in Rows)
            {
                row.Number = (row.Number ?? "").Trim(); row.Name = (row.Name ?? "").Trim(); row.Error = "";
                if (row.Number.Length == 0 || row.Name.Length == 0 || row.TitleBlock == null) row.Error = "Thiếu số / tên / khung tên";
                else if (!seen.Add(row.Number)) row.Error = "Trùng Sheet Number";
                if (row.Error.Length > 0) failed = true;
            }
            table.Items.Refresh(); if (failed) { status.Text = "Kiểm tra các dòng báo lỗi trước khi tạo."; return; }
            DialogResult = true;
        }
        private static Button Button(string text, Action click)
        { var b = new Button { Content = text, Height = 40, Margin = new Thickness(3), Background = new SolidColorBrush(Color.FromRgb(228, 237, 210)), Foreground = Avocado, BorderBrush = Brushes.DarkSeaGreen }; b.Click += (s, e) => click(); return b; }
    }
}

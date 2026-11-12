using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public sealed class MultiLevelPresetCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ThenBy(l => l.Name).ToList();
            var types = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .ToList();
            var templates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate).OrderBy(v => v.Name).ToList();
            var window = new MultiLevelPresetWindow(levels, types, templates);
            new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
            if (window.ShowDialog() != true) return Result.Cancelled;
            var selectedLevels = window.SelectedLevels;
            var selectedPackages = window.SelectedPackages;
            if (selectedLevels.Count == 0 || selectedPackages.Count == 0) return Result.Cancelled;

            var created = new List<string>();
            var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            using (var tx = new Transaction(doc, "KTool - Preset nhiều tầng"))
            {
                try
                {
                    tx.Start();
                    foreach (Level level in selectedLevels)
                    foreach (PresetPackage package in selectedPackages)
                    {
                        if (package.ViewFamilyType == null) throw new InvalidOperationException("Thiếu loại view cho preset " + package.Label + ".");
                        ViewPlan view = ViewPlan.Create(doc, package.ViewFamilyType.Id, level.Id);
                        string requestedName = package.NamePattern.Replace("{LEVEL}", level.Name).Replace("{LEVEL_NO}", level.Name);
                        string finalName = UniqueName(names, requestedName);
                        view.Name = finalName;
                        view.Scale = package.Scale;
                        if (package.TemplateId != ElementId.InvalidElementId)
                        {
                            if (!view.IsValidViewTemplate(package.TemplateId))
                                throw new InvalidOperationException("Template " + doc.GetElement(package.TemplateId).Name + " không phù hợp với " + finalName + ".");
                            view.ViewTemplateId = package.TemplateId;
                        }
                        names.Add(finalName);
                        created.Add(level.Name + " · " + package.Label + " → " + finalName);
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu được các view preset.");
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    message = ex.Message;
                    return Result.Failed;
                }
            }
            new TaskDialog("KTool - Preset nhiều tầng")
            {
                MainInstruction = "Đã tạo " + created.Count + " view cho " + selectedLevels.Count + " tầng",
                MainContent = "Các view đã được tạo theo View Type, template và tên trong preset. Dùng SheetTool để tạo sheet và đặt view.",
                ExpandedContent = string.Join("\n", created),
                CommonButtons = TaskDialogCommonButtons.Close
            }.Show();
            return Result.Succeeded;
        }

        private static string UniqueName(HashSet<string> names, string requested)
        {
            string root = string.IsNullOrWhiteSpace(requested) ? "KTool - View" : requested.Trim();
            string name = root; int suffix = 2;
            while (names.Contains(name)) name = root + " (" + suffix++ + ")";
            return name;
        }
    }

    internal sealed class PresetPackage
    {
        internal string Key;
        internal string Label;
        internal string NamePattern;
        internal ViewFamily Family;
        internal int Scale;
        internal ViewFamilyType ViewFamilyType;
        internal ElementId TemplateId = ElementId.InvalidElementId;
    }

    internal sealed class MultiLevelPresetWindow : Window
    {
        private sealed class LevelRow { internal Level Level; internal CheckBox Check; }
        private sealed class PackageRow { internal PresetPackage Package; internal CheckBox Check; internal ComboBox Type; internal ComboBox Template; internal TextBox Name; }
        private sealed class TypeChoice { internal ViewFamilyType Type; public string Label { get; set; } }
        private sealed class TemplateChoice { internal ElementId Id; public string Label { get; set; } }
        private readonly List<LevelRow> _levelRows = new List<LevelRow>();
        private readonly List<PackageRow> _packageRows = new List<PackageRow>();
        private readonly List<PresetPackage> _packages;
        private readonly Brush _navy = Brush("#17334F");
        private readonly Brush _cafe = Brush("#8B6248");
        private readonly Brush _cream = Brush("#F5F0E8");

        internal List<Level> SelectedLevels => _levelRows.Where(r => r.Check.IsChecked == true).Select(r => r.Level).ToList();
        internal List<PresetPackage> SelectedPackages => _packageRows.Where(r => r.Check.IsChecked == true).Select(r =>
        {
            r.Package.ViewFamilyType = ((TypeChoice)r.Type.SelectedItem)?.Type;
            r.Package.TemplateId = ((TemplateChoice)r.Template.SelectedItem)?.Id ?? ElementId.InvalidElementId;
            r.Package.NamePattern = r.Name.Text.Trim();
            return r.Package;
        }).ToList();

        internal MultiLevelPresetWindow(List<Level> levels, List<ViewFamilyType> types, List<View> templates)
        {
            Title = "KTool - Preset nhiều tầng"; Width = 1080; Height = 720; MinWidth = 900; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brush("#F4F6F9"); FontFamily = new FontFamily("Segoe UI");
            _packages = BuildPackages();
            var root = new Grid { Margin = new Thickness(14) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            heading.Children.Add(new TextBlock { Text = "TRIỂN KHAI PRESET NHIỀU TẦNG", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = _navy });
            heading.Children.Add(new TextBlock { Text = "Chọn tầng, nhóm bản vẽ và View Template. Mỗi lựa chọn sẽ tạo view tương ứng cho tất cả tầng đã chọn.", Foreground = Brush("#526174") });
            Grid.SetColumnSpan(heading, 2); root.Children.Add(heading);

            var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; Grid.SetRow(left, 1); root.Children.Add(left);
            left.Children.Add(new TextBlock { Text = "TẦNG / LEVEL", FontWeight = FontWeights.SemiBold, Foreground = _navy, Margin = new Thickness(2, 4, 2, 8) });
            var levelButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var allLevels = SmallButton("Tất cả"); allLevels.Click += (s, e) => SetLevels(true);
            var noLevels = SmallButton("Bỏ chọn"); noLevels.Click += (s, e) => SetLevels(false);
            levelButtons.Children.Add(allLevels); levelButtons.Children.Add(noLevels); left.Children.Add(levelButtons);
            var levelsPanel = new StackPanel();
            foreach (Level level in levels)
            {
                var check = new CheckBox { Content = level.Name + "  ·  " + (level.Elevation * 304.8).ToString("0") + " mm", IsChecked = true, Margin = new Thickness(4, 5, 4, 5), Foreground = Brush("#24354A") };
                _levelRows.Add(new LevelRow { Level = level, Check = check }); levelsPanel.Children.Add(check);
            }
            var levelsScroll = new ScrollViewer { Content = levelsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderBrush = Brush("#C8D2DE"), BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(5) };
            left.Children.Add(levelsScroll);
            left.Children.Add(new TextBlock { Text = "Preset nhanh", FontWeight = FontWeights.SemiBold, Foreground = _navy, Margin = new Thickness(2, 12, 2, 6) });
            var presetButtons = new WrapPanel();
            foreach (var item in new[] { new { Label = "Kiến trúc", Keys = "ARCH,DIM,FURN" }, new { Label = "Hoàn thiện", Keys = "FLOOR,CEILING" },
                new { Label = "Kết cấu", Keys = "STRUCT" }, new { Label = "Điện", Keys = "ELECT,LIGHT" },
                new { Label = "Cấp thoát nước", Keys = "DRAIN,WATER" }, new { Label = "Máy lạnh", Keys = "AC" },
                new { Label = "Toàn bộ", Keys = "ALL" } })
            {
                var b = SmallButton(item.Label); b.Margin = new Thickness(3); string keys = item.Keys; b.Click += (s, e) => ApplyPreset(keys); presetButtons.Children.Add(b);
            }
            left.Children.Add(presetButtons);

            var right = new Grid { Margin = new Thickness(6, 0, 0, 0) }; Grid.SetColumn(right, 1); Grid.SetRow(right, 1); root.Children.Add(right);
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); right.RowDefinitions.Add(new RowDefinition());
            var labels = new Grid { Margin = new Thickness(5, 4, 5, 6) };
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
            labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            labels.Children.Add(Header("Nhóm bản vẽ",1)); labels.Children.Add(Header("View Type",2)); labels.Children.Add(Header("View Template",3)); labels.Children.Add(Header("Tên view ({LEVEL})",4));
            right.Children.Add(labels);
            var packageList = new StackPanel();
            foreach (PresetPackage package in _packages)
            {
                var row = new Grid { Margin = new Thickness(3, 3, 3, 3), Background = Brushes.White };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(185) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var enabled = new CheckBox { IsChecked = package.Key == "ARCH", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var title = new TextBlock { Text = package.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3) };
                var typeCombo = new ComboBox { Margin = new Thickness(3), Height = 28, DisplayMemberPath = "Label" };
                foreach (ViewFamilyType type in types.Where(t => t.ViewFamily == package.Family).OrderBy(t => t.Name)) typeCombo.Items.Add(new TypeChoice { Type = type, Label = type.Name });
                if (typeCombo.Items.Count > 0) typeCombo.SelectedIndex = 0; else { enabled.IsEnabled = false; title.ToolTip = "Project không có View Type cho nhóm này."; }
                var templateCombo = new ComboBox { Margin = new Thickness(3), Height = 28, DisplayMemberPath = "Label" };
                templateCombo.Items.Add(new TemplateChoice { Id = ElementId.InvalidElementId, Label = "(Không gán)" });
                foreach (View template in templates.Where(t => t.ViewType == ToViewType(package.Family)))
                    templateCombo.Items.Add(new TemplateChoice { Id = template.Id, Label = template.Name });
                templateCombo.SelectedIndex = 0;
                var name = new TextBox { Text = package.NamePattern, Margin = new Thickness(3), Height = 28, VerticalContentAlignment = VerticalAlignment.Center };
                Add(row, enabled, 0); Add(row, title, 1); Add(row, typeCombo, 2); Add(row, templateCombo, 3); Add(row, name, 4);
                packageList.Children.Add(row); _packageRows.Add(new PackageRow { Package = package, Check = enabled, Type = typeCombo, Template = templateCombo, Name = name });
            }
            var packageScroll = new ScrollViewer { Content = packageList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#E9EEF4"), Padding = new Thickness(3) };
            Grid.SetRow(packageScroll, 1); right.Children.Add(packageScroll);
            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = true }; Grid.SetRow(footer, 2); Grid.SetColumnSpan(footer, 2); root.Children.Add(footer);
            var cancel = new Button { Content = "Hủy", Width = 90, Height = 36, Margin = new Thickness(5), IsCancel = true };
            var create = new Button { Content = "Tạo bộ view", Width = 140, Height = 36, Margin = new Thickness(5), Background = _navy, Foreground = _cream, BorderBrush = _navy, FontWeight = FontWeights.SemiBold, IsDefault = true };
            var scaleBox = new TextBox { Text = "50", Width = 56, Height = 28, Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
            create.Click += (s, e) =>
            {
                if (SelectedLevels.Count == 0) { MessageBox.Show(this, "Chọn ít nhất một Level.", "KTool - Preset nhiều tầng"); return; }
                if (SelectedPackages.Count == 0 || SelectedPackages.Any(p => p.ViewFamilyType == null)) { MessageBox.Show(this, "Chọn ít nhất một nhóm có View Type hợp lệ.", "KTool - Preset nhiều tầng"); return; }
                if (SelectedPackages.Any(p => string.IsNullOrWhiteSpace(p.NamePattern) || !p.NamePattern.Contains("{LEVEL}")))
                { MessageBox.Show(this, "Tên view phải có token {LEVEL} để phân biệt từng tầng.", "KTool - Preset nhiều tầng"); return; }
                int badScale; if (!int.TryParse(scaleBox.Text, out badScale) || badScale < 1 || badScale > 24000) { MessageBox.Show(this, "Tỷ lệ phải là số nguyên từ 1 đến 24000.", "KTool - Preset nhiều tầng"); return; }
                foreach (var p in SelectedPackages) p.Scale = badScale;
                DialogResult = true;
            };
            var scaleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            scaleStack.Children.Add(new TextBlock { Text = "Tỷ lệ 1 :", VerticalAlignment = VerticalAlignment.Center }); scaleStack.Children.Add(scaleBox);
            DockPanel.SetDock(create, Dock.Right); DockPanel.SetDock(cancel, Dock.Right); DockPanel.SetDock(scaleStack, Dock.Left); footer.Children.Add(create); footer.Children.Add(cancel); footer.Children.Add(scaleStack);
            footer.Children.Add(new TextBlock { Text = "Các view sẽ được tạo trong một lượt và có thể Undo.", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("#526174") });
            Content = root;
        }

        private static List<PresetPackage> BuildPackages() => new List<PresetPackage>
        {
            P("ARCH", "Mặt bằng kiến trúc", ViewFamily.FloorPlan, "A.01.02 - BỐ TRÍ VẬT DỤNG - {LEVEL}"),
            P("DIM", "Mặt bằng kích thước", ViewFamily.FloorPlan, "A.01.06 - KÍCH THƯỚC - {LEVEL}"),
            P("FURN", "Mặt bằng bố trí vật dụng", ViewFamily.FloorPlan, "A.01 - BỐ TRÍ - {LEVEL}"),
            P("FLOOR", "Mặt bằng lát sàn / hoàn thiện", ViewFamily.FloorPlan, "A.02 - LÁT SÀN - {LEVEL}"),
            P("CEILING", "Mặt bằng trần", ViewFamily.CeilingPlan, "A.02 - MẶT BẰNG TRẦN - {LEVEL}"),
            P("STRUCT", "Mặt bằng kết cấu", ViewFamily.StructuralPlan, "STR - KẾT CẤU - {LEVEL}"),
            P("ELECT", "Bố trí thiết bị điện", ViewFamily.FloorPlan, "MEP - THIẾT BỊ ĐIỆN - {LEVEL}"),
            P("LIGHT", "Mặt bằng chiếu sáng", ViewFamily.FloorPlan, "MEP - CHIẾU SÁNG - {LEVEL}"),
            P("DRAIN", "Mặt bằng thoát nước", ViewFamily.FloorPlan, "PLP - THOÁT NƯỚC - {LEVEL}"),
            P("WATER", "Mặt bằng cấp nước", ViewFamily.FloorPlan, "PLP - CẤP NƯỚC - {LEVEL}"),
            P("AC", "Mặt bằng máy lạnh", ViewFamily.FloorPlan, "AC - MÁY LẠNH - {LEVEL}")
        };
        private static PresetPackage P(string key, string label, ViewFamily family, string pattern) => new PresetPackage { Key = key, Label = label, Family = family, NamePattern = pattern, Scale = 50 };
        private static ViewType ToViewType(ViewFamily family) => family == ViewFamily.CeilingPlan ? ViewType.CeilingPlan : family == ViewFamily.StructuralPlan ? ViewType.EngineeringPlan : family == ViewFamily.FloorPlan ? ViewType.EngineeringPlan : ViewType.FloorPlan;
        private void ApplyPreset(string keys)
        {
            var set = new HashSet<string>(keys.Split(',')); bool all = set.Contains("ALL");
            foreach (var row in _packageRows) row.Check.IsChecked = all || set.Contains(row.Package.Key);
        }
        private void SetLevels(bool value) { foreach (var row in _levelRows) row.Check.IsChecked = value; }
        private Button SmallButton(string text) => new Button { Content = text, Height = 28, Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2), Background = _cafe, Foreground = _cream, BorderBrush = _cafe };
        private static TextBlock Header(string text, int column) { var x = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = Brush("#17334F"), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(x, column); return x; }
        private static void Add(Grid grid, UIElement element, int column) { Grid.SetColumn(element, column); grid.Children.Add(element); }
        private static SolidColorBrush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }
}

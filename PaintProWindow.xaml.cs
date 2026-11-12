using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitOperationCanceledException = Autodesk.Revit.Exceptions.OperationCanceledException;
using RevitColor = Autodesk.Revit.DB.Color;
using MediaColor = System.Windows.Media.Color;

namespace SDK_HD.Dim
{
    public partial class PaintProWindow : Window
    {
        private readonly Document _doc;
        private readonly UIDocument _uiDoc;
        private readonly PaintProEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private bool _busy;
        private readonly List<MaterialItem> _materials = new List<MaterialItem>();

        public PaintProWindow(UIDocument uiDoc)
        {
            InitializeComponent();
            _uiDoc = uiDoc ?? throw new ArgumentNullException(nameof(uiDoc));
            _doc = _uiDoc.Document;
            _handler = new PaintProEventHandler(this, _uiDoc);
            _externalEvent = ExternalEvent.Create(_handler);
            Closed += (s, e) => _externalEvent.Dispose();
            RefreshMaterials();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => QueueAction(PaintProAction.Refresh, ElementId.InvalidElementId);
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => FilterMaterials();
        private void MaterialsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

        private void RefreshMaterials()
        {
            try
            {
                _materials.Clear();
                foreach (Material m in new FilteredElementCollector(_doc)
                    .OfClass(typeof(Material)).Cast<Material>().OrderBy(m => m.Name))
                {
                    RevitColor c = m.Color;
                    _materials.Add(new MaterialItem
                    {
                        Name = m.Name,
                        Id = m.Id.ToString(),
                        MaterialId = m.Id,
                        Red = c.Red, Green = c.Green, Blue = c.Blue,
                        Transparency = m.Transparency,
                        Shininess = m.Shininess,
                        Smoothness = m.Smoothness,
                        Appearance = m.AppearanceAssetId != ElementId.InvalidElementId ? "Có" : "Không"
                    });
                }
                FilterMaterials();
                StatusText.Text = "Đã tải " + _materials.Count + " vật liệu từ dự án: " + _doc.Title;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Lỗi đọc vật liệu: " + ex.Message;
            }
        }

        private void FilterMaterials()
        {
            if (SearchBox == null || MaterialsList == null || CountText == null) return;
            string query = (SearchBox.Text ?? "").Trim();
            var filtered = _materials.Where(m => m.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            MaterialsList.ItemsSource = filtered;
            CountText.Text = filtered.Count + " / " + _materials.Count;
            if (filtered.Count > 0) MaterialsList.SelectedIndex = 0;
            else ShowSelected();
        }

        private void ShowSelected()
        {
            if (MaterialsList == null || MaterialNameText == null || MaterialDetailsText == null || ColorSwatch == null) return;
            var m = MaterialsList.SelectedItem as MaterialItem;
            if (m == null)
            {
                MaterialNameText.Text = "Chưa chọn vật liệu";
                MaterialDetailsText.Text = "Chọn vật liệu bên trái để xem thuộc tính.";
                ColorSwatch.Background = new SolidColorBrush(MediaColor.FromRgb(23, 28, 37));
                return;
            }
            MaterialNameText.Text = m.Name;
            ColorSwatch.Background = new SolidColorBrush(MediaColor.FromRgb(m.Red, m.Green, m.Blue));
            MaterialDetailsText.Text = "Element ID: " + m.Id + "\n"
                + "RGB: " + m.Red + ", " + m.Green + ", " + m.Blue + "\n"
                + "HEX: #" + m.Red.ToString("X2") + m.Green.ToString("X2") + m.Blue.ToString("X2") + "\n"
                + "Transparency: " + m.Transparency + "%\n"
                + "Shininess: " + m.Shininess + "\n"
                + "Smoothness: " + m.Smoothness + "\n"
                + "Appearance asset: " + m.Appearance;
        }


        private void PickMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            QueueAction(PaintProAction.Pick, ElementId.InvalidElementId);
        }

        private void PaintButton_Click(object sender, RoutedEventArgs e)
        {
            MaterialItem selected = MaterialsList.SelectedItem as MaterialItem;
            if (selected == null)
            {
                System.Windows.MessageBox.Show(this, "Vui lòng chọn vật liệu trước khi Paint.", "PaintPro");
                return;
            }
            QueueAction(PaintProAction.Paint, selected.MaterialId);
        }

        private void QueueAction(PaintProAction action, ElementId materialId)
        {
            if (_busy) return;
            _busy = true;
            _handler.SetRequest(action, materialId);
            // Không gọi Revit API từ Click event của cửa sổ modeless.
            if (action != PaintProAction.Refresh) Hide();
            ExternalEventRequest result = _externalEvent.Raise();
            if (result != ExternalEventRequest.Accepted)
            {
                _busy = false;
                Show(); Activate();
                StatusText.Text = "Không gửi được lệnh đến Revit: " + result;
            }
        }

        internal void CompleteAction(string status, ElementId pickedMaterialId)
        {
            _busy = false;
            if (!IsVisible) Show();
            Activate();
            if (pickedMaterialId != null && pickedMaterialId != ElementId.InvalidElementId)
            {
                // Refresh có thể cần nếu vật liệu mới được thêm từ bên ngoài.
                RefreshMaterials();
                MaterialItem item = _materials.FirstOrDefault(m => m.MaterialId == pickedMaterialId);
                if (item != null)
                {
                    SearchBox.Text = "";
                    FilterMaterials();
                    MaterialsList.SelectedItem = item;
                    MaterialsList.ScrollIntoView(item);
                }
            }
            StatusText.Text = status;
        }

        internal void RefreshFromRevit()
        {
            RefreshMaterials();
        }

        private sealed class MaterialItem
        {
            public string Name { get; set; }
            public string Id { get; set; }
            public ElementId MaterialId { get; set; }
            public byte Red { get; set; }
            public byte Green { get; set; }
            public byte Blue { get; set; }
            public int Transparency { get; set; }
            public int Shininess { get; set; }
            public int Smoothness { get; set; }
            public string Appearance { get; set; }
        }
    }
}

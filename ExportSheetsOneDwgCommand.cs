using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ExportSheetsOneDwgCommand : IExternalCommand
    {
        private const string Acad = @"C:\Program Files\Autodesk\AutoCAD 2023\acad.exe";
        internal static System.Windows.Media.ImageSource CreateIcon(int size)
        {
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var ink = System.Windows.Media.Brushes.OliveDrab; var pen = new System.Windows.Media.Pen(ink, 1.5);
                dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new System.Windows.Rect(0, 0, 32, 32));
                for (int i = 0; i < 3; i++) dc.DrawRectangle(System.Windows.Media.Brushes.Honeydew, pen, new System.Windows.Rect(2 + i * 3, 3 + i * 4, 10, 15));
                dc.DrawRectangle(System.Windows.Media.Brushes.Honeydew, pen, new System.Windows.Rect(23, 7, 7, 20));
                dc.DrawLine(new System.Windows.Media.Pen(ink, 2), new System.Windows.Point(16, 16), new System.Windows.Point(24, 16));
                dc.DrawLine(new System.Windows.Media.Pen(ink, 2), new System.Windows.Point(20, 12), new System.Windows.Point(24, 16));
                dc.DrawLine(new System.Windows.Media.Pen(ink, 2), new System.Windows.Point(20, 20), new System.Windows.Point(24, 16));
            }
            var image = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96.0 * size / 32, 96.0 * size / 32, System.Windows.Media.PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
        }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            var doc = ui.Document;
            string helper = Path.Combine(Path.GetDirectoryName(typeof(ExportSheetsOneDwgCommand).Assembly.Location), "KTool.DwgMerge.dll");
            if (!File.Exists(Acad) || !File.Exists(helper)) { TaskDialog.Show("Xuất một DWG", "Cần AutoCAD 2023 và bộ gom KTool.DwgMerge.dll được cài cùng add-in."); return Result.Cancelled; }
            var rows = new List<DwgSheetRow>();
            foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(s => !s.IsPlaceholder && s.CanBePrinted).OrderBy(s => s.SheetNumber))
            {
                string prefix = Regex.Match(sheet.SheetNumber, @"^[^\d.\-]+|^\d{1,2}").Value.Trim();
                var row = new DwgSheetRow { Id = sheet.Id.Value, Number = sheet.SheetNumber, Name = sheet.Name, Prefix = prefix.Length == 0 ? "Sheets" : prefix };
                foreach (Parameter parameter in sheet.Parameters)
                    if (parameter.Definition != null && parameter.StorageType == StorageType.String)
                    { string value = parameter.AsString(); if (!string.IsNullOrWhiteSpace(value)) row.Groups[parameter.Definition.Name] = value; }
                rows.Add(row);
            }
            if (rows.Count == 0) { TaskDialog.Show("Xuất một DWG", "Không có sheet in được trong dự án."); return Result.Cancelled; }
            var sets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheetSet)).Cast<ViewSheetSet>().OrderBy(s => s.Name)
                .Select(s => new DwgSheetSet { Name = s.Name, Ids = new HashSet<long>(s.Views.Cast<View>().OfType<ViewSheet>().Select(v => v.Id.Value)) }).ToList();
            var window = new ExportSheetsDwgWindow(rows, sets, BaseExportOptions.GetPredefinedSetupNames(doc));
            new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
            if (window.ShowDialog() != true) return Result.Cancelled;
            var save = new SaveFileDialog { Filter = "AutoCAD drawing (*.dwg)|*.dwg", DefaultExt = ".dwg", FileName = "Tat_ca_Sheets.dwg", OverwritePrompt = true };
            if (save.ShowDialog() != true) return Result.Cancelled;
            string jobDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KTool", "DwgJobs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(jobDir);
            var progress = new DwgExportProgressWindow();
            new System.Windows.Interop.WindowInteropHelper(progress).Owner = data.Application.MainWindowHandle;
            try
            {
                progress.Run(update =>
                {
                    var sheets = window.Selected; var files = new List<string>();
                    using (var options = window.SetupName == null ? new DWGExportOptions() : DWGExportOptions.GetPredefinedOptions(doc, window.SetupName))
                    {
                        options.MergedViews = true; options.TargetUnit = ExportUnit.Millimeter; options.FileVersion = ACADVersion.R2018;
                        for (int i = 0; i < sheets.Count; i++)
                        {
                            update("Xuất DWG " + (i + 1) + "/" + sheets.Count + ": " + sheets[i].Number);
                            string folder = Path.Combine(jobDir, "S" + (i + 1).ToString("D4")); Directory.CreateDirectory(folder);
                            string name = "S" + (i + 1).ToString("D4");
                            if (!doc.Export(folder, name, new List<ElementId> { new ElementId(sheets[i].Id) }, options)) throw new InvalidOperationException("Revit không xuất được sheet " + sheets[i].Number);
                            string path = Path.Combine(folder, name + ".dwg");
                            if (!File.Exists(path))
                            {
                                var candidates = Directory.GetFiles(folder, "*.dwg");
                                if (candidates.Length != 1) throw new InvalidOperationException("Không xác định được DWG chính của sheet " + sheets[i].Number + ". Xem " + folder);
                                path = candidates[0];
                            }
                            files.Add(path);
                        }
                    }
                    string merged = Path.Combine(jobDir, "Merged_Model.dwg"), result = Path.Combine(jobDir, "success.xml"), failure = Path.Combine(jobDir, "failure.txt"), status = Path.Combine(jobDir, "progress.txt");
                    string manifest = Path.Combine(jobDir, "job.xml");
                    new XElement("MergeJob", new XAttribute("output", merged), new XAttribute("result", result), new XAttribute("failure", failure), new XAttribute("progress", status),
                        new XAttribute("looseEntities", window.LooseEntities), new XAttribute("columns", window.Columns), new XAttribute("gapMm", window.GapMm),
                        sheets.Select((s, i) => new XElement("Sheet", new XAttribute("file", files[i]), new XAttribute("name", s.Number + " - " + s.Name)))).Save(manifest);
                    string script = Path.Combine(jobDir, "merge.scr");
                    File.WriteAllText(script, "_.NETLOAD\r\n\"" + helper + "\"\r\nKTOOLDWGMERGE\r\n\"" + manifest + "\"\r\n_.QUIT\r\n_Y\r\n", new UTF8Encoding(false));
                    update("Đang khởi động AutoCAD 2023 và chuyển các Layout sang Model…");
                    var info = new ProcessStartInfo(Acad, "/nologo \"" + files[0] + "\" /b \"" + script + "\"")
                    { UseShellExecute = false, WorkingDirectory = jobDir, WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true };
                    using (var process = Process.Start(info))
                    {
                        try
                        {
                            var started = DateTime.UtcNow;
                            while (!process.WaitForExit(250))
                            {
                                if (File.Exists(failure)) throw new InvalidOperationException(File.ReadAllText(failure));
                                string current = "AutoCAD đang xử lý…";
                                try { if (File.Exists(status)) current = File.ReadAllText(status); } catch (IOException) { }
                                update(current);
                                if (File.Exists(result)) break;
                                if ((DateTime.UtcNow - started).TotalMinutes > 45) throw new TimeoutException("AutoCAD chưa hoàn tất. Kiểm tra đăng nhập/giấy phép AutoCAD và log tại " + jobDir);
                            }
                            if (File.Exists(failure)) throw new InvalidOperationException(File.ReadAllText(failure));
                            if (!File.Exists(result) || !File.Exists(merged)) throw new InvalidOperationException("AutoCAD chưa tạo được DWG chung. Kiểm tra giấy phép/khởi động AutoCAD và log tại " + jobDir);
                            var success = XElement.Load(result);
                            if ((int)success.Attribute("count") != sheets.Count) throw new InvalidOperationException("Số sheet trong DWG chung không khớp lựa chọn.");
                            update("Đang lưu DWG chung…");
                            File.Copy(merged, save.FileName, true);
                            // The helper has saved and verified the output. Its script exits
                            // AutoCAD; do not wait on UI shutdown after output is complete.
                        }
                        catch
                        {
                            if (!process.HasExited) { try { process.Kill(); } catch { } }
                            throw;
                        }
                    }
                });
                new TaskDialog("Xuất một file AutoCAD") { MainInstruction = "Đã xuất " + window.Selected.Count + " sheet vào một DWG", MainContent = save.FileName + "\nTất cả sheet nằm trong Model, theo bố cục/tỷ lệ in và đơn vị mm.",
                    ExpandedContent = "Log và tệp hỗ trợ: " + jobDir + "\nEXPORTLAYOUT có thể chuyển một số đối tượng thành block/đường/text. Ảnh/font liên kết có thể cần tệp hỗ trợ. Kiểm tra DWG trước khi bàn giao.", CommonButtons = TaskDialogCommonButtons.Close }.Show();
                return Result.Succeeded;
            }
            catch (System.OperationCanceledException) { TaskDialog.Show("Xuất một DWG", "Đã dừng. Tệp trung gian được giữ tại " + jobDir); return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message + "\nLog: " + jobDir; return Result.Failed; }
        }
    }
    internal sealed class DwgExportProgressWindow : Window
    {
        private readonly TextBlock text = new TextBlock { Margin = new Thickness(18), TextWrapping = TextWrapping.Wrap, Text = "Đang chuẩn bị…" };
        private bool stop, done;
        internal DwgExportProgressWindow()
        {
            Title = "KTool | Xuất các Sheet vào một DWG"; Width = 520; Height = 180; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 247, 235));
            var panel = new StackPanel(); panel.Children.Add(text); var cancel = new Button { Content = "Dừng xuất", Height = 32, Margin = new Thickness(18, 0, 18, 18) };
            cancel.Click += (s, e) => { stop = true; text.Text = "Đang dừng…"; }; panel.Children.Add(cancel); Content = panel;
            Closing += (s, e) => { if (!done) { stop = true; e.Cancel = true; } };
        }
        internal void Run(Action<Action<string>> work)
        {
            Exception error = null;
            ContentRendered += (s, e) => Dispatcher.BeginInvoke(new Action(() => { try { work(Update); } catch (Exception ex) { error = ex; } finally { done = true; Close(); } }));
            ShowDialog(); if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        private void Update(string value)
        { text.Text = value; Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { })); if (stop) throw new System.OperationCanceledException(); }
    }
}


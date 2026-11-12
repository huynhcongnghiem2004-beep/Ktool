using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    public class App : IExternalApplication
    {
        private const string TAB_NAME = "KTool";
        private const string PANEL_NAME = "Dim";
        private const string PANEL_FIT = "Crop View";
        private const string PANEL_STRUCT = "Kết cấu";
        private const string PANEL_ELEC = "Điện";
        private const string PANEL_WATER = "Nước";
        private const string PANEL_UTILITIES = "Tiện ích";

        /// <summary>true: nếu thiếu icon thì hiện hộp thoại liệt kê icon thiếu khi Revit khởi động. Tắt khi đã ổn.</summary>
        private const bool SHOW_ICON_DIAGNOSTIC = true;

        private static readonly System.Collections.Generic.List<string> _missingIcons =
            new System.Collections.Generic.List<string>();
        private RibbonPanel paintPanel;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                return BuildRibbon(application);
            }
            catch (Exception ex)
            {
                // Các panel đã tạo trước chỗ lỗi vẫn dùng được.
                TaskDialog.Show("KTool - Lỗi khởi động", ex.Message);
                return Result.Succeeded;
            }
        }

        private Result BuildRibbon(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TAB_NAME);
            }
            catch (Exception)
            {
                // Tab đã tồn tại
            }

            RibbonPanel panel = application.GetRibbonPanels(TAB_NAME)
                                    .FirstOrDefault(p => p.Name == PANEL_NAME)
                                ?? application.CreateRibbonPanel(TAB_NAME, PANEL_NAME);

            RibbonPanel fitPanel = application.GetRibbonPanels(TAB_NAME)
                                       .FirstOrDefault(p => p.Name == PANEL_FIT)
                                   ?? application.CreateRibbonPanel(TAB_NAME, PANEL_FIT);

            string dllPath = Assembly.GetExecutingAssembly().Location;
            RibbonPanel deploymentPanel = application.GetRibbonPanels(TAB_NAME)
                .FirstOrDefault(p => p.Name == "Công cụ triển khai")
                ?? application.CreateRibbonPanel(TAB_NAME, "Công cụ triển khai");
            var stairButton = new PushButtonData("KTool_StairDeployment", "Triển khai" + Environment.NewLine + "cầu thang bộ", dllPath, typeof(StairDeploymentCommand).FullName);
            stairButton.ToolTip = "Chọn cầu thang; tạo mặt bằng các tầng, cắt dọc/ngang và sheet.";
            stairButton.LongDescription = "Chọn tầng, Template, tỷ lệ, crop và kiểu Dim trong WPF. Hướng cắt theo vế thang, dùng reference mặt phẳng thật; có Fit Crop và căn Title. Sheet chưa có khung tên.";
            KToolRibbonIcons.Set(stairButton, KToolIcon.StairDeploy);
            deploymentPanel.AddItem(stairButton);
            var multiLevelPreset = new PushButtonData("KTool_MultiLevelPreset", "Preset" + Environment.NewLine + "nhiều tầng", dllPath, typeof(MultiLevelPresetCommand).FullName);
            multiLevelPreset.ToolTip = "Tạo đồng loạt các view kiến trúc, hoàn thiện và MEP theo các Level; chọn View Type, Template và quy tắc tên.";
            KToolRibbonIcons.Set(multiLevelPreset, KToolIcon.MultiLevelPreset);
            deploymentPanel.AddItem(multiLevelPreset);
            var cropOverrideButton = new PushButtonData("KTool_StairCropOverride", "Override Crop" + Environment.NewLine + "View Region", dllPath, typeof(CropRegionOverrideCommand).FullName);
            cropOverrideButton.ToolTip = "Đổi màu, kiểu nét và Line Weight của đường Crop cho view hiện tại hoặc các viewport/view đã chọn.";
            KToolRibbonIcons.Set(cropOverrideButton, KToolIcon.CropRegion);

            var browseButton = new PushButtonData("KTool_ViewSheetBrowser", "Duyệt" + Environment.NewLine + "View / Sheet", dllPath, typeof(ViewSheetBrowserCommand).FullName);
            browseButton.ToolTip = "Duyệt view/sheet theo nhóm, tìm kiếm và nhấp đúp để mở; cửa sổ giữ mở khi chuyển view.";
            KToolRibbonIcons.Set(browseButton, KToolIcon.ViewBrowser);

            var createSheetsButton = new PushButtonData("KTool_CreateSheets", "Tạo" + Environment.NewLine + "Sheets", dllPath, typeof(CreateSheetsCommand).FullName);
            createSheetsButton.ToolTip = "Tạo nhiều sheet, chọn khung tên từng dòng và nhập/xuất mẫu Excel .xlsx.";
            KToolRibbonIcons.Set(createSheetsButton, KToolIcon.CreateSheet);


            RibbonPanel alignPanel = application.GetRibbonPanels(TAB_NAME)
                .FirstOrDefault(p => p.Name == "Align")
                ?? application.CreateRibbonPanel(TAB_NAME, "Align");
            var alignTitle = new PushButtonData("KTool_AlignViewportTitle",
                "Align Title" + Environment.NewLine + "Căn giữa", dllPath,
                typeof(AlignViewportTitleCommand).FullName);
            alignTitle.ToolTip = "Căn giữa tiêu đề theo chiều ngang của viewport trên Sheet.";
            alignTitle.LongDescription = "Chọn sẵn một hoặc nhiều viewport trên Sheet, hoặc chạy lệnh rồi chọn. "
                + "Căn giữa toàn bộ vùng tiêu đề (kể cả đường gạch), giữ cao độ tiêu đề và vị trí viewport. "
                + "Viewport không có tiêu đề hiển thị sẽ được bỏ qua. Có thể Undo cả lượt căn.";
            alignTitle.LargeImage = KToolRibbonIcons.Create(KToolIcon.AlignTitle, 32);
            alignTitle.Image = KToolRibbonIcons.Create(KToolIcon.AlignTitle, 16);
            var placeViewsButton = new PushButtonData("KTool_PlaceViews", "Đặt View vào Sheet", dllPath, typeof(PlaceViewsCommand).FullName);
            placeViewsButton.ToolTip = "Chọn view và sheet đích, lên kế hoạch đặt/gỡ rồi áp dụng; giữ nguyên view trong model.";
            placeViewsButton.Image = KToolRibbonIcons.Create(KToolIcon.PlaceView, 16);
            placeViewsButton.LargeImage = KToolRibbonIcons.Create(KToolIcon.PlaceView, 32);
            var sheetToolData = new PulldownButtonData("KTool_SheetTool", "SheetTool");
            sheetToolData.ToolTip = "Tạo Sheet, đặt View vào Sheet và duyệt View/Sheet.";
            KToolRibbonIcons.Set(sheetToolData, KToolIcon.CreateSheet);
            var sheetTool = (PulldownButton)deploymentPanel.AddItem(sheetToolData);
            sheetTool.AddPushButton(createSheetsButton);
            sheetTool.AddPushButton(placeViewsButton);
            sheetTool.AddPushButton(browseButton);
            var exportOneDwgButton = new PushButtonData("KTool_ExportSheetsOneDwg", "Xuất Sheets → một DWG", dllPath, typeof(ExportSheetsOneDwgCommand).FullName);
            exportOneDwgButton.ToolTip = "Chọn sheet/bộ sheet in và xuất một DWG chung, tất cả sheet nằm trong Model; dùng AutoCAD 2023 để gom.";
            KToolRibbonIcons.Set(exportOneDwgButton, KToolIcon.ExportDwg);
            sheetTool.AddPushButton(exportOneDwgButton);

            alignPanel.AddItem(alignTitle);

            var calloutMenuData = new PulldownButtonData("KTool_CalloutThreeDimMenu",
                "Triển khai" + Environment.NewLine + "nhà WC");
            calloutMenuData.ToolTip = "Triển khai WC theo Room: mặt bằng lát sàn, thiết bị, trần và Elevation A/B/C/D; chọn Template từng view.";
            calloutMenuData.LargeImage = KToolRibbonIcons.Create(KToolIcon.WcCreate, 32);
            calloutMenuData.Image = KToolRibbonIcons.Create(KToolIcon.WcCreate, 16);
            var calloutMenu = deploymentPanel.AddItem(calloutMenuData) as PulldownButton;
            if (calloutMenu != null)
            {
                var createCallout = new PushButtonData("KTool_CreateCalloutThreeDim",
                    "Tạo bộ triển khai WC", dllPath, typeof(CreateCalloutThreeDimCommand).FullName);
                createCallout.ToolTip = "Chọn Room WC; tạo các mặt bằng bằng Duplicate, mặt bằng trần RCP và mặt đứng Elevation, Dim theo reference thật.";
                createCallout.LongDescription = "Lớp 1: tâm thiết bị từ mặt hoàn thiện Room. "
                    + "Lớp 2: thông thủy và mép ô cửa có reference. Lớp 3: tổng phủ bì tường. "
                    + "Chọn tên, loại Callout, tỷ lệ, kiểu Dim và khoảng lớp (mm giấy). "
                    + "Dùng reference mặt phẳng thật, bỏ qua mặt xiên/cong không phù hợp.";
                createCallout.Image = KToolRibbonIcons.Create(KToolIcon.WcCreate, 16);
                calloutMenu.AddPushButton(createCallout);
                var batchCallout = new PushButtonData("KTool_BatchCreateCalloutThreeDim",
                    "Triển khai hàng loạt WC", dllPath, typeof(BatchCreateCalloutThreeDimCommand).FullName);
                batchCallout.ToolTip = "Chọn nhiều Room WC ở các tầng, cấu hình một lần và tạo bộ view riêng cho từng phòng.";
                batchCallout.LongDescription = "Tự tìm mặt bằng tầng tương ứng cho mỗi Room. Tạo mặt bằng thiết bị, lát sàn, trần, Elevation được chọn, Dim và sheet theo cấu hình chung. Room không có mặt bằng tầng sẽ được báo bỏ qua.";
                batchCallout.Image = KToolRibbonIcons.Create(KToolIcon.WcBatch, 16);
                calloutMenu.AddPushButton(batchCallout);
                var updateCallout = new PushButtonData("KTool_UpdateCalloutThreeDim",
                    "Cập nhật Dim view WC hiện tại", dllPath, typeof(UpdateCalloutThreeDimCommand).FullName);
                updateCallout.ToolTip = "Cập nhật Dim trong mặt bằng hoặc mặt đứng đã tạo bằng công cụ triển khai WC.";
                updateCallout.LongDescription = "Chỉ thay thế các Dim do tổ hợp lệnh này tạo trong view hiện tại. "
                    + "Giữ các Dim vẽ thủ công. Dùng sau khi chỉnh mô hình hoặc vùng Crop. "
                    + "Tắt Annotation Crop để hiển thị các lớp Dim ngoài mép vùng trích.";
                updateCallout.Image = KToolRibbonIcons.Create(KToolIcon.WcUpdate, 16);
                calloutMenu.AddPushButton(updateCallout);
            }

            deploymentPanel.AddItem(cropOverrideButton);

            PushButtonData btn = new PushButtonData(
                "SDK_HD_DimGrid",
                "Dim" + Environment.NewLine + "Lưới trục",
                dllPath,
                typeof(DimGridCommand).FullName);

            btn.ToolTip = "Dim từng nhịp và Dim tổng cho lưới trục.";
            btn.LongDescription = "Tự động Dim các trục song song (từng nhịp + tổng), "
                                + "sau đó chọn điểm đặt đường Dim. "
                                + "Có thể chọn sẵn các trục trước khi chạy lệnh.";
            btn.LargeImage = KToolRibbonIcons.Create(KToolIcon.DimensionGrid, 32);
            btn.Image = KToolRibbonIcons.Create(KToolIcon.DimensionGrid, 16);

            panel.AddItem(btn);

            PushButtonData btnLevel = new PushButtonData(
                "SDK_HD_DimLevel",
                "Dim" + Environment.NewLine + "Level",
                dllPath,
                typeof(DimLevelCommand).FullName);

            btnLevel.ToolTip = "Dim từng nhịp và Dim tổng cho Level.";
            btnLevel.LongDescription = "Chạy trong view mặt đứng / mặt cắt. Tự động Dim các Level "
                                     + "(từng nhịp + tổng), sau đó chọn điểm đặt đường Dim. "
                                     + "Có thể chọn sẵn các Level trước khi chạy lệnh.";
            btnLevel.LargeImage = KToolRibbonIcons.Create(KToolIcon.DimensionLevel, 32);
            btnLevel.Image = KToolRibbonIcons.Create(KToolIcon.DimensionLevel, 16);

            panel.AddItem(btnLevel);

            PushButtonData btnFlip = new PushButtonData(
                "SDK_HD_FlipDim",
                "Đảo" + Environment.NewLine + "chiều Dim",
                dllPath,
                typeof(FlipDimCommand).FullName);

            btnFlip.ToolTip = "Đảo chiều (lật) đường Dim sang phía đối diện.";
            btnFlip.LongDescription = "Lật các đường Dim đã có sang phía đối diện của đối tượng được Dim "
                                    + "(trục, Level, ...). Có thể chọn sẵn các Dim trước khi chạy lệnh, "
                                    + "hoặc chạy lệnh rồi chọn Dim.";
            btnFlip.LargeImage = KToolRibbonIcons.Create(KToolIcon.FlipDimension, 32);
            btnFlip.Image = KToolRibbonIcons.Create(KToolIcon.FlipDimension, 16);

            panel.AddItem(btnFlip);

            PushButtonData btnWitness = new PushButtonData(
                "SDK_HD_ToggleWitnessLine",
                "Witness" + Environment.NewLine + "Line Control",
                dllPath,
                typeof(ToggleWitnessLineCommand).FullName);

            btnWitness.ToolTip = "Đảo Witness Line Control của Dim (Gap to Element <-> Fixed to Dimension Line).";
            btnWitness.LongDescription = "Đảo thông số Witness Line Control của các Dim đã chọn giữa "
                                       + "Gap to Element và Fixed to Dimension Line. Lệnh tạo Type Dim nhân bản "
                                       + "nên không làm đổi các Dim khác dùng chung Type gốc.";
            btnWitness.LargeImage = KToolRibbonIcons.Create(KToolIcon.WitnessLine, 32);
            btnWitness.Image = KToolRibbonIcons.Create(KToolIcon.WitnessLine, 16);

            panel.AddItem(btnWitness);

            PushButtonData btnFit = new PushButtonData(
                "SDK_HD_FitToCrop",
                "Fit Crop Grid/Level",
                dllPath,
                typeof(FitToCropCommand).FullName);

            btnFit.ToolTip = "Tự động chỉnh Grid và Level vừa khung Crop View.";
            btnFit.LongDescription = "Chỉnh 2D extent của Grid và Level cho vừa khung Crop View của view đang mở "
                                   + "(hoặc nhiều view chọn trong Project Browser). Có thể chọn sẵn Grid/Level "
                                   + "để chỉ xử lý các đối tượng đó. Không đổi hình học model của Grid/Level.";
            btnFit.LargeImage = KToolRibbonIcons.Create(KToolIcon.CropFit, 32);
            btnFit.Image = KToolRibbonIcons.Create(KToolIcon.CropFit, 16);

            PushButtonData btnOffset = new PushButtonData(
                "SDK_HD_FitCropOffset",
                "Offset Fit Crop",
                dllPath,
                typeof(OffsetSettingsCommand).FullName);

            btnOffset.ToolTip = "Cài đặt offset đầu Grid/Level cho lệnh Fit Crop Grid/Level.";
            btnOffset.LongDescription = "Đặt khoảng đầu Grid/Level vươn ra ngoài mép Crop, tách riêng cho đầu có bubble "
                                      + "và đầu không có bubble, theo phương ngang và phương đứng. "
                                      + "Cài đặt được lưu lại cho các lần dùng sau.";
            btnOffset.LargeImage = KToolRibbonIcons.Create(KToolIcon.CropOffset, 32);
            btnOffset.Image = KToolRibbonIcons.Create(KToolIcon.CropOffset, 16);

            var fitAlignTitle = new PushButtonData("SDK_HD_FitCropAlignViewportTitle",
                "Align Title - Căn giữa viewport", dllPath,
                typeof(AlignViewportTitleCommand).FullName);
            fitAlignTitle.ToolTip = "Căn giữa tiêu đề theo chiều ngang của viewport trên Sheet.";
            fitAlignTitle.LongDescription = "Chọn sẵn một hoặc nhiều viewport trên Sheet, hoặc chạy lệnh rồi chọn. "
                + "Căn giữa toàn bộ vùng tiêu đề (kể cả đường gạch), giữ cao độ tiêu đề và vị trí viewport.";
            fitAlignTitle.LargeImage = KToolRibbonIcons.Create(KToolIcon.AlignTitle, 32);
            fitAlignTitle.Image = KToolRibbonIcons.Create(KToolIcon.AlignTitle, 16);
            // Gộp 2 lệnh thành 1 nút xổ xuống "FitCrop"
            PulldownButtonData pullData = new PulldownButtonData("SDK_HD_FitCropMenu", "FitCrop");
            pullData.ToolTip = "Fit Grid/Level vào Crop View và cài đặt offset.";
            pullData.LongDescription = "Bấm để chọn: Fit Crop Grid/Level hoặc Offset Fit Crop (cài đặt offset).";
            pullData.LargeImage = KToolRibbonIcons.Create(KToolIcon.CropFit, 32);
            pullData.Image = KToolRibbonIcons.Create(KToolIcon.CropFit, 16);

            PulldownButton pull = fitPanel.AddItem(pullData) as PulldownButton;
            if (pull != null)
            {
                pull.AddPushButton(btnFit);
                pull.AddPushButton(btnOffset);
                pull.AddPushButton(fitAlignTitle);
            }
            // Panel "Kết cấu": công cụ cho bản vẽ kết cấu
            RibbonPanel structPanel = application.GetRibbonPanels(TAB_NAME)
                                          .FirstOrDefault(p => p.Name == PANEL_STRUCT)
                                      ?? application.CreateRibbonPanel(TAB_NAME, PANEL_STRUCT);

            PushButtonData btnTag = new PushButtonData(
                "SDK_HD_AutoTag",
                "Auto Tag" + Environment.NewLine + "Kết cấu",
                dllPath,
                typeof(AutoTagCommand).FullName);

            btnTag.ToolTip = "Tự động đánh Tag cho cột, dầm, móng, sàn.";
            btnTag.LongDescription = "Tự động đánh Tag cho cấu kiện kết cấu (cột, dầm, móng, sàn) trong view đang mở "
                                   + "hoặc nhiều view chọn trong Project Browser. Có thể chọn sẵn cấu kiện để chỉ Tag "
                                   + "các cấu kiện đó. Dùng family Tag mặc định của từng loại cấu kiện.";
            btnTag.LargeImage = KToolRibbonIcons.Create(KToolIcon.AutoTag, 32);
            btnTag.Image = KToolRibbonIcons.Create(KToolIcon.AutoTag, 16);

            PushButtonData btnJoin = new PushButtonData(
                "KTool_JoinGeometry", "Join / Unjoin", dllPath,
                typeof(JoinGeometryCommand).FullName);
            btnJoin.ToolTip = "Join/Unjoin theo quy tắc ưu tiên cắt, hỗ trợ đảo chiều Join.";
            btnJoin.LongDescription = "Chọn nhóm ưu tiên cắt ở cột trái và nhóm bị cắt ở cột phải. "
                + "Áp dụng trong view, toàn dự án, đối tượng đã chọn hoặc vùng quét. "
                + "Có nhập/xuất quy tắc và hai chế độ cắt theo đối tượng được chọn.";
            btnJoin.LargeImage = KToolRibbonIcons.Create(KToolIcon.JoinGeometry, 32);
            btnJoin.Image = KToolRibbonIcons.Create(KToolIcon.JoinGeometry, 16);
            // StairSlabPro - tự tạo/cập nhật bản bê tông dưới đối tượng Revit Stair.
            var stairSlabData = new PushButtonData(
                "KTool_StairSlabPro", "Bản thang" + Environment.NewLine + "BTCT",
                dllPath, typeof(StairSlabProCommand).FullName);
            stairSlabData.ToolTip = "Tạo bản nghiêng và chiếu nghỉ BTCT từ Stair; hỗ trợ DirectShape hoặc Family.";
            stairSlabData.LargeImage = KToolRibbonIcons.Create(KToolIcon.StairSlab, 32);
            stairSlabData.Image = KToolRibbonIcons.Create(KToolIcon.StairSlab, 16);
            var flatLandingData = new PushButtonData(
                "KTool_StairFlatLanding", "Chiếu nghỉ" + Environment.NewLine + "phẳng",
                dllPath, typeof(StairFlatLandingCommand).FullName);
            flatLandingData.ToolTip = "Tạo phương án Stair chữ U: ba vế thẳng, hai chiếu nghỉ đi lại phẳng; hiển thị chiều dài vế cuối cần thay đổi.";
            flatLandingData.LargeImage = KToolRibbonIcons.Create(KToolIcon.FlatLanding, 32);
            flatLandingData.Image = KToolRibbonIcons.Create(KToolIcon.FlatLanding, 16);



            structPanel.AddItem(stairSlabData);
            structPanel.AddItem(flatLandingData);
            structPanel.AddItem(btnJoin);
            structPanel.AddItem(btnTag);

            // Panel "Điện": vẽ dây điện bằng Detail Line theo cỡ dây
            RibbonPanel elecPanel = application.GetRibbonPanels(TAB_NAME)
                                        .FirstOrDefault(p => p.Name == PANEL_ELEC)
                                    ?? application.CreateRibbonPanel(TAB_NAME, PANEL_ELEC);

            PushButtonData btnWireStyle = new PushButtonData(
                "SDK_HD_CreateWireStyles",
                "Tạo Style" + Environment.NewLine + "dây điện",
                dllPath,
                typeof(CreateWireStylesCommand).FullName);

            btnWireStyle.ToolTip = "Tạo hàng loạt Line Style cho các cỡ dây điện.";
            btnWireStyle.LongDescription = "Tạo (hoặc cập nhật) các Line Style \"Dây 1.5\", \"Dây 2.5\", \"Dây 4.0\", "
                                         + "\"Dây 6.0\", \"Dây 10.0\", \"Dây 16.0\" với màu và độ dày nét theo quy ước.";
            btnWireStyle.LargeImage = KToolRibbonIcons.Create(KToolIcon.WireStyle, 32);
            btnWireStyle.Image = KToolRibbonIcons.Create(KToolIcon.WireStyle, 16);

            PushButtonData btnDrawWire = new PushButtonData(
                "SDK_HD_DrawWire",
                "Vẽ" + Environment.NewLine + "dây điện",
                dllPath,
                typeof(DrawWireCommand).FullName);

            btnDrawWire.ToolTip = "Vẽ Detail Line theo cỡ dây đã chọn.";
            btnDrawWire.LongDescription = "Chọn cỡ dây, sau đó bấm lần lượt các điểm để vẽ đường dây gấp khúc "
                                        + "bằng Detail Line với Line Style tương ứng. Bấm Esc để kết thúc. "
                                        + "Hỗ trợ snap vào điểm đầu, điểm giữa, giao điểm và tâm.";
            btnDrawWire.LargeImage = KToolRibbonIcons.Create(KToolIcon.DrawWire, 32);
            btnDrawWire.Image = KToolRibbonIcons.Create(KToolIcon.DrawWire, 16);

            PushButtonData btnWireSize = new PushButtonData(
                "SDK_HD_ChangeWireSize",
                "Đổi cỡ" + Environment.NewLine + "dây",
                dllPath,
                typeof(ChangeWireSizeCommand).FullName);

            btnWireSize.ToolTip = "Đổi cỡ dây (Line Style) cho các Detail Line đã chọn.";
            btnWireSize.LongDescription = "Chọn các Detail Line (hoặc chọn sẵn trước khi chạy lệnh), "
                                        + "chọn cỡ dây, lệnh gán Line Style tương ứng cho tất cả.";
            btnWireSize.LargeImage = KToolRibbonIcons.Create(KToolIcon.WireSize, 32);
            btnWireSize.Image = KToolRibbonIcons.Create(KToolIcon.WireSize, 16);

            PushButtonData btnWireLabel = new PushButtonData(
                "SDK_HD_LabelWire",
                "Ghi chú" + Environment.NewLine + "dây điện",
                dllPath,
                typeof(AddWireLabelsCommand).FullName);

            btnWireLabel.ToolTip = "Ghi chú tên (cỡ dây) lên các đường dây điện.";
            btnWireLabel.LongDescription = "Ghi tên Line Style (ví dụ \"Dây 2.5\") lên các Detail Line dây điện trong view, "
                                         + "xoay theo hướng dây. Có thể chọn sẵn các đường để chỉ ghi chú các đường đó. "
                                         + "Chạy lại không tạo ghi chú trùng.";
            btnWireLabel.LargeImage = KToolRibbonIcons.Create(KToolIcon.WireLabel, 32);
            btnWireLabel.Image = KToolRibbonIcons.Create(KToolIcon.WireLabel, 16);
            PushButtonData btnWireTag = new PushButtonData(
    "SDK_HD_TagWire",
    "Tag" + Environment.NewLine + "dây điện",
    dllPath,
    typeof(TagWireLinesCommand).FullName);
            btnWireTag.ToolTip = "Tag by Category cho các đường dây điện.";
            btnWireTag.LargeImage = KToolRibbonIcons.Create(KToolIcon.WireTag, 32);
            btnWireTag.Image = KToolRibbonIcons.Create(KToolIcon.WireTag, 16);
            elecPanel.AddItem(btnWireStyle);
            elecPanel.AddItem(btnWireSize);
            elecPanel.AddItem(btnDrawWire);
            elecPanel.AddItem(btnWireLabel);
            elecPanel.AddItem(btnWireTag);

            // Panel "Nước": vẽ đường ống cấp thoát nước bằng Detail Line
            RibbonPanel waterPanel = application.GetRibbonPanels(TAB_NAME)
                                         .FirstOrDefault(p => p.Name == PANEL_WATER)
                                     ?? application.CreateRibbonPanel(TAB_NAME, PANEL_WATER);

            PushButtonData btnPipeStyle = new PushButtonData(
                "SDK_HD_CreatePipeStyles",
                "Tạo Style" + Environment.NewLine + "ống nước",
                dllPath,
                typeof(CreatePipeStylesCommand).FullName);

            btnPipeStyle.ToolTip = "Tạo hàng loạt Line Style cho các hệ thống ống nước.";
            btnPipeStyle.LongDescription = "Tạo (hoặc cập nhật) Line Style cho 8 hệ thống: CW, C, HW, WP, RP, VP, SP, DP "
                                         + "với màu, độ dày nét và kiểu nét theo quy ước, kèm kiểu text ghi ký hiệu. "
                                         + "Cần chạy lệnh này trước khi vẽ ống.";
            btnPipeStyle.LargeImage = KToolRibbonIcons.Create(KToolIcon.PipeStyle, 32);
            btnPipeStyle.Image = KToolRibbonIcons.Create(KToolIcon.PipeStyle, 16);

            // Nút xổ xuống "Vẽ ống nước": mỗi hệ thống ống một lệnh, bấm là vẽ ngay
            PulldownButtonData drawPipeMenu = new PulldownButtonData(
                "SDK_HD_DrawPipeMenu",
                "Vẽ" + Environment.NewLine + "ống nước");
            drawPipeMenu.ToolTip = "Vẽ đường ống bằng Detail Line theo hệ thống.";
            drawPipeMenu.LongDescription = "Chọn hệ thống ống (CW, C, HW, WP, RP, VP, SP, DP), sau đó bấm lần lượt các điểm "
                                         + "để vẽ đường ống gấp khúc. Bấm Esc để kết thúc. "
                                         + "Ký hiệu hệ thống được ghi giữa mỗi đoạn.";
            drawPipeMenu.LargeImage = KToolRibbonIcons.Create(KToolIcon.DrawPipe, 32);
            drawPipeMenu.Image = KToolRibbonIcons.Create(KToolIcon.DrawPipe, 16);



            PushButtonData btnPipeType = new PushButtonData(
                "SDK_HD_ChangePipeType",
                "Đổi loại" + Environment.NewLine + "ống",
                dllPath,
                typeof(ChangePipeTypeCommand).FullName);

            btnPipeType.ToolTip = "Đổi hệ thống ống (Line Style) cho các Detail Line đã chọn.";
            btnPipeType.LongDescription = "Chọn các Detail Line (hoặc chọn sẵn trước khi chạy lệnh), chọn hệ thống ống, "
                                        + "lệnh gán Line Style tương ứng và cập nhật ký hiệu cho tất cả.";
            btnPipeType.LargeImage = KToolRibbonIcons.Create(KToolIcon.PipeType, 32);
            btnPipeType.Image = KToolRibbonIcons.Create(KToolIcon.PipeType, 16);

            PushButtonData btnDetailLine = new PushButtonData(
                "SDK_HD_NativeDetailLine",
                "Detail Line" + Environment.NewLine + "mặc định",
                dllPath,
                typeof(NativeDetailLineCommand).FullName);

            btnDetailLine.ToolTip = "Mở lệnh Detail Line mặc định của Revit.";
            btnDetailLine.LongDescription = "Mở lệnh Annotate > Detail Line. Sau khi tạo Line Style ống nước, "
                                          + "chọn Line Style tương ứng trong thanh Modify | Place Detail Lines.";
            btnDetailLine.LargeImage = KToolRibbonIcons.Create(KToolIcon.DetailLine, 32);
            btnDetailLine.Image = KToolRibbonIcons.Create(KToolIcon.DetailLine, 16);

            PushButtonData btnPipeLabel = new PushButtonData(
                "SDK_HD_LabelPipe",
                "Ghi chú" + Environment.NewLine + "ống nước",
                dllPath,
                typeof(AddPipeLabelsCommand).FullName);

            btnPipeLabel.ToolTip = "Ghi chú tên (ký hiệu hệ thống) lên các đường ống nước.";
            btnPipeLabel.LongDescription = "Ghi ký hiệu hệ thống (CW, C, HW, WP, RP, VP, SP, DP) lên các Detail Line ống nước "
                                         + "trong view, xoay theo hướng ống. Có thể chọn sẵn các đường để chỉ ghi chú các đường đó. "
                                         + "Chạy lại không tạo ghi chú trùng.";
            btnPipeLabel.LargeImage = KToolRibbonIcons.Create(KToolIcon.WireLabel, 32);
            btnPipeLabel.Image = KToolRibbonIcons.Create(KToolIcon.WireLabel, 16);
            PushButtonData btnPipeTag = new PushButtonData(
    "SDK_HD_TagPipe",
    "Tag" + Environment.NewLine + "ống nước",
    dllPath,
    typeof(TagPipeLinesCommand).FullName);
            btnPipeTag.ToolTip = "Tag by Category cho các đường ống nước.";
            btnPipeTag.LargeImage = KToolRibbonIcons.Create(KToolIcon.WireTag, 32);
            btnPipeTag.Image = KToolRibbonIcons.Create(KToolIcon.WireTag, 16);
            waterPanel.AddItem(btnPipeStyle);
            waterPanel.AddItem(btnPipeType);
            waterPanel.AddItem(btnDetailLine);
            PulldownButton drawPipePull = waterPanel.AddItem(drawPipeMenu) as PulldownButton;
            if (drawPipePull != null)
            {
                AddPipeItem(drawPipePull, dllPath, "CW", "Cấp nước lạnh", typeof(DrawPipeCwCommand));
                AddPipeItem(drawPipePull, dllPath, "C", "Cấp nước thủy cục", typeof(DrawPipeCCommand));
                AddPipeItem(drawPipePull, dllPath, "HW", "Cấp nước nóng", typeof(DrawPipeHwCommand));
                AddPipeItem(drawPipePull, dllPath, "WP", "Thoát nước thải", typeof(DrawPipeWpCommand));
                AddPipeItem(drawPipePull, dllPath, "RP", "Thoát nước mưa", typeof(DrawPipeRpCommand));
                AddPipeItem(drawPipePull, dllPath, "VP", "Thông hơi", typeof(DrawPipeVpCommand));
                AddPipeItem(drawPipePull, dllPath, "SP", "Thoát phân", typeof(DrawPipeSpCommand));
                AddPipeItem(drawPipePull, dllPath, "DP", "Thoát nước máy lạnh", typeof(DrawPipeDpCommand));
            }
            waterPanel.AddItem(btnPipeLabel);
            waterPanel.AddItem(btnPipeTag);

            // Tạo panel "PaintPro" nếu chưa có
            paintPanel = application.GetRibbonPanels(TAB_NAME)
                .FirstOrDefault(p => p.Name == "PaintPro")
                ?? application.CreateRibbonPanel(TAB_NAME, "PaintPro");

            // Nút mở giao diện PaintPro
            PushButtonData btnPaintPro = new PushButtonData(
                "SDK_HD_PaintPro",
                "Paint" + Environment.NewLine + "Pro",
                dllPath,
                typeof(PaintProCommand).FullName);

            btnPaintPro.ToolTip = "Mở giao diện Paint Pro để quản lý vật liệu, màu sắc và texture.";
            btnPaintPro.LongDescription = "Công cụ hỗ trợ chọn, chỉnh sửa, và gán vật liệu cho mô hình Revit. "
                + "Cho phép xem trước màu RGB, texture, và danh sách vật liệu hiện có.";
            btnPaintPro.LargeImage = KToolRibbonIcons.Create(KToolIcon.Paint, 32);
            btnPaintPro.Image = KToolRibbonIcons.Create(KToolIcon.Paint, 16);

            paintPanel.AddItem(btnPaintPro);

            // Hoàn thiện theo Room, ngạch cửa và viên gạch 3D.
            RibbonPanel finishPanel = application.GetRibbonPanels(TAB_NAME)
                .FirstOrDefault(p => p.Name == "Hoàn thiện")
                ?? application.CreateRibbonPanel(TAB_NAME, "Hoàn thiện");
            var finishMenuData = new PulldownButtonData("KTool_FinishMenu", "Hoàn thiện");
            finishMenuData.ToolTip = "Sàn, trần, tường theo Room; ngạch cửa theo Family; viên gạch ốp lát 3D.";
            finishMenuData.LargeImage = KToolRibbonIcons.Create(KToolIcon.FinishMenu, 32);
            finishMenuData.Image = KToolRibbonIcons.Create(KToolIcon.FinishMenu, 16);
            PulldownButton finishMenu = finishPanel.AddItem(finishMenuData) as PulldownButton;
            if (finishMenu != null)
            {
                Type[] finishCommands = { typeof(Finish.FinishFloorCommand), typeof(Finish.FinishWallCommand),
                    typeof(Finish.FinishCeilingCommand), typeof(Finish.FinishTileCommand), typeof(Finish.FinishThresholdCommand) };
                string[] finishLabels = { "Tạo sàn hoàn thiện (Room)", "Tạo tường hoàn thiện (trừ lỗ cửa)",
                    "Tạo trần hoàn thiện (Room)", "Tạo viên gạch ốp lát 3D", "Tạo ngạch cửa (Family)" };
                int[] finishTooltipIndex = { 0, 2, 1, 4, 3 };
                KToolIcon[] finishIcons = { KToolIcon.FinishFloor, KToolIcon.FinishWall, KToolIcon.FinishCeiling,
                    KToolIcon.FinishTile, KToolIcon.FinishThreshold };
                for (int i = 0; i < finishCommands.Length; i++)
                {
                    var item = new PushButtonData("KTool_Finish_" + i, finishLabels[i], dllPath, finishCommands[i].FullName);
                    item.ToolTip = Finish.FinishWindow.Names[finishTooltipIndex[i]];
                    item.LongDescription = "Chọn nguồn và loại cấu kiện, nhập thông số bằng mm. "
                        + "Có chống tạo trùng, thay thế kết quả KTool và Undo lượt tạo. "
                        + (i == 3 ? "Mỗi viên là một Generic Model 3D, cắt theo mặt phẳng sàn/tường." : "");
                    KToolRibbonIcons.Set(item, finishIcons[i]);
                    finishMenu.AddPushButton(item);
                }
            }

            var lintelData = new PushButtonData("KTool_Lintel", "Tạo lanh tô", dllPath, typeof(Finish.LintelCommand).FullName);
            lintelData.ToolTip = "Tạo lanh tô trên cửa đi/cửa sổ bằng Family dầm, tùy chỉnh chiều dài gác và khe hở.";
            lintelData.LargeImage = KToolRibbonIcons.Create(KToolIcon.Lintel, 32);
            lintelData.Image = KToolRibbonIcons.Create(KToolIcon.Lintel, 16);
            finishPanel.AddItem(lintelData);
            var plasterData = new PushButtonData("KTool_Plaster", "Tạo vữa trát", dllPath, typeof(Finish.PlasterCommand).FullName);
            plasterData.ToolTip = "Tạo vữa trát theo Room bằng Wall Type riêng, trừ lỗ cửa. Chạy độc lập với tường hoàn thiện.";
            plasterData.LargeImage = KToolRibbonIcons.Create(KToolIcon.Plaster, 32);
            plasterData.Image = KToolRibbonIcons.Create(KToolIcon.Plaster, 16);
            finishPanel.AddItem(plasterData);

            // Door Detail Legend - bản sạch, độc lập với Door Schedule Pro cũ.
            // Bọc riêng try/catch để lỗi ở nút này không ảnh hưởng các panel khác.
            try
            {
                RibbonPanel doorDetailPanel = application.GetRibbonPanels(TAB_NAME)
                    .FirstOrDefault(p => p.Name == "Chi tiết cửa")
                    ?? application.CreateRibbonPanel(TAB_NAME, "Chi tiết cửa");

                PushButtonData btnDoorDetail = new PushButtonData(
                    "KTool_DoorDetailLegend_Clean",
                    "Triển khai" + Environment.NewLine + "chi tiết cửa",
                    dllPath,
                    typeof(DoorDetailLegend.DoorDetailLegendCommand).FullName);
                btnDoorDetail.ToolTip = "Chọn cửa và thiết lập WPF để tạo Legend chi tiết cửa.";
                btnDoorDetail.LongDescription = "Mỗi loại cửa tạo một Legend riêng với mặt đứng, mặt bằng, Tag và Dim theo thiết lập WPF. "
                    + "Tự tìm Legend Component bất kỳ để khởi tạo, không yêu cầu Legend mẫu theo tên và không sao chép bố cục cũ.";
                btnDoorDetail.LargeImage = KToolRibbonIcons.Create(KToolIcon.DoorDetail, 32);
                btnDoorDetail.Image = KToolRibbonIcons.Create(KToolIcon.DoorDetail, 16);
                doorDetailPanel.AddItem(btnDoorDetail);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("KTool - Chi tiết cửa", "Không tạo được nút: " + ex.Message);
            }

            RibbonPanel utilitiesPanel = application.GetRibbonPanels(TAB_NAME)
                .FirstOrDefault(p => p.Name == PANEL_UTILITIES)
                ?? application.CreateRibbonPanel(TAB_NAME, PANEL_UTILITIES);
            var cleanupButton = new PushButtonData("KTool_FileCleanup", "Dọn dẹp" + Environment.NewLine + "file", dllPath, typeof(FileCleanupCommand).FullName);
            cleanupButton.ToolTip = "Tìm và xóa Views, Sheets, Templates, Schedules, Rooms, Line Patterns, Parameters, Filters và file CAD đã nhập.";
            cleanupButton.LongDescription = "Chọn nhóm ở danh sách bên phải, tìm kiếm và đánh dấu mục cần xóa. Xem lại xác nhận trước khi áp dụng; có thể Undo trong Revit.";
            KToolRibbonIcons.Set(cleanupButton, KToolIcon.FileCleanup);
            utilitiesPanel.AddItem(cleanupButton);

            if (SHOW_ICON_DIAGNOSTIC && _missingIcons.Count > 0)
            {
                string[] embedded = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                    .Where(n => n.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                TaskDialog.Show("KTool - Icon",
                    "Không tìm thấy " + _missingIcons.Count + " icon:" + Environment.NewLine
                    + string.Join(", ", _missingIcons.Distinct()) + Environment.NewLine + Environment.NewLine
                    + "PNG đang được nhúng trong DLL: "
                    + (embedded.Length == 0 ? "(không có file nào)" : string.Join(", ", embedded.Take(30))));
            }

            return Result.Succeeded;
        }

        private static KToolIcon PipeIcon(string code)
        {
            switch (code)
            {
                case "CW": return KToolIcon.PipeCW;
                case "C": return KToolIcon.PipeC;
                case "HW": return KToolIcon.PipeHW;
                case "WP": return KToolIcon.PipeWP;
                case "RP": return KToolIcon.PipeRP;
                case "VP": return KToolIcon.PipeVP;
                case "SP": return KToolIcon.PipeSP;
                default: return KToolIcon.PipeDP;
            }
        }
        /// <summary>Thêm một lệnh vẽ ống (một hệ thống) vào nút xổ xuống "Vẽ ống nước".</summary>
        private static void AddPipeItem(
            PulldownButton menu, string dllPath, string code, string name, Type commandType)
        {
            PushButtonData data = new PushButtonData(
                "SDK_HD_DrawPipe_" + code,
                code + " - " + name,
                dllPath,
                commandType.FullName);

            data.ToolTip = "Vẽ ống " + code + " - " + name + " bằng Detail Line.";
            data.LongDescription = "Bấm lần lượt các điểm để vẽ đường ống " + code + " gấp khúc bằng Detail Line. "
                                 + "Bấm Esc để kết thúc. Ký hiệu " + code + " được ghi giữa mỗi đoạn. "
                                 + "Cần chạy \"Tạo Style ống nước\" trước.";
            KToolRibbonIcons.Set(data, PipeIcon(code));

            menu.AddPushButton(data);
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        /// <summary>
        /// Đọc icon PNG: ưu tiên resource nhúng (Embedded Resource); nếu không có thì đọc file
        /// trong thư mục Icons hoặc cùng thư mục với DLL. Không có icon thì nút vẫn hoạt động.
        /// </summary>
        private static BitmapImage LoadIcon(string fileName)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();

                string resName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                                         || n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
                if (resName != null)
                {
                    using (Stream s = asm.GetManifestResourceStream(resName))
                    {
                        return ReadImage(s);
                    }
                }

                string dir = Path.GetDirectoryName(asm.Location);
                string[] candidates =
                {
                    Path.Combine(dir, "Icons", fileName),
                    Path.Combine(dir, fileName)
                };
                foreach (string path in candidates)
                {
                    if (!File.Exists(path)) continue;
                    using (FileStream fs = File.OpenRead(path))
                    {
                        return ReadImage(fs);
                    }
                }

                _missingIcons.Add(fileName);
                return null;
            }
            catch (Exception)
            {
                _missingIcons.Add(fileName);
                return null;
            }
        }

        private static BitmapImage ReadImage(Stream s)
        {
            BitmapImage img = new BitmapImage();
            img.BeginInit();
            img.StreamSource = s;
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            return img;
        }
    }
}













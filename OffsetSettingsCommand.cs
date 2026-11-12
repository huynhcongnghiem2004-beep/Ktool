using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    /// <summary>Mở hộp thoại cài đặt offset cho lệnh Fit Crop Grid/Level.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class OffsetSettingsCommand : IExternalCommand
    {
        private class RevitWindow : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }

            public RevitWindow(IntPtr handle)
            {
                Handle = handle;
            }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            FitCropSettings settings = FitCropSettings.Load();

            using (OffsetSettingsForm form = new OffsetSettingsForm(settings))
            {
                RevitWindow owner = new RevitWindow(commandData.Application.MainWindowHandle);
                if (form.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
                    return Result.Cancelled;
            }

            try
            {
                settings.Save();
            }
            catch (Exception ex)
            {
                message = "Không lưu được cài đặt: " + ex.Message;
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }
}

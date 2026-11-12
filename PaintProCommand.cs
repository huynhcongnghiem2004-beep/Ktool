using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public class PaintProCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = commandData.Application.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    message = "Vui lòng mở một dự án Revit trước khi chạy PaintPro.";
                    return Result.Failed;
                }

                var window = new PaintProWindow(commandData.Application.ActiveUIDocument);
                // Modeless: mọi thao tác Revit sau khi mở đều chạy qua ExternalEvent.
                var owner = System.Windows.Interop.HwndSource.FromHwnd(
                    commandData.Application.MainWindowHandle);
                if (owner != null && owner.RootVisual is System.Windows.Window)
                    window.Owner = (System.Windows.Window)owner.RootVisual;
                else
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = commandData.Application.MainWindowHandle;
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

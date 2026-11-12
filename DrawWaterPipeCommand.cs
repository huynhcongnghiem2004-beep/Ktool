using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public class DrawWaterPipeCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc.Document;
                View view = doc.ActiveView;

                string code = GetPipeCode(commandData);

                if (string.IsNullOrWhiteSpace(code))
                    return Result.Cancelled;

                WaterPipeDefinition pipe = WaterPipeDefinitions.Get(code);

                if (pipe == null)
                {
                    message = "Không tìm thấy định nghĩa cho loại ống: " + code;
                    return Result.Failed;
                }

                if (!SupportsDetailLines(view))
                {
                    TaskDialog.Show(
                        "Thông báo",
                        "View hiện tại không hỗ trợ Detail Line.");

                    return Result.Cancelled;
                }

                GraphicsStyle graphicsStyle = GetGraphicsStyle(doc, pipe);
                TextNoteType textNoteType = GetDefaultTextNoteType(doc);
                double shortCurveTolerance = uiapp.Application.ShortCurveTolerance;

                int createdCount = DrawPipeSegments(
                    uidoc,
                    doc,
                    view,
                    pipe,
                    graphicsStyle,
                    textNoteType,
                    shortCurveTolerance);

                return createdCount > 0
                    ? Result.Succeeded
                    : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string GetPipeCode(ExternalCommandData commandData)
        {
            string code = null;

            if (commandData.JournalData.ContainsKey("Id"))
                code = commandData.JournalData["Id"];

            if (string.IsNullOrWhiteSpace(code))
                code = ShowPipeSelectionDialog();

            return string.IsNullOrWhiteSpace(code)
                ? null
                : code.Trim().ToUpperInvariant();
        }

        private static string ShowPipeSelectionDialog()
        {
            TaskDialog dialog = new TaskDialog("Chọn loại ống nước")
            {
                MainInstruction = "Chọn loại ống cần vẽ:"
            };

            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink1,
                "CW - Cấp nước lạnh");

            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink2,
                "HW - Cấp nước nóng");

            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink3,
                "VP - Thông hơi");

            dialog.AddCommandLink(
                TaskDialogCommandLinkId.CommandLink4,
                "SP - Thoát phân");

            switch (dialog.Show())
            {
                case TaskDialogResult.CommandLink1:
                    return "CW";

                case TaskDialogResult.CommandLink2:
                    return "HW";

                case TaskDialogResult.CommandLink3:
                    return "VP";

                case TaskDialogResult.CommandLink4:
                    return "SP";

                default:
                    return null;
            }
        }

        private static int DrawPipeSegments(
            UIDocument uidoc,
            Document doc,
            View view,
            WaterPipeDefinition pipe,
            GraphicsStyle graphicsStyle,
            TextNoteType textNoteType,
            double shortCurveTolerance)
        {
            int createdCount = 0;
            XYZ previousPoint = null;

            using (TransactionGroup group = new TransactionGroup(
                doc,
                "KTool - Vẽ ống " + pipe.Code))
            {
                group.Start();

                try
                {
                    while (true)
                    {
                        XYZ pickedPoint;

                        try
                        {
                            string prompt = previousPoint == null
                                ? pipe.Code + " - Chọn điểm đầu (Esc để kết thúc)"
                                : pipe.Code + " - Chọn điểm tiếp theo (Esc để kết thúc)";

                            pickedPoint = uidoc.Selection.PickPoint(
                                ObjectSnapTypes.Endpoints |
                                ObjectSnapTypes.Midpoints |
                                ObjectSnapTypes.Intersections |
                                ObjectSnapTypes.Centers,
                                prompt);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break;
                        }

                        XYZ currentPoint = ProjectToView(pickedPoint, view);

                        if (previousPoint == null)
                        {
                            previousPoint = currentPoint;
                            continue;
                        }

                        double segmentLength = previousPoint.DistanceTo(currentPoint);

                        if (segmentLength <= shortCurveTolerance)
                            continue;

                        CreatePipeSegment(
                            doc,
                            view,
                            pipe,
                            previousPoint,
                            currentPoint,
                            graphicsStyle,
                            textNoteType);

                        previousPoint = currentPoint;
                        createdCount++;
                    }

                    if (createdCount > 0)
                        group.Assimilate();
                    else
                        group.RollBack();
                }
                catch
                {
                    if (group.GetStatus() == TransactionStatus.Started)
                        group.RollBack();

                    throw;
                }
            }

            return createdCount;
        }

        private static void CreatePipeSegment(
            Document doc,
            View view,
            WaterPipeDefinition pipe,
            XYZ startPoint,
            XYZ endPoint,
            GraphicsStyle graphicsStyle,
            TextNoteType textNoteType)
        {
            using (Transaction transaction = new Transaction(
                doc,
                "Vẽ ống " + pipe.Code))
            {
                transaction.Start();

                Line line = Line.CreateBound(startPoint, endPoint);
                DetailCurve detailCurve = doc.Create.NewDetailCurve(view, line);
                detailCurve.LineStyle = graphicsStyle;

                // Ghi chú tên ống: xoay theo hướng ống, nằm phía trên nét
                LineLabelHelper.CreateLabel(
                    doc,
                    view,
                    textNoteType,
                    startPoint,
                    endPoint,
                    pipe.Code);

                transaction.Commit();
            }
        }

        private static GraphicsStyle GetGraphicsStyle(
            Document doc,
            WaterPipeDefinition pipe)
        {
            Category linesCategory =
                doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);

            if (linesCategory == null)
            {
                throw new InvalidOperationException(
                    "Không tìm thấy danh mục Lines trong tài liệu.");
            }

            Category subCategory =
                linesCategory.SubCategories.get_Item(pipe.StyleName);

            if (subCategory == null)
            {
                throw new InvalidOperationException(
                    "Không tìm thấy line style: " + pipe.StyleName);
            }

            GraphicsStyle graphicsStyle =
                subCategory.GetGraphicsStyle(
                    GraphicsStyleType.Projection);

            if (graphicsStyle == null)
            {
                throw new InvalidOperationException(
                    "Không thể lấy GraphicsStyle cho: " + pipe.StyleName);
            }

            return graphicsStyle;
        }

        private static TextNoteType GetDefaultTextNoteType(Document doc)
        {
            // Text Note Type mặc định của project
            return LineLabelHelper.DefaultTextNoteType(doc);
        }

        private static XYZ ProjectToView(XYZ point, View view)
        {
            XYZ normal = view.ViewDirection;
            double distance =
                (point - view.Origin).DotProduct(normal);

            return point - distance * normal;
        }

        private static bool SupportsDetailLines(View view)
        {
            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                case ViewType.DraftingView:
                    return true;

                default:
                    return false;
            }
        }
    }
}
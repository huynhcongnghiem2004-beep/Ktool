using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitCancel = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace SDK_HD.Dim
{
    internal enum PaintProAction { None, Refresh, Pick, Paint }

    internal sealed class PaintProEventHandler : IExternalEventHandler
    {
        private readonly PaintProWindow _window;
        private readonly UIDocument _uiDoc;
        private PaintProAction _action;
        private ElementId _materialId = ElementId.InvalidElementId;

        public PaintProEventHandler(PaintProWindow window, UIDocument uiDoc)
        {
            _window = window;
            _uiDoc = uiDoc;
        }

        public void SetRequest(PaintProAction action, ElementId materialId)
        {
            _action = action;
            _materialId = materialId;
        }

        public string GetName() { return "KTool PaintPro - Revit API Handler"; }

        // Không dùng toán tử != trên wrapper Document: có thể so sánh wrapper,
        // thay vì nhận diện cùng một tài liệu Revit.
        private static bool IsSameDocument(Document active, Document original)
        {
            if (active == null || original == null) return false;
            if (!active.IsValidObject || !original.IsValidObject) return false;
            if (Object.ReferenceEquals(active, original) || active.Equals(original)) return true;

            // Dự án đã lưu: đối chiếu đường dẫn đầy đủ (không dùng Title đơn lẻ).
            string activePath = active.PathName;
            string originalPath = original.PathName;
            return !string.IsNullOrWhiteSpace(activePath)
                && !string.IsNullOrWhiteSpace(originalPath)
                && string.Equals(activePath, originalPath, StringComparison.OrdinalIgnoreCase);
        }

        public void Execute(UIApplication app)
        {
            PaintProAction action = _action;
            ElementId materialId = _materialId;
            _action = PaintProAction.None;
            _materialId = ElementId.InvalidElementId;
            string status = "";
            ElementId pickedId = ElementId.InvalidElementId;
            try
            {
                UIDocument activeUiDoc = app.ActiveUIDocument;
                if (activeUiDoc == null)
                    throw new InvalidOperationException("Chưa có dự án Revit đang hoạt động.");

                Document doc = activeUiDoc.Document;
                Document originalDoc = _uiDoc.Document;
                if (!IsSameDocument(doc, originalDoc))
                    throw new InvalidOperationException(
                        "Bạn đang ở dự án khác. Hãy kích hoạt dự án đã mở PaintPro, " +
                        "hoặc đóng PaintPro rồi mở lại trong dự án hiện tại.");
                if (action == PaintProAction.Refresh)
                {
                    _window.RefreshFromRevit();
                    status = "Đã cập nhật danh sách vật liệu.";
                }
                else if (action == PaintProAction.Pick || action == PaintProAction.Paint)
                {
                    if (action == PaintProAction.Paint && doc.GetElement(materialId) as Material == null)
                        throw new InvalidOperationException("Vật liệu đã chọn không còn tồn tại trong dự án.");

                    int painted = 0;
                    int skipped = 0;
                    // Mỗi lần PickObject được gọi từ Execute của ExternalEvent, nên Transaction hợp lệ.
                    // Esc kết thúc vòng lặp; không cần nhấn nút Paint lại.
                    while (true)
                    {
                        Reference r;
                        try
                        {
                            r = activeUiDoc.Selection.PickObject(ObjectType.Face,
                                action == PaintProAction.Pick
                                    ? "PaintPro: Chọn một mặt để hút vật liệu (Esc để hủy)"
                                    : "PaintPro: Chọn mặt tiếp theo để sơn; nhấn Esc để kết thúc");
                        }
                        catch (RevitCancel)
                        {
                            status = action == PaintProAction.Paint
                                ? string.Format("Đã Paint {0} mặt. Bỏ qua {1} mặt. Nhấn Paint để bắt đầu lượt mới.", painted, skipped)
                                : "Đã hủy thao tác hút vật liệu.";
                            break;
                        }

                        Element element = doc.GetElement(r.ElementId);
                        Face face = element == null ? null : element.GetGeometryObjectFromReference(r) as Face;
                        if (face == null)
                        {
                            skipped++;
                            if (action == PaintProAction.Pick)
                                throw new InvalidOperationException("Không lấy được hình học bề mặt.");
                            continue;
                        }

                        if (action == PaintProAction.Pick)
                        {
                            pickedId = doc.IsPainted(element.Id, face)
                                ? doc.GetPaintedMaterial(element.Id, face)
                                : face.MaterialElementId;
                            Material material = doc.GetElement(pickedId) as Material;
                            status = material == null ? "Mặt này chưa có vật liệu hợp lệ." : "Đã hút vật liệu: " + material.Name;
                            break;
                        }

                        // Một transaction cho mỗi mặt: mặt lỗi không làm mất các mặt đã sơn.
                        try
                        {
                            using (Transaction tx = new Transaction(doc, "PaintPro - Paint face"))
                            {
                                tx.Start();
                                if (doc.IsPainted(element.Id, face))
                                {
                                    // Không cần gỡ và sơn lại nếu đã đúng vật liệu.
                                    if (doc.GetPaintedMaterial(element.Id, face) == materialId)
                                    {
                                        tx.RollBack();
                                        skipped++;
                                        continue;
                                    }
                                    doc.RemovePaint(element.Id, face);
                                }
                                doc.Paint(element.Id, face, materialId);
                                tx.Commit();
                                painted++;
                            }
                        }
                        catch (Exception)
                        {
                            skipped++;
                            // Tiếp tục chọn mặt khác; tránh chặn cả phiên Paint.
                        }
                    }
                }
            }
            catch (RevitCancel)
            {
                status = "Đã hủy thao tác.";
            }
            catch (Exception ex)
            {
                status = "Lỗi PaintPro: " + ex.Message;
            }
            finally
            {
                _window.CompleteAction(status, pickedId);
            }
        }
    }
}

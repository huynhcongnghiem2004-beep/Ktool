using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.Dim
{
    /// <summary>Chọn nhiều đối tượng cùng loại (Grid, Level, ...) trong view đang mở.</summary>
    internal static class PickHelper
    {
        private class TypeFilter<T> : ISelectionFilter where T : Element
        {
            public bool AllowElement(Element elem)
            {
                return elem is T;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        /// <summary>
        /// Cho người dùng click chọn 1 đối tượng loại T (chọn xong là kết thúc, không cần bấm Finish).
        /// Ném OperationCanceledException nếu người dùng bấm ESC.
        /// </summary>
        public static T PickElement<T>(UIDocument uidoc, string prompt) where T : Element
        {
            Reference r = uidoc.Selection.PickObject(ObjectType.Element, new TypeFilter<T>(), prompt);
            return uidoc.Document.GetElement(r) as T;
        }

        /// <summary>
        /// Cho người dùng click hoặc quét chọn các đối tượng loại T, rồi bấm Finish.
        /// Các đối tượng loại T đã chọn sẵn trước khi chạy lệnh được tự đưa vào danh sách chọn.
        /// Ném OperationCanceledException nếu người dùng bấm ESC / Cancel.
        /// </summary>
        public static List<T> PickElements<T>(UIDocument uidoc, string prompt) where T : Element
        {
            Document doc = uidoc.Document;

            List<Reference> existing = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<T>()
                .Select(e => new Reference(e))
                .ToList();

            IList<Reference> picked = uidoc.Selection.PickObjects(
                ObjectType.Element,
                new TypeFilter<T>(),
                prompt,
                existing);

            return picked
                .Select(r => doc.GetElement(r))
                .OfType<T>()
                .ToList();
        }
    }
}

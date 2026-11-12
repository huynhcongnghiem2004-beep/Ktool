using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;

namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public class JoinGeometryCommand : IExternalCommand
    {
        private static JoinRequest _last;
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument)
            {
                message = "Hãy mở Project Revit để Join/Unjoin.";
                return Result.Failed;
            }
            try
            {
                var window = new JoinGeometryWindow(_last);
                new System.Windows.Interop.WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                if (window.ShowDialog() != true) return Result.Cancelled;
                JoinRequest request = window.Request;
                _last = request;
                HashSet<long> scope = ResolveScope(ui, request);
                string report = null;
                var progress = new JoinProgressWindow();
                new System.Windows.Interop.WindowInteropHelper(progress).Owner = data.Application.MainWindowHandle;
                progress.Execute(update => report = Run(ui, request, scope, update));
                if (report != null) TaskDialog.Show("KTool - Join/Unjoin", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (System.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }

        private class CategorySelection : ISelectionFilter
        {
            private readonly HashSet<long> _ids;
            public CategorySelection(IEnumerable<int> ids) { _ids = new HashSet<long>(ids.Select(i => (long)i)); }
            public bool AllowElement(Element e) { return e.Category != null && _ids.Contains(e.Category.Id.Value) && !(e is ElementType); }
            public bool AllowReference(Reference reference, XYZ point) { return false; }
        }

        private static HashSet<long> ResolveScope(UIDocument ui, JoinRequest request)
        {
            Document doc = ui.Document;
            var categories = request.Rules.SelectMany(r => new[] { r.Priority, r.Target }).Distinct().ToList();
            var filter = new ElementMulticategoryFilter(categories.Select(c => (BuiltInCategory)c).ToList());
            HashSet<long> scope = null;
            if (request.Action >= 2 || request.Scope == 2)
            {
                var selected = ui.Selection.GetElementIds().Where(id =>
                {
                    Element e = doc.GetElement(id);
                    return e != null && new CategorySelection(categories).AllowElement(e);
                }).ToList();
                if (selected.Count == 0)
                    selected = ui.Selection.PickObjects(ObjectType.Element, new CategorySelection(categories),
                        "Chọn đối tượng trong các nhóm của quy tắc, rồi bấm Finish.").Select(r => r.ElementId).ToList();
                scope = new HashSet<long>(selected.Select(id => id.Value));
            }
            else if (request.Scope == 3)
                scope = new HashSet<long>(ui.Selection.PickElementsByRectangle(new CategorySelection(categories),
                    "Quét vùng đối tượng cần Join/Unjoin.").Select(e => e.Id.Value));
            else if (request.Scope == 0)
            {
                if (!FilteredElementCollector.IsViewValidForElementIteration(doc, ui.ActiveView.Id))
                    throw new InvalidOperationException("View hiện tại không hỗ trợ thu thập đối tượng model.");
                scope = new HashSet<long>(new FilteredElementCollector(doc, ui.ActiveView.Id)
                    .WherePasses(filter).WhereElementIsNotElementType().ToElementIds().Select(id => id.Value));
            }
            return scope;
        }

        private static string Run(UIDocument ui, JoinRequest request, HashSet<long> scope, Action<string> update)
        {
            Document doc = ui.Document;
            var categories = request.Rules.SelectMany(r => new[] { r.Priority, r.Target }).Distinct().ToList();
            var filter = new ElementMulticategoryFilter(categories.Select(c => (BuiltInCategory)c).ToList());
            List<Element> pool = new FilteredElementCollector(doc).WherePasses(filter)
                .WhereElementIsNotElementType().Where(e => e.Category != null).OrderBy(e => e.Id.Value).ToList();
            // Phạm vi thường: cả hai đối tượng đều phải nằm trong phạm vi đã chọn.
            // Hai nút cắt nhanh: một đối tượng được chọn, đối tượng kia được tìm theo quy tắc trong model.
            if (request.Action < 2 && scope != null) pool = pool.Where(e => scope.Contains(e.Id.Value)).ToList();
            var poolIds = new HashSet<long>(pool.Select(e => e.Id.Value));
            if (pool.Count == 0 || (scope != null && scope.Count == 0))
            { return "Không có đối tượng phù hợp trong phạm vi đã chọn."; }

            int joined = 0, switched = 0, unjoined = 0, unchanged = 0, failed = 0;
            var errors = new List<string>();
            var seen = new HashSet<string>();
            int warnings = 0;
            using (TransactionGroup transaction = new TransactionGroup(doc, request.Action == 1 ? "KTool Unjoin" : "KTool Join / Switch Join"))
            {
                transaction.Start();
                foreach (JoinRule rule in request.Rules)
                {
                    foreach (Element first in pool.Where(e => e.Category.Id.Value == rule.Priority))
                    {
                        update("Đang xử lý Id " + first.Id.Value + " • Đã xét " + seen.Count + " cặp");
                        IEnumerable<ElementId> candidateIds;
                        if (request.Action == 1)
                            candidateIds = JoinGeometryUtils.GetJoinedElements(doc, first);
                        else
                        {
                            BoundingBoxXYZ box = first.get_BoundingBox(null);
                            if (box == null) continue;
                            var corners = new List<XYZ>();
                            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                                corners.Add(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
                            var outline = new Outline(new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)),
                                new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)));
                            candidateIds = new FilteredElementCollector(doc).OfCategory((BuiltInCategory)rule.Target)
                                .WhereElementIsNotElementType().WherePasses(new BoundingBoxIntersectsFilter(outline, 0.001))
                                .ToElementIds();
                        }
                        // Snapshot trước khi Join thay đổi hình học / danh sách join.
                        foreach (ElementId secondId in candidateIds.ToList())
                        {
                            if (secondId == first.Id || !poolIds.Contains(secondId.Value)) continue;
                            Element second = doc.GetElement(secondId);
                            if (second == null || second.Category == null || second.Category.Id.Value != rule.Target) continue;
                            Element cutter = first, cut = second;
                            if (request.Action >= 2)
                            {
                                bool firstSelected = scope.Contains(first.Id.Value), secondSelected = scope.Contains(second.Id.Value);
                                if (firstSelected == secondSelected) continue;
                                bool selectedCuts = request.Action == 2;
                                cutter = firstSelected == selectedCuts ? first : second;
                                cut = cutter.Id == first.Id ? second : first;
                            }
                            string pair = Math.Min(first.Id.Value, second.Id.Value) + ":" + Math.Max(first.Id.Value, second.Id.Value);
                            if (!seen.Add(pair)) continue;
                            update("Đang xử lý cặp " + pair + " • Nhấn Dừng để hủy toàn bộ lượt chạy");
                            using (Transaction sub = new Transaction(doc, "KTool Join: " + pair))
                            {
                                sub.Start();
                                var pairFailures = new JoinFailures();
                                sub.SetFailureHandlingOptions(sub.GetFailureHandlingOptions()
                                    .SetFailuresPreprocessor(pairFailures).SetClearAfterRollback(true));
                                try
                                {
                                    bool already = JoinGeometryUtils.AreElementsJoined(doc, cutter, cut);
                                    int kind;
                                    if (request.Action == 1)
                                    {
                                        if (!already) { sub.RollBack(); unchanged++; continue; }
                                        JoinGeometryUtils.UnjoinGeometry(doc, cutter, cut); kind = 1;
                                    }
                                    else
                                    {
                                        if (!already) JoinGeometryUtils.JoinGeometry(doc, cutter, cut);
                                        doc.Regenerate();
                                        bool needSwitch = !JoinGeometryUtils.IsCuttingElementInJoin(doc, cutter, cut);
                                        if (needSwitch) JoinGeometryUtils.SwitchJoinOrder(doc, cutter, cut);
                                        if (!JoinGeometryUtils.AreElementsJoined(doc, cutter, cut) ||
                                            !JoinGeometryUtils.IsCuttingElementInJoin(doc, cutter, cut))
                                            throw new InvalidOperationException("Revit không giữ được chiều cắt yêu cầu.");
                                        kind = !already ? 2 : needSwitch ? 3 : 0;
                                    }
                                    if (sub.Commit() != TransactionStatus.Committed)
                                        throw new InvalidOperationException("Cặp Join đã rollback. " + string.Join("; ", pairFailures.Errors));
                                    warnings += pairFailures.Warnings;
                                    if (kind == 1) unjoined++; else if (kind == 2) joined++; else if (kind == 3) switched++; else unchanged++;
                                }
                                catch (Exception ex)
                                {
                                    if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                    failed++;
                                    if (errors.Count < 8) errors.Add(first.Id.Value + " / " + second.Id.Value + ": " + ex.Message);
                                }
                            }
                        }
                    }
                }
                if (transaction.Assimilate() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit đã rollback lượt Join/Unjoin.");
            }
            return "Join mới: " + joined + "\nĐảo chiều cắt: " + switched +
                "\nUnjoin: " + unjoined + "\nĐã đúng / không đổi: " + unchanged + "\nCặp không xử lý được: " + failed +
                (warnings > 0 ? "\nCảnh báo Revit: " + warnings : "") +
                (errors.Count > 0 ? "\n\nMột số lỗi:\n" + string.Join("\n", errors) : "") +
                (seen.Count == 0 ? "\nKhông tìm thấy cặp phù hợp theo quy tắc và phạm vi." : "");
        }

        private class JoinFailures : IFailuresPreprocessor
        {
            public int Warnings;
            public List<string> Errors = new List<string>();
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                    {
                        // Không giữ Join giả giữa hai vật thể chỉ có bounding box giao nhau.
                        if (failure.GetFailureDefinitionId() == BuiltInFailures.JoinElementsFailures.JoiningDisjointWarn)
                            Errors.Add(failure.GetDescriptionText());
                        else { Warnings++; accessor.DeleteWarning(failure); }
                    }
                    else Errors.Add(failure.GetDescriptionText());
                }
                return Errors.Count == 0 ? FailureProcessingResult.Continue : FailureProcessingResult.ProceedWithRollBack;
            }
        }
    }


}

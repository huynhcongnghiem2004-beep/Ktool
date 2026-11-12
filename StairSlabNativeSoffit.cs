using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace SDK_HD.Dim
{
    // Uses the actual concrete body's soffit, including short runs with only
    // one tread. Both clearance and shell thickness are measured vertically.
    internal static class StairSlabNativeSoffit
    {
        internal static IEnumerable<Solid> Solids(GeometryElement geometry)
        {
            if (geometry == null) yield break;
            foreach (GeometryObject obj in geometry)
            {
                var solid = obj as Solid;
                if (solid != null && solid.Volume > 1e-8) yield return solid;
                var instance = obj as GeometryInstance;
                if (instance != null)
                    foreach (var nested in Solids(instance.GetInstanceGeometry())) yield return nested;
            }
        }

        internal static bool TryBuild(Element element, double thickness, double clearance,
            ElementId material, out Solid result, out string reason)
        {
            result = null;
            reason = "";
            try
            {
                var stair = (element as StairsRun)?.GetStairs() ?? (element as StairsLanding)?.GetStairs();
                var type = stair == null ? null : element.Document.GetElement(stair.GetTypeId()) as StairsType;
                if (type == null || type.ConstructionMethod != StairsConstructionMethod.CastInPlace)
                    throw new InvalidOperationException("Chỉ hỗ trợ thân Stair BTCT đổ tại chỗ (Cast-In-Place).");
                if (double.IsNaN(thickness) || double.IsInfinity(thickness) || thickness <= 0 ||
                    double.IsNaN(clearance) || double.IsInfinity(clearance) || clearance < 0)
                    throw new InvalidOperationException("Chiều dày/khoảng hở không hợp lệ.");
                var all = Solids(element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })).ToList();
                // For the monolithic Stair run/landing the largest solid is
                // its structural body; separate tread/riser finishes are smaller.
                var body = all.OrderByDescending(x => x.Volume).FirstOrDefault();
                if (body == null) throw new InvalidOperationException("Không đọc được thân Stair.");
                var pieces = new List<Solid>();
                foreach (Face face in body.Faces)
                {
                    var bb = face.GetBoundingBox();
                    if (face.ComputeNormal((bb.Min + bb.Max) * .5).Z >= -.01) continue;
                    var planar = face as PlanarFace;
                    if (planar == null) throw new InvalidOperationException("Đáy cong chưa được hỗ trợ.");
                    var move = Transform.CreateTranslation(-XYZ.BasisZ * clearance);
                    var loops = planar.GetEdgesAsCurveLoops().Select(c => CurveLoop.CreateViaTransform(c, move)).ToList();
                    pieces.Add(GeometryCreationUtilities.CreateExtrusionGeometry(loops, -XYZ.BasisZ,
                        thickness, new SolidOptions(material, ElementId.InvalidElementId)));
                }
                if (pieces.Count == 0) throw new InvalidOperationException("Không tìm thấy mặt đáy thân Stair.");
                var shell = pieces[0];
                foreach (var piece in pieces.Skip(1))
                    shell = BooleanOperationsUtils.ExecuteBooleanOperation(shell, piece, BooleanOperationsType.Union);
                if (SolidUtils.SplitVolumes(shell).Count != 1)
                    throw new InvalidOperationException("Các phần đáy không tạo thành một bản liên tục ở chiều dày đã chọn.");
                foreach (var original in all)
                    if (BooleanOperationsUtils.ExecuteBooleanOperation(shell, original, BooleanOperationsType.Intersect).Volume > 1e-6)
                        throw new InvalidOperationException("Bản tạo mới giao thân Stair; cần kiểm tra mặt cắt.");
                result = shell;
                return true;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace SDK_HD.Dim.Finish
{
    internal static class FinishGeometry
    {
        public const double Mm = 1.0 / 304.8;
        public static CurveLoop Polygon(params XYZ[] points)
        {
            var loop = new CurveLoop();
            for (int i = 0; i < points.Length; i++) loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Length]));
            return loop;
        }
        public static List<CurveLoop> RoomLoops(Room room)
        {
            var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
            var boundaries = room.GetBoundarySegments(options);
            if (boundaries == null || boundaries.Count == 0) throw new InvalidOperationException("Room chưa có biên kín.");
            var result = new List<CurveLoop>();
            foreach (var boundary in boundaries)
            {
                var loop = new CurveLoop();
                foreach (var segment in boundary)
                {
                    Curve curve = segment.GetCurve();
                    double z = curve.GetEndPoint(0).Z;
                    if (Math.Abs(curve.GetEndPoint(1).Z - z) > Mm) throw new InvalidOperationException("Biên Room không nằm ngang.");
                    loop.Append(curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, -z))));
                }
                result.Add(loop);
            }
            if (!BoundaryValidation.IsValidHorizontalBoundary(result)) throw new InvalidOperationException("Biên Room không hợp lệ để tạo sàn/trần.");
            return result;
        }
        public static double BaseOffset(Room room)
        { Parameter p = room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET); return p == null ? 0 : p.AsDouble(); }
        public static double Height(Room room, FinishSettings settings)
        {
            double height = settings.HeightMm > 0 ? settings.HeightMm * Mm : room.UnboundedHeight;
            if (height < 10 * Mm || height > 100000 * Mm) throw new InvalidOperationException("Chiều cao Room không hợp lệ; hãy nhập chiều cao hoàn thiện.");
            return height;
        }
        public static IEnumerable<Solid> Solids(GeometryElement geometry)
        {
            if (geometry == null) yield break;
            foreach (GeometryObject item in geometry)
            {
                var solid = item as Solid;
                if (solid != null && solid.Volume > 1e-9) yield return solid;
                var instance = item as GeometryInstance;
                if (instance != null) foreach (var nested in Solids(instance.GetInstanceGeometry())) yield return nested;
            }
        }
        public static List<CurveLoop> TranslateLoops(IList<CurveLoop> loops, XYZ delta)
        { return loops.Select(l => CurveLoop.CreateViaTransform(l, Transform.CreateTranslation(delta))).ToList(); }

        // Lấy mặt thật của tường chủ: giữ lỗ cửa, vòm và profile đã chỉnh trên tường.
        // Sau đó chuyển mặt đó vào Room, cắt theo đoạn biên và chiều cao hoàn thiện.
        public static List<Wall> WallSegment(Document doc, Room room, Wall source, Curve boundary,
                                              WallType type, FinishSettings settings)
        {
            Line line = boundary as Line;
            if (line == null) return CurvedWallSegment(doc, room, source, boundary, type, settings);
            Level level = doc.GetElement(room.LevelId) as Level;
            double offset = BaseOffset(room) + settings.OffsetMm * Mm;
            double z = level.ProjectElevation + offset, height = Height(room, settings) - (settings.HeightMm == 0 ? settings.OffsetMm * Mm : 0);
            if (height < 10 * Mm) throw new InvalidOperationException("Cao độ chân tường vượt đỉnh Room.");
            XYZ a = new XYZ(line.GetEndPoint(0).X, line.GetEndPoint(0).Y, z);
            XYZ b = new XYZ(line.GetEndPoint(1).X, line.GetEndPoint(1).Y, z);
            XYZ along = (b - a).Normalize(), normal = XYZ.BasisZ.CrossProduct(along).Normalize();
            XYZ probe = (a + b) / 2 + XYZ.BasisZ * Math.Min(height / 2, 1);
            if (!room.IsPointInRoom(probe + normal * 20 * Mm)) normal = -normal;
            if (!room.IsPointInRoom(probe + normal * 20 * Mm)) throw new InvalidOperationException("Không xác định được phía trong Room.");
            CurveLoop rectangle = Polygon(a, b, b + XYZ.BasisZ * height, a + XYZ.BasisZ * height);
            Solid region = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { rectangle }, normal, type.Width);
            var faces = Solids(source.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }))
                .SelectMany(s => s.Faces.Cast<Face>()).OfType<PlanarFace>()
                .Where(f => f.FaceNormal.DotProduct(normal) > 0.9999).ToList();
            if (faces.Count == 0) throw new InvalidOperationException("Không lấy được mặt tường chủ có lỗ cửa.");
            double nearest = faces.Min(f => Math.Abs((a - f.Origin).DotProduct(normal)));
            var walls = new List<Wall>();
            foreach (PlanarFace face in faces.Where(f => Math.Abs((a - f.Origin).DotProduct(normal)) <= nearest + Mm))
            {
                double distance = (a - face.Origin).DotProduct(normal);
                var loops = TranslateLoops(face.GetEdgesAsCurveLoops(), normal * distance);
                Solid skin = GeometryCreationUtilities.CreateExtrusionGeometry(loops, normal, type.Width);
                Solid clipped = BooleanOperationsUtils.ExecuteBooleanOperation(skin, region, BooleanOperationsType.Intersect);
                if (clipped == null || clipped.Volume < 1e-8) continue;
                foreach (PlanarFace profile in clipped.Faces.OfType<PlanarFace>().Where(f => f.FaceNormal.DotProduct(normal) < -0.9999))
                {
                    var profileLoops = TranslateLoops(profile.GetEdgesAsCurveLoops(), normal * (type.Width / 2));
                    Wall wall = Wall.Create(doc, profileLoops.SelectMany(l => l).ToList(), type.Id, level.Id, false, normal);
                    SetNonBounding(wall); walls.Add(wall);
                }
            }
            if (walls.Count == 0) throw new InvalidOperationException("Mặt tường chủ không giao vùng hoàn thiện.");
            return walls;
        }
        private static List<Wall> CurvedWallSegment(Document doc, Room room, Wall source, Curve boundary, WallType type, FinishSettings settings)
        {
            Arc arc = boundary as Arc;
            if (arc == null) throw new InvalidOperationException("Biên tường chỉ hỗ trợ Line hoặc Arc.");
            var level = (Level)doc.GetElement(room.LevelId);
            double offset = BaseOffset(room) + settings.OffsetMm * Mm;
            double z = level.ProjectElevation + offset, height = Height(room, settings) - (settings.HeightMm == 0 ? settings.OffsetMm * Mm : 0);
            if (height < 10 * Mm) throw new InvalidOperationException("Cao độ chân tường vượt đỉnh Room.");
            XYZ midpoint = arc.Evaluate(0.5, true);
            XYZ tangent = arc.ComputeDerivatives(0.5, true).BasisX.Normalize();
            XYZ normal = XYZ.BasisZ.CrossProduct(tangent).Normalize();
            XYZ probe = new XYZ(midpoint.X, midpoint.Y, z + Math.Min(height / 2, 1));
            if (!room.IsPointInRoom(probe + normal * 20 * Mm)) normal = -normal;
            if (!room.IsPointInRoom(probe + normal * 20 * Mm)) throw new InvalidOperationException("Không xác định được phía trong Room cong.");
            Curve plus = arc.CreateOffset(type.Width / 2, XYZ.BasisZ), minus = arc.CreateOffset(-type.Width / 2, XYZ.BasisZ);
            Curve centered = (plus.Evaluate(0.5, true) - midpoint).DotProduct(normal) > 0 ? plus : minus;
            centered = centered.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, -centered.GetEndPoint(0).Z)));
            Wall wall = Wall.Create(doc, centered, type.Id, level.Id, height, offset, false, false);
            SetNonBounding(wall); doc.Regenerate();
            foreach (ElementId id in source.FindInserts(true, false, true, true))
            {
                FamilyInstance door = doc.GetElement(id) as FamilyInstance;
                Opening existing = doc.GetElement(id) as Opening;
                if (door == null && existing == null) continue;
                CurveLoop opening = null;
                if (door != null)
                {
                    XYZ cutDirection;
                    opening = Autodesk.Revit.DB.IFC.ExporterIFCUtils.GetInstanceCutoutFromWall(doc, source, door, out cutDirection);
                }
                else if (existing.IsRectBoundary)
                {
                    var points = existing.BoundaryRect;
                    XYZ p0 = points[0].Z < points[1].Z ? points[0] : points[1];
                    XYZ p1 = points[0].Z < points[1].Z ? points[1] : points[0];
                    opening = Polygon(p0, new XYZ(p1.X, p1.Y, p0.Z), p1, new XYZ(p0.X, p0.Y, p1.Z));
                }
                if (opening == null || opening.Count() != 4 || opening.Any(c => !(c is Line)))
                    throw new InvalidOperationException("Tường cong có lỗ không chữ nhật; đã bỏ qua đoạn để không tạo tường che cửa.");
                var verts = opening.Select(c => c.GetEndPoint(0)).ToList();
                XYZ dir = (door != null ? door.HandOrientation : (verts[1] - verts[0])).Normalize();
                XYZ origin = verts[0];
                double u0 = verts.Min(p => (p - origin).DotProduct(dir)), u1 = verts.Max(p => (p - origin).DotProduct(dir));
                double z0 = Math.Max(z, verts.Min(p => p.Z)), z1 = Math.Min(z + height, verts.Max(p => p.Z));
                if (z1 - z0 <= Mm) continue;
                XYZ openingCenter = verts.Aggregate(XYZ.Zero, (sum, p) => sum + p) / verts.Count;
                IntersectionResult onArc = centered.Project(new XYZ(openingCenter.X, openingCenter.Y, centered.GetEndPoint(0).Z));
                if (onArc == null || onArc.Parameter < centered.GetEndParameter(0) - 1e-6 || onArc.Parameter > centered.GetEndParameter(1) + 1e-6) continue;
                XYZ corner = origin + dir * u0;
                // NewOpening hỗ trợ tường Arc với lỗ chữ nhật.
                doc.Create.NewOpening(wall, new XYZ(corner.X, corner.Y, z0), new XYZ((origin + dir * u1).X, (origin + dir * u1).Y, z1));
            }
            return new List<Wall> { wall };
        }
        private static void SetNonBounding(Wall wall)
        {
            Parameter p = wall.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
            if (p != null && !p.IsReadOnly) p.Set(0);
        }

        public static double Length(Element instance, Element type, string name)
        {
            Parameter parameter = instance.LookupParameter(name) ?? type.LookupParameter(name);
            return parameter != null && parameter.StorageType == StorageType.Double ? parameter.AsDouble() : 0;
        }
        public static CurveLoop DoorCutout(Document doc, FamilyInstance door)
        {
            Wall wall = door.Host as Wall;
            if (wall == null) throw new InvalidOperationException("Cửa không có tường chủ trong model hiện tại.");
            XYZ cut;
            CurveLoop loop = Autodesk.Revit.DB.IFC.ExporterIFCUtils.GetInstanceCutoutFromWall(doc, wall, door, out cut);
            if (loop == null) throw new InvalidOperationException("Không lấy được biên lỗ cắt thực của cửa.");
            return loop;
        }

        public static List<CurveLoop> FaceLoops(Element host, Reference reference, out XYZ normal, out XYZ origin)
        {
            PlanarFace face = host.GetGeometryObjectFromReference(reference) as PlanarFace;
            if (face == null) throw new InvalidOperationException("Hãy chọn một mặt phẳng; không hỗ trợ mặt cong hoặc model liên kết.");
            normal = face.FaceNormal.Normalize(); origin = face.Origin;
            var loops = face.GetEdgesAsCurveLoops().ToList();
            FamilyInstance family = host as FamilyInstance;
            if (family != null && Math.Abs((reference.GlobalPoint - origin).DotProduct(normal)) > Mm)
            {
                Transform transform = family.GetTransform();
                loops = loops.Select(l => CurveLoop.CreateViaTransform(l, transform)).ToList();
                normal = transform.OfVector(normal).Normalize(); origin = transform.OfPoint(origin);
                if (Math.Abs((reference.GlobalPoint - origin).DotProduct(normal)) > Mm)
                    throw new InvalidOperationException("Không chuyển được tọa độ mặt Family về model.");
            }
            return loops;
        }
    }
}

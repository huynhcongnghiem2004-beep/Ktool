using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace SDK_HD.Dim
{
    public sealed partial class StairDeploymentCommand
    {
        private static void AnnotateStairPlan(Document doc, ViewPlan view, Stairs stair, List<StairsRun> runs, StairDeploymentWindow settings, List<string> notes)
        {
            if (settings.StepNumbers)
            foreach (var run in runs)
            {
                bool exists = new FilteredElementCollector(doc, view.Id).OfClass(typeof(NumberSystem)).Cast<NumberSystem>()
                    .Any(n => n.NumberedElementId.HostElementId == run.Id);
                if (exists) continue;
                using (var attempt = new SubTransaction(doc))
                {
                    attempt.Start();
                    try
                    {
                        var numbers = NumberSystem.Create(doc, view.Id, new LinkElementId(run.Id), run.GetNumberSystemReference(StairsNumberSystemReferenceOption.Center));
                        numbers.NumberDisplayRule = NumberSystemDisplayRule.All;
                        doc.Regenerate();
                        if (numbers.Category != null && view.GetCategoryHidden(numbers.Category.Id)) notes.Add(view.Name + ": Template đang ẩn category số bậc.");
                        if (attempt.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu số bậc.");
                    }
                    catch (Exception ex) { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); notes.Add(view.Name + ": số bậc vế " + run.Id + ": " + ex.Message); }
                }
            }
            if (!settings.StairPath) return;
            if (settings.PathType == ElementId.InvalidElementId) { notes.Add(view.Name + ": thiếu Stairs Path Type; chưa tạo mũi tên."); return; }
            if (new FilteredElementCollector(doc, view.Id).OfClass(typeof(StairsPath)).Cast<StairsPath>().Any(p => p.StairsId.HostElementId == stair.Id)) return;
            using (var attempt = new SubTransaction(doc))
            {
                attempt.Start();
                try
                {
                    var path = StairsPath.Create(doc, new LinkElementId(stair.Id), settings.PathType, view.Id);
                    path.UpText = "LÊN"; path.DownText = "XUỐNG"; path.ShowUpText = true;
                    try { path.ShowDownText = true; }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException) { notes.Add(view.Name + ": loại Stairs Path này không có chế độ chữ xuống tự động; chọn loại Up/Down để hiển thị đủ."); }
                    doc.Regenerate();
                    if (path.Category != null && view.GetCategoryHidden(path.Category.Id)) notes.Add(view.Name + ": Template đang ẩn Stairs Path.");
                    if (attempt.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu Stairs Path.");
                }
                catch (Exception ex) { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); notes.Add(view.Name + ": Stairs Path: " + ex.Message); }
            }
        }
        private sealed class WallPlane
        {
            internal ElementId WallId;
            internal FaceRef Face;
        }
        private static int PlanDimensionAnnotations(Document doc, ViewPlan view, Stairs stair, List<Element> components, XYZ along, XYZ across, StairDeploymentWindow settings, List<string> notes)
        {
            var axes = new[] { along, across };
            var sizes = new[] { new List<FaceRef>(), new List<FaceRef>() };
            var crop = Corners(view.CropBox);
            var stairPoints = components.OfType<StairsRun>().SelectMany(r=>r.GetFootprintBoundary()).SelectMany(c=>new[]{c.GetEndPoint(0),c.GetEndPoint(1)}).ToList();
            if(stairPoints.Count==0)stairPoints=Corners(stair.get_BoundingBox(null));
            foreach (var component in components.Where(e => e is StairsRun || e is StairsLanding))
            {
                var refs = new[] { new List<FaceRef>(), new List<FaceRef>() };
                using (var options = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine })
                { var geometry = component.get_Geometry(options); if (geometry != null) Faces(geometry, Transform.Identity, axes, refs); }
                var run = component as StairsRun;
                if(run!=null)refs=FootprintFaceReferences(run,axes);
                for (int axis = 0; axis < 2; axis++)
                {
                    // Run width: use only side faces, never internal riser planes.
                    if (refs[axis].Count < 2) continue;
                    sizes[axis].AddRange(refs[axis]);
                }
            }
            var wallPlanes = new[] { new List<WallPlane>(), new List<WallPlane>() };
            double search=1500.0/304.8;
            var range=view.GetViewRange();
            var cutLevel=doc.GetElement(range.GetLevelId(PlanViewPlane.CutPlane)) as Level;
            double cutZ=(cutLevel??view.GenLevel).ProjectElevation+range.GetOffset(PlanViewPlane.CutPlane);
            var searchBox=new Outline(new XYZ(stairPoints.Min(p=>p.X)-search,stairPoints.Min(p=>p.Y)-search,cutZ-Tol),
                new XYZ(stairPoints.Max(p=>p.X)+search,stairPoints.Max(p=>p.Y)+search,cutZ+Tol));
            foreach (Wall wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).WherePasses(new BoundingBoxIntersectsFilter(searchBox)))
            {
                var box = wall.get_BoundingBox(null); if (box == null) continue;
                var wallPoints = Corners(box);
                try
                {
                    foreach (var shell in new[] { ShellLayerType.Interior, ShellLayerType.Exterior })
                    foreach (var reference in HostObjectUtils.GetSideFaces(wall, shell))
                    {
                        var face = wall.GetGeometryObjectFromReference(reference) as PlanarFace; if (face == null) continue;
                        var facePoints=face.GetEdgesAsCurveLoops().SelectMany(l=>l).SelectMany(e=>e.Tessellate()).ToList();
                        for (int a = 0; a < 2; a++) if (Math.Abs(face.FaceNormal.DotProduct(axes[a])) > .999999)
                        {
                            var perpendicular=axes[1-a];double low=stairPoints.Min(p=>p.DotProduct(perpendicular)),high=stairPoints.Max(p=>p.DotProduct(perpendicular));
                            double overlap=Math.Min(high,facePoints.Max(p=>p.DotProduct(perpendicular)))-Math.Max(low,facePoints.Min(p=>p.DotProduct(perpendicular)));
                            if(overlap<(high-low)*.6)continue;
                            wallPlanes[a].Add(new WallPlane { WallId = wall.Id, Face = new FaceRef { Reference = reference, Position = face.Origin.DotProduct(axes[a]) } });
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { notes.Add(view.Name + ": tường " + wall.Id + " không cung cấp mặt bên để Dim ô thang."); }
            }
            // Walls enclosing the stair may lie outside the stair-only crop.
            // Expand only to the nearest real enclosing planes at this plan's
            // cut elevation; do not manufacture a missing enclosure reference.
            var extent=new[]{new[]{crop.Min(p=>p.DotProduct(axes[0])),crop.Max(p=>p.DotProduct(axes[0]))},
                new[]{crop.Min(p=>p.DotProduct(axes[1])),crop.Max(p=>p.DotProduct(axes[1]))}};
            for(int a=0;a<2;a++)
            {
                double low=stairPoints.Min(p=>p.DotProduct(axes[a])),high=stairPoints.Max(p=>p.DotProduct(axes[a]));
                var left=wallPlanes[a].Where(w=>w.Face.Position<=low+Tol&&low-w.Face.Position<=search).OrderByDescending(w=>w.Face.Position).FirstOrDefault();
                var right=wallPlanes[a].Where(w=>w.Face.Position>=high-Tol&&w.Face.Position-high<=search).OrderBy(w=>w.Face.Position).FirstOrDefault();
                if(left!=null)extent[a][0]=Math.Min(extent[a][0],wallPlanes[a].Where(w=>w.WallId==left.WallId).Min(w=>w.Face.Position)-settings.CropMargin);
                if(right!=null)extent[a][1]=Math.Max(extent[a][1],wallPlanes[a].Where(w=>w.WallId==right.WallId).Max(w=>w.Face.Position)+settings.CropMargin);
            }
            var oldBox=view.CropBox;var inverse=oldBox.Transform.Inverse;var expanded=new List<XYZ>();
            foreach(double a in extent[0])foreach(double b in extent[1])expanded.Add(inverse.OfPoint(axes[0]*a+axes[1]*b+XYZ.BasisZ*cutZ));
            var newBox=new BoundingBoxXYZ{Transform=oldBox.Transform,Min=new XYZ(expanded.Min(p=>p.X),expanded.Min(p=>p.Y),oldBox.Min.Z),
                Max=new XYZ(expanded.Max(p=>p.X),expanded.Max(p=>p.Y),oldBox.Max.Z)};
            if(newBox.Min.DistanceTo(oldBox.Min)>Tol || newBox.Max.DistanceTo(oldBox.Max)>Tol)
            {
                var rectangle=new[]{axes[0]*extent[0][0]+axes[1]*extent[1][0],axes[0]*extent[0][1]+axes[1]*extent[1][0],
                    axes[0]*extent[0][1]+axes[1]*extent[1][1],axes[0]*extent[0][0]+axes[1]*extent[1][1]};
                var loop=new CurveLoop();for(int i=0;i<4;i++)loop.Append(Line.CreateBound(new XYZ(rectangle[i].X,rectangle[i].Y,cutZ),new XYZ(rectangle[(i+1)%4].X,rectangle[(i+1)%4].Y,cutZ)));
                using(var manager=view.GetCropRegionShapeManager()){if(manager.Split)manager.RemoveSplit();manager.SetCropShape(loop);}
                doc.Regenerate();notes.Add(view.Name+": mở rộng crop để hiện các tường bao dùng làm reference Dim.");
            }
            crop=Corners(view.CropBox);
            int count = 0;
            int firstLayer = components.OfType<StairsRun>().Count() + 2;
            for (int axis = 0; axis < 2; axis++)
            {
                double low = stairPoints.Min(p => p.DotProduct(axes[axis])), high = stairPoints.Max(p => p.DotProduct(axes[axis]));
                var left = wallPlanes[axis].Where(w => w.Face.Position <= low + Tol && low-w.Face.Position<=search).OrderByDescending(w => w.Face.Position).FirstOrDefault();
                var right = wallPlanes[axis].Where(w => w.Face.Position >= high - Tol && w.Face.Position-high<=search).OrderBy(w => w.Face.Position).FirstOrDefault();
                var inner = new List<FaceRef>(); var outer = new List<FaceRef>();
                if (left != null && right != null && left.WallId != right.WallId)
                {
                    inner.Add(left.Face); inner.Add(right.Face);
                    outer.Add(wallPlanes[axis].Where(w => w.WallId == left.WallId).OrderBy(w => w.Face.Position).First().Face);
                    outer.Add(wallPlanes[axis].Where(w => w.WallId == right.WallId).OrderByDescending(w => w.Face.Position).First().Face);
                    notes.Add(view.Name + ": Dim ô thang phương " + (axis == 0 ? "dọc" : "ngang") + " theo tường " + left.WallId + " / " + right.WallId + "; kiểm tra đúng tường bao.");
                }
                else notes.Add(view.Name + ": chưa có hai mặt tường bao đối diện cho phương " + (axis == 0 ? "dọc" : "ngang") + "; không tạo Dim ô thang giả.");
                var groups = new[] { sizes[axis].Concat(inner).ToList(), inner, outer };
                for (int layer = 0; layer < groups.Length; layer++)
                {
                    var ordered = new List<FaceRef>();
                    foreach (var r in groups[layer].OrderBy(r => r.Position)) if (ordered.Count == 0 || r.Position - ordered.Last().Position > Tol) ordered.Add(r);
                    if (ordered.Count < 2) continue;
                    foreach (int sign in new[] { -1, 1 })
                    {
                        var perpendicular = axes[1 - axis];
                        double baseline = (sign < 0 ? crop.Min(p => p.DotProduct(perpendicular)) : crop.Max(p => p.DotProduct(perpendicular)))
                            + sign * settings.Gap / 304.8 * view.Scale * (firstLayer + layer);
                        count += CreatePlanChain(doc, view, axes[axis], perpendicular * baseline + view.ViewDirection * view.Origin.DotProduct(view.ViewDirection), ordered, settings.DimType, notes);
                    }
                }
            }
            return count;
        }
        private static int CreatePlanChain(Document doc, View view, XYZ axis, XYZ origin, List<FaceRef> refs, DimensionType type, List<string> notes)
        {
            using (var attempt = new SubTransaction(doc))
            {
                attempt.Start();
                try
                {
                    var array = new ReferenceArray(); foreach (var reference in refs) array.Append(reference.Reference);
                    var dim = doc.Create.NewDimension(view, Line.CreateBound(origin + axis * refs.First().Position, origin + axis * refs.Last().Position), array, type); doc.Regenerate();
                    var values = dim.NumberOfSegments > 1 ? dim.Segments.Cast<DimensionSegment>().Select(s => s.Value).ToList() : new List<double?> { dim.Value };
                    if (values.Count != refs.Count - 1) throw new InvalidOperationException("Số đoạn Dim không khớp.");
                    for (int i = 0; i < values.Count; i++) if (!values[i].HasValue || Math.Abs(values[i].Value - (refs[i + 1].Position - refs[i].Position)) > Tol) throw new InvalidOperationException("Số đo Dim không khớp reference.");
                    if (attempt.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không lưu Dim.");
                    return 1;
                }
                catch (Exception ex) { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); notes.Add(view.Name + ": Dim mặt bằng theo mẫu: " + ex.Message); return 0; }
            }
        }
    }
}


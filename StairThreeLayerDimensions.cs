using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
namespace SDK_HD.Dim
{
    public sealed partial class StairDeploymentCommand
    {
        private static double DimensionRunWidth(StairsRun run)
        {
            return run.ActualRunWidth>Tol?run.ActualRunWidth:StairSlabSoffitGeometry.PathWidth(run);
        }
        private static void SeparateGridHeadsFromDimensions(Document doc,View view,StairDeploymentWindow settings,List<string> notes)
        {
            double paper=(view is ViewPlan?5:3)*settings.Gap+8;
            double margin=paper/304.8*view.Scale;var corners=Corners(view.CropBox);
            foreach(var grid in new FilteredElementCollector(doc,view.Id).OfClass(typeof(Grid)).Cast<Grid>())
            {
                try
                {
                    var line=grid.GetCurvesInView(DatumExtentType.ViewSpecific,view).OfType<Line>().FirstOrDefault();
                    if(line==null)continue;var direction=line.Direction.Normalize();
                    if(Math.Abs(direction.DotProduct(view.ViewDirection))>1e-6)continue;
                    double low=corners.Min(p=>p.DotProduct(direction))-margin,high=corners.Max(p=>p.DotProduct(direction))+margin;
                    var origin=line.GetEndPoint(0);double parameter=origin.DotProduct(direction);
                    grid.SetDatumExtentType(DatumEnds.End0,view,DatumExtentType.ViewSpecific);
                    grid.SetDatumExtentType(DatumEnds.End1,view,DatumExtentType.ViewSpecific);
                    grid.SetCurveInView(DatumExtentType.ViewSpecific,view,Line.CreateBound(origin+direction*(low-parameter),origin+direction*(high-parameter)));
                }
                catch(Exception ex){notes.Add(view.Name+": cần kiểm tra vị trí bubble trục "+grid.Name+": "+ex.Message);}
            }
            doc.Regenerate();
        }
        private static void FilterExposedRisers(StairsRun run,Line path,List<FaceRef>[] references)
        {
            XYZ axis=new XYZ(path.Direction.X,path.Direction.Y,0).Normalize(),side=XYZ.BasisZ.CrossProduct(axis);
            double a=path.GetEndPoint(0).DotProduct(axis),b=path.GetEndPoint(1).DotProduct(axis);
            double center=path.Evaluate(.5,true).DotProduct(side),height=run.GetStairs().ActualRiserHeight,baseZ=run.GetStairs().BaseElevation;
            for(int i=0;i<references.Length;i++)
            {
                var candidates=references[i].Where(r=>r.RunPosition>=Math.Min(a,b)-Tol && r.RunPosition<=Math.Max(a,b)+Tol &&
                    r.Center!=null && Math.Abs(r.Center.DotProduct(side)-center)<=DimensionRunWidth(run)*.5+Tol).ToList();
                if(candidates.Count<2){references[i]=new List<FaceRef>();continue;}
                double p=candidates.Average(r=>r.RunPosition),z=candidates.Average(r=>r.MidZ);
                double trend=candidates.Sum(r=>(r.RunPosition-p)*(r.MidZ-z));
                if(Math.Abs(trend)<Tol*Tol){references[i]=new List<FaceRef>();continue;}
                double ascent=Math.Sign(trend);
                references[i]=candidates.Where(r=>r.NormalSign*ascent<-.999999)
                    .GroupBy(r=>(long)Math.Floor((r.MidZ-baseZ)/height+.05))
                    .Select(g=>g.OrderBy(r=>r.RunPosition*ascent).First()).ToList();
            }
        }
        private static void VisitPlanarFaces(GeometryElement geometry,Transform tr,Action<PlanarFace,XYZ,List<XYZ>> visit)
        {
            if(geometry==null)return;
            foreach(GeometryObject obj in geometry)
            {
                if(obj is GeometryInstance instance){VisitPlanarFaces(instance.GetSymbolGeometry(),tr.Multiply(instance.Transform),visit);continue;}
                if(!(obj is Solid solid))continue;
                foreach(Face f in solid.Faces)
                {
                    if(!(f is PlanarFace face)||face.Reference==null)continue;
                    var points=new List<XYZ>();foreach(EdgeArray loop in face.EdgeLoops)foreach(Edge edge in loop)points.AddRange(edge.Tessellate().Select(tr.OfPoint));
                    if(points.Count>0)visit(face,tr.OfVector(face.FaceNormal).Normalize(),points);
                }
            }
        }
        private static List<FaceRef>[] FootprintFaceReferences(StairsRun run,XYZ[] axes,XYZ cutNormal=null,double cut=0)
        {
            var result=new[]{new List<FaceRef>(),new List<FaceRef>()};var edges=run.GetFootprintBoundary().OfType<Line>().ToList();
            using(var options=new Options{ComputeReferences=true,DetailLevel=ViewDetailLevel.Fine})
            VisitPlanarFaces(run.get_Geometry(options),Transform.Identity,(face,normal,points)=>
            {
                if(cutNormal!=null && (points.Min(p=>p.DotProduct(cutNormal))>cut+Tol || points.Max(p=>p.DotProduct(cutNormal))<cut-Tol))return;
                for(int axis=0;axis<2;axis++)
                {
                    if(Math.Abs(normal.DotProduct(axes[axis]))<.999999)continue;
                    XYZ perpendicular=XYZ.BasisZ.CrossProduct(axes[axis]);
                    double position=points[0].DotProduct(axes[axis]),lo=points.Min(p=>p.DotProduct(perpendicular)),hi=points.Max(p=>p.DotProduct(perpendicular));
                    bool boundary=edges.Any(e=>Math.Abs(e.Direction.DotProduct(axes[axis]))<1e-6 &&
                        Math.Abs(e.GetEndPoint(0).DotProduct(axes[axis])-position)<Tol &&
                        Math.Min(hi,Math.Max(e.GetEndPoint(0).DotProduct(perpendicular),e.GetEndPoint(1).DotProduct(perpendicular)))-
                        Math.Max(lo,Math.Min(e.GetEndPoint(0).DotProduct(perpendicular),e.GetEndPoint(1).DotProduct(perpendicular)))>Tol);
                    if(boundary)result[axis].Add(new FaceRef{Reference=face.Reference,Position=position,
                        Center=new XYZ(points.Average(p=>p.X),points.Average(p=>p.Y),points.Average(p=>p.Z))});
                }
            });
            return result;
        }
        private static List<FaceRef> UniqueDimensionRefs(IEnumerable<FaceRef> values)
        {
            var result=new List<FaceRef>();foreach(var r in values.OrderBy(x=>x.Position))
                if(result.Count==0 || r.Position-result.Last().Position>Tol)result.Add(r);
            return result;
        }
        private static Reference SectionHorizontalReference(PlanarFace face,View view)
        {
            var edges=new List<Edge>();foreach(EdgeArray loop in face.EdgeLoops)foreach(Edge edge in loop)
            {var line=edge.AsCurve() as Line;if(line!=null && Math.Abs(line.Direction.DotProduct(view.RightDirection))>.999999 && edge.Reference!=null)edges.Add(edge);}
            return edges.OrderBy(e=>Math.Abs((e.AsCurve().Evaluate(.5,true)-view.Origin).DotProduct(view.ViewDirection))).Select(e=>e.Reference).FirstOrDefault()??face.Reference;
        }
        private static int DimensionStairSectionThreeLayers(Document doc,View view,Stairs stair,List<Element> components,StairDeploymentWindow settings,List<string> notes)
        {
            var axes=new[]{view.RightDirection.Normalize(),view.UpDirection.Normalize()};
            var risers=new List<FaceRef>();var perimeter=new List<FaceRef>();var slice=new List<FaceRef>();var treads=new List<FaceRef>();var transitions=new List<FaceRef>();
            double baseZ=stair.BaseElevation,h=stair.ActualRiserHeight;
            foreach(var run in components.OfType<StairsRun>())
            {
                perimeter.AddRange(FootprintFaceReferences(run,new[]{axes[0],XYZ.BasisZ.CrossProduct(axes[0])})[0]);
                slice.AddRange(FootprintFaceReferences(run,new[]{axes[0],XYZ.BasisZ.CrossProduct(axes[0])},view.ViewDirection,view.Origin.DotProduct(view.ViewDirection))[0]);
                var paths=run.GetStairsPath().OfType<Line>().ToList();
                foreach(var path in paths)
                {
                    var direction=new XYZ(path.Direction.X,path.Direction.Y,0).Normalize();
                    if(Math.Abs(direction.DotProduct(axes[0]))<.999999)continue;
                    if(Math.Abs((path.Evaluate(.5,true)-view.Origin).DotProduct(view.ViewDirection))>DimensionRunWidth(run)*.5+Tol)continue;
                    var candidates=new[]{new List<FaceRef>(),new List<FaceRef>()};
                    using(var options=new Options{ComputeReferences=true,DetailLevel=ViewDetailLevel.Fine})
                    RiserFaces(run.get_Geometry(options),Transform.Identity,axes,direction,h,DimensionRunWidth(run),candidates);
                    FilterExposedRisers(run,path,candidates);risers.AddRange(candidates[0]);
                }
                var runTreads=new List<FaceRef>();
                using(var options=new Options{ComputeReferences=true,DetailLevel=ViewDetailLevel.Fine})
                VisitPlanarFaces(run.get_Geometry(options),Transform.Identity,(face,normal,points)=>
                {
                    if(normal.Z<.999999)return;
                    double z=points[0].Z;double index=Math.Round((z-baseZ)/h);
                    if(index<0||index>stair.ActualRisersNumber||Math.Abs(z-baseZ-index*h)>Tol)return;
                    runTreads.Add(new FaceRef{Reference=SectionHorizontalReference(face,view),Position=points[0].DotProduct(axes[1]),
                        Center=new XYZ(points.Average(p=>p.X),points.Average(p=>p.Y),z)});
                });
                runTreads=UniqueDimensionRefs(runTreads);treads.AddRange(runTreads);
                for(int i=0;i+1<paths.Count;i++)
                {
                    var corner=paths[i].GetEndPoint(1);var incoming=paths[i].Direction;var outgoing=paths[i+1].Direction;double half=DimensionRunWidth(run)*.5;
                    var nearby=runTreads.Where(r=>Math.Abs((r.Center-corner).DotProduct(incoming))<=half+Tol &&
                        Math.Abs((r.Center-corner).DotProduct(outgoing))<=half+Tol).OrderBy(r=>r.Position).ToList();
                    if(nearby.Count>0){transitions.Add(nearby.First());transitions.Add(nearby.Last());}
                }
                if(runTreads.Count>0){transitions.Add(runTreads.First());transitions.Add(runTreads.Last());}
            }
            foreach(var landing in components.OfType<StairsLanding>())
            using(var options=new Options{ComputeReferences=true,DetailLevel=ViewDetailLevel.Fine})
            VisitPlanarFaces(landing.get_Geometry(options),Transform.Identity,(face,normal,points)=>
            {
                double z=points[0].Z;
                if(normal.Z<.999999 || Math.Abs(z-baseZ-Math.Round((z-baseZ)/h)*h)>Tol)return;
                var reference=new FaceRef{Reference=SectionHorizontalReference(face,view),Position=points[0].DotProduct(axes[1])};
                treads.Add(reference);transitions.Add(reference);
            });
            var levelRefs=new List<FaceRef>();
            foreach(int param in new[]{-1007200,-1007201})
            {
                var level=doc.GetElement(stair.get_Parameter((BuiltInParameter)param).AsElementId()) as Level;
                double expected=param==-1007200?stair.BaseElevation:stair.TopElevation;
                FaceRef floorRef=null;
                var stairBox=stair.get_BoundingBox(null);
                var nearbyFloors=new FilteredElementCollector(doc,view.Id).OfClass(typeof(Floor)).WherePasses(new BoundingBoxIntersectsFilter(new Outline(stairBox.Min-new XYZ(2,2,2),stairBox.Max+new XYZ(2,2,2))));
                foreach(var floor in nearbyFloors)
                using(var options=new Options{ComputeReferences=true,DetailLevel=ViewDetailLevel.Fine})
                VisitPlanarFaces(floor.get_Geometry(options),Transform.Identity,(face,normal,points)=>
                {
                    if(floorRef==null && normal.Z>.999999 && points.All(p=>Math.Abs(p.Z-expected)<Tol))
                        floorRef=new FaceRef{Reference=SectionHorizontalReference(face,view),Position=points[0].DotProduct(axes[1])};
                });
                if(floorRef!=null){levelRefs.Add(floorRef);continue;}
                if(level!=null && Math.Abs(level.ProjectElevation-expected)<Tol)
                    levelRefs.Add(new FaceRef{Reference=new Reference(level),Position=new XYZ(0,0,level.ProjectElevation).DotProduct(axes[1])});
            }
            perimeter=UniqueDimensionRefs(perimeter);slice=UniqueDimensionRefs(slice);risers=UniqueDimensionRefs(risers);
            var detail=risers.Count>2?risers:slice;
            var horizontal=new[]{detail,perimeter,perimeter.Count<2?new List<FaceRef>():new List<FaceRef>{perimeter.First(),perimeter.Last()}};
            var vertical=new[]{UniqueDimensionRefs(treads.Concat(levelRefs)),UniqueDimensionRefs(transitions.Concat(levelRefs)),UniqueDimensionRefs(levelRefs)};
            var crop=Corners(view.CropBox);int count=0;
            for(int axis=0;axis<2;axis++)for(int layer=0;layer<3;layer++)
            {
                var refs=UniqueDimensionRefs((axis==0?horizontal:vertical)[layer]);
                if(refs.Count<2){notes.Add(view.Name+": thiếu reference thật cho lớp "+(layer+1)+" phương "+(axis==0?"ngang":"đứng")+".");continue;}
                foreach(int sign in new[]{-1,1})
                {
                    var perpendicular=axes[1-axis];
                    double baseline=(sign<0?crop.Min(p=>p.DotProduct(perpendicular)):crop.Max(p=>p.DotProduct(perpendicular)))+
                        sign*settings.Gap/304.8*view.Scale*(layer+1);
                    count+=CreatePlanChain(doc,view,axes[axis],perpendicular*baseline+view.ViewDirection*view.Origin.DotProduct(view.ViewDirection),refs,settings.DimType,notes);
                }
            }
            return count;
        }
    }
}

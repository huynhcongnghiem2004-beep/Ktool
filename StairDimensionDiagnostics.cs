using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
namespace SDK_HD.Dim
{
 public sealed partial class StairDeploymentCommand
 {
  private static string DescribeDimensionLayers(Document doc,View view,XYZ along,XYZ across,int runCount,StairDeploymentWindow settings)
  {
   if(!settings.Dimensions || (view is ViewPlan && !settings.PlanDimensions))return view.Name+": Dim 3 lớp đang tắt trong tùy chọn.";
   var axes=view is ViewPlan?new[]{along,across}:new[]{view.RightDirection,view.UpDirection};
   var corners=Corners(view.CropBox);int first=view is ViewPlan?runCount+2:1;
   var dimensions=new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().Where(d=>d.OwnerViewId==view.Id).ToList();
   var result=new List<string>();
   for(int axis=0;axis<2;axis++)foreach(int sign in new[]{-1,1})
   {
    var normal=axes[1-axis];double boundary=sign<0?corners.Min(p=>p.DotProduct(normal)):corners.Max(p=>p.DotProduct(normal));int found=0;
    for(int layer=0;layer<3;layer++)
    {
     double expected=boundary+sign*settings.Gap/304.8*view.Scale*(first+layer);
     if(dimensions.Any(d=>d.Curve is Line line && Math.Abs(line.Direction.DotProduct(axes[axis]))>.999999 && Math.Abs(line.Origin.DotProduct(normal)-expected)*304.8/view.Scale<.05))found++;
    }
    result.Add((axis==0?(sign<0?"dưới":"trên"):(sign<0?"trái":"phải"))+": "+found+"/3");
   }
   return view.Name+": kiểm tra lớp Dim — "+string.Join("; ",result)+". Tỷ lệ thực 1:"+view.Scale+". Thiếu lớp: xem thông báo reference/tường bao.";
  }
 }
}

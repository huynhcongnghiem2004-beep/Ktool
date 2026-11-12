using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;


namespace SDK_HD.Dim
{
    [Transaction(TransactionMode.Manual)]
    public class CreateCalloutThreeDimCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            return CalloutThreeDim.RunWc(data, true, ref message);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class BatchCreateCalloutThreeDimCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            return CalloutThreeDim.RunWcBatch(data, ref message);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class UpdateCalloutThreeDimCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            return CalloutThreeDim.RunWc(data, false, ref message);
        }
    }

    internal static partial class CalloutThreeDim
    {
        private const string Title = "Triển khai nhà WC";
        private const double Tol = 0.001; // feet; merge coincident references (~0.3 mm).
        private static readonly Guid SchemaId = new Guid("05F91931-676A-4D92-9F84-A08C83DD84B0");

        internal static System.Windows.Media.ImageSource CreateIcon()
        {
            var drawing = new System.Windows.Media.DrawingGroup();
            using (var dc = drawing.Open())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new System.Windows.Rect(0, 0, 32, 32));
                var pen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.SteelBlue, 1.5);
                dc.DrawRectangle(null, pen, new System.Windows.Rect(10, 3, 18, 17));
                for (int i = 0; i < 3; i++)
                {
                    double x = 7 - i * 3, y = 23 + i * 3;
                    dc.DrawLine(pen, new System.Windows.Point(x, 3), new System.Windows.Point(x, 20));
                    dc.DrawLine(pen, new System.Windows.Point(10, y), new System.Windows.Point(28, y));
                }
                dc.DrawRectangle(System.Windows.Media.Brushes.DarkOrange, null, new System.Windows.Rect(14, 8, 9, 7));
            }
            var image = new System.Windows.Media.DrawingImage(drawing);
            image.Freeze();
            return image;
        }

        private static string UniqueName(Document doc, string requested)
        {
            var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View))
                .Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            string name = requested;
            int suffix = 2;
            while (names.Contains(name)) name = requested + " (" + suffix++ + ")";
            return name;
        }

        private sealed class MeasureRef
        {
            internal Reference Reference;
            internal double Position;
        }

        private sealed class DimReport
        {
            internal readonly int[] Layers = new int[3];
            internal readonly List<string> Notes = new List<string>();
            internal int Count { get { return Layers.Sum(); } }
        }

        private static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KToolCalloutThreeLayerDim");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("Layer", typeof(int));
            return builder.Finish();
        }

        private static List<MeasureRef> Unique(IEnumerable<MeasureRef> references)
        {
            var result = new List<MeasureRef>();
            foreach (var reference in references.OrderBy(r => r.Position))
                if (result.Count == 0 || reference.Position - result.Last().Position > Tol) result.Add(reference);
            return result;
        }

        private static void CollectFaces(GeometryElement geometry, Transform transform, XYZ right, XYZ up,
            List<MeasureRef>[] references)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is GeometryInstance instance)
                {
                    // Parameterless GetSymbolGeometry returns original reference-bearing geometry.
                    CollectFaces(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), right, up, references);
                }
                else if (obj is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace planar) || planar.Reference == null) continue;
                        XYZ normal = transform.OfVector(planar.FaceNormal).Normalize();
                        XYZ point = transform.OfPoint(planar.Origin);
                        if (Math.Abs(normal.DotProduct(right)) > 0.999999)
                            references[0].Add(new MeasureRef { Reference = planar.Reference, Position = point.DotProduct(right) });
                        if (Math.Abs(normal.DotProduct(up)) > 0.999999)
                            references[1].Add(new MeasureRef { Reference = planar.Reference, Position = point.DotProduct(up) });
                    }
                }
            }
        }
    }

}







using Avalonia.Media;
using Avalonia.Platform;
using ProGPU.Vector;
using PathGeometry = ProGPU.Vector.PathGeometry;

namespace Avalonia.ProGpu
{
    internal class CombinedGeometryImpl : GeometryImpl
    {
        public override ProGPU.Vector.PathGeometry Path { get; }

        public CombinedGeometryImpl(ProGPU.Vector.PathGeometry path)
        {
            Path = path ?? new ProGPU.Vector.PathGeometry();
        }

        public static CombinedGeometryImpl ForceCreate(GeometryCombineMode combineMode, IGeometryImpl g1, IGeometryImpl g2)
        {
            if (g1 is GeometryImpl i1 && g2 is GeometryImpl i2)
            {
                var operation = combineMode switch
                {
                    GeometryCombineMode.Intersect => 1,
                    GeometryCombineMode.Union => 2,
                    GeometryCombineMode.Xor => 3,
                    GeometryCombineMode.Exclude => 0,
                    _ => 2
                };

                return new CombinedGeometryImpl(new ProGPU.Vector.PathGeometry
                {
                    IsCombined = true,
                    PathA = i1.Path,
                    PathB = i2.Path,
                    Op = operation
                });
            }

            return new CombinedGeometryImpl(new ProGPU.Vector.PathGeometry());
        }

        public static CombinedGeometryImpl? TryCreate(GeometryCombineMode combineMode, GeometryImpl g1, GeometryImpl g2)
        {
            return ForceCreate(combineMode, g1, g2);
        }
    }
}

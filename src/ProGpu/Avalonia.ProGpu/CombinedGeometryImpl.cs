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
            var path = new ProGPU.Vector.PathGeometry();
            if (g1 is GeometryImpl i1)
            {
                foreach (var fig in i1.Path.Figures) path.Figures.Add(fig);
            }
            if (g2 is GeometryImpl i2)
            {
                foreach (var fig in i2.Path.Figures) path.Figures.Add(fig);
            }
            return new CombinedGeometryImpl(path);
        }

        public static CombinedGeometryImpl? TryCreate(GeometryCombineMode combineMode, GeometryImpl g1, GeometryImpl g2)
        {
            return ForceCreate(combineMode, g1, g2);
        }
    }
}

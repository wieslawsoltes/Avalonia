using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Platform;
using ProGPU.Vector;

namespace Avalonia.ProGpu
{
    internal class GeometryGroupImpl : GeometryImpl
    {
        public override ProGPU.Vector.PathGeometry Path { get; }

        public GeometryGroupImpl(FillRule fillRule, IReadOnlyList<IGeometryImpl> children)
        {
            var path = new ProGPU.Vector.PathGeometry();
            foreach (var child in children)
            {
                if (child is GeometryImpl geo)
                {
                    foreach (var figure in geo.Path.Figures)
                    {
                        path.Figures.Add(figure);
                    }
                }
            }
            Path = path;
        }
    }
}

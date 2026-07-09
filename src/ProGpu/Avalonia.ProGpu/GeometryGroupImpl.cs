using System.Collections.Generic;
using Avalonia.Platform;

namespace Avalonia.ProGpu
{
    internal class GeometryGroupImpl : GeometryImpl
    {
        public override ProGPU.Vector.PathGeometry Path { get; }

        public GeometryGroupImpl(Avalonia.Media.FillRule fillRule, IReadOnlyList<IGeometryImpl> children)
        {
            var path = new ProGPU.Vector.PathGeometry
            {
                FillRule = fillRule == Avalonia.Media.FillRule.EvenOdd
                    ? ProGPU.Vector.FillRule.EvenOdd
                    : ProGPU.Vector.FillRule.Nonzero
            };
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

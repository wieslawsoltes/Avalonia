using System.Numerics;
using Avalonia.Platform;

namespace Avalonia.ProGpu
{
    internal class TransformedGeometryImpl : GeometryImpl, ITransformedGeometryImpl
    {
        public override ProGPU.Vector.PathGeometry Path { get; }

        public IGeometryImpl SourceGeometry { get; }

        public Matrix Transform { get; }

        public TransformedGeometryImpl(GeometryImpl source, Matrix transform)
        {
            SourceGeometry = source;
            Transform = transform;
            Path = source.Path.CreateTransformed(ToMatrix4x4(transform));
        }

        private static Matrix4x4 ToMatrix4x4(Matrix matrix)
        {
            return new Matrix4x4(
                (float)matrix.M11, (float)matrix.M12, 0f, 0f,
                (float)matrix.M21, (float)matrix.M22, 0f, 0f,
                0f, 0f, 1f, 0f,
                (float)matrix.M31, (float)matrix.M32, 0f, 1f);
        }
    }
}

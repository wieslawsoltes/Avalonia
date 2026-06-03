using Avalonia.Platform;
using ProGPU.Backend;

namespace Avalonia.ProGpu
{
    /// <summary>
    /// Extended bitmap implementation that allows for drawing its contents.
    /// </summary>
    internal interface IDrawableBitmapImpl : IBitmapImpl
    {
        /// <summary>
        /// Gets the underlying GPU texture.
        /// </summary>
        GpuTexture? Texture { get; }

        /// <summary>
        /// Uploads the texture to the GPU.
        /// </summary>
        void UploadToGpu();
    }
}

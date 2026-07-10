using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Platform;

namespace Avalonia.ProGpu
{
    internal static class ProGpuImageBrushSource
    {
        private static readonly PropertyInfo? s_bitmapProperty = typeof(IImageBrushSource).GetProperty(
            "Bitmap",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly PropertyInfo? s_itemProperty = GetItemProperty();

        public static IBitmapImpl? GetBitmap(IImageBrushSource? source)
        {
            // Avalonia 12.0.5 exposes the backend bitmap through an internal interface member.
            var bitmapReference = source is null ? null : s_bitmapProperty?.GetValue(source);
            return bitmapReference is null ? null : s_itemProperty?.GetValue(bitmapReference) as IBitmapImpl;
        }

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2075",
            Justification = "The internal IRef<T>.Item member is part of Avalonia's live image-brush contract and is used by Avalonia itself.")]
        private static PropertyInfo? GetItemProperty() => s_bitmapProperty?.PropertyType.GetProperty(
            "Item",
            BindingFlags.Instance | BindingFlags.Public);
    }
}

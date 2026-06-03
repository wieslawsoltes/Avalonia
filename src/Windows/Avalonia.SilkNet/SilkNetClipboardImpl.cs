using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Silk.NET.GLFW;

namespace Avalonia.SilkNet
{
    internal class SilkNetClipboardImpl : IClipboardImpl
    {
        private readonly Glfw _glfw = Glfw.GetApi();

        public unsafe Task<IAsyncDataTransfer?> TryGetDataAsync()
        {
            string text = _glfw.GetClipboardString(null);
            if (string.IsNullOrEmpty(text))
            {
                return Task.FromResult<IAsyncDataTransfer?>(null);
            }
            return Task.FromResult<IAsyncDataTransfer?>(new SilkNetClipboardDataTransfer(text));
        }

        public async Task SetDataAsync(IAsyncDataTransfer dataTransfer)
        {
            foreach (var item in dataTransfer.Items)
            {
                if (item.Contains(DataFormat.Text))
                {
                    var textObj = await item.TryGetRawAsync(DataFormat.Text);
                    if (textObj is string text)
                    {
                        SetClipboardText(text);
                        return;
                    }
                }
            }
        }

        private unsafe void SetClipboardText(string text)
        {
            _glfw.SetClipboardString(null, text);
        }

        public unsafe Task ClearAsync()
        {
            _glfw.SetClipboardString(null, string.Empty);
            return Task.CompletedTask;
        }
    }

    internal class SilkNetClipboardDataTransfer : IAsyncDataTransfer
    {
        public SilkNetClipboardDataTransfer(string text)
        {
            Items = new[] { PlatformDataTransferItem.Create(DataFormat.Text, text) };
            Formats = new[] { DataFormat.Text };
        }

        public IReadOnlyList<DataFormat> Formats { get; }
        public IReadOnlyList<IAsyncDataTransferItem> Items { get; }

        public void Dispose()
        {
        }
    }
}

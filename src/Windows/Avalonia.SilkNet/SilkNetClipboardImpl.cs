using System;
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

            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.CreateText(text));
            return Task.FromResult<IAsyncDataTransfer?>(dataTransfer);
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

    internal sealed class SilkNetClipboard : IClipboard
    {
        private readonly IClipboardImpl _clipboardImpl;

        public SilkNetClipboard(IClipboardImpl clipboardImpl)
        {
            _clipboardImpl = clipboardImpl;
        }

        public Task ClearAsync() => _clipboardImpl.ClearAsync();

        public Task SetDataAsync(IAsyncDataTransfer? dataTransfer) =>
            dataTransfer is null ? ClearAsync() : _clipboardImpl.SetDataAsync(dataTransfer);

        public Task FlushAsync() => Task.CompletedTask;

        public Task<IAsyncDataTransfer?> TryGetDataAsync() => _clipboardImpl.TryGetDataAsync();

        public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() =>
            Task.FromResult<IAsyncDataTransfer?>(null);
    }
}

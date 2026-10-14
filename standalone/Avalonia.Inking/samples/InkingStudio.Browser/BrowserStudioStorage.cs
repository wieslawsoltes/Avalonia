using System.Runtime.InteropServices.JavaScript;
namespace InkingStudio.Browser;
internal sealed partial class BrowserStudioStorage : IStudioStorage
{
    public Task<string?> OpenInkAsync() => OpenInk();
    public Task<bool> SaveAsync(string name, byte[] data, string mimeType) => Task.FromResult(SaveFile(name, Convert.ToBase64String(data), mimeType));
    public string? ReadRecovery() => ReadDraft();
    public void WriteRecovery(string json) => WriteDraft(json);
    [JSImport("openInk", "studio-host")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    private static partial Task<string?> OpenInk();
    [JSImport("saveFile", "studio-host")]
    private static partial bool SaveFile(string name, string base64, string mimeType);
    [JSImport("readDraft", "studio-host")]
    private static partial string? ReadDraft();
    [JSImport("writeDraft", "studio-host")]
    private static partial void WriteDraft(string json);
}

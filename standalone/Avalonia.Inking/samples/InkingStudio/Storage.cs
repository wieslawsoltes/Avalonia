using System.Text;
using Avalonia.Controls;
using Avalonia.Inking.Core;
using Avalonia.Platform.Storage;
namespace InkingStudio;
public interface IStudioStorage
{
    Task<string?> OpenInkAsync();
    Task<bool> SaveAsync(string name, byte[] data, string mimeType);
    string? ReadRecovery();
    void WriteRecovery(string json);
}
/// <summary>The sample's desktop adapter. Browser hosts supply the same interface using file inputs and Blob downloads.</summary>
public sealed class DesktopStudioStorage(Control owner) : IStudioStorage
{
    private static string RecoveryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Avalonia.Inking", "recovery.ink.json");
    public async Task<string?> OpenInkAsync()
    {
        var top = TopLevel.GetTopLevel(owner) ?? throw new InvalidOperationException("Window is not attached.");
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open ink document", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Ink document") { Patterns = ["*.ink.json", "*.json"] }]
        });
        if (files.Count == 0) return null;
        using var file = files[0]; await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[8192]; var text = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            if (text.Length + read > InkLimits.MaxJsonCharacters) throw new InkFormatException("Ink document exceeds the import size limit.");
            text.Append(buffer, 0, read);
        }
        return text.ToString();
    }
    public async Task<bool> SaveAsync(string name, byte[] data, string mimeType)
    {
        var top = TopLevel.GetTopLevel(owner) ?? throw new InvalidOperationException("Window is not attached.");
        using var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save ink", SuggestedFileName = name, ShowOverwritePrompt = true });
        if (file is null) return false;
        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        await stream.WriteAsync(data); await stream.FlushAsync(); return true;
    }
    public string? ReadRecovery()
    {
        if (!File.Exists(RecoveryPath)) return null;
        if (new FileInfo(RecoveryPath).Length > 16 * 1024 * 1024) return null;
        return File.ReadAllText(RecoveryPath);
    }
    public void WriteRecovery(string json)
    {
        if (json.Length > 4 * 1024 * 1024) throw new IOException("Recovery draft exceeds the sample's 4 Mi-character budget. Save an ink file instead.");
        var path = RecoveryPath; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", json, Encoding.UTF8); File.Move(path + ".tmp", path, true);
    }
}

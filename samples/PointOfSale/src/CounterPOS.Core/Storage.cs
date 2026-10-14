namespace CounterPOS.Core;
/// <summary>Implementations must atomically compare the persisted revision and replace the entire state.</summary>
public interface IPosStore
{
    Task<PosState?> ReadAsync(CancellationToken cancellationToken = default);
    Task<bool> CompareExchangeAsync(long expectedRevision, PosState replacement, CancellationToken cancellationToken = default);
}
public sealed class MemoryPosStore : IPosStore
{
    private readonly object _gate = new();
    private string? _json;
    public Task<PosState?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_json is null ? null : StateJson.Deserialize(_json));
    }
    public Task<bool> CompareExchangeAsync(long expectedRevision, PosState replacement, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var actual = _json is null ? -1 : StateJson.Deserialize(_json).Revision;
            if (expectedRevision != actual) return Task.FromResult(false);
            _json = StateJson.Serialize(replacement);
            return Task.FromResult(true);
        }
    }
}
/// <summary>Single-machine sample store: exclusive lock file, flushed temporary file, atomic rename.</summary>
public sealed class FilePosStore(string path) : IPosStore
{
    private readonly string _path = Path.GetFullPath(path);
    public async Task<PosState?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await LockAsync(cancellationToken);
        return await ReadUnlockedAsync(cancellationToken);
    }
    public async Task<bool> CompareExchangeAsync(long expectedRevision, PosState replacement, CancellationToken cancellationToken = default)
    {
        await using var lease = await LockAsync(cancellationToken);
        var current = await ReadUnlockedAsync(cancellationToken);
        if ((current?.Revision ?? -1) != expectedRevision) return false;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(StateJson.Serialize(replacement));
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await file.WriteAsync(bytes, cancellationToken);
                file.Flush(true);
            }
            File.Move(temporary, _path, true);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<PosState?> ReadUnlockedAsync(CancellationToken token) => File.Exists(_path) ? StateJson.Deserialize(await File.ReadAllTextAsync(_path, token)) : null;
    private async Task<FileStream> LockAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { await Task.Delay(25, token); }
        }
    }
}

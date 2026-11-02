using System.Collections.Concurrent;

namespace DiceFight.DiscordBot;

// Where features keep data that must outlive a redeploy (a game library).
// The API backs it with the same Cloud Storage bucket as game records and
// player accounts, under "discord/"; without a bucket it's memory only.
public interface IBotDataStore
{
    // Whether data survives a restart - features mention it when it doesn't.
    bool Durable { get; }
    Task<byte[]?> ReadAsync(string name, CancellationToken cancel = default);
    Task WriteAsync(string name, byte[] data, CancellationToken cancel = default);
}

public sealed class MemoryBotDataStore : IBotDataStore
{
    private readonly ConcurrentDictionary<string, byte[]> _data = new();
    public bool Durable => false;
    public Task<byte[]?> ReadAsync(string name, CancellationToken cancel = default) =>
        Task.FromResult(_data.TryGetValue(name, out var data) ? data : null);
    public Task WriteAsync(string name, byte[] data, CancellationToken cancel = default)
    {
        _data[name] = data;
        return Task.CompletedTask;
    }
}

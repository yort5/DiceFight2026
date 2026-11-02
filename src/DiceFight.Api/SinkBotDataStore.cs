using DiceFight.Api.Recording;
using DiceFight.DiscordBot;

namespace DiceFight.Api;

// The Discord bots' data (game libraries) in the same place as game
// records and player accounts - the Cloud Storage bucket, or the local
// folder in development - under its own discord/ names.
public sealed class SinkBotDataStore(IGameRecordSink sink) : IBotDataStore
{
    public bool Durable => true;
    public Task<byte[]?> ReadAsync(string name, CancellationToken cancel = default) => sink.ReadAsync(name, cancel);
    public Task WriteAsync(string name, byte[] data, CancellationToken cancel = default) => sink.WriteAsync(name, data, cancel);
}

using System.Text.Json.Nodes;

namespace ResumeStudio.Services;

/// <summary>The Java service owns validation, compare-and-swap, backup and transaction recovery.</summary>
public sealed class SyncBackendStore(BackendClient api) : ISyncLocalStore
{
    public async Task<SyncLocal> ReadAsync(CancellationToken cancellationToken)
    {
        var result=await api.Get<System.Text.Json.JsonElement>("sync/extended-snapshot",cancellationToken);
        DateTimeOffset? modified=result.TryGetProperty("modifiedAt",out var time)&&DateTimeOffset.TryParse(time.GetString(),out var parsed)?parsed:null;
        return new(JsonNode.Parse(result.GetProperty("snapshot").GetRawText())!.AsObject(),result.GetProperty("hash").GetString()!,modified);
    }
    public async Task ValidateAsync(JsonObject snapshot,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await api.Post<System.Text.Json.JsonElement>("sync/validate",new {snapshot});
    }
    public async Task<SyncLocal> ApplyAsync(string expectedHash,JsonObject snapshot,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result=await api.Post<System.Text.Json.JsonElement>("sync/import",new {expectedHash,snapshot});
        return new(JsonNode.Parse(result.GetProperty("snapshot").GetRawText())!.AsObject(),result.GetProperty("hash").GetString()!);
    }
}

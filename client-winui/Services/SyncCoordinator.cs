using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ResumeStudio.Services;

public sealed record SyncLocal(JsonObject Snapshot,string Hash,DateTimeOffset? ModifiedAt=null);
public interface ISyncLocalStore
{
    Task<SyncLocal> ReadAsync(CancellationToken cancellationToken);
    Task ValidateAsync(JsonObject snapshot,CancellationToken cancellationToken);
    Task<SyncLocal> ApplyAsync(string expectedHash,JsonObject snapshot,CancellationToken cancellationToken);
}
public sealed record SyncPlan(SyncAction Action,SyncLocal Local,JsonObject? RemoteEnvelope,string? ETag,string Target,SyncBaseline? Baseline=null,DateTimeOffset? RemoteModifiedAt=null,long? RemoteFileBytes=null)
{
    public bool RequiresConfirmation=>Action!=SyncAction.Unchanged;
}
public sealed record SyncBaseline(string Target,string DataHash,string ETag,string At,long? DataBytes=null);

/// <summary>Whole-snapshot comparison: every changed plan requires an explicit direction.</summary>
public sealed class SyncCoordinator(ISyncLocalStore local,ISyncRemoteStore remote,string directory,string target,string deviceId)
{
    private readonly string baselineFile=Path.Combine(directory,"sync-baseline.json");
    private SyncBaseline? ReadBaseline()
    {
        try { var baseline=JsonSerializer.Deserialize<SyncBaseline>(File.ReadAllText(baselineFile)); return baseline?.Target==target?baseline:null; }
        catch(Exception ex) when(ex is IOException or JsonException) { return null; }
    }
    public async Task<SyncPlan> InspectAsync(CancellationToken cancellationToken=default)
    {
        var current=await local.ReadAsync(cancellationToken); var fetched=await remote.ReadAsync(cancellationToken); var baseline=ReadBaseline();
        if(fetched is null) return new(baseline is null?SyncAction.Push:SyncAction.Conflict,current,null,null,target,baseline);
        var envelope=SyncProtocol.Parse(fetched.Bytes); var data=SyncProtocol.Snapshot(envelope,current.Snapshot);
        // Backend's schema and document validation runs even before deciding to upload local data over remote.
        await local.ValidateAsync(data,cancellationToken);
        var action=SyncDecision.Choose(SyncProtocol.Hash(current.Snapshot),SyncProtocol.Hash(data),baseline?.DataHash);
        if(action==SyncAction.Unchanged&&current.Snapshot["interviews"] is JsonArray {Count:>0}&&envelope["extensions"]?["interviewStudy"] is null)action=SyncAction.Push;
        return new(action,current,envelope,fetched.ETag,target,baseline,fetched.ModifiedAt,fetched.Bytes.LongLength);
    }
    public async Task<SyncAction> ExecuteAsync(SyncPlan plan,SyncAction? resolution=null,CancellationToken cancellationToken=default)
    {
        if(plan.Target!=target) throw new IOException("同步目标已改变，请重新检查。");
        if(plan.RequiresConfirmation&&resolution is not (SyncAction.Push or SyncAction.Pull)) throw new IOException("请先确认要保留的版本，再执行同步。");
        SyncAction action=plan.RequiresConfirmation?resolution!.Value:SyncAction.Unchanged;
        var current=await local.ReadAsync(cancellationToken);
        if(current.Hash!=plan.Local.Hash) throw new IOException("本机资料已改变，请重新检查同步。");
        // Re-read the observed remote, so a stale conflict dialog cannot overwrite newer cloud data.
        var observed=await remote.ReadAsync(cancellationToken);
        if(observed?.ETag!=plan.ETag) throw new IOException("云端资料已改变，请重新检查同步。");
        string etag=plan.ETag??"",dataHash;long dataBytes=SyncProtocol.DataSize(current.Snapshot);
        if(action==SyncAction.Push)
        {
            var envelope=plan.RemoteEnvelope is null?SyncProtocol.Create(current.Snapshot,deviceId):(JsonObject)plan.RemoteEnvelope.DeepClone();
            SyncProtocol.WriteSnapshot(envelope,current.Snapshot); envelope["deviceId"]=deviceId; envelope["snapshotId"]=Guid.NewGuid().ToString("N"); envelope["createdAt"]=DateTimeOffset.UtcNow.ToString("O");
            var bytes=Encoding.UTF8.GetBytes(envelope.ToJsonString()); etag=await remote.WriteAsync(bytes,plan.ETag,cancellationToken); dataHash=SyncProtocol.Hash(current.Snapshot);
        }
        else if(action==SyncAction.Pull)
        {
            if(plan.RemoteEnvelope is null) throw new IOException("云端同步包已不存在，无法下载。可明确选择重新上传本机资料。");
            var data=SyncProtocol.Snapshot(plan.RemoteEnvelope,plan.Local.Snapshot); var result=await local.ApplyAsync(plan.Local.Hash,data,cancellationToken); dataHash=SyncProtocol.Hash(result.Snapshot);
            if(dataHash!=SyncProtocol.Hash(data)) throw new IOException("导入结果不一致，本次不建立同步基线，请检查本机备份。");
            dataBytes=SyncProtocol.DataSize(result.Snapshot);
        }
        else dataHash=SyncProtocol.Hash(current.Snapshot);
        if(action==SyncAction.Unchanged&&plan.Baseline is { } previous&&previous.DataHash==dataHash&&previous.DataBytes==dataBytes&&previous.ETag==etag)return action;
        Directory.CreateDirectory(directory); string temporary=baselineFile+".tmp";
        await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new SyncBaseline(target,dataHash,etag,DateTimeOffset.UtcNow.ToString("O"),dataBytes)),cancellationToken); File.Move(temporary,baselineFile,true);
        return action;
    }
}

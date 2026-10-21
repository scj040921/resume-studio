using System.Text.Json.Nodes;
using ResumeStudio.Services;

static class ComparisonCoverage
{
    static void Check(bool value,string label) {if(!value) throw new Exception("Sync comparison: "+label);}
    public static async Task RunAsync()
    {
        var a=JsonNode.Parse("{\"resumes\":[],\"favorites\":[],\"applications\":[],\"sources\":[],\"schedule\":[],\"history\":[],\"settings\":{\"aiModel\":\"one\"}}")!.AsObject();
        var b=(JsonObject)a.DeepClone();b["settings"]!["aiModel"]="two";
        Check(SyncProtocol.DataSize(a)==SyncProtocol.DataSize(b)&&SyncProtocol.Hash(a)!=SyncProtocol.Hash(b),"same size can contain different data");
        Check(SyncProtocol.DataSize(JsonNode.Parse("{\"n\":1.00,\"a\":true}")!)==SyncProtocol.DataSize(JsonNode.Parse("{\"a\":true,\"n\":1}")!),"size uses canonical JSON across both versions");
        var localTime=DateTimeOffset.Parse("2026-01-01T01:00:00Z");var remoteTime=DateTimeOffset.Parse("2026-01-02T01:00:00Z");
        var baseline=new SyncBaseline("target",SyncProtocol.Hash(a),"\"old\"","2026-01-01T00:00:00Z",SyncProtocol.DataSize(a));
        var plan=new SyncPlan(SyncAction.Pull,new SyncLocal(a,SyncProtocol.Hash(a),localTime),SyncProtocol.Create(b,"other"),"\"new\"","target",baseline,remoteTime,1000);
        var comparison=SyncComparison.Create(plan);
        Check(plan.RequiresConfirmation&&comparison.Local.DeltaBytes==0&&comparison.Remote.DeltaBytes==0,"same-size content change still requires confirmation");
        Check(comparison.Local.ModifiedAt==localTime&&comparison.Remote.ModifiedAt==remoteTime,"actual timestamps remain distinct");
        Check(comparison.Summary.Contains("云端"),"change summary follows content baseline");
        var larger=(JsonObject)b.DeepClone();larger["settings"]!["aiModel"]="larger value";
        comparison=SyncComparison.Create(plan with {RemoteEnvelope=SyncProtocol.Create(larger,"other")});
        Check(comparison.Remote.DeltaBytes>0,"size delta is relative to last successful baseline");
        comparison=SyncComparison.Create(plan with {Baseline=null,RemoteModifiedAt=null});
        Check(comparison.BaselineText.Contains("无历史基线")&&comparison.Remote.DeltaBytes is null,"first sync never fabricates size history");
        Check(SyncComparison.Describe(comparison.Remote,comparison.BaselineText).Contains("未提供"),"unknown file time not replaced with package time");
        comparison=SyncComparison.Create(plan with {Baseline=baseline with {DataBytes=null}});
        Check(comparison.Remote.DeltaBytes is null&&comparison.BaselineText.Contains("未记录大小"),"old baseline still readable with unknown size");
        comparison=SyncComparison.Create(plan with {RemoteEnvelope=null,RemoteModifiedAt=null});
        Check(!comparison.Remote.Exists&&SyncComparison.Describe(comparison.Remote,comparison.BaselineText).Contains("不存在"),"missing cloud version distinguished from empty data");
        string directory=Path.Combine(Path.GetTempPath(),"ResumePreferenceTime-"+Guid.NewGuid().ToString("N"));var prefs=new AppPreferences();await prefs.SaveAsync(directory);
        string path=Path.Combine(directory,"settings.json");File.SetLastWriteTimeUtc(path,localTime.UtcDateTime);
        await prefs.SaveAsync(directory,skipUnchanged:true);Check(File.GetLastWriteTimeUtc(path)==localTime.UtcDateTime,"sync inspection does not refresh unchanged preference modification time");
        prefs.Theme="dark";await prefs.SaveAsync(directory,skipUnchanged:true);Check(File.GetLastWriteTimeUtc(path)!=localTime.UtcDateTime,"changed preferences still save");
        Console.WriteLine("PASS sync comparison: canonical size/delta, same-size changes, actual/unknown timestamps, first/legacy baselines, missing cloud, unchanged-preference write time");
    }
}

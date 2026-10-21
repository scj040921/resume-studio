using System.Globalization;
using System.Text.Json.Nodes;

namespace ResumeStudio.Services;

public sealed record SyncVersionInfo(bool Exists,long DataBytes,long? DeltaBytes,DateTimeOffset? ModifiedAt,string Counts);
public sealed record SyncComparison(SyncVersionInfo Local,SyncVersionInfo Remote,string Summary,string BaselineText,string Note)
{
    public static SyncComparison Create(SyncPlan plan)
    {
        string baseline=plan.Baseline is null?"无历史基线":plan.Baseline.DataBytes is null?"上次同步未记录大小":$"上次成功同步：{plan.Baseline.At}";
        SyncVersionInfo Version(JsonObject? data,DateTimeOffset? time) {long bytes=data is null?0:SyncProtocol.DataSize(data);return new(data is not null,bytes,data is null||plan.Baseline?.DataBytes is not long previous?null:bytes-previous,time,data is null?"":Counts(data));}
        bool legacy=plan.RemoteEnvelope is not null&&plan.Local.Snapshot.ContainsKey("interviews")&&plan.RemoteEnvelope["extensions"]?["interviewStudy"] is null;
        var remote=plan.RemoteEnvelope is null?null:SyncProtocol.Snapshot(plan.RemoteEnvelope,plan.Local.Snapshot);
        string summary=plan.RemoteEnvelope is null?"云端同步包不存在，请确认是否上传本机资料。":plan.Action switch {SyncAction.Push=>"本机资料发生变化，请选择最终保留的版本。",SyncAction.Pull=>"云端资料发生变化，请选择最终保留的版本。",SyncAction.Unchanged=>"两端资料一致，无需同步。",_=>"两端资料不同，请选择最终保留的版本。"};
        return new(Version(plan.Local.Snapshot,plan.Local.ModifiedAt),Version(remote,plan.RemoteModifiedAt),summary,baseline,legacy?"云端旧格式未包含面试题库。云端版本的比较大小包含将保留的本机题库；选择下载也会保留这些题库，选择上传会补齐云端扩展。":"");
    }
    public static string Counts(JsonObject data)=>$"{data["resumes"]!.AsArray().Count} 份简历 / {data["favorites"]!.AsArray().Count} 个收藏 / {data["applications"]!.AsArray().Count} 条投递 / {data["schedule"]!.AsArray().Count} 条日程 / {data["interviews"]?.AsArray().Count??0} 个题库";
    public static string Describe(SyncVersionInfo version,string baseline)
    {
        if(!version.Exists)return "云端同步包不存在，无法选择下载。";
        string delta=version.DeltaBytes is long change?(change>0?"+":"")+change.ToString("N0",CultureInfo.CurrentCulture)+" 字节":baseline;
        return $"资料大小：{version.DataBytes:N0} 字节\n相对上次同步：{delta}\n文件修改时间：{(version.ModifiedAt is DateTimeOffset time?time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"):"未提供 / 未知")}\n{version.Counts}";
    }
}

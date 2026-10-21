using System.Text;
using System.Text.Json.Nodes;
using ResumeStudio.Services;

static class ConfirmationCoverage
{
    static void Check(bool value,string label) {if(!value) throw new Exception("Sync confirmation: "+label);}
    static JsonObject Data(string title)=>JsonNode.Parse("{\"resumes\":[{\"id\":\"one\",\"title\":\""+title+"\"}],\"favorites\":[],\"applications\":[],\"sources\":[],\"schedule\":[],\"history\":[],\"settings\":{}}")!.AsObject();
    public static async Task RunAsync()
    {
        string directory=Path.Combine(Path.GetTempPath(),"ResumeConfirmation-"+Guid.NewGuid().ToString("N"));
        var local=new LocalStore(Data("first"));var remote=new RemoteStore();var sync=new SyncCoordinator(local,remote,directory,"test","device");
        async Task Rejected(SyncPlan plan,SyncAction? selection,string expected)
        {
            int writes=remote.Writes,imports=local.Imports;
            try {await sync.ExecuteAsync(plan,selection);throw new Exception("Expected rejection: "+expected);}
            catch(IOException ex) {Check(ex.Message.Contains(expected),ex.Message);}
            Check(remote.Writes==writes&&local.Imports==imports,"rejected plan never writes or imports");
        }
        var initial=await sync.InspectAsync();Check(initial.Action==SyncAction.Push,"first upload suggested");
        await Rejected(initial,null,"确认");Check(!File.Exists(Path.Combine(directory,"sync-baseline.json")),"unconfirmed first upload does not create baseline");
        await sync.ExecuteAsync(initial,SyncAction.Push);Check(remote.Writes==1,"confirmed upload executes");
        var equal=await sync.InspectAsync();Check(equal.Action==SyncAction.Unchanged,"equal content needs no selection");await sync.ExecuteAsync(equal);
        string equalBaseline=File.ReadAllText(Path.Combine(directory,"sync-baseline.json"));await sync.ExecuteAsync(await sync.InspectAsync());Check(File.ReadAllText(Path.Combine(directory,"sync-baseline.json"))==equalBaseline,"repeat equality does not rewrite completed baseline");
        local.Data["interviews"]=new JsonArray();Check((await sync.InspectAsync()).Action==SyncAction.Unchanged,"legacy empty decks do not require a migration dialog");local.Data.Remove("interviews");
        local.Data=Data("local changed");var localChange=await sync.InspectAsync();Check(localChange.Action==SyncAction.Push,"local-only change suggested");
        await Rejected(localChange,null,"确认");await sync.ExecuteAsync(localChange,SyncAction.Pull);
        Check(local.Data["resumes"]![0]!["title"]!.GetValue<string>()=="first","user may keep cloud even when only local changed");
        remote.Set(Data("remote changed"));var remoteChange=await sync.InspectAsync();Check(remoteChange.Action==SyncAction.Pull,"remote-only change suggested");
        await Rejected(remoteChange,null,"确认");await sync.ExecuteAsync(remoteChange,SyncAction.Push);
        Check(SyncProtocol.Parse(remote.Bytes!)["data"]!["resumes"]![0]!["title"]!.GetValue<string>()=="first","user may keep local even when only cloud changed");
        var old=await sync.InspectAsync();local.Data=Data("later local");await Rejected(old,SyncAction.Push,"本机资料已改变");
        var pending=await sync.InspectAsync();remote.Set(Data("later remote"));await Rejected(pending,SyncAction.Pull,"云端资料已改变");
        remote.Bytes=null;remote.Tag=null;var missing=await sync.InspectAsync();await Rejected(missing,SyncAction.Pull,"不存在");
        string baseline=File.ReadAllText(Path.Combine(directory,"sync-baseline.json"));await sync.InspectAsync();
        Check(File.ReadAllText(Path.Combine(directory,"sync-baseline.json"))==baseline,"inspect/cancel keeps last successful baseline");
        Console.WriteLine("PASS sync confirmation: first/one-sided changes require selection, either side may be selected, equality, stale-plan refusal, missing cloud, cancellation");
    }
    sealed class LocalStore(JsonObject initial) : ISyncLocalStore
    {
        public JsonObject Data=initial;public int Imports;
        public Task<SyncLocal> ReadAsync(CancellationToken token)=>Task.FromResult(new SyncLocal((JsonObject)Data.DeepClone(),SyncProtocol.Hash(Data)));
        public Task ValidateAsync(JsonObject data,CancellationToken token)=>Task.CompletedTask;
        public Task<SyncLocal> ApplyAsync(string expected,JsonObject data,CancellationToken token)
        {if(SyncProtocol.Hash(Data)!=expected) throw new IOException("本机资料已改变");Data=(JsonObject)data.DeepClone();Imports++;return ReadAsync(token);}
    }
    sealed class RemoteStore : ISyncRemoteStore
    {
        public byte[]? Bytes;public string? Tag;public int Writes;int version;
        public void Set(JsonObject data) {Bytes=Encoding.UTF8.GetBytes(SyncProtocol.Create(data,"other").ToJsonString());Tag="\""+(++version)+"\"";}
        public Task<SyncRemote?> ReadAsync(CancellationToken token)=>Task.FromResult(Bytes is null?null:new SyncRemote(Bytes.ToArray(),Tag!));
        public Task<string> WriteAsync(byte[] bytes,string? expected,CancellationToken token)
        {if(expected!=Tag) throw new IOException("云端资料已改变");Bytes=bytes.ToArray();Tag="\""+(++version)+"\"";Writes++;return Task.FromResult(Tag);}
    }
}

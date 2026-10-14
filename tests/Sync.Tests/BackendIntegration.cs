using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ResumeStudio.Services;

static class BackendIntegration
{
    static void Check(bool value,string label) { if(!value) throw new Exception("Backend sync integration: "+label); }
    public static async Task RunAsync(ISyncRemoteStore remote)
    {
        string repository=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        string jar=Path.Combine(repository,"backend/build/resume-backend.jar");
        if(!File.Exists(jar)) throw new IOException("Build the backend before running Sync.Tests: "+jar);
        string fixture=Path.Combine(Path.GetTempPath(),"ResumeSyncIntegration-"+Guid.NewGuid().ToString("N"));
        using var a=await JavaStore.StartAsync(jar,Path.Combine(fixture,"device-a"));
        using var b=await JavaStore.StartAsync(jar,Path.Combine(fixture,"device-b"));
        await File.WriteAllTextAsync(Path.Combine(a.Directory,"settings.json"),"{\"theme\":\"dark\",\"apiKey\":\"A-LOCAL\"}");
        await File.WriteAllTextAsync(Path.Combine(b.Directory,"settings.json"),"{\"apiKey\":\"B-LOCAL\"}");
        const string photo="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==";
        var resume=JsonNode.Parse("{\"id\":\"resume-a\",\"title\":\"中文同步简历\",\"templateId\":\"campus\",\"profile\":{\"name\":\"张三\"},\"sections\":[],\"futureField\":{\"preserved\":true}}")!.AsObject();
        resume["photo"]=photo;
        await a.SaveAsync("resumes",resume);
        await a.SaveAsync("favorites",JsonNode.Parse("{\"id\":\"favorite-a\",\"title\":\"开发岗位\",\"company\":\"示例公司\",\"url\":\"https://www.nowcoder.com/jobs/detail/12\"}")!.AsObject());
        var application=JsonNode.Parse("{\"id\":\"application-a\",\"title\":\"开发岗位\",\"url\":\"https://www.nowcoder.com/jobs/detail/12\",\"status\":\"已投递\",\"jobId\":\"favorite-a\",\"resumeTitle\":\"投递原版\"}")!.AsObject();
        application["resumeSnapshot"]=resume.DeepClone(); await a.SaveAsync("applications",application);
        await a.SaveAsync("sources",JsonNode.Parse("{\"id\":\"source-a\",\"name\":\"公开招聘\",\"adapter\":\"jsonld\",\"url\":\"https://example.org/jobs\",\"slug\":\"\",\"region\":\"国内\",\"enabled\":true,\"builtin\":false}")!.AsObject());
        await a.SaveAsync("schedule",JsonNode.Parse("{\"id\":\"schedule-a\",\"title\":\"面试\",\"kind\":\"面试\",\"startAt\":\"2026-10-14T10:00:00+08:00\",\"notes\":\"同步日程\",\"applicationId\":\"application-a\",\"allDay\":false,\"completed\":false,\"reminderEnabled\":true,\"reminderMinutes\":60}")!.AsObject());
        await a.SaveAsync("resumes/resume-a/history",new JsonObject());
        var ca=new SyncCoordinator(a,remote,a.Directory,"integration-dav","device-a");
        var cb=new SyncCoordinator(b,remote,b.Directory,"integration-dav","device-b");
        var initial=await ca.InspectAsync(); Check(initial.Action==SyncAction.Push,"new target uploads"); await ca.ExecuteAsync(initial,SyncAction.Push);
        var download=await cb.InspectAsync(); Check(download.Action==SyncAction.Conflict,"first association requires direction"); await cb.ExecuteAsync(download,SyncAction.Pull);
        var first=(await b.ReadAsync(default)).Snapshot;
        foreach(string kind in new[] {"resumes","favorites","applications","sources","schedule","history"}) Check(first[kind]!.AsArray().Count==1,kind+" transferred");
        Check(first["resumes"]![0]!["photo"]!.GetValue<string>()==photo,"photo intact");
        Check(first["resumes"]![0]!["futureField"]!["preserved"]!.GetValue<bool>(),"unknown field intact");
        Check(first["applications"]![0]!["resumeSnapshot"]!["title"]!.GetValue<string>()=="中文同步简历","submitted snapshot intact");
        Check(first["applications"]![0]!["timeline"]!.AsArray().Count>0,"timeline intact");
        Check(File.ReadAllText(Path.Combine(b.Directory,"settings.json")).Contains("B-LOCAL"),"receiver local key retained");
        var cloud=await remote.ReadAsync(default); Check(!Encoding.UTF8.GetString(cloud!.Bytes).Contains("A-LOCAL"),"key absent from cloud");
        Check((await cb.InspectAsync()).Action==SyncAction.Unchanged,"roundtrip establishes baseline");
        resume["title"]="A 修改版"; await a.SaveAsync("resumes",resume);
        var upload=await ca.InspectAsync(); Check(upload.Action==SyncAction.Push,"local-only change uploads"); await ca.ExecuteAsync(upload,SyncAction.Push);
        var pull=await cb.InspectAsync(); Check(pull.Action==SyncAction.Pull,"remote-only change downloads"); await cb.ExecuteAsync(pull,SyncAction.Pull);
        Check((await b.ReadAsync(default)).Snapshot["resumes"]![0]!["title"]!.GetValue<string>()=="A 修改版","updated title arrived");
        await a.DeleteAsync("favorites/favorite-a"); await ca.ExecuteAsync(await ca.InspectAsync(),SyncAction.Push); await cb.ExecuteAsync(await cb.InspectAsync(),SyncAction.Pull);
        Check((await b.ReadAsync(default)).Snapshot["favorites"]!.AsArray().Count==0,"deletion propagated");
        resume["title"]="A 分支"; await a.SaveAsync("resumes",resume);
        var other=(JsonObject)((await b.ReadAsync(default)).Snapshot["resumes"]![0]!.DeepClone()); other["title"]="B 分支"; await b.SaveAsync("resumes",other);
        await ca.ExecuteAsync(await ca.InspectAsync(),SyncAction.Push);
        var conflict=await cb.InspectAsync(); Check(conflict.Action==SyncAction.Conflict,"divergent changes conflict");
        string unchanged=(await b.ReadAsync(default)).Hash;
        try { await cb.ExecuteAsync(conflict); throw new Exception("Conflict silently applied"); } catch(IOException) { }
        Check((await b.ReadAsync(default)).Hash==unchanged,"unresolved conflict leaves data intact");
        await cb.ExecuteAsync(conflict,SyncAction.Pull);
        Check(Directory.GetFiles(Path.Combine(b.Directory,"sync-backups"),"*.json").Any(path=>File.ReadAllText(path).Contains("B 分支")),"replaced branch is in backup");
        var stale=await cb.InspectAsync(); other["title"]="B 新修改"; await b.SaveAsync("resumes",other);
        try { await cb.ExecuteAsync(stale,SyncAction.Push); throw new Exception("Stale local plan silently applied"); } catch(IOException) { }
        // Metadata extensions survive a subsequent local upload.
        cloud=await remote.ReadAsync(default); var envelope=SyncProtocol.Parse(cloud!.Bytes); envelope["futureEnvelope"]=new JsonObject {["retained"]=true};
        await remote.WriteAsync(Encoding.UTF8.GetBytes(envelope.ToJsonString()),cloud.ETag,default);
        await cb.ExecuteAsync(await cb.InspectAsync(),SyncAction.Push);
        var preserved=SyncProtocol.Parse((await remote.ReadAsync(default))!.Bytes); Check(preserved["futureEnvelope"]!["retained"]!.GetValue<bool>(),"unknown envelope retained on upload");
        var observing=await ca.InspectAsync();
        cloud=await remote.ReadAsync(default);var changedEnvelope=SyncProtocol.Parse(cloud!.Bytes);changedEnvelope["snapshotId"]=Guid.NewGuid().ToString("N");
        await remote.WriteAsync(Encoding.UTF8.GetBytes(changedEnvelope.ToJsonString()),cloud.ETag,default);
        try { await ca.ExecuteAsync(observing,SyncAction.Pull); throw new Exception("Stale cloud plan silently applied"); } catch(IOException) { }
        Console.WriteLine("PASS backend/DAV integration: two Java stores, all collections/history/photo/snapshots, keys excluded, roundtrip/update/delete, conflict backup, local/cloud stale plans, extensions");
        Console.WriteLine("Integration fixtures: "+fixture);
    }
    sealed class JavaStore : ISyncLocalStore,IDisposable
    {
        readonly Process process; readonly HttpClient http;
        public string Directory {get;}
        JavaStore(Process process,HttpClient http,string directory) {this.process=process;this.http=http;Directory=directory;}
        public static async Task<JavaStore> StartAsync(string jar,string directory)
        {
            System.IO.Directory.CreateDirectory(directory);
            var probe=new TcpListener(IPAddress.Loopback,0); probe.Start(); int port=((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
            string token=Guid.NewGuid().ToString("N"),home=Environment.GetEnvironmentVariable("JAVA_HOME")??"";
            var start=new ProcessStartInfo(home==""?"java":Path.Combine(home,"bin/java.exe")) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string argument in new[] {"--add-modules","jdk.httpserver","-jar",jar,"--port",port.ToString(),"--data",directory,"--token",token}) start.ArgumentList.Add(argument);
            var process=Process.Start(start)??throw new IOException("Java test process failed to start");
            process.OutputDataReceived+=(_,_)=>{};process.ErrorDataReceived+=(_,_)=>{};process.BeginOutputReadLine();process.BeginErrorReadLine();
            var http=new HttpClient(new HttpClientHandler {UseProxy=false}) {BaseAddress=new Uri($"http://127.0.0.1:{port}/api/"),Timeout=TimeSpan.FromSeconds(15)}; http.DefaultRequestHeaders.Add("X-Resume-Token",token);
            var store=new JavaStore(process,http,directory);
            try {for(int i=0;i<100;i++) {if(process.HasExited) throw new IOException("Java test server exited");try {using var response=await http.GetAsync("health");if(response.IsSuccessStatusCode)return store;}catch(HttpRequestException){}await Task.Delay(100);}throw new IOException("Java test server startup timed out");} catch {store.Dispose();throw;}
        }
        async Task<JsonObject> PostAsync(string path,JsonObject document,CancellationToken token=default)
        {
            using var response=await http.PostAsJsonAsync(path,document,token); string body=await response.Content.ReadAsStringAsync(token);
            if(!response.IsSuccessStatusCode) throw new IOException($"HTTP {(int)response.StatusCode}: {body}"); return JsonNode.Parse(body)!.AsObject();
        }
        public Task<JsonObject> SaveAsync(string kind,JsonObject document)=>PostAsync(kind,document);
        public async Task DeleteAsync(string path) {using var response=await http.DeleteAsync(path);response.EnsureSuccessStatusCode();}
        static SyncLocal Local(JsonObject result)=>new(result["snapshot"]!.AsObject(),result["hash"]!.GetValue<string>());
        public async Task<SyncLocal> ReadAsync(CancellationToken token) {using var response=await http.GetAsync("sync/snapshot",token);response.EnsureSuccessStatusCode();return Local(JsonNode.Parse(await response.Content.ReadAsStringAsync(token))!.AsObject());}
        public async Task ValidateAsync(JsonObject snapshot,CancellationToken token) {await PostAsync("sync/validate",new JsonObject {["snapshot"]=snapshot.DeepClone()},token);}
        public async Task<SyncLocal> ApplyAsync(string expectedHash,JsonObject snapshot,CancellationToken token)=>Local(await PostAsync("sync/import",new JsonObject {["expectedHash"]=expectedHash,["snapshot"]=snapshot.DeepClone()},token));
        public void Dispose() {http.Dispose();try {if(!process.HasExited) {process.Kill(true);process.WaitForExit(5000);}}catch(InvalidOperationException){}process.Dispose();}
    }
}

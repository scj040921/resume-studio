using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ResumeStudio.Services;

static void Check(bool value,string label) { if(!value) throw new Exception(label); }
static async Task Throws(Func<Task> action,string label) { try { await action(); } catch(IOException) { return; } throw new Exception(label); }
static JsonObject Data(string title="") => JsonNode.Parse("{\"resumes\":[],\"favorites\":[],\"applications\":[],\"sources\":[],\"schedule\":[],\"history\":[],\"settings\":{}}")!.AsObject().WithTitle(title);
var local=Data("one"); var remote=Data("two");
ModelRoundTrip.Verify();
await ConfirmationCoverage.RunAsync();
await ComparisonCoverage.RunAsync();
await MergeCoverage.RunAsync();
await DirectoryCoverage.RunAsync();
await AutoCreateCoverage.RunAsync();
await ContentRevisionCoverage.RunAsync();
Check(SyncDecision.Choose(SyncProtocol.Hash(local),SyncProtocol.Hash(remote),null)==SyncAction.Conflict,"initial conflict");
Check(SyncDecision.Choose(SyncProtocol.Hash(remote),SyncProtocol.Hash(remote),null)==SyncAction.Unchanged,"initial equal");
Check(SyncDecision.Choose(SyncProtocol.Hash(local),SyncProtocol.Hash(remote),SyncProtocol.Hash(local))==SyncAction.Pull,"remote changed");
Check(SyncDecision.Choose(SyncProtocol.Hash(local),SyncProtocol.Hash(remote),SyncProtocol.Hash(remote))==SyncAction.Push,"local changed");
Check(SyncDecision.Choose(SyncProtocol.Hash(local),SyncProtocol.Hash(remote),SyncProtocol.Hash(Data("base")))==SyncAction.Conflict,"both changed");
var envelope=SyncProtocol.Create(local,"device"); envelope["futureEnvelope"]=new JsonObject { ["foo"]=true }; envelope["extensions"]=new JsonObject {["next"]=7};
var parsed=SyncProtocol.Parse(Encoding.UTF8.GetBytes(envelope.ToJsonString()));
Check(parsed["futureEnvelope"]!["foo"]!.GetValue<bool>(),"unknown envelope retained");
var reordered=JsonNode.Parse("{\"b\":12.00,\"a\":{\"x\":true}}")!;
Check(SyncProtocol.Hash(reordered)==SyncProtocol.Hash(JsonNode.Parse("{\"a\":{\"x\":true},\"b\":12}")!),"canonical order and numbers");
Check(SyncProtocol.Hash(JsonNode.Parse("{\"future\":792281625142643375935439503360}")!)!=SyncProtocol.Hash(JsonNode.Parse("{\"future\":792281625142643375935439503361}")!),"large unknown integers retain precision");
Check(SyncProtocol.Hash(JsonNode.Parse("{\"future\":0.1234567890123456789012345678901}")!)!=SyncProtocol.Hash(JsonNode.Parse("{\"future\":0.1234567890123456789012345678902}")!),"unknown fractional numbers retain precision");
Check(SyncProtocol.Hash(JsonNode.Parse("{\"n\":123000.000e-4}")!)==SyncProtocol.Hash(JsonNode.Parse("{\"n\":12.3}")!),"arbitrary precision equivalent exponent forms");
Check(SyncProtocol.Hash(JsonNode.Parse("{\"n\":-0.000e999999999999999999999999}")!)==SyncProtocol.Hash(JsonNode.Parse("{\"n\":0}")!),"zero ignores sign and exponent");
await Throws(()=>Task.Run(()=>SyncProtocol.Parse(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":2}"))),"duplicate keys rejected");
await Throws(()=>Task.Run(()=>SyncProtocol.Parse(Encoding.UTF8.GetBytes(envelope.ToJsonString().Replace("\"schemaVersion\":1","\"schemaVersion\":99")))),"future version rejected");
await Throws(()=>Task.Run(()=>SyncWebDav.Address("http://example.org/dav/")),"plaintext endpoint rejected");

// Real local HTTP endpoint exercises request headers, no redirect, missing ETag and conditional conflicts.
using var listener=new HttpListener(); var probe=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0); probe.Start(); int port=((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
byte[]? stored=null; string etag="\"1\""; string mode="normal"; bool sawCreate=false,sawReplace=false; int count=0,writeVersion=1;
using var stop=new CancellationTokenSource();
var server=Task.Run(async ()=> { while(!stop.IsCancellationRequested) {
    HttpListenerContext ctx; try {ctx=await listener.GetContextAsync();} catch(HttpListenerException) { break; } catch(ObjectDisposedException) { break; }
    Interlocked.Increment(ref count);
    if(mode=="redirect") {ctx.Response.StatusCode=302;ctx.Response.RedirectLocation=$"http://127.0.0.1:{port}/leak";ctx.Response.Close();continue;}
    if(ctx.Request.HttpMethod=="PROPFIND") {ctx.Response.StatusCode=207;ctx.Response.ContentType="application/xml";await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/dav/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response></d:multistatus>"));}
    else if(ctx.Request.HttpMethod=="GET") { if(stored is null) ctx.Response.StatusCode=404; else {ctx.Response.StatusCode=200;if(mode!="no-etag") ctx.Response.Headers["ETag"]=etag;await ctx.Response.OutputStream.WriteAsync(stored);} }
    else { sawCreate|=ctx.Request.Headers["If-None-Match"]=="*"; sawReplace|=ctx.Request.Headers["If-Match"]==etag;
        if(mode=="conflict"||(ctx.Request.Headers["If-None-Match"]=="*"&&stored is not null)||(ctx.Request.Headers["If-Match"] is string condition&&(condition=="*"?stored is null:condition!=etag))) ctx.Response.StatusCode=412; else {using var buffer=new MemoryStream();await ctx.Request.InputStream.CopyToAsync(buffer);stored=buffer.ToArray();etag="\""+Interlocked.Increment(ref writeVersion)+"\"";ctx.Response.Headers["ETag"]=etag;ctx.Response.StatusCode=201;} }
    ctx.Response.Close();
} });
using var dav=new SyncWebDav(SyncWebDav.Address($"http://127.0.0.1:{port}/dav/"),"user","password");
Check(await dav.ReadAsync(default) is null,"404 is missing snapshot");
var bytes=Encoding.UTF8.GetBytes(envelope.ToJsonString()); await dav.WriteAsync(bytes,null,default); Check(sawCreate,"create condition");
var fetched=await dav.ReadAsync(default); Check(fetched is not null&&fetched.ETag=="\"2\"","strong ETag read");
await dav.WriteAsync(bytes,fetched!.ETag,default); Check(sawReplace,"replace condition");
mode="conflict"; await Throws(()=>dav.WriteAsync(bytes,fetched.ETag,default),"precondition failure");
mode="no-etag";Check((await dav.ReadAsync(default))!.ETag.StartsWith("sha256:",StringComparison.Ordinal),"no ETag uses content revision");
mode="redirect"; int previous=count; await Throws(()=>dav.ReadAsync(default),"redirect refused"); Check(count==previous+1,"redirect not followed");
mode="normal";stored=null;
try { await BackendIntegration.RunAsync(dav);mode="no-etag";stored=null;await BackendIntegration.RunAsync(dav); }
finally { stop.Cancel(); listener.Stop(); await server; }
await TimeoutCoverage.RunAsync();
Console.WriteLine("PASS Sync.Tests: conflict decisions, protocol extensions/version/duplicates, canonical hash, actual HTTP conditional PUT/redirect/no-ETag");

static class Fixtures { public static JsonObject WithTitle(this JsonObject data,string title) { if(title!="") data["resumes"]!.AsArray().Add(new JsonObject { ["id"]="one",["title"]=title }); return data; } }

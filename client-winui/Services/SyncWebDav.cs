using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ResumeStudio.Services;

public sealed record SyncRemote(byte[] Bytes,string ETag,DateTimeOffset? ModifiedAt=null);
/// <summary>ETag is an opaque revision: a server ETag or a sha256: content token for compatible DAV services.</summary>
public interface ISyncRemoteStore
{
    Task<SyncRemote?> ReadAsync(CancellationToken cancellationToken);
    Task<string> WriteAsync(byte[] bytes,string? expectedETag,CancellationToken cancellationToken);
}

public sealed class SyncWebDav : ISyncRemoteStore,IDisposable
{
    private readonly HttpClient client;
    private readonly Uri directory;
    private readonly Uri file;
    private readonly TimeSpan operationTimeout;
    public bool UsesContentHash { get; private set; }
    public static Uri Address(string directory)
    {
        if(!Uri.TryCreate(directory.Trim(),UriKind.Absolute,out var uri)||string.IsNullOrEmpty(uri.Host)||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!=""||
           (uri.Scheme!="https"&&!(uri.Scheme=="http"&&uri.IsLoopback))) throw new IOException("请填写 HTTPS WebDAV 目录地址；离线测试可用本机 HTTP。");
        if(Uri.UnescapeDataString(uri.AbsolutePath).Split('/').Any(p=>p is "." or ".."||p.Contains('\\'))) throw new IOException("WebDAV 目录路径无效。");
        return new Uri(uri.AbsoluteUri.TrimEnd('/')+"/");
    }
    public static Uri SyncDirectory(string address,string? savedDirectory=null)
    {
        var directory=Address(address);
        bool nutstoreEntry=directory.Host.Equals("dav.jianguoyun.com",StringComparison.OrdinalIgnoreCase)&&directory.AbsolutePath.TrimEnd('/')=="/dav";
        if(directory.AbsolutePath.TrimEnd('/').EndsWith("/ResumeStudio",StringComparison.Ordinal)||(!nutstoreEntry&&directory.AbsoluteUri==savedDirectory)) return directory;
        return new Uri(directory,"ResumeStudio/");
    }
    public SyncWebDav(Uri directory,string user,string password,TimeSpan? timeout=null)
    {
        this.directory=Address(directory.AbsoluteUri);
        file=new Uri(this.directory,"resume-studio.sync.json");
        operationTimeout=timeout??TimeSpan.FromSeconds(90);
        if(operationTimeout<=TimeSpan.Zero||operationTimeout.TotalMilliseconds>int.MaxValue) throw new ArgumentOutOfRangeException(nameof(timeout));
        client=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false}) {Timeout=Timeout.InfiniteTimeSpan};
        if(user.Contains(':')||user.Contains('\r')||user.Contains('\n')) throw new IOException("WebDAV 用户名格式无效。");
        if(user!=""||password!="") client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(user+":"+password)));
    }
    private static void Ensure(HttpResponseMessage response,string operation)
    {
        if((int)response.StatusCode is >=300 and <400) throw new IOException("WebDAV 地址返回重定向，请填写最终目录地址后重试。");
        if(response.StatusCode==HttpStatusCode.PreconditionFailed) throw new IOException("云端资料已改变，条件写入已拒绝。请重新检查同步。");
        if(response.StatusCode==HttpStatusCode.Unauthorized) throw new IOException($"WebDAV {operation}失败（HTTP 401），请核对用户名和应用密码；坚果云需要第三方应用密码，不能使用网页登录密码。");
        if(response.StatusCode==HttpStatusCode.Forbidden) throw new IOException($"WebDAV {operation}失败（HTTP 403），请检查该目录的访问或写入权限。");
        if((int)response.StatusCode==429) throw new IOException($"WebDAV {operation}失败（HTTP 429），请求频率已受限制，请暂停同步并稍后重试。");
        if(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict) throw new IOException($"WebDAV {operation}失败（HTTP {(int)response.StatusCode}），请确认目标文件夹已在服务端创建、路径和大小写正确，且允许写入；坚果云请填写 /dav/ 下的实际文件夹。");
        if(!response.IsSuccessStatusCode) throw new IOException($"WebDAV {operation}失败（HTTP {(int)response.StatusCode}），请检查目录和服务状态。");
    }
    private async Task<bool> CheckDirectoryAsync(CancellationToken cancellationToken)
    {
        using var request=new HttpRequestMessage(new HttpMethod("PROPFIND"),directory)
        {Content=new StringContent("<d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>",Encoding.UTF8,"application/xml")};
        request.Headers.Add("Depth","0");
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(response.StatusCode==HttpStatusCode.NotFound) return false;
        Ensure(response,"目录检查");
        const int maxProperties=64*1024;
        if(response.Content.Headers.ContentLength>maxProperties) throw new IOException("WebDAV 目录属性响应过大，无法确认目标文件夹。");
        using var stream=await response.Content.ReadAsStreamAsync(cancellationToken);using var buffer=new MemoryStream();byte[] block=new byte[4096];
        int read;while((read=await stream.ReadAsync(block,cancellationToken))>0)
        {if(buffer.Length+read>maxProperties) throw new IOException("WebDAV 目录属性响应过大，无法确认目标文件夹。");await buffer.WriteAsync(block.AsMemory(0,read),cancellationToken);}
        buffer.Position=0;XDocument properties;
        try {using var reader=XmlReader.Create(buffer,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=maxProperties});properties=XDocument.Load(reader);}
        catch(XmlException ex) {throw new IOException("WebDAV 目录属性无效，无法确认目标文件夹。",ex);}
        XNamespace dav="DAV:";
        foreach(var item in properties.Root?.Elements(dav+"response")??Enumerable.Empty<XElement>())
        {
            if(!Uri.TryCreate(directory,item.Element(dav+"href")?.Value,out var href)||href.AbsoluteUri.TrimEnd('/')!=directory.AbsoluteUri.TrimEnd('/')) continue;
            foreach(var propstat in item.Elements(dav+"propstat"))
            {
                var status=propstat.Element(dav+"status")?.Value.Split(' ',StringSplitOptions.RemoveEmptyEntries);
                if(status is null||status.Length<2||!int.TryParse(status[1],out int code)||code is <200 or >=300) continue;
                var type=propstat.Element(dav+"prop")?.Element(dav+"resourcetype");if(type is null) continue;
                if(type.Element(dav+"collection") is not null) return true;
                throw new IOException("WebDAV 地址不是文件夹，请填写同步包所在的目录地址。");
            }
        }
        throw new IOException("WebDAV 服务未返回有效的目录属性，无法确认目标文件夹。");
    }
    private async Task EnsureDirectoryAsync(CancellationToken cancellationToken)
    {
        if(await CheckDirectoryAsync(cancellationToken)) return;
        using var request=new HttpRequestMessage(new HttpMethod("MKCOL"),directory);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(response.StatusCode==HttpStatusCode.MethodNotAllowed)
        {
            // Another device may have created the same folder between PROPFIND and MKCOL.
            if(await CheckDirectoryAsync(cancellationToken)) return;
            throw new IOException("WebDAV 服务不支持创建该目录（HTTP 405），请确认服务支持 MKCOL，或填写已有的可写目录。");
        }
        if(response.StatusCode==HttpStatusCode.Conflict) throw new IOException("WebDAV 创建目录失败（HTTP 409），父目录不存在；请填写服务提供的已有 WebDAV 入口或文件夹地址。");
        Ensure(response,"创建目录");
        if(!await CheckDirectoryAsync(cancellationToken)) throw new IOException("WebDAV 服务未确认目录创建成功，请重新检查目录地址和服务状态。");
    }
    private static string ContentRevision(byte[] bytes)=>"sha256:"+Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public async Task<SyncRemote?> ReadAsync(CancellationToken cancellationToken)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(operationTimeout);
        try { return await ReadCoreAsync(deadline.Token); }
        catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested&&deadline.IsCancellationRequested) { throw new IOException("WebDAV 读取超时，请检查网络后重新同步。",ex); }
    }
    private async Task<SyncRemote?> ReadCoreAsync(CancellationToken cancellationToken)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,file); request.Headers.CacheControl=new CacheControlHeaderValue {NoCache=true};
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(response.StatusCode==HttpStatusCode.NotFound) {await EnsureDirectoryAsync(cancellationToken);return null;}
        Ensure(response,"读取");
        if(response.Content.Headers.ContentLength>SyncProtocol.MaxBytes) throw new IOException("同步包超过 64 MiB。");
        using var stream=await response.Content.ReadAsStreamAsync(cancellationToken); using var buffer=new MemoryStream(); byte[] block=new byte[8192];
        int read; while((read=await stream.ReadAsync(block,cancellationToken))>0) { if(buffer.Length+read>SyncProtocol.MaxBytes) throw new IOException("同步包超过 64 MiB。"); await buffer.WriteAsync(block.AsMemory(0,read),cancellationToken); }
        var bytes=buffer.ToArray();var etag=response.Headers.ETag;
        if(etag is not null&&!etag.IsWeak) return new(bytes,etag.ToString(),response.Content.Headers.LastModified);
        UsesContentHash=true;
        return new(bytes,ContentRevision(bytes),response.Content.Headers.LastModified);
    }
    public async Task<string> WriteAsync(byte[] bytes,string? expectedETag,CancellationToken cancellationToken)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(operationTimeout);
        try { return await WriteCoreAsync(bytes,expectedETag,deadline.Token); }
        catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested&&deadline.IsCancellationRequested) { throw new IOException("WebDAV 上传或回读验证超时，云端可能已写入；请重新检查同步。",ex); }
    }
    private async Task<string> WriteCoreAsync(byte[] bytes,string? expectedETag,CancellationToken cancellationToken)
    {
        if(bytes.Length>SyncProtocol.MaxBytes) throw new IOException("同步包超过 64 MiB。");
        if(expectedETag is null) await EnsureDirectoryAsync(cancellationToken);
        using var request=new HttpRequestMessage(HttpMethod.Put,file) {Content=new ByteArrayContent(bytes)};
        request.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json") {CharSet="utf-8"};
        if(expectedETag is null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        else if(expectedETag.StartsWith("sha256:",StringComparison.Ordinal))
        {
            if(expectedETag.Length!=71||!expectedETag[7..].All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f')) throw new IOException("云端版本标识无效。");
            UsesContentHash=true;
            var observed=await ReadCoreAsync(cancellationToken);
            if(observed is null||ContentRevision(observed.Bytes)!=expectedETag) throw new IOException("云端资料已改变，内容校验已拒绝上传。请重新检查同步。");
            // A content hash is not a server validator. Recheck before writing, prefer a newly available ETag,
            // otherwise require existence; this cannot close the check-to-PUT race on a server without ETags.
            request.Headers.IfMatch.Add(EntityTagHeaderValue.TryParse(observed.ETag,out var currentTag)&&!currentTag.IsWeak?currentTag:EntityTagHeaderValue.Any);
        }
        else { if(!EntityTagHeaderValue.TryParse(expectedETag,out var tag)||tag.IsWeak) throw new IOException("云端 ETag 无效。"); request.Headers.IfMatch.Add(tag); }
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);
        if(response.StatusCode==HttpStatusCode.PreconditionFailed&&expectedETag?.StartsWith("sha256:",StringComparison.Ordinal)==true&&request.Headers.IfMatch.Any(t=>t.Tag=="*"))
        {
            // Some DAV services reject wildcard If-Match even for an existing file.
            // Revalidate bytes after that rejection. Strong validators are never downgraded.
            var latest=await ReadCoreAsync(cancellationToken);
            if(latest is null||ContentRevision(latest.Bytes)!=expectedETag)throw new IOException("云端资料已改变，内容校验已拒绝上传。请重新检查同步。");
            using var retry=new HttpRequestMessage(HttpMethod.Put,file){Content=new ByteArrayContent(bytes)};
            retry.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json"){CharSet="utf-8"};
            if(EntityTagHeaderValue.TryParse(latest.ETag,out var validator)&&!validator.IsWeak)retry.Headers.IfMatch.Add(validator);
            using var retried=await client.SendAsync(retry,HttpCompletionOption.ResponseHeadersRead,cancellationToken);Ensure(retried,"上传");
        }
        else Ensure(response,"上传");
        // Some DAV servers omit the PUT response ETag. Read back to verify the exact committed bytes.
        var committed=await ReadCoreAsync(cancellationToken);
        if(committed is null||!committed.Bytes.AsSpan().SequenceEqual(bytes)) throw new IOException("云端写入后的内容不一致，请重新检查同步；本次不建立同步基线。");
        return committed.ETag;
    }
    public void Dispose()=>client.Dispose();
}

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ResumeStudio.Services;

/// <summary>Versioned transport retains unknown document and envelope fields without using typed resume models.</summary>
public static class SyncProtocol
{
    public const int MaxBytes=64*1024*1024;
    public static readonly string[] Collections=["resumes","favorites","applications","sources","schedule","history"];
    // v1 core data stays wire compatible. Updated clients carry decks in a named extension.
    public static JsonObject WireData(JsonObject snapshot) {var data=(JsonObject)snapshot.DeepClone();data.Remove("interviews");return data;}
    public static JsonObject Snapshot(JsonObject envelope,JsonObject local)
    {
        var data=(JsonObject)envelope["data"]!.DeepClone();
        if(local.ContainsKey("interviews")) data["interviews"]=(envelope["extensions"]?["interviewStudy"]?["decks"]??local["interviews"])!.DeepClone();
        return data;
    }
    public static void WriteSnapshot(JsonObject envelope,JsonObject snapshot)
    {
        envelope["data"]=WireData(snapshot);
        if(snapshot["interviews"] is JsonArray decks)
        {
            var extensions=envelope["extensions"]!.AsObject();var interview=extensions["interviewStudy"] as JsonObject??new JsonObject();
            if(interview.Parent is null)extensions["interviewStudy"]=interview;
            interview["version"]=1;interview["decks"]=decks.DeepClone();
        }
    }
    public static JsonObject Create(JsonObject snapshot,string deviceId)
    {
        var result=new JsonObject {
        ["protocol"]="resume-studio-sync", ["schemaVersion"]=1, ["snapshotId"]=Guid.NewGuid().ToString("N"),
        ["createdAt"]=DateTimeOffset.UtcNow.ToString("O"), ["deviceId"]=deviceId, ["extensions"]=new JsonObject()
        };WriteSnapshot(result,snapshot);return result;
    }
    public static JsonObject Parse(byte[] bytes)
    {
        if(bytes.Length>MaxBytes) throw new IOException("同步包超过 64 MiB。");
        try
        {
            using var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions {MaxDepth=40}); CheckDuplicates(doc.RootElement);
            var root=JsonNode.Parse(bytes)!.AsObject();
            if(root["protocol"]?.GetValue<string>()!="resume-studio-sync"||root["schemaVersion"]?.GetValue<int>()!=1) throw new IOException("同步协议版本不兼容，请更新软件。");
            foreach(string key in new[] {"snapshotId","deviceId"}) if(root[key]?.GetValue<string>() is not {Length:>0 and <=100}) throw new IOException("同步包标识无效。");
            if(!DateTimeOffset.TryParse(root["createdAt"]?.GetValue<string>(),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out _)) throw new IOException("同步包时间无效。");
            if(root["extensions"] is not JsonObject) throw new IOException("同步扩展格式无效。");
            if(root["extensions"]!["interviewStudy"] is JsonNode study && (study is not JsonObject||study["version"]?.GetValue<int>()!=1||study["decks"] is not JsonArray))throw new IOException("面试题同步扩展版本或格式无效。");
            var data=root["data"]!.AsObject();
            if(data.Count!=Collections.Length+1||Collections.Any(key=>data[key] is not JsonArray)||data["settings"] is not JsonObject) throw new IOException("同步资料集合不完整。");
            return root;
        }
        catch(Exception ex) when(ex is JsonException or InvalidOperationException or FormatException or NullReferenceException) { throw new IOException("同步包 JSON 格式无效。",ex); }
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object) { var names=new HashSet<string>(StringComparer.Ordinal); foreach(var property in element.EnumerateObject()) { if(!names.Add(property.Name)) throw new IOException("同步 JSON 包含重复字段。"); CheckDuplicates(property.Value); } }
        else if(element.ValueKind==JsonValueKind.Array) foreach(var item in element.EnumerateArray()) CheckDuplicates(item);
    }
    private static byte[] CanonicalBytes(JsonNode data)
    {
        using var document=JsonDocument.Parse(data.ToJsonString()); using var buffer=new MemoryStream();
        using(var writer=new Utf8JsonWriter(buffer)) Canonical(writer,document.RootElement);
        return buffer.ToArray();
    }
    public static string Hash(JsonNode data)=>Convert.ToHexString(SHA256.HashData(CanonicalBytes(data))).ToLowerInvariant();
    // Comparison bytes have a shared Java/C# definition; existing revision hashes stay unchanged.
    public static long DataSize(JsonNode data)
    {
        using var document=JsonDocument.Parse(data.ToJsonString());var text=new System.Text.StringBuilder();
        void String(string value) {text.Append('"');foreach(char c in value)text.Append(c switch {'"'=>"\\\"",'\\'=>"\\\\",'\n'=>"\\n",'\r'=>"\\r",'\t'=>"\\t",< ' '=>"\\u"+((int)c).ToString("x4",CultureInfo.InvariantCulture),_=>c.ToString()});text.Append('"');}
        void Write(JsonElement element)
        {
            if(element.ValueKind==JsonValueKind.Object) {text.Append('{');bool first=true;foreach(var p in element.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal)){if(!first)text.Append(',');first=false;String(p.Name);text.Append(':');Write(p.Value);}text.Append('}');}
            else if(element.ValueKind==JsonValueKind.Array) {text.Append('[');bool first=true;foreach(var v in element.EnumerateArray()){if(!first)text.Append(',');first=false;Write(v);}text.Append(']');}
            else if(element.ValueKind==JsonValueKind.String)String(element.GetString()!);
            else if(element.ValueKind==JsonValueKind.Number)text.Append(CanonicalNumber(element.GetRawText()));
            else text.Append(element.GetRawText());
        }
        Write(document.RootElement);return System.Text.Encoding.UTF8.GetByteCount(text.ToString());
    }
    private static void Canonical(Utf8JsonWriter writer,JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object) { writer.WriteStartObject(); foreach(var property in element.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal)) { writer.WritePropertyName(property.Name); Canonical(writer,property.Value); } writer.WriteEndObject(); }
        else if(element.ValueKind==JsonValueKind.Array) { writer.WriteStartArray(); foreach(var item in element.EnumerateArray()) Canonical(writer,item); writer.WriteEndArray(); }
        else if(element.ValueKind==JsonValueKind.Number) writer.WriteRawValue(CanonicalNumber(element.GetRawText()),skipInputValidation:true);
        else element.WriteTo(writer);
    }
    private static string CanonicalNumber(string token)
    {
        // Normalize the exact JSON token; floating-point conversions can hide changes in unknown fields.
        bool negative=token[0]=='-'; string unsigned=negative?token[1..]:token;
        int exponentIndex=unsigned.IndexOfAny(['e','E']);
        string mantissa=exponentIndex<0?unsigned:unsigned[..exponentIndex];
        BigInteger exponent=exponentIndex<0?BigInteger.Zero:BigInteger.Parse(unsigned[(exponentIndex+1)..],CultureInfo.InvariantCulture);
        int point=mantissa.IndexOf('.');
        if(point>=0) { exponent-=mantissa.Length-point-1; mantissa=mantissa.Remove(point,1); }
        string digits=mantissa.TrimStart('0'); if(digits.Length==0) return "0";
        int significantLength=digits.TrimEnd('0').Length; exponent+=digits.Length-significantLength;
        string coefficient=(negative?"-":"")+digits[..significantLength];
        return exponent.IsZero?coefficient:coefficient+"e"+exponent.ToString(CultureInfo.InvariantCulture);
    }
}

public enum SyncAction { Unchanged, Push, Pull, Conflict }
public static class SyncDecision
{
    public static SyncAction Choose(string local,string remote,string? baseline)
    {
        if(local==remote) return SyncAction.Unchanged;
        if(baseline is null) return SyncAction.Conflict;
        if(local==baseline) return SyncAction.Pull;
        if(remote==baseline) return SyncAction.Push;
        return SyncAction.Conflict;
    }
}

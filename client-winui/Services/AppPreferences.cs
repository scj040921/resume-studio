using System.Text.Json;

namespace ResumeStudio.Services;

/// <summary>Appearance is local to this app, with conservative defaults for existing users.</summary>
public sealed class AppPreferences
{
    private readonly SemaphoreSlim fileLock = new(1,1);
    public string InterfaceStyle { get; set; } = "classic";
    public string Theme { get; set; } = "system";
    public bool LivePreview { get; set; } = true;
    public bool MotionEnabled { get; set; } = true;
    public int PreviewDelayMs { get; set; } = 900;
    public string AiEndpoint { get; set; } = "";
    public string AiModel { get; set; } = "";
    public string UpdateChannel { get; set; } = "stable";
    public string UpdateRoute { get; set; } = "auto";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public static AppPreferences Load(string directory)
    {
        try
        {
            var result=JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(Path.Combine(directory,"settings.json")),BackendClient.Json)??new();
            result.InterfaceStyle=result.InterfaceStyle=="haru"?"haru":"classic";
            result.Theme=result.Theme is "light" or "dark"?result.Theme:"system";
            result.UpdateChannel=result.UpdateChannel=="nightly"?"nightly":"stable";
            result.UpdateRoute=result.UpdateRoute is "direct" or "proxy"?result.UpdateRoute:"auto";
            result.PreviewDelayMs=Math.Clamp(result.PreviewDelayMs,350,2000); return result;
        }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public async Task SaveAsync(string directory,bool skipUnchanged=false)
    {
        await fileLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"settings.json"),temporary=path+".tmp";
            string json=JsonSerializer.Serialize(this,BackendClient.Json);
            if(skipUnchanged&&File.Exists(path)&&await File.ReadAllTextAsync(path)==json)return;
            await File.WriteAllTextAsync(temporary,json);
            File.Move(temporary,path,true);
        }
        finally { fileLock.Release(); }
    }
}

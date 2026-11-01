using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private readonly CancellationTokenSource updateCancellation=new();
    private bool updateBusy;
    private AvailableUpdate? availableUpdate;
    private TextBlock? updateStatus;
    private Button? installUpdateButton;
    private StackPanel? updateLogPanel;
    private IReadOnlyList<ReleaseNote> updateNotes=[];
    private string updateNotesError="";
    private ReleaseIdentity? checkedReleaseIdentity;
    private static bool VerificationSession=>Environment.GetCommandLineArgs().Any(a=>a is "--self-test" or "--feature-test" or "--interaction-test" or "--interaction-smoke-test" or "--iteration-test" or "--benchmark" or "--appearance-review" or "--template-assets" or "--workbench-test" or "--navigation-test" or "--capture-layout-test" or "--favorites-editor-test" or "--lightweight-test" or "--interview-test" or "--notification-test" or "--resume-import-test" or "--picker-test");
    private ReleaseUpdate UpdateClient()=>new(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Assets","UpdatePublicKey.pem")));
    private UIElement UpdateSettingsCard()
    {
        var panel=Stack(12); var identity=ReleaseIdentity.Current(AppContext.BaseDirectory); panel.Children.Add(Text("软件更新",20,true)); panel.Children.Add(Text("当前版本："+identity.Display,14,true));
        var channel=new ComboBox {Header="更新通道",ItemsSource=new[] {"Stable · 稳定发行版","Nightly · 每日测试版"},SelectedIndex=preferences.UpdateChannel=="nightly"?1:0,HorizontalAlignment=HorizontalAlignment.Stretch};
        channel.SelectionChanged+=async (_,_)=>await Run(async ()=> {preferences.UpdateChannel=channel.SelectedIndex==1?"nightly":"stable"; availableUpdate=null; checkedReleaseIdentity=null; updateNotes=[]; ShowUpdateNotes(); if(installUpdateButton is not null) installUpdateButton.IsEnabled=false; await preferences.SaveAsync(api.DataDirectory); if(updateStatus is not null) updateStatus.Text="通道已切换，请检查更新。";}); panel.Children.Add(channel);
        var route=new ComboBox {Header="更新连接方式",ItemsSource=new[] {"自动 · 直连失败时使用公益代理","GitHub 直连","GH-Proxy 公益代理"},SelectedIndex=preferences.UpdateRoute=="direct"?1:preferences.UpdateRoute=="proxy"?2:0,HorizontalAlignment=HorizontalAlignment.Stretch};
        route.SelectionChanged+=async (_,_)=>await Run(async ()=> {preferences.UpdateRoute=route.SelectedIndex==1?"direct":route.SelectedIndex==2?"proxy":"auto"; await preferences.SaveAsync(api.DataDirectory);}); panel.Children.Add(route);
        var automatic=new ToggleSwitch {Header="启动时检查更新",IsOn=preferences.CheckUpdatesOnStartup}; automatic.Toggled+=async (_,_)=>await Run(async ()=> {preferences.CheckUpdatesOnStartup=automatic.IsOn; await preferences.SaveAsync(api.DataDirectory);}); panel.Children.Add(automatic);
        panel.Children.Add(Text("Stable 为正式稳定版；Nightly 根据最新提交每日生成，有源码变化才发布。更新来源："+ReleaseUpdate.Repository+"，签名和文件校验通过后才能安装。",12));
        panel.Children.Add(Text("公益代理仅用于公开的更新信息和程序包，不传输简历、API Key 或登录信息。",12));
        updateStatus=Text(availableUpdate is null?"尚未检查更新。":"可更新到："+availableUpdate.Identity.Display,13); panel.Children.Add(updateStatus);
        var actions=Row(); actions.Children.Add(Button("检查更新",()=>CheckForUpdates(false),true)); installUpdateButton=Button("下载并安装",InstallAvailableUpdate); installUpdateButton.IsEnabled=availableUpdate is not null; actions.Children.Add(installUpdateButton); actions.Children.Add(Button("查看发行记录 ↗",()=>Launch("https://github.com/"+ReleaseUpdate.Repository+"/releases"))); panel.Children.Add(actions);
        updateLogPanel=Stack(8);panel.Children.Add(updateLogPanel);ShowUpdateNotes();
        return Card(panel);
    }
    private void ShowUpdateNotes()
    {
        if(updateLogPanel is null)return;updateLogPanel.Children.Clear();
        if(checkedReleaseIdentity is null)return;
        var installed=ReleaseIdentity.Current(AppContext.BaseDirectory);
        updateLogPanel.Children.Add(Text($"更新日志 · {checkedReleaseIdentity.Display} → 当前 {installed.Display}",14,true));
        if(checkedReleaseIdentity.Channel=="nightly"&&checkedReleaseIdentity.Version==installed.Version&&checkedReleaseIdentity.Commit!=installed.Commit)
            updateLogPanel.Children.Add(Text("同版本 Nightly 展示本版本累计说明。",11));
        if(updateNotesError!="")updateLogPanel.Children.Add(Text(updateNotesError,12));
        var entries=Stack(8);
        for(int i=0;i<updateNotes.Count;i++) {
            var note=updateNotes[i];bool baseline=note.Version==installed.Version;
            var text=Text(note.Body,13);text.IsTextSelectionEnabled=true;
            entries.Children.Add(Disclosure("v"+note.Version+(baseline?" · 当前版本基线":""),text,i==0));
        }
        if(updateNotes.Count>0)updateLogPanel.Children.Add(new ScrollViewer {Content=entries,MaxHeight=360,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    private async Task CheckForUpdates(bool background)
    {
        if(updateBusy||closing) return; updateBusy=true; string channel=preferences.UpdateChannel,route=preferences.UpdateRoute;
        try {
            if(updateStatus is not null) updateStatus.Text="正在读取 GitHub Releases…";
            using var client=UpdateClient(); var found=await client.Check(channel,route,ReleaseIdentity.Current(AppContext.BaseDirectory),updateCancellation.Token);
            if(channel!=preferences.UpdateChannel) return; availableUpdate=found;
            if(installUpdateButton is not null) installUpdateButton.IsEnabled=found is not null;
            if(updateStatus is not null) updateStatus.Text=found is null?"当前通道暂无可用的新版本。":"发现 "+found.Identity.Display+" · "+client.LastRoute;
            checkedReleaseIdentity=client.LatestCheckedRelease?.Identity;updateNotes=client.ChangeLog;updateNotesError=client.ChangeLogError;ShowUpdateNotes();
            if(background&&found is not null) Success("发现软件更新",found.Identity.Display+"，可在设置 → 软件更新中下载并安装。");
        }
        catch(Exception ex) when(ex is IOException or HttpRequestException or JsonException or System.Security.Cryptography.CryptographicException or FormatException or OperationCanceledException) {
            if(updateStatus is not null) updateStatus.Text="更新检查未完成："+ex.Message;
            if(!background&&!updateCancellation.IsCancellationRequested) Success("更新检查未完成",ex.Message);
        }
        finally {updateBusy=false;}
    }
    private async Task InstallAvailableUpdate()
    {
        if(updateBusy||availableUpdate is null) return; var selected=availableUpdate;
        if(await new ContentDialog {XamlRoot=shell.XamlRoot,Title="安装软件更新",Content="更新到 "+selected.Identity.Display+"。下载和校验完成后会保存当前简历、关闭并重新打开软件。原程序会保留一份备份。",PrimaryButtonText="下载并安装",CloseButtonText="返回"}.ShowAsync()!=ContentDialogResult.Primary) return;
        updateBusy=true; string workspace=Path.Combine(api.DataDirectory,"updates",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(workspace);
        try {
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(updateCancellation.Token);
            var watch=System.Diagnostics.Stopwatch.StartNew();var status=Text("正在下载更新包…",14); var progress=new ProgressBar {Minimum=0,Maximum=100,Height=6};var progressPanel=Stack(10);progressPanel.Children.Add(status);progressPanel.Children.Add(progress);var loading=new ContentDialog {XamlRoot=shell.XamlRoot,Title="下载软件更新",Content=progressPanel,CloseButtonText="取消"};
            loading.CloseButtonClick+=(_,_)=>cancellation.Cancel(); var shown=loading.ShowAsync(); string source;
            try {
                using var client=UpdateClient(); string zip=Path.Combine(workspace,selected.Identity.PackageName);
                await client.Download(selected,preferences.UpdateRoute,zip,new Progress<double>(p=>{progress.Value=p;status.Text=$"正在下载：{p:0}% · {selected.Package.Size*p/100/1024/Math.Max(0.1,watch.Elapsed.TotalSeconds):0} KiB/s";}),cancellation.Token);
                status.Text="下载完成，正在校验和准备安装…"; source=await UpdateInstaller.Prepare(zip,workspace,selected.Identity,cancellation.Token);
                File.Delete(zip); // The verified extracted payload is sufficient; avoid keeping both copies.
            }
            finally {loading.Hide(); await shown;}
            closeRequested=true; await FinishSyncBeforeClose();
            await maintenanceLock.WaitAsync(cancellation.Token);maintenanceLock.Release();
            editTimer.Stop(); previewTimer.Stop(); await SaveCurrent();
            reminderTimer.Stop();CancelJobSearch();await DetachPreviewAsync(true);await api.StopAsync();
            await UpdateInstaller.Launch(source,workspace,AppContext.BaseDirectory,api.DataDirectory);
            closing=true;updateCancellation.Cancel();api.Dispose();Close();
        }
        catch(OperationCanceledException) {Success("更新已取消","当前程序保持原样。");}
        finally {updateBusy=false;if(!closing) closeRequested=false;}
    }
    private async Task AcknowledgeUpdateStartup()
    {
        string? path=Environment.GetEnvironmentVariable("RESUMESTUDIO_UPDATE_ACK"); if(string.IsNullOrEmpty(path)) return;
        string root=Path.GetFullPath(Path.Combine(api.DataDirectory,"updates"))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(path).StartsWith(root,StringComparison.OrdinalIgnoreCase)) return;
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new {status="ready",version=ReleaseIdentity.Current(AppContext.BaseDirectory).Display},ReleaseUpdate.Json));
    }
}

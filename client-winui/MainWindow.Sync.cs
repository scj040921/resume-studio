using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private bool syncBusy;
    private TaskCompletionSource? activeSync;
    private ContentDialog? syncConflictDialog;
    private List<(Control Control,bool Enabled)> DisableSyncInputs()
    {
        var controls=new List<(Control,bool)>();
        void Visit(DependencyObject parent)
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            {
                var child=VisualTreeHelper.GetChild(parent,i);
                if(child is Control control) controls.Add((control,control.IsEnabled));
                Visit(child);
            }
        }
        Visit(shell);
        foreach(var (control,_) in controls) control.IsEnabled=false;
        return controls;
    }
    private async Task FinishSyncBeforeClose()
    {
        if(activeSync is null) return;
        syncConflictDialog?.Hide();
        saveState.Text="正在完成同步，然后关闭…";
        await activeSync.Task;
    }
    private void AddSyncSettings(StackPanel panel)
    {
        SyncSettings settings;
        try {settings=SyncSettings.Load(api.DataDirectory);} catch(IOException ex) {panel.Children.Add(Card(Text(ex.Message,13)));return;}
        var content=Stack(12); content.Children.Add(Text("跨端同步 · WebDAV",20,true));
        content.Children.Add(Text("手动同步简历、内嵌照片、历史版本、职位收藏、投递记录、日程、公司来源、面试题库与掌握进度和非敏感设置。电脑和安卓填写相同实际同步目录。API Key、网站 Cookie、登录会话与同步凭据只留在本机。",13));
        var address=new TextBox {Header="WebDAV 地址",PlaceholderText="https://dav.jianguoyun.com/dav/",Text=settings.DirectoryUrl};
        content.Children.Add(Text("填写服务提供的 WebDAV 入口或已有文件夹地址，首次同步自动创建并使用其下的 ResumeStudio 文件夹；已有实际同步目录继续使用。旧坚果云 /dav/ 入口会改为专用目录，目录变化时需重新填写密码。坚果云可直接填 https://dav.jianguoyun.com/dav/，用户名用坚果云账号，密码用第三方应用密码。",12));
        var username=new TextBox {Header="用户名",Text=settings.Username};
        var password=new PasswordBox {Header="密码 / 应用密码（留空保留该目标已保存密码）"};
        content.Children.Add(address);content.Children.Add(username);content.Children.Add(password);
        var status=Text("同步包：resume-studio.sync.json · 目前仅 WebDAV，GitHub 后端接口已预留。",12);content.Children.Add(status);
        async Task<SyncSettings> Configure()
        {
            var next=new SyncSettings {DirectoryUrl=SyncWebDav.SyncDirectory(address.Text,settings.DirectoryUrl).AbsoluteUri,Username=username.Text.Trim(),DeviceId=settings.DeviceId};
            bool same=next.DirectoryUrl==settings.DirectoryUrl&&next.Username==settings.Username;
            next.ProtectedPassword=same?settings.ProtectedPassword:"";
            await next.SaveAsync(api.DataDirectory,password.Password.Length>0?password.Password:null);
            password.Password="";address.Text=next.DirectoryUrl;settings=next;return next;
        }
        var actions=Row(8);
        actions.Children.Add(Button("保存同步配置",async ()=> {await Configure();status.Text="已保存本机配置，密码按当前 Windows 用户加密。尚未执行同步。";}));
        actions.Children.Add(Button("检查并同步",async ()=>
        {
            if(syncBusy) return;
            syncBusy=true; activeSync=new(TaskCreationOptions.RunContinuationsAsynchronously);
            bool remindersWereRunning=reminderTimer.IsEnabled;
            List<(Control Control,bool Enabled)> blockedInputs=[];
            try
            {
                blockedInputs=DisableSyncInputs();
                var connection=await Configure();
                await SaveCurrent(); await preferences.SaveAsync(api.DataDirectory,skipUnchanged:true); editTimer.Stop();previewTimer.Stop();reminderTimer.Stop();CancelJobSearch();
                shell.IsHitTestVisible=false; status.Text="正在检查本机与云端资料…";
                using var dav=new SyncWebDav(new Uri(connection.DirectoryUrl),connection.Username,await connection.PasswordAsync());
                var coordinator=new SyncCoordinator(new SyncBackendStore(api),dav,api.DataDirectory,connection.Target,connection.DeviceId);
                var plan=await coordinator.InspectAsync(); SyncAction? direction=null;
                if(dav.UsesContentHash) status.Text="云端缺少强 ETag，正在使用内容校验；请避免多台设备同时同步。";
                if(closeRequested) return;
                if(plan.RequiresConfirmation)
                {
                    var dialog=CreateSyncComparisonDialog(plan,dav.UsesContentHash);
                    syncConflictDialog=dialog;
                    var choice=await dialog.ShowAsync(); syncConflictDialog=null;
                    if(choice==ContentDialogResult.None||closeRequested) {status.Text="同步已取消，资料未改变。";return;}
                    direction=choice==ContentDialogResult.Primary?SyncAction.Push:SyncAction.Pull;
                }
                var action=await coordinator.ExecuteAsync(plan,direction);
                if(action==SyncAction.Pull)
                {
                    current=null;dirty=false;shownCompanyJobs=null;companySearch=null;preferences=AppPreferences.Load(api.DataDirectory);
                    await DetachPreviewAsync(true);BuildShell();await Navigate("settings");
                }
                string message=action switch {SyncAction.Push=>"本机资料已上传，云端内容回读验证通过。",SyncAction.Pull=>"云端资料已导入；替换前备份保存在数据目录 sync-backups。",_=>"本机与云端资料一致，无需同步。"};
                if(dav.UsesContentHash) message+=" 当前使用内容校验，无法完全阻止同时上传造成的覆盖；请同一时间只在一台设备同步。";
                status.Text=message;Success("同步完成",message);
            }
            catch(Exception ex) {status.Text="同步未完成："+ex.Message;throw;}
            finally {foreach(var (control,enabled) in blockedInputs) control.IsEnabled=enabled;shell.IsHitTestVisible=true;syncBusy=false;syncConflictDialog=null;if(remindersWereRunning&&!closeRequested) reminderTimer.Start();activeSync?.TrySetResult();activeSync=null;}
        },true));
        actions.Children.Add(Button("清除本机同步密码",async ()=> {settings.ProtectedPassword="";await settings.SaveAsync(api.DataDirectory,"");password.Password="";status.Text="已清除本机保存密码。";}));
        content.Children.Add(actions); content.Children.Add(Text("两端一致时无需同步；只要内容不同，就显示资料大小、相对上次同步的变化和文件修改时间，由你确认保留版本。支持强 ETag 时使用条件写入；缺少强 ETag 时使用内容校验，请同一时间只在一台设备同步。同步包以明文保存于你选择的 WebDAV，包含个人简历信息；请使用个人目录。每次下载替换前保留本机整包备份，备份需自行管理。",12));
        panel.Children.Add(Card(content));
    }
    private ContentDialog CreateSyncComparisonDialog(SyncPlan plan,bool contentCheck=false)
    {
        var comparison=SyncComparison.Create(plan);var details=Stack(12);
        details.Children.Add(Text(comparison.Summary,14,true));
        details.Children.Add(Text(comparison.BaselineText,12));
        details.Children.Add(Card(Text("本机版本\n"+SyncComparison.Describe(comparison.Local,comparison.BaselineText),14)));
        details.Children.Add(Card(Text("云端版本\n"+SyncComparison.Describe(comparison.Remote,comparison.BaselineText),14)));
        if(comparison.Note.Length>0)details.Children.Add(Text(comparison.Note,12));
        details.Children.Add(Text("资料大小按统一规范化 JSON 的 UTF-8 字节数计算，包含题库，不包含传输包头。大小和时间仅供核对，内容不同即需选择。选择会替换目标端整套资料并传播删除；下载前保存本机恢复备份。",12));
        if(contentCheck)details.Children.Add(Text("云端缺少强 ETag，使用内容校验；请同一时间只在一台设备同步。",12));
        return new ContentDialog
        {
            XamlRoot=shell.XamlRoot,Title="比较同步版本",CloseButtonText="取消",PrimaryButtonText="保留本机并上传",
            SecondaryButtonText=plan.RemoteEnvelope is null?"":"保留云端并下载",DefaultButton=ContentDialogButton.Close,
            Content=new ScrollViewer {Content=details,MaxHeight=520}
        };
    }
}

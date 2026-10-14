using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task NavigationTest()
    {
        string output = Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT") ?? Path.Combine(api.DataDirectory, "navigation");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Check(bool condition, string label) { if(!condition) throw new Exception(label); checks.Add(label); }
        async Task Resize(double width)
        {
            double scale = shell.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(850 * scale)));
            await Task.Delay(350);
        }
        void Selected(string key)
        {
            if(haruStyle) Check(ReferenceEquals(modernNavigation!.SelectedItem, modernNavigationItems[key]), "Haru selects " + key);
            else Check(((SolidColorBrush)navigation.Single(b => (string)b.Tag == key).Background).Color == ((SolidColorBrush)Brush("#294267")).Color, "classic highlights " + key);
        }
        try
        {
            Title = "拾简 · 多级导航验证（隔离数据）";
            foreach(string style in new[] { "classic", "haru" })
            {
                preferences.InterfaceStyle = style; preferences.Theme = "light";
                await Resize(1400); BuildShell(); await Navigate("jobs"); await Task.Delay(200);
                Selected("jobs");
                if(haruStyle)
                {
                    var group = modernNavigationItems["discovery"];
                    Check(!group.SelectsOnInvoked && group.IsExpanded, "Haru discovery is an expanded non-route group");
                    Check(group.MenuItems.OfType<NavigationViewItem>().Select(i => (string)i.Tag).SequenceEqual(new[] { "jobs", "sources" }), "Haru renders both child routes in order");
                    group.IsExpanded = false;
                }
                else
                {
                    Check(classicChildMenus["discovery"].Visibility == Visibility.Visible, "classic renders expanded child routes");
                    await ToggleClassicNavigationGroup("discovery");
                    Check(classicChildMenus["discovery"].Visibility == Visibility.Collapsed, "classic group collapses without changing page");
                    Check(currentPage == "jobs", "classic group toggle keeps current page");
                }
                // Invoke the actual page button, so the verification follows the user entry point.
                var sourceButton = FeatureButton(body, "公司来源管理") ?? throw new Exception("source management page button missing");
                ((IInvokeProvider)new ButtonAutomationPeer(sourceButton).GetPattern(PatternInterface.Invoke)).Invoke();
                for(int n = 0; n < 50 && (currentPage != "sources" || pageTitle.Text != "公司来源管理"); n++) await Task.Delay(50);
                Check(currentPage == "sources" && pageTitle.Text == "公司来源管理", style + " page action reaches source management");
                Selected("sources");
                Check(haruStyle ? modernNavigationItems["discovery"].IsExpanded : classicChildMenus["discovery"].Visibility == Visibility.Visible, style + " child navigation reopens its parent");
                await SaveNativePreview(Path.Combine(output, style + "-sources.png"));
                UpdateNavigation("editor"); Selected("resumes");
                Check(currentPage == "sources", style + " selection update does not change page data");
                BuildShell(); Selected("sources");
                Check(haruStyle ? modernNavigationItems["discovery"].IsExpanded : classicChildMenus["discovery"].Visibility == Visibility.Visible, style + " rebuild preserves active child selection");
                await Resize(720);
                if(haruStyle)
                {
                    Check(modernNavigation!.DisplayMode != NavigationViewDisplayMode.Expanded, "Haru narrows its navigation pane");
                    Selected("sources");
                }
                else
                {
                    Check(classicCompact && shell.ColumnDefinitions[0].Width.Value == 64, "classic compact sidebar reserves only 64 DIP");
                    Check(classicChildMenus["discovery"].Visibility == Visibility.Collapsed, "classic compact group hides inline children");
                    var group = navigation.Single(b => (string)b.Tag == "discovery");
                    var flyout = (MenuFlyout)FlyoutBase.GetAttachedFlyout(group);
                    Check(flyout.Items.OfType<MenuFlyoutItem>().Select(i => (string)i.Tag).SequenceEqual(new[] { "jobs", "sources" }), "classic compact flyout keeps both routes");
                    await ToggleClassicNavigationGroup("discovery"); await Task.Delay(150);
                    Check(flyout.IsOpen, "classic compact discovery button opens the native flyout");
                    await SaveNativePreview(Path.Combine(output, style + "-compact-menu.png"));
                    flyout.Hide();
                }
                await SaveNativePreview(Path.Combine(output, style + "-compact.png"));
                await Navigate("jobs"); Selected("jobs");
                await Resize(1400);
                Check(haruStyle || !classicCompact && classicChildMenus["discovery"].Visibility == Visibility.Visible, style + " widening restores active group");
            }
            preferences.Theme = "dark"; BuildShell(); await Navigate("sources"); Selected("sources");
            await SaveNativePreview(Path.Combine(output, "haru-dark-sources.png"));
            await Navigate("settings");
            Button? syncButton=null;
            for(int attempt=0;attempt<40&&syncButton is null;attempt++) {await Task.Delay(50);syncButton=FeatureButton(body,"检查并同步");}
            Check(syncButton is not null,"settings exposes native WebDAV sync action");
            var blocked=DisableSyncInputs();
            Check(syncButton is not null&&!syncButton.IsEnabled,"sync blocks keyboard and pointer controls");
            var comparisonSnapshot=System.Text.Json.Nodes.JsonNode.Parse("{\"resumes\":[],\"favorites\":[],\"applications\":[],\"sources\":[],\"schedule\":[],\"history\":[],\"settings\":{},\"interviews\":[]}")!.AsObject();
            var comparisonLocal=new SyncLocal(comparisonSnapshot,SyncProtocol.Hash(comparisonSnapshot),DateTimeOffset.Parse("2026-01-01T03:04:05Z"));
            var comparisonPlan=new SyncPlan(SyncAction.Push,comparisonLocal,null,null,"fixture");
            var conflictFixture=CreateSyncComparisonDialog(comparisonPlan);syncConflictDialog=conflictFixture;
            Check(conflictFixture.DefaultButton==ContentDialogButton.Close&&conflictFixture.SecondaryButtonText=="","missing cloud dialog defaults to cancel and has no download");
            var conflictShown=conflictFixture.ShowAsync();await Task.Delay(200);
            Check(conflictFixture.IsEnabled&&FeatureButton(conflictFixture,"取消")?.IsEnabled==true,"comparison dialog remains interactive while underlying inputs are blocked");
            Check(FeatureButton(conflictFixture,"保留本机并上传")?.IsEnabled==true,"first upload requires selectable explicit confirmation");
            await SaveNativePreview(Path.Combine(output,"sync-first-upload-confirmation.png"),conflictFixture);
            conflictFixture.Hide();Check(await conflictShown==ContentDialogResult.None,"cancel returns no selected version");syncConflictDialog=null;
            var changedSnapshot=(System.Text.Json.Nodes.JsonObject)comparisonSnapshot.DeepClone();changedSnapshot["settings"]!["theme"]="dark";
            var baseline=new SyncBaseline("fixture",comparisonLocal.Hash,"old","2026-01-01T00:00:00Z",SyncProtocol.DataSize(comparisonSnapshot));
            comparisonPlan=new(SyncAction.Pull,comparisonLocal,SyncProtocol.Create(changedSnapshot,"other"),"new","fixture",baseline,DateTimeOffset.Parse("2026-01-02T03:04:05Z"));
            conflictFixture=CreateSyncComparisonDialog(comparisonPlan);conflictShown=conflictFixture.ShowAsync();await Task.Delay(200);
            Check(FeatureButton(conflictFixture,"保留本机并上传")?.IsEnabled==true&&FeatureButton(conflictFixture,"保留云端并下载")?.IsEnabled==true,"one-sided change offers either final version");
            var comparison=SyncComparison.Create(comparisonPlan);Check(comparison.Local.DeltaBytes==0&&comparison.Remote.DeltaBytes>0,"dialog uses last successful baseline size for both sides");
            Check(comparison.Local.ModifiedAt!=comparison.Remote.ModifiedAt,"dialog keeps actual local and remote file times");
            await SaveNativePreview(Path.Combine(output,"sync-version-comparison.png"),conflictFixture);
            conflictFixture.Hide();await conflictShown;
            foreach(var (control,enabled) in blocked) control.IsEnabled=enabled;
            Check(syncButton!.IsEnabled,"sync restores input controls");
            var connection=new SyncSettings {DirectoryUrl="http://127.0.0.1:19427/dav/",Username="fixture-user"};
            await connection.SaveAsync(api.DataDirectory,"fixture-password-local-only");
            var loaded=SyncSettings.Load(api.DataDirectory);
            Check(await loaded.PasswordAsync()=="fixture-password-local-only","unpackaged app roundtrips Windows LOCAL=user password protection");
            Check(!File.ReadAllText(Path.Combine(api.DataDirectory,"sync-local.json")).Contains("fixture-password-local-only"),"local password is protected on disk");
            var syncSnapshot=await api.Get<JsonElement>("sync/snapshot");
            Check(!syncSnapshot.GetRawText().Contains("fixture-user")&&!syncSnapshot.GetRawText().Contains("fixture-password-local-only"),"local sync connection is excluded from backend snapshot");
            activeSync=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting=FinishSyncBeforeClose();
            Check(!waiting.IsCompleted,"closing waits for an active synchronization");
            activeSync.TrySetResult();await waiting;activeSync=null;
            Check(waiting.IsCompletedSuccessfully,"closing continues only after synchronization finishes");
            await SaveNativePreview(Path.Combine(output,"haru-dark-sync-settings.png"));
            await File.WriteAllTextAsync(Path.Combine(output, "navigation-result.json"), JsonSerializer.Serialize(new { status = "passed", checks }, BackendClient.Json));
        }
        catch(Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(output, "navigation-result.json"), JsonSerializer.Serialize(new { status = "failed", checks, error = ex.ToString() }, BackendClient.Json));
            await SaveNativePreview(Path.Combine(output, "failed.png"));
        }
        finally { Title = "拾简 · 多级导航验证已结束（隔离数据）"; Close(); }
    }
}

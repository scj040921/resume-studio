using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private readonly List<string> iterationChecks=[];
    private static string NormalizeLines(string value)=>value.Replace("\r\n","\n").Replace('\r','\n');
    private async Task IterationOnlyTest()
    {
        Title="拾简 · 界面自动验证窗口"; shell.IsHitTestVisible=false;
        var output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"iteration"); Directory.CreateDirectory(output);
        try
        {
            await Navigate("jobs");
            shownCompanyJobs=new JobResult { Items=[new Job { Title="隔离 UI 测试数据",Company="测试公司",Url="https://example.com/jobs/test",Source="测试数据" }] };
            await VerifyIteration(output);
        }
        catch(Exception e) { await File.WriteAllTextAsync(Path.Combine(output,"iteration-result.json"),JsonSerializer.Serialize(new {status="failed",error=e.ToString()},BackendClient.Json)); await SaveNativePreview(Path.Combine(output,"failed-state.png")); }
        finally { shell.IsHitTestVisible=true; }
    }
    private static ComboBox? FindCombo(DependencyObject parent,string header)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is ComboBox combo&&combo.Header?.ToString()==header) return combo;
            var found=FindCombo(child,header); if(found is not null) return found;
        }
        return null;
    }
    private async Task VerifyIteration(string output)
    {
        if(visibleJobList is null||shownCompanyJobs is null) throw new Exception("Job list verification missing");
        var realJobs=shownCompanyJobs.Items;
        var fixtures=Enumerable.Range(0,1000).Select(i=>new Job { Id="virtual-"+i,Title="隔离虚拟列表验证 "+i,Company="测试公司",Url="https://example.com/jobs/"+i,Source="测试数据",Description=new string('字',200) }).ToList();
        visibleJobList.ItemsSource=fixtures; await Task.Delay(300);
        if(visibleJobList.ContainerFromIndex(999) is not null) throw new Exception("Long list was fully realized");
        visibleJobList.ScrollIntoView(fixtures[^1]); await Task.Delay(350);
        if(visibleJobList.ContainerFromIndex(999) is null) throw new Exception("Virtual list cannot scroll to last item");
        visibleJobList.ItemsSource=realJobs; iterationChecks.Add("1000-job virtualized list and scrolling");

        current=await api.Save("resumes",Demo(templates[0])); dirty=false; await OpenEditor();
        var originalWeb=preview; editorParts!.SelectedIndex=0; await Task.Delay(150);
        var field=FindTextField(body,"内容")??throw new Exception("Module editor field missing");
        field.Text="## 模块导航验证 | 2026\n- 最新内容必须保存并进入预览。";
        await Task.Delay(100); await SaveCurrent(); await WaitForPreview();
        var saved=(await api.Get<List<Resume>>("resumes")).First(r=>r.Id==current.Id);
        if(saved.Sections[0].Content!=NormalizeLines(field.Text)) throw new Exception("Module content was not saved");
        if(await preview!.CoreWebView2.ExecuteScriptAsync("document.querySelector('article').textContent.includes('最新内容必须保存')")!="true") throw new Exception("Preview does not contain latest module edit");
        SelectBasicInformation(); await Task.Delay(50); editorParts.SelectedIndex=0; await Task.Delay(150);
        if(NormalizeLines(FindTextField(body,"内容")?.Text??"")!=saved.Sections[0].Content||preview!=originalWeb) throw new Exception("Module selection lost state: "+JsonSerializer.Serialize(new {selectedEditorPart,selected=editorParts.SelectedIndex,ui=FindTextField(body,"内容")?.Text,saved=saved.Sections[0].Content,model=current.Sections[0].Content,webReused=preview==originalWeb}));
        iterationChecks.Add("module navigation, native edit, autosave and current preview");

        await Navigate("settings"); await Task.Delay(100);
        var selector=FindCombo(body,"界面版本")??throw new Exception("Settings selector missing"); selector.SelectedIndex=1;
        for(int i=0;i<100&&(modernNavigation is null||FindCombo(body,"界面版本")?.SelectedIndex!=1);i++) await Task.Delay(50);
        if(modernNavigation is null||AppPreferences.Load(api.DataDirectory).InterfaceStyle!="haru") throw new Exception("Native appearance selection not persisted");
        iterationChecks.Add("native interface selector and settings persistence");
        preferences.Theme="light"; await ApplyPreferences(); await Navigate("templates"); await SaveNativePreview(Path.Combine(output,"haru-templates-light.png"));
        if(!nativeGlass && SystemBackdrop is not DesktopAcrylicBackdrop || shell.Background is not SolidColorBrush baseBrush || baseBrush.Color.A==255 || headerFrame?.Background is not LinearGradientBrush glass || glass.GradientStops.Any(s=>s.Color.A==255))
            throw new Exception("Haru glass window/header material missing or covered by an opaque root");
        iterationChecks.Add("whole-window acrylic, translucent root and glass header");
        await VerifyGlassCaptionRegions();
        iterationChecks.Add("Haru top edge and page header are native drag regions; menu and caption buttons excluded");
        var originalSize=AppWindow.Size;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(800,700)); await Task.Delay(200);
        await VerifyGlassCaptionRegions();
        AppWindow.Resize(originalSize); await Task.Delay(200); await VerifyGlassCaptionRegions();
        iterationChecks.Add("Haru drag regions follow compact navigation and restored window size");
        await OpenEditor(); editorParts!.SelectedIndex=0; await SaveNativePreview(Path.Combine(output,"haru-editor-light.png"));
        preferences.Theme="dark"; await ApplyPreferences(); await SaveNativePreview(Path.Combine(output,"haru-editor-dark.png"));
        if(shell.RequestedTheme!=ElementTheme.Dark||preview!=originalWeb) throw new Exception("Dark theme or WebView reuse failed");
        iterationChecks.Add("Haru light and dark themes without WebView recreation");
        await VerifyGlassCaptionRegions();
        iterationChecks.Add("Haru drag regions survive theme switch and WebView editor navigation");
        await PrintPdf(Path.Combine(output,"module-edited-dark-theme.pdf"));
        var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(output);
        var image=await folder.CreateFileAsync("browser-preview-dark.png",Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using(var stream=await image.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite)) await preview!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,stream);

        preferences.LivePreview=false; await preferences.SaveAsync(api.DataDirectory); editorParts.SelectedIndex=0; await Task.Delay(150);
        var content=FindTextField(body,"内容")!; content.Text+="\n- 关闭实时预览后，PDF 仍必须同步。"; await Task.Delay(100); await SaveCurrent();
        AppWindow.Hide();
        try { await PrintPdf(Path.Combine(output,"manual-preview-latest.pdf")); }
        finally { AppWindow.Show(); }
        if((await preview!.CoreWebView2.ExecuteScriptAsync("document.querySelector('article').textContent.includes('PDF 仍必须同步')"))!="true") throw new Exception("Manual preview export stale");
        iterationChecks.Add("manual preview preserves autosave and fresh PDF export");
        iterationChecks.Add("PDF export completes while the window is hidden");
        await VerifyVisibleJobCapture(output);

        preferences.InterfaceStyle="classic"; preferences.LivePreview=true; await ApplyPreferences(); await Navigate("templates"); await SaveNativePreview(Path.Combine(output,"classic-templates.png"));
        if(haruStyle||ExtendsContentIntoTitleBar||AppPreferences.Load(api.DataDirectory).InterfaceStyle!="classic") throw new Exception("Classic restoration failed");
        iterationChecks.Add("classic interface restoration with shared data");
        await File.WriteAllTextAsync(Path.Combine(output,"iteration-result.json"),JsonSerializer.Serialize(new {status="passed",checks=iterationChecks,startup=timings,webInitializations,previewNavigations,previewPatches},BackendClient.Json));
    }
    private async Task VerifyGlassCaptionRegions()
    {
        await Task.Delay(150);
        var regions=InputNonClientPointerSource.GetForWindowId(AppWindow.Id).GetRegionRects(NonClientRegionKind.Caption);
        var scale=shell.XamlRoot.RasterizationScale;
        var header=headerFrame!.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point(headerFrame.ActualWidth/2,headerFrame.ActualHeight/2));
        var headerBounds=headerFrame.TransformToVisual(shell).TransformBounds(new Windows.Foundation.Rect(0,0,headerFrame.ActualWidth,headerFrame.ActualHeight));
        var pageBounds=pageHost.TransformToVisual(shell).TransformBounds(new Windows.Foundation.Rect(0,0,pageHost.ActualWidth,pageHost.ActualHeight));
        if(Math.Abs(headerBounds.Left-pageBounds.Left)>1||Math.Abs(headerBounds.Right-pageBounds.Right)>1)throw new Exception("Haru header does not align with the page content");
        if(headerBounds.Top*scale+1<AppWindow.TitleBar.Height)throw new Exception("Haru header overlaps window caption buttons");
        bool Contains(double x,double y)=>regions.Any(r=>x>=r.X&&x<r.X+r.Width&&y>=r.Y&&y<r.Y+r.Height);
        double right=shell.ActualWidth*scale-AppWindow.TitleBar.RightInset;
        if(!Contains((48*scale+right)/2,12*scale)||!Contains(header.X*scale,header.Y*scale)) throw new Exception("Native caption region missing from visible top edge or page header");
        if(Contains(24*scale,12*scale)||Contains(shell.ActualWidth*scale-16,12*scale)) throw new Exception($"Drag region intercepts navigation menu or caption controls (scale={scale}, width={shell.ActualWidth}, rightInset={AppWindow.TitleBar.RightInset}, regions={string.Join(";",regions.Select(r=>$"{r.X},{r.Y},{r.Width},{r.Height}"))})");
        if(Contains(shell.ActualWidth*scale/2,shell.ActualHeight*scale/2)) throw new Exception("Drag region intercepts page controls");
    }
    private async Task VerifyVisibleJobCapture(string output)
    {
        var browser=new WebView2 {Width=560,Height=350}; body.Children.Add(browser);
        Dictionary<string,string> fields;
        try
        {
            await browser.EnsureCoreWebView2Async(await webEnvironment!);
            var loaded=new TaskCompletionSource<bool>();
            browser.CoreWebView2!.NavigationCompleted+=(_,e)=>loaded.TrySetResult(e.IsSuccess);
            browser.NavigateToString("<div class='job-banner'><div class='name'><h1>芯片验证工程师</h1><span class='salary'>20–30K</span></div></div><div class='company-info'><a><img alt='图标'></a><h3><a>验证用示例公司</a></h3></div><span class='job-area'>深圳</span><div class='job-sec-text'>验证芯片功能<br>开发测试工具</div>");
            if(!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(12))) throw new Exception("Job browser fixture failed to load");
            using var parsed=JsonDocument.Parse(await browser.CoreWebView2.ExecuteScriptAsync(ReadVisibleJobScript));
            fields=JsonSerializer.Deserialize<Dictionary<string,string>>(parsed.RootElement.GetProperty("fields").GetRawText(),BackendClient.Json)!;
            if(fields["title"]!="芯片验证工程师"||fields["company"]!="验证用示例公司"||fields["salary"]!="20–30K"||!fields["description"].Contains("开发测试工具")) throw new Exception("Visible job browser extraction lost fields");
        }
        finally { body.Children.Remove(browser); browser.Close(); }
        const string original="https://www.zhipin.com/job_detail/browser-fixture.html?securityId=retain-for-opening&ka=share";
        var capture=await api.Post<CapturePreview>("capture-preview",new {text=original,visibleFields=fields});
        if(capture.Job.Url!=original||capture.Job.CanonicalUrl.Contains("securityId")) throw new Exception("Original and dedup job URLs were mixed");
        var favorite=await api.Save("favorites",capture.Job); await Navigate("favorites");
        // Native virtualized containers are not realized while a verification window is minimized.
        if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State==Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) presenter.Restore();
        Activate();
        var items=(visibleJobList!.ItemsSource as IEnumerable<Job>)!.ToList();
        var item=items.FirstOrDefault(j=>j.Id==favorite.Id)??throw new Exception($"Favorite filter omitted saved item: saved={favorite.Id}, shown={string.Join(',',items.Select(j=>j.Id))}, persisted={string.Join(',',(await api.Get<List<Job>>("favorites")).Select(j=>j.Id))}");
        visibleJobList.ScrollIntoView(item); visibleJobList.UpdateLayout(); await Task.Delay(400);
        bool FoundLink(DependencyObject parent)
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) {
                var child=VisualTreeHelper.GetChild(parent,i);
                if(child is HyperlinkButton link && link.Content is TextBlock text && text.Text==original) return true;
                if(FoundLink(child)) return true;
            }
            return false;
        }
        if(!FoundLink(body)) throw new Exception($"Saved job link is not displayed in the native favorite card: root={shell.ActualWidth}x{shell.ActualHeight}, container={visibleJobList.ContainerFromIndex(0)?.GetType().Name??"missing"}");
        await SaveNativePreview(Path.Combine(output,"favorites-with-visible-link.png"));
        iterationChecks.Add("visible browser job fields, original access URL persistence and native favorite link display");
    }
}

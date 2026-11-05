using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task FavoritesEditorTest()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"favorites-editor");Directory.CreateDirectory(output);
        var checks=new List<string>();
        try {
            await VerifyEditorExit(output,checks);
            await VerifyFavoritePaging(output,checks);
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new {status="passed",checks},BackendClient.Json));
        }catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new {status="failed",checks,error=ex.ToString()},BackendClient.Json));}
        finally {await SaveCurrent();closing=true;api.Dispose();Close();}
    }
    private async Task VerifyFavoritePaging(string output,List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        async Task Click(string label){var button=FeatureButton(body,label)??throw new Exception("Missing paging action: "+label);((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(80);}
        for(int i=0;i<53;i++)await api.Save("favorites",new Job {Id="paging-"+i,Title="分页验证职位 "+i,Company="分页公司",Source=new[] {"BOSS直聘","牛客","官网"}[i%3],Url="https://example.com/jobs/paging-"+i,Notes="旧备注 "+i});
        foreach(string style in new[] {"classic","haru"}) {
            preferences.InterfaceStyle=style;preferences.Theme="light";BuildShell();favoritePageSize=20;ResetFavoriteView();await Navigate("favorites");await Task.Delay(160);
            var pages=InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="favorite-pages")!;
            var size=InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="favorite-page-size")!;
            Check(visibleJobList!.Items.Count==20&&pages.Items.Count==3&&!FeatureButton(body,"上一页")!.IsEnabled,style+": initial page exposes count and three reachable pages");
            var seen=new HashSet<string>();
            for(int page=0;page<3;page++) {
                foreach(var job in visibleJobList!.Items.OfType<Job>())Check(seen.Add(job.Id),style+": page contains no duplicates "+job.Id);
                if(page<2)await Click("下一页");
            }
            Check(seen.Count==53&&visibleJobList.Items.Count==13&&!FeatureButton(body,"下一页")!.IsEnabled,style+": all old favorites are accessible exactly once through next-page navigation");
            await Click("上一页");Check(favoritePage==2&&visibleJobList.Items.Count==20,style+": previous page restores the preceding favorites");
            pages.SelectedIndex=2;await Task.Delay(80);await Navigate("applications");await Navigate("favorites");await Task.Delay(160);
            Check(favoritePage==3&&visibleJobList!.Items.Count==13,style+": returning to favorites preserves the current page");
            size=InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="favorite-page-size")!;size.SelectedIndex=0;await Task.Delay(80);
            pages=InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="favorite-pages")!;
            Check(favoritePage==1&&favoritePageSize==10&&visibleJobList.Items.Count==10&&pages.Items.Count==6,style+": page-size changes recalculate pages and return to page one");
            pages.SelectedIndex=5;await Task.Delay(80);Check(visibleJobList.Items.Count==3,style+": direct page selection reaches the oldest favorites");
            var removed=visibleJobList.Items.OfType<Job>().First();await RemoveFavorite(removed);await Task.Delay(120);
            Check(favoritePage==6&&visibleJobList!.Items.Count==2,style+": removal refreshes the current page without hiding remaining old records");
            await api.Post<CollectionResult<Job>>("favorites/collect",removed);await Navigate("favorites");await Task.Delay(120);
            pages=InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="favorite-pages")!;
            var source=InteractionElement<ComboBox>(body,c=>c.Items.OfType<string>().Contains("全部平台"))!;source.SelectedItem="牛客";await Task.Delay(80);
            Check(favoritePage==1&&visibleJobList!.Items.Count==10&&visibleJobList.Items.OfType<Job>().All(j=>j.Source=="牛客"),style+": platform filtering resets pagination and preserves the selected platform");
            var query=InteractionElement<TextBox>(body,t=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(t)=="搜索职位收藏")!;query.Text="不存在的分页测试项";await Task.Delay(80);
            Check(visibleJobList.Items.Count==0&&!FeatureButton(body,"下一页")!.IsEnabled,style+": empty search results disable paging");
            Check(visibleJobList.Visibility==Microsoft.UI.Xaml.Visibility.Collapsed&&InteractionElement<TextBlock>(body,t=>t.Text=="没有找到匹配的职位") is not null,style+": empty results explain recovery instead of leaving a blank list");
            await Click("清空筛选");
            Check(favoriteSource=="全部平台"&&favoritePage==1&&pages.Items.Count==6&&visibleJobList.Items.Count==10,style+": clearing filters restores all old favorites and valid pagination");
            await SaveNativePreview(Path.Combine(output,"favorites-paged-"+style+".png"));
        }
        var newest=new Job {Id="paging-new",Title="新增收藏页验证",Company="新公司",Source="官网",Url="https://example.com/jobs/paging-new"};
        favoritePage=6;favoriteSource="BOSS直聘";favoriteQuery="分页验证";await CollectFavorite(newest);await Navigate("favorites");await Task.Delay(140);
        Check(favoriteSource=="全部平台"&&favoriteQuery==""&&favoritePage==1&&visibleJobList!.Items.OfType<Job>().Any(j=>j.Id==newest.Id),"new collection opens at page one and cannot hide prior platforms behind an automatic filter");
        Check((await api.Get<List<Job>>("favorites")).Count==54,"adding and paging favorites preserves every previously stored record");
        var selector=InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="favorite-pages")!;selector.SelectedIndex=5;await Task.Delay(80);
        foreach(var job in visibleJobList!.Items.OfType<Job>().ToList())await RemoveFavorite(job);
        await Task.Delay(120);Check(favoritePage==5&&visibleJobList!.Items.Count==10,"deleting the final page clamps to the nearest remaining page");
        await SaveNativePreview(Path.Combine(output,"favorites-last-page.png"));
    }
    private async Task VerifyEditorExit(string output,List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        async Task Stage(string stage)=>await File.AppendAllTextAsync(Path.Combine(output,"stages.log"),DateTimeOffset.Now+" "+stage+"\n");
        current=await api.Save("resumes",Blank(templates.First(t=>t.Origin=="github")));dirty=false;
        foreach(var mode in new[] {("classic","light"),("haru","light"),("haru","dark")}) {
            preferences.InterfaceStyle=mode.Item1;preferences.Theme=mode.Item2;BuildShell();
            for(int n=0;n<4;n++) {
                await Stage(mode+" editor opening "+n);await OpenEditor();await WaitForPreview();await Task.Delay(160);
                string marker="切页保存验证-"+mode.Item1+"-"+mode.Item2+"-"+n;
                FindTextField(body,"姓名")!.Text=marker;
                await Task.Delay(60);
                if(n>0) {
                    if(!layoutExpanded)ToggleLayoutSettings();await Task.Delay(80);
                    FindCombo(editorLayoutPanel!,"简历字体")!.SelectedIndex=n%3;
                    var template=FindCombo(editorLayoutPanel!,"模板");
                    if(template is not null&&n>=2){
                        Check(template.Focus(Microsoft.UI.Xaml.FocusState.Programmatic),"template selector receives native focus "+mode+" "+n);
                        template.IsDropDownOpen=true;await Task.Delay(100);
                        int index=(template.SelectedIndex+1)%templates.Count;
                        var item=template.ContainerFromIndex(index) as ComboBoxItem??throw new Exception("Native template dropdown item missing");
                        item.IsSelected=true;template.IsDropDownOpen=false;await Task.Delay(100);
                        Check(template.SelectedIndex==index,"native dropdown switches the selected template "+mode+" "+n);
                    }
                    var size=InteractionElement<Slider>(editorLayoutPanel!,s=>s.Header?.ToString()=="正文字号");if(size is not null)size.Value=11+n;
                    await Task.Delay(40);
                    if(template is not null&&n>=2){template.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);await SaveCurrent();await WaitForPreview();}
                    Check(editorLayoutPanel!.Visibility==Microsoft.UI.Xaml.Visibility.Visible,"layout settings remain expanded when leaving editor "+mode+" "+n);
                }
                await Stage(mode+" pending render and navigation "+n);var render=RenderPreview();
                if(haruStyle)modernNavigation!.SelectedItem=modernNavigationItems["templates"];
                else ((IInvokeProvider)new ButtonAutomationPeer(navigation.Single(b=>(string)b.Tag=="templates")).GetPattern(PatternInterface.Invoke)).Invoke();
                for(int i=0;i<100&&currentPage!="templates";i++)await Task.Delay(30);
                await render;await Task.Delay(300);
                Check(currentPage=="templates"&&FeatureButton(body,"使用模板") is not null,"edited resume reaches template center with overlapping preview "+mode+" "+n);
                Check((await api.Get<List<Resume>>("resumes")).Single(r=>r.Id==current.Id).Profile.Name==marker,"navigation flushes the edited content before leaving "+mode+" "+n);
                await Stage(mode+" template resize "+n);
                double scale=shell.XamlRoot.RasterizationScale;
                foreach(int width in new[] {760,1400}){AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(width*scale),(int)(850*scale)));await Task.Delay(200);}
            }
        }
        await Stage("editor/template repetition completed");await SaveNativePreview(Path.Combine(output,"templates-after-edit.png"));
    }
}

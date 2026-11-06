using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task VerifyWorkflow(string output,List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        var job=new Job {Id="workflow-job",Title="工作流嵌入式岗位",Company="联动测试公司",Source="官网",Url="https://example.com/jobs/embedded?utm_source=search",Notes="手写备注保留"};
        var first=await api.Post<CollectionResult<Job>>("favorites/collect",job);
        var alias=CloneDocument(job);alias.Id="external-workflow";alias.Url="https://example.com/jobs/embedded?utm_source=share";alias.Notes="";
        var repeated=await api.Post<CollectionResult<Job>>("favorites/collect",alias);
        Check(first.Created&&!repeated.Created&&repeated.Record.Notes==job.Notes,"canonical favorite collection preserves notes across discovery and shared links");
        await Navigate("favorites");await Task.Delay(100);var favoriteOrder=InteractionElement<ComboBox>(body,b=>b.Tag as string=="favorite-sort")!;favoriteOrder.SelectedIndex=1;
        favoriteQuery="联动测试公司";favoriteSource="官网";await Navigate("favorites");await Navigate("applications");await Navigate("favorites");
        await Task.Delay(100);Check(InteractionElement<ComboBox>(body,b=>b.Tag as string=="favorite-sort")?.SelectedIndex==1,"favorite sort selection survives leaving and reopening the list");
        Check(InteractionElement<TextBox>(body,b=>Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b)=="搜索职位收藏")?.Text==favoriteQuery&&visibleJobList!.Items.Count==1,"favorite filters survive page switching and refresh");
        await RemoveFavorite(first.Record);Check(!(await api.Get<List<Job>>("favorites")).Any(j=>j.Id==first.Record.Id)&&feedback.ActionButton is Button,"removing a favorite offers undo");
        ((IInvokeProvider)new ButtonAutomationPeer((Button)feedback.ActionButton!).GetPattern(PatternInterface.Invoke)).Invoke();
        for(int attempt=0;attempt<50;attempt++){if((await api.Get<List<Job>>("favorites")).Any(j=>j.Id==first.Record.Id)&&feedback.ActionButton.IsEnabled)break;await Task.Delay(50);}
        Check((await api.Get<List<Job>>("favorites")).Single(j=>j.Id==first.Record.Id).Notes==job.Notes,"favorite undo retains the latest saved notes");
        var lookup=await api.Post<CollectionResult<ApplicationRecord>>("applications/find",job);Check(lookup.Record is null,"record editor lookup does not record a fictitious browser opening");
        var record=(await api.Post<CollectionResult<ApplicationRecord>>("applications/open",job)).Record;
        record.Status="面试中";record.Notes="最新投递备注";record.DueAt=DateTimeOffset.Now.AddDays(1).ToString("O");record.EventKind="面试";record=await api.Save("applications",record);
        var second=(await api.Post<CollectionResult<ApplicationRecord>>("applications/open",alias));
        Check(!second.Created&&second.Record.Id==record.Id&&second.Record.Status=="面试中"&&second.Record.ResumeTitle=="","reopening a matched job preserves progress and does not attach the open resume");
        applicationQuery="联动测试公司";applicationStage="面试中";applicationPendingOnly=true;await Navigate("applications");await Navigate("schedule");await Navigate("applications");
        await Task.Delay(180);
        Check(InteractionElement<TextBox>(body,b=>b.PlaceholderText=="搜索公司、职位或备注")?.Text==applicationQuery&&InteractionElement<ComboBox>(body,b=>b.SelectedItem?.ToString()=="面试中") is not null,"application search and stage filters survive navigation");
        var independent=await api.Save("schedule",new ScheduleItem {Title="关联二面安排",ApplicationId=record.Id,StartAt=DateTimeOffset.Now.AddDays(2).ToString("O"),Kind="面试",Completed=true});
        await OpenApplicationSchedule(record);await Task.Delay(180);Check(scheduleApplicationId==record.Id&&scheduleView==3&&InteractionElement<TextBlock>(body,t=>t.Text=="关联二面安排") is not null,"application opens its linked calendar including completed rounds");
        await ViewApplication(record.Id);Check(focusedApplicationId==record.Id&&applicationQuery==""&&applicationStage=="全部进度"&&!applicationPendingOnly,"calendar locates the exact application and clears unrelated list filters");
        var stale=(await ReadAgenda()).Single(e=>e.Application?.Id==record.Id);record.Notes="在别处修改的新备注";record=await api.Save("applications",record);await SetAgendaCompleted(stale,true);
        Check((await api.Get<List<ApplicationRecord>>("applications")).Single(r=>r.Id==record.Id).Notes==record.Notes,"calendar completion reads latest application and retains newer notes");
        record.DueAt=DateTimeOffset.Now.AddDays(4).ToString("O");await api.Save("applications",record);
        try {await SetAgendaCompleted(stale,true);throw new Exception("Stale arrangement was completed");}catch(InvalidOperationException){checks.Add("rescheduled arrangements reject a stale completion action");}
        var editing=EditApplication(new ApplicationRecord {Title="",Company="表单测试",Url="invalid",Notes="失败后保留的输入"});var form=await FeatureDialog("更新投递记录");await FeatureClick(form,"保存记录");await Task.Delay(160);
        Check(!editing.IsCompleted&&FindTextField(form,"备注／地点／会议链接")?.Text=="失败后保留的输入"&&InteractionElement<InfoBar>(form,b=>b.IsOpen) is not null,"invalid application stays open with all entered content and inline errors");
        FindTextField(form,"职位名称")!.Text="原生重试岗位";FindTextField(form,"招聘页面链接（HTTPS）")!.Text="https://example.com/jobs/retry";
        InteractionElement<CheckBox>(form,b=>b.Content?.ToString()=="有下一次笔试／面试／跟进安排")!.IsChecked=true;
        InteractionElement<CheckBox>(form,b=>b.Content?.ToString()=="这次安排已处理，不再提醒")!.IsChecked=true;
        await FeatureClick(form,"保存记录");await editing.WaitAsync(TimeSpan.FromSeconds(15));
        Check((await api.Get<List<ApplicationRecord>>("applications")).Single(r=>r.Title=="原生重试岗位").ReminderDone,"corrected application saves once and respects an explicitly completed new arrangement");
        var fields=Stack();var input=new TextBox {Header="保留字段",Text="后端失败也保留"};fields.Children.Add(input);bool fail=true;
        var retry=SaveForm("后端失败验证",fields,"保存",async ()=>{if(fail)await api.Save("schedule",new ScheduleItem {Title="测试",StartAt="bad-date"});});var shown=retry.ShowAsync();await FeatureDialog("后端失败验证");await FeatureClick(retry,"保存");await Task.Delay(180);
        Check(input.Text=="后端失败也保留"&&InteractionElement<InfoBar>(retry,b=>b.IsOpen)?.Message.Contains("日期和时间")==true&&retry.IsPrimaryButtonEnabled,"backend rejection is readable and keeps the same dialog ready for retry");
        fail=false;await FeatureClick(retry,"保存");Check(await shown==ContentDialogResult.Primary,"a successful retry closes the original form normally");
        var enriched=CloneDocument(first.Record);enriched.Title="补全后的职位";var editFavorite=EditFavorite(enriched,false);var favoriteDialog=await FeatureDialog("编辑收藏");
        Check(FindTextField(favoriteDialog,"职位名称")?.Text==enriched.Title,"opening a favorite confirmation preserves newly parsed enrichment");
        FindTextField(favoriteDialog,"职位名称")!.Text="";await FeatureClick(favoriteDialog,"保存收藏");await Task.Delay(120);
        Check(!editFavorite.IsCompleted&&FindTextField(favoriteDialog,"备注")?.Text==job.Notes,"invalid favorite remains open without losing handwritten notes");
        FindTextField(favoriteDialog,"职位名称")!.Text=enriched.Title;await FeatureClick(favoriteDialog,"保存收藏");await editFavorite.WaitAsync(TimeSpan.FromSeconds(15));
        Check((await api.Get<List<Job>>("favorites")).Single(j=>j.Id==first.Record.Id).Title==enriched.Title,"favorite retry persists enrichment under its existing identity");
        var editSchedule=EditSchedule(new ScheduleItem {StartAt=DateTimeOffset.Now.AddDays(3).ToString("O")});var scheduleDialog=await FeatureDialog("编辑日程");await FeatureClick(scheduleDialog,"保存日程");await Task.Delay(120);
        Check(!editSchedule.IsCompleted&&InteractionElement<InfoBar>(scheduleDialog,b=>b.IsOpen) is not null,"empty schedule uses the same inline validation and retry behavior");
        FindTextField(scheduleDialog,"事项名称")!.Text="统一表单日程";await FeatureClick(scheduleDialog,"保存日程");await editSchedule.WaitAsync(TimeSpan.FromSeconds(15));
        Check((await api.Get<List<ScheduleItem>>("schedule")).Any(i=>i.Title=="统一表单日程")&&currentPage=="favorites","schedule saves and returns to the page where it was opened");
        var sourceDraft=new CompanySource {Name="",Url="invalid"};var editSource=EditCompanySource(sourceDraft);var sourceDialog=await FeatureDialog("配置公司招聘官网");await FeatureClick(sourceDialog,"保存来源");await Task.Delay(120);
        Check(!editSource.IsCompleted&&sourceDraft.Name==""&&InteractionElement<InfoBar>(sourceDialog,b=>b.IsOpen) is not null,"source validation keeps the form open and its original model unchanged");
        FindTextField(sourceDialog,"公司名称")!.Text="工作流测试官网";FindTextField(sourceDialog,"招聘官网链接（HTTPS）")!.Text="https://example.com/jobs";await FeatureClick(sourceDialog,"保存来源");await editSource.WaitAsync(TimeSpan.FromSeconds(15));
        Check((await api.Get<List<CompanySource>>("job-sources")).Any(s=>s.Name=="工作流测试官网"),"corrected company source saves through the shared form");
        var early=Navigate("sources");var late=Navigate("favorites");await Task.WhenAll(early,late);await Task.WhenAll(Navigate("favorites"),Navigate("favorites"));
        Check(currentPage=="favorites"&&body.RowDefinitions.Count==3&&body.Children.Count==4,"quick and repeated navigation retains one controls/list/empty-state/footer set");
        int generation=navigationRevision;await OpenEditor();Check(!PageIsCurrent("favorites",generation),"opening the editor invalidates in-flight list render generations");
        await SaveNativePreview(Path.Combine(output,"workflow-editor.png"));
        var homeItem=await api.Save("schedule",new ScheduleItem {Title="首页快捷完成验证",StartAt=DateTimeOffset.Now.AddHours(1).ToString("O")});
        var homeApplication=await api.Save("applications",new ApplicationRecord {Title="首页投递面试验证",Company="测试公司",Url="https://example.com/jobs/home-agenda",Status="面试中",DueAt=DateTimeOffset.Now.AddHours(2).ToString("O"),EventKind="面试"});
        await Navigate("home");await Task.Delay(200);
        Check(InteractionElement<Button>(body,b=>b.Tag as string=="home-complete:application:"+homeApplication.Id) is not null,"home shows appointments projected directly from applications");
        var homeComplete=InteractionElement<Button>(body,b=>b.Tag as string=="home-complete:schedule:"+homeItem.Id)!;
        ((IInvokeProvider)new ButtonAutomationPeer(homeComplete).GetPattern(PatternInterface.Invoke)).Invoke();
        for(int i=0;i<60;i++){await Task.Delay(100);if(currentPage=="home"&&(await api.Get<List<ScheduleItem>>("schedule")).Single(s=>s.Id==homeItem.Id).Completed&&feedback.ActionButton is Button)break;}
        Check(currentPage=="home"&&(await api.Get<List<ScheduleItem>>("schedule")).Single(s=>s.Id==homeItem.Id).Completed,"home completes an appointment without leaving the dashboard");
        ((IInvokeProvider)new ButtonAutomationPeer((Button)feedback.ActionButton!).GetPattern(PatternInterface.Invoke)).Invoke();
        for(int i=0;i<60;i++){await Task.Delay(100);if(!(await api.Get<List<ScheduleItem>>("schedule")).Single(s=>s.Id==homeItem.Id).Completed)break;}
        Check(!(await api.Get<List<ScheduleItem>>("schedule")).Single(s=>s.Id==homeItem.Id).Completed,"home completion can be undone");
        await Task.Delay(200);await SaveNativePreview(Path.Combine(output,"home-agenda-light.png"));
        var quickApplication=InteractionElement<Button>(body,b=>b.Content as string=="记录一次投递")!;
        ((IInvokeProvider)new ButtonAutomationPeer(quickApplication).GetPattern(PatternInterface.Invoke)).Invoke();
        var quickDialog=await FeatureDialog("更新投递记录");
        Check(currentPage=="home"&&FindTextField(quickDialog,"职位名称")!.Text=="","home opens a blank application form directly without a list detour");
        await FeatureClick(quickDialog,"取消");await Task.Delay(150);
        Check(currentPage=="home","canceling quick application returns to home");
    }
}

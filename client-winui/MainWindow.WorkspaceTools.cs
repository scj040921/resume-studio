using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;
namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task ShowWorkspaceTools(string kind="")
    {
        await SaveCurrent();string query="";
        for(;;){
            var data=await api.Post<JsonElement>("workspace",new {kind,query});var records=data.GetProperty("rows").EnumerateArray().Select(r=>r.Clone()).ToList();var panel=Stack(10);
            var search=new TextBox {Header="跨模块搜索（标题、公司、备注、标签、题目）",Text=query,MaxLength=200};panel.Children.Add(search);
            panel.Children.Add(Text($"共 {data.GetProperty("total")} 项，显示前 200 项；可缩小搜索范围。",12));
            var selections=new List<(CheckBox Select,JsonElement Record)>();var list=Stack(5);
            foreach(var record in records){var select=new CheckBox {Content=record.GetProperty("kind").GetString()+" · "+record.GetProperty("title").ToString()+" · "+record.GetProperty("tags").GetString()+" "+record.GetProperty("alert").GetString()};list.Children.Add(select);selections.Add((select,record));}
            panel.Children.Add(new ScrollViewer {Content=list,MaxHeight=270});
            var tags=new TextBox {Header="批量设置标签（逗号分隔，留空保留）",MaxLength=500};
            var status=new ComboBox {Header="批量更新投递状态",ItemsSource=new[]{"保持原状态"}.Concat(ApplicationStages),SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
            var deadline=new CalendarDatePicker {Header="截止日期（可选）",HorizontalAlignment=HorizontalAlignment.Stretch};var follow=new CheckBox {Content="标记今天已跟进"};
            var clearTags=new CheckBox {Content="清空选中记录标签"};var clearDeadline=new CheckBox {Content="清空截止日期"};
            if(kind!=""){panel.Children.Add(tags);panel.Children.Add(clearTags);if(kind=="applications")panel.Children.Add(status);panel.Children.Add(deadline);panel.Children.Add(clearDeadline);panel.Children.Add(follow);}
            var action=await new ContentDialog {XamlRoot=shell.XamlRoot,Title=kind==""?"全局搜索":"批量管理 · "+kind,Content=new ScrollViewer {Content=panel,MaxHeight=540},PrimaryButtonText="搜索",SecondaryButtonText=kind==""?"打开选中记录":"应用到选中记录",CloseButtonText="关闭"}.ShowAsync();
            if(action==ContentDialogResult.None)return;if(action==ContentDialogResult.Primary){query=search.Text;continue;}
            var selected=selections.Where(s=>s.Select.IsChecked==true).Select(s=>s.Record).ToList();if(selected.Count==0){Success("请选择记录","在结果中勾选后再操作。");continue;}
            if(kind==""){
                var record=selected[0];string target=record.GetProperty("kind").GetString()!,id=record.GetProperty("id").GetString()!;
                if(target=="resumes"){await SaveCurrent();current=(await api.Get<List<Resume>>(target)).First(r=>r.Id==id);dirty=false;await OpenEditor();}
                else if(target=="favorites")await EditFavorite((await api.Get<List<Job>>(target)).First(r=>r.Id==id),false);
                else if(target=="applications")await EditApplication((await api.Get<List<ApplicationRecord>>(target)).First(r=>r.Id==id));
                else if(target=="schedule")await EditSchedule((await api.Get<List<ScheduleItem>>(target)).First(r=>r.Id==id));
                else if(target=="interviews"){
                    var deck=(await api.Get<List<InterviewDeck>>(target)).FirstOrDefault(d=>d.Id==id)??throw new IOException("题库已删除，请重新搜索。");
                    if(deck.StudyType=="exam"){examDeckId=id;await Navigate("exams");}
                    else{interviewDeckId=id;await Navigate("interviews");}
                }
                else await Navigate(target);return;
            }
            if(!await Confirm("确认批量修改？",$"将修改 {selected.Count} 条记录；原资料会自动备份。"))continue;
            string at=deadline.Date is null?"":new DateTimeOffset(deadline.Date.Value.LocalDateTime.Date.AddDays(1).AddTicks(-1),TimeZoneInfo.Local.GetUtcOffset(deadline.Date.Value.LocalDateTime)).ToString("O");
            await api.Post<JsonElement>("workspace",new {operation="batch",kind,query,ids=selected.Select(r=>r.GetProperty("id").GetString()).ToList(),expectedHash=data.GetProperty("hash").GetString(),tags=tags.Text,status=status.SelectedIndex>0?status.SelectedItem?.ToString():"",deadlineAt=at,clearTags=clearTags.IsChecked==true,clearDeadline=clearDeadline.IsChecked==true,followUp=follow.IsChecked==true});
            Feedback("批量修改已保存","原资料已备份。可在备份管理恢复。");query=search.Text;
        }
    }
}

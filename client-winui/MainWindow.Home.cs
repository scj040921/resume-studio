using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task ShowHome()
    {
        int generation=navigationRevision;
        Heading("求职首页","把简历、职位、投递和准备事项放到同一条行动线上。");
        var resumeTask=api.Get<List<Resume>>("resumes");var favoriteTask=api.Get<List<Job>>("favorites");
        var applicationTask=api.Get<List<ApplicationRecord>>("applications");var scheduleTask=api.Get<List<ScheduleItem>>("schedule");var deckTask=api.Get<List<InterviewDeck>>("interviews");
        await Task.WhenAll(resumeTask,favoriteTask,applicationTask,scheduleTask,deckTask);
        if(!PageIsCurrent("home",generation))return;
        var resumes=resumeTask.Result;var favorites=favoriteTask.Result;var applications=applicationTask.Result;var schedule=scheduleTask.Result;var decks=deckTask.Result;
        var panel=Stack(16);var actions=Row();
        actions.Children.Add(Button("继续编辑简历",()=>Navigate("resumes"),true));
        actions.Children.Add(Button("查看职位收藏",()=>Navigate("favorites")));
        actions.Children.Add(Button("记录一次投递",()=>Navigate("applications")));
        actions.Children.Add(Button("新增求职日程",()=>EditSchedule(new ScheduleItem {Title="",StartAt=DateTimeOffset.Now.AddDays(1).ToString("O")})));
        panel.Children.Add(Card(actions,new Thickness(16)));

        var metrics= new Grid {ColumnSpacing=12};
        foreach(var _ in Enumerable.Range(0,4))metrics.ColumnDefinitions.Add(new ColumnDefinition());
        var open=applications.Count(a=>!new[]{"已录用","已结束"}.Contains(a.Status));
        var now=DateTimeOffset.Now;var due=schedule.Count(s=>!s.Completed&&s.ReminderEnabled&&DateTimeOffset.TryParse(s.StartAt,out var at)&&at.AddMinutes(-s.ReminderMinutes)<=now);
        var questionList=decks.Where(d=>d.StudyType!="exam").SelectMany(d=>d.Questions).ToList();
        var reviewDue=questionList.Count(q=>q.Status!="remembered"||!DateTimeOffset.TryParse(q.NextReviewAt,out var next)||next<=DateTimeOffset.Now);
        var values=new[]{("简历",resumes.Count,"resumes"),("收藏职位",favorites.Count,"favorites"),("进行中投递",open,"applications"),("待处理日程",due,"schedule")};
        for(int i=0;i<values.Length;i++){var(item,count,key)=values[i];var tile=Stack(5);tile.Children.Add(Text(item,12));tile.Children.Add(Text(count.ToString(),26,true));tile.Children.Add(Button("打开 →",()=>Navigate(key)));var card=Card(tile,new Thickness(15));Grid.SetColumn(card,i);metrics.Children.Add(card);}
        panel.Children.Add(metrics);

        var columns=new Grid {ColumnSpacing=14};columns.ColumnDefinitions.Add(new ColumnDefinition());columns.ColumnDefinitions.Add(new ColumnDefinition());
        var next=Stack(10);next.Children.Add(Text("接下来的安排",18,true));
        var upcoming=schedule.Where(s=>!s.Completed&&DateTimeOffset.TryParse(s.StartAt,out _)).OrderBy(s=>DateTimeOffset.Parse(s.StartAt)).Take(5).ToList();
        if(upcoming.Count==0)next.Children.Add(Text("暂无待办。可以从职位投递记录添加笔试、面试或跟进安排。",13));
        foreach(var item in upcoming)next.Children.Add(Button(item.Title+"  ·  "+item.Kind+"  ·  "+LocalDate(item.StartAt),()=>EditSchedule(item)));
        var nextCard=Card(next);Grid.SetColumn(nextCard,0);columns.Children.Add(nextCard);

        var follow=Stack(10);follow.Children.Add(Text("需要跟进",18,true));
        var followUps=applications.Where(a=>!new[]{"已录用","已结束"}.Contains(a.Status))
            .Select(a=>(Record:a,Alert:WorkspaceAlerts.For(a.DeadlineAt,string.IsNullOrWhiteSpace(a.LastFollowedAt)?a.CreatedAt:a.LastFollowedAt,a.Status)))
            .Where(x=>!string.IsNullOrWhiteSpace(x.Alert)).OrderBy(x=>x.Record.DueAt).Take(5).ToList();
        if(followUps.Count==0)follow.Children.Add(Text("当前没有逾期或长期未跟进的投递。",13));
        foreach(var item in followUps)follow.Children.Add(Button(item.Record.Company+" · "+item.Record.Title+"  —  "+item.Alert,()=>ViewApplication(item.Record.Id)));
        if(reviewDue>0)follow.Children.Add(Button($"面试题复习提醒 · {reviewDue} 道待复习",()=>Navigate("interviews")));
        var examQuestions=decks.Where(d=>d.StudyType=="exam").SelectMany(d=>d.Questions).ToList();
        int examDue=examQuestions.Count(q=>q.Status!="remembered");
        if(examDue>0)follow.Children.Add(Button($"笔试复盘 · {examDue} 道待完成",()=>Navigate("exams")));
        var followCard=Card(follow);Grid.SetColumn(followCard,1);columns.Children.Add(followCard);panel.Children.Add(columns);
        panel.Children.Add(Text("最近修改的简历",18,true));
        foreach(var resume in resumes.OrderByDescending(r=>r.UpdatedAt).Take(3))panel.Children.Add(Button(resume.Title+"  ·  "+(resume.Profile.Role is {Length:>0} role?role:"待填写目标岗位")+"  ·  "+LocalDate(resume.UpdatedAt),async ()=>{current=resume;dirty=false;await OpenEditor();}));
        if(resumes.Count==0)panel.Children.Add(Card(Text("导入 PDF 简历，或从模板中心新建第一份简历。",14)));
        FillScroll(panel);
    }
}

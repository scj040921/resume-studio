using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task ShowHome()
    {
        int generation=navigationRevision;
        Heading("求职工作台","继续准备简历，查看安排与投递进度。");
        var resumeTask=api.Get<List<Resume>>("resumes");var favoriteTask=api.Get<List<Job>>("favorites");
        var applicationTask=api.Get<List<ApplicationRecord>>("applications");var scheduleTask=api.Get<List<ScheduleItem>>("schedule");var deckTask=api.Get<List<InterviewDeck>>("interviews");
        await Task.WhenAll(resumeTask,favoriteTask,applicationTask,scheduleTask,deckTask);
        if(!PageIsCurrent("home",generation))return;
        var resumes=resumeTask.Result;var favorites=favoriteTask.Result;var applications=applicationTask.Result;var schedule=scheduleTask.Result;var decks=deckTask.Result;
        var panel=Stack(18);var quick=Stack(12);
        var latest=resumes.OrderByDescending(r=>r.UpdatedAt).FirstOrDefault();
        quick.Children.Add(Text(latest is null?"从一份简历开始":"继续完善这份简历",20,true));
        var resumeHint=Text(latest is null?"选择模板或导入 PDF，建立你的第一份简历。":latest.Title+"  ·  "+(latest.Profile.Role is {Length:>0} latestRole?latestRole:"待填写目标岗位"),13);
        resumeHint.Opacity=.75;quick.Children.Add(resumeHint);
        var actions=new Grid {ColumnSpacing=8,RowSpacing=8,HorizontalAlignment=HorizontalAlignment.Left};
        var shortcuts=new[]{Button(latest is null?"新建简历":"继续编辑简历",()=>latest is null?Navigate("templates"):OpenRecentResume(latest),true),Button("查看职位收藏",()=>Navigate("favorites")),Button("记录一次投递",()=>Navigate("applications")),Button("新增求职日程",()=>EditSchedule(new ScheduleItem {Title="",StartAt=DateTimeOffset.Now.AddDays(1).ToString("O")}))};
        foreach(var shortcut in shortcuts){shortcut.HorizontalAlignment=HorizontalAlignment.Stretch;shortcut.HorizontalContentAlignment=HorizontalAlignment.Center;actions.Children.Add(shortcut);}
        int actionColumns=0;
        void ArrangeActions(double available){
            if(available<1)return;double width=Math.Min(760,available);actions.Width=width;int count=width<560?2:4;if(count==actionColumns)return;actionColumns=count;
            actions.ColumnDefinitions.Clear();actions.RowDefinitions.Clear();for(int i=0;i<count;i++)actions.ColumnDefinitions.Add(new ColumnDefinition());
            for(int i=0;i<(shortcuts.Length+count-1)/count;i++)actions.RowDefinitions.Add(new RowDefinition());
            for(int i=0;i<shortcuts.Length;i++){Grid.SetColumn(shortcuts[i],i%count);Grid.SetRow(shortcuts[i],i/count);}
        }
        quick.SizeChanged+=(_,e)=>ArrangeActions(e.NewSize.Width);ArrangeActions(520);
        quick.Children.Add(actions);var resumeCard=Card(quick,new Thickness(20));

        var metrics= new Grid {ColumnSpacing=8};
        foreach(var _ in Enumerable.Range(0,4))metrics.ColumnDefinitions.Add(new ColumnDefinition());
        var open=applications.Count(a=>!new[]{"已录用","已结束"}.Contains(a.Status));
        var due=schedule.Count(s=>!s.Completed);
        var questionList=decks.Where(d=>d.StudyType!="exam").SelectMany(d=>d.Questions).ToList();
        var reviewDue=questionList.Count(q=>q.Status!="remembered"||!DateTimeOffset.TryParse(q.NextReviewAt,out var next)||next<=DateTimeOffset.Now);
        var values=new[]{("简历",resumes.Count,"resumes"),("收藏职位",favorites.Count,"favorites"),("进行中投递",open,"applications"),("待处理日程",due,"schedule")};
        for(int i=0;i<values.Length;i++){
            var(item,count,key)=values[i];var tile=Stack(4);var caption=Text(item,12);caption.Opacity=.72;caption.TextWrapping=TextWrapping.NoWrap;caption.TextTrimming=TextTrimming.CharacterEllipsis;tile.Children.Add(caption);tile.Children.Add(Text(count.ToString(),24,true));
            var metric=Button(item,()=>Navigate(key));metric.Content=tile;metric.HorizontalAlignment=HorizontalAlignment.Stretch;metric.HorizontalContentAlignment=HorizontalAlignment.Left;
            metric.Background=Brush("#00000000");metric.BorderThickness=new Thickness(0);metric.Padding=new Thickness(12,8,12,8);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(metric,$"{item}，{count} 条");Grid.SetColumn(metric,i);metrics.Children.Add(metric);
        }
        var progress=Stack(8);progress.Children.Add(Text("工作进度",16,true));progress.Children.Add(metrics);var progressCard=Card(progress,new Thickness(20));
        var overview=new Grid {ColumnSpacing=14,RowSpacing=14};overview.ColumnDefinitions.Add(new ColumnDefinition());overview.ColumnDefinitions.Add(new ColumnDefinition());overview.RowDefinitions.Add(new RowDefinition());overview.RowDefinitions.Add(new RowDefinition());
        overview.Children.Add(resumeCard);overview.Children.Add(progressCard);
        void ArrangeOverview(double width){bool wide=width>=900;overview.ColumnDefinitions[0].Width=wide?new GridLength(3,GridUnitType.Star):new GridLength(1,GridUnitType.Star);overview.ColumnDefinitions[1].Width=wide?new GridLength(2,GridUnitType.Star):new GridLength(0);Grid.SetColumn(progressCard,wide?1:0);Grid.SetRow(progressCard,wide?0:1);}
        overview.SizeChanged+=(_,e)=>ArrangeOverview(e.NewSize.Width);ArrangeOverview(0);panel.Children.Add(overview);

        var columns=new Grid {ColumnSpacing=24,RowSpacing=24};columns.ColumnDefinitions.Add(new ColumnDefinition());columns.ColumnDefinitions.Add(new ColumnDefinition());columns.RowDefinitions.Add(new RowDefinition());columns.RowDefinitions.Add(new RowDefinition());
        var next=Stack(10);next.Children.Add(Text("接下来的安排",18,true));
        var upcoming=schedule.Where(s=>!s.Completed&&DateTimeOffset.TryParse(s.StartAt,out _)).OrderBy(s=>DateTimeOffset.Parse(s.StartAt)).Take(5).ToList();
        if(upcoming.Count==0)next.Children.Add(Text("暂无待办。可以从职位投递记录添加笔试、面试或跟进安排。",13));
        foreach(var item in upcoming)next.Children.Add(Button(item.Title+"  ·  "+item.Kind+"  ·  "+LocalDate(item.StartAt),()=>EditSchedule(item)));
        Grid.SetColumn(next,0);columns.Children.Add(next);

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
        Grid.SetColumn(follow,1);columns.Children.Add(follow);
        void ArrangeTasks(double width){bool wide=width>=640;columns.ColumnDefinitions[1].Width=wide?new GridLength(1,GridUnitType.Star):new GridLength(0);Grid.SetColumn(follow,wide?1:0);Grid.SetRow(follow,wide?0:1);}
        columns.SizeChanged+=(_,e)=>ArrangeTasks(e.NewSize.Width);ArrangeTasks(0);panel.Children.Add(Card(columns));
        if(resumes.Count>1){panel.Children.Add(Text("最近修改的简历",18,true));
            foreach(var resume in resumes.OrderByDescending(r=>r.UpdatedAt).Skip(1).Take(3))panel.Children.Add(Button(resume.Title+"  ·  "+(resume.Profile.Role is {Length:>0} role?role:"待填写目标岗位")+"  ·  "+LocalDate(resume.UpdatedAt),async ()=>{current=resume;dirty=false;await OpenEditor();}));}
        FillScroll(panel);
    }
    private Task OpenRecentResume(Resume resume){current=resume;dirty=false;return OpenEditor();}
}

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
        var agenda=ScheduleService.Merge(schedule,applications);var now=DateTimeOffset.Now;
        var panel=Stack(24);panel.MaxWidth=1120;panel.HorizontalAlignment=HorizontalAlignment.Left;var quick=Stack(16);
        var latest=resumes.OrderByDescending(r=>r.UpdatedAt).FirstOrDefault();
        var intro=Stack(8);intro.Children.Add(Text(latest is null?"从一份简历开始":latest.Title,24,true));
        var resumeHint=Text(latest is null?"选择模板或导入 PDF，建立你的第一份简历。":(latest.Profile.Role is {Length:>0} latestRole?latestRole:"待填写目标岗位"),13);
        resumeHint.Opacity=.75;intro.Children.Add(resumeHint);
        var heroHeader=new Grid {ColumnSpacing=24,RowSpacing=16};heroHeader.ColumnDefinitions.Add(new ColumnDefinition());heroHeader.ColumnDefinitions.Add(new ColumnDefinition());heroHeader.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});heroHeader.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});heroHeader.Children.Add(intro);
        var actions=new Grid {ColumnSpacing=8,RowSpacing=8,MaxWidth=520,VerticalAlignment=VerticalAlignment.Center};heroHeader.Children.Add(actions);
        var secondary=new Grid {ColumnSpacing=8,RowSpacing=8};for(int i=0;i<4;i++)secondary.ColumnDefinitions.Add(new ColumnDefinition());secondary.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});secondary.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var shortcuts=new[]{Button(latest is null?"新建简历":"继续编辑简历",()=>latest is null?Navigate("templates"):OpenRecentResume(latest),true),Button("导入 PDF 简历",ImportPdfResume),Button("查看职位收藏",()=>Navigate("favorites")),Button("记录一次投递",()=>EditApplication(new ApplicationRecord(),"home")),Button("新增求职日程",()=>EditSchedule(new ScheduleItem {Title="",StartAt=DateTimeOffset.Now.AddDays(1).ToString("O")})),Button("开始面试复习",()=>Navigate("interviews"))};
        foreach(var shortcut in shortcuts.Take(2)){shortcut.HorizontalAlignment=HorizontalAlignment.Stretch;shortcut.HorizontalContentAlignment=HorizontalAlignment.Center;actions.Children.Add(shortcut);}
        int actionColumns=0;
        void ArrangeActions(double available){
            if(available<1)return;bool wide=available>=800;heroHeader.ColumnDefinitions[0].Width=new GridLength(wide?3:1,GridUnitType.Star);heroHeader.ColumnDefinitions[1].Width=wide?new GridLength(2,GridUnitType.Star):new GridLength(0);Grid.SetColumn(actions,wide?1:0);Grid.SetRow(actions,wide?0:1);actions.HorizontalAlignment=HorizontalAlignment.Stretch;
            int secondaryColumns=wide?4:2;secondary.RowSpacing=wide?0:8;for(int i=0;i<4;i++){secondary.ColumnDefinitions[i].Width=i<secondaryColumns?new GridLength(1,GridUnitType.Star):new GridLength(0);Grid.SetColumn(shortcuts[i+2],i%secondaryColumns);Grid.SetRow(shortcuts[i+2],i/secondaryColumns);}
            int count=2;if(count==actionColumns)return;actionColumns=count;
            actions.ColumnDefinitions.Clear();actions.RowDefinitions.Clear();for(int i=0;i<count;i++)actions.ColumnDefinitions.Add(new ColumnDefinition());
            for(int i=0;i<(2+count-1)/count;i++)actions.RowDefinitions.Add(new RowDefinition());
            for(int i=0;i<2;i++){Grid.SetColumn(shortcuts[i],i%count);Grid.SetRow(shortcuts[i],i/count);}
        }
        quick.SizeChanged+=(_,e)=>ArrangeActions(e.NewSize.Width);ArrangeActions(520);
        quick.Children.Add(heroHeader);foreach(var shortcut in shortcuts.Skip(2)){shortcut.Background=Brush(ClearSurface);shortcut.BorderThickness=new Thickness(0);shortcut.HorizontalAlignment=HorizontalAlignment.Stretch;secondary.Children.Add(shortcut);}quick.Children.Add(secondary);var resumeCard=Card(quick,new Thickness(24));resumeCard.Tag="home-resume";panel.Children.Add(resumeCard);

        var metrics= new Grid {ColumnSpacing=12,RowSpacing=12,Tag="home-metrics"};
        for(int i=0;i<4;i++)metrics.ColumnDefinitions.Add(new ColumnDefinition());
        metrics.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});metrics.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var open=applications.Count(a=>!new[]{"已录用","已结束"}.Contains(a.Status));
        var due=agenda.Count(s=>!s.Completed);
        var questionList=decks.Where(d=>d.StudyType!="exam").SelectMany(d=>d.Questions).ToList();
        var reviewDue=questionList.Count(q=>q.Status!="remembered"||!DateTimeOffset.TryParse(q.NextReviewAt,out var next)||next<=DateTimeOffset.Now);
        var values=new[]{("简历",resumes.Count,"resumes"),("收藏职位",favorites.Count,"favorites"),("进行中投递",open,"applications"),("待处理日程",due,"schedule")};
        for(int i=0;i<values.Length;i++){
            var(item,count,key)=values[i];var tile=Stack(4);var caption=Text(item,12);caption.Opacity=.72;caption.TextWrapping=TextWrapping.NoWrap;caption.TextTrimming=TextTrimming.CharacterEllipsis;tile.Children.Add(caption);tile.Children.Add(Text(count.ToString(),24,true));
            var metric=Button(item,()=>Navigate(key));metric.Content=tile;metric.HorizontalAlignment=HorizontalAlignment.Stretch;metric.HorizontalContentAlignment=HorizontalAlignment.Left;
            metric.Background=Brush(ClearSurface);metric.BorderThickness=new Thickness(0);metric.Padding=new Thickness(12,8,12,8);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(metric,$"{item}，{count} 条");Grid.SetColumn(metric,i);metrics.Children.Add(metric);
        }
        void ArrangeMetrics(double width){int count=width>=600?4:2;metrics.RowSpacing=count==4?0:12;for(int i=0;i<4;i++){metrics.ColumnDefinitions[i].Width=i<count?new GridLength(1,GridUnitType.Star):new GridLength(0);Grid.SetColumn((FrameworkElement)metrics.Children[i],i%count);Grid.SetRow((FrameworkElement)metrics.Children[i],i/count);}}
        metrics.SizeChanged+=(_,e)=>ArrangeMetrics(e.NewSize.Width);ArrangeMetrics(0);
        var progress=Stack(12);progress.Children.Add(Text("工作进度",16,true));progress.Children.Add(metrics);var progressCard=Card(progress,new Thickness(24));progressCard.Tag="home-progress";panel.Children.Add(progressCard);

        var columns=new Grid {ColumnSpacing=24,RowSpacing=24};columns.ColumnDefinitions.Add(new ColumnDefinition());columns.ColumnDefinitions.Add(new ColumnDefinition());columns.RowDefinitions.Add(new RowDefinition());columns.RowDefinitions.Add(new RowDefinition());
        var next=Stack(12);next.Children.Add(Text("今日与近期",18,true));
        var summary=Text($"今日 {agenda.Count(e=>!e.Completed&&e.Date==DateTime.Today)} 项，已到期 {agenda.Count(e=>e.Overdue(now))} 项",12);summary.Opacity=.72;next.Children.Add(summary);
        var upcoming=ScheduleService.Overview(agenda,now);
        if(upcoming.Count==0){next.Children.Add(Text("安排都处理好了。添加下一次面试、笔试或跟进时间。",13));next.Children.Add(Button("添加安排",()=>EditSchedule(new ScheduleItem {StartAt=DateTimeOffset.Now.AddDays(1).ToString("O")}),true));}
        foreach(var entry in upcoming)next.Children.Add(HomeAgendaRow(entry,now));
        next.Children.Add(Button("查看全部日程",async ()=>{scheduleView=2;scheduleApplicationId="";scheduleQuery="";scheduleKind="全部类型";await Navigate("schedule");}));
        var nextCard=Card(next,new Thickness(24));nextCard.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(nextCard,0);columns.Children.Add(nextCard);

        var follow=Stack(10);follow.Children.Add(Text("需要跟进",18,true));
        var followUps=applications.Where(a=>!new[]{"已录用","已结束"}.Contains(a.Status))
            .Select(a=>(Record:a,Alert:WorkspaceAlerts.For(a.DeadlineAt,string.IsNullOrWhiteSpace(a.LastFollowedAt)?a.CreatedAt:a.LastFollowedAt,a.Status)))
            .Where(x=>!string.IsNullOrWhiteSpace(x.Alert)).OrderBy(x=>x.Record.DueAt).Take(5).ToList();
        if(followUps.Count==0)follow.Children.Add(Text("当前没有逾期或长期未跟进的投递。",13));
        foreach(var item in followUps)follow.Children.Add(Button(item.Record.Company+"\n"+item.Record.Title+"\n"+item.Alert,()=>ViewApplication(item.Record.Id)));
        if(reviewDue>0)follow.Children.Add(Button($"面试题复习提醒：{reviewDue} 道待复习",()=>Navigate("interviews")));
        var examQuestions=decks.Where(d=>d.StudyType=="exam").SelectMany(d=>d.Questions).ToList();
        int examDue=examQuestions.Count(q=>q.Status!="remembered");
        if(examDue>0)follow.Children.Add(Button($"笔试复盘：{examDue} 道待完成",()=>Navigate("exams")));
        var followCard=Card(follow,new Thickness(24));followCard.VerticalAlignment=VerticalAlignment.Top;Grid.SetColumn(followCard,1);columns.Children.Add(followCard);
        void ArrangeTasks(double width){bool wide=width>=820;columns.ColumnDefinitions[0].Width=new GridLength(wide?3:1,GridUnitType.Star);columns.ColumnDefinitions[1].Width=wide?new GridLength(2,GridUnitType.Star):new GridLength(0);Grid.SetColumn(followCard,wide?1:0);Grid.SetRow(followCard,wide?0:1);}
        columns.SizeChanged+=(_,e)=>ArrangeTasks(e.NewSize.Width);ArrangeTasks(0);panel.Children.Add(columns);
        if(resumes.Count>1){var recent=Stack(12);recent.Children.Add(Text("最近修改的简历",18,true));var recentGrid=new Grid {ColumnSpacing=12,RowSpacing=12};
            for(int i=0;i<3;i++){recentGrid.ColumnDefinitions.Add(new ColumnDefinition());recentGrid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});}
            foreach(var resume in resumes.OrderByDescending(r=>r.UpdatedAt).Skip(1).Take(3)){var item=Button(resume.Title+"\n"+(resume.Profile.Role is {Length:>0} role?role:"待填写目标岗位")+"\n"+LocalDate(resume.UpdatedAt),()=>OpenRecentResume(resume));item.HorizontalAlignment=HorizontalAlignment.Stretch;item.HorizontalContentAlignment=HorizontalAlignment.Left;item.MinHeight=92;recentGrid.Children.Add(item);}
            void ArrangeRecent(double width){int count=width>=900?3:width>=600?2:1;for(int i=0;i<3;i++)recentGrid.ColumnDefinitions[i].Width=i<count?new GridLength(1,GridUnitType.Star):new GridLength(0);for(int i=0;i<recentGrid.Children.Count;i++){Grid.SetColumn((FrameworkElement)recentGrid.Children[i],i%count);Grid.SetRow((FrameworkElement)recentGrid.Children[i],i/count);}}
            recentGrid.SizeChanged+=(_,e)=>ArrangeRecent(e.NewSize.Width);ArrangeRecent(0);recent.Children.Add(recentGrid);panel.Children.Add(recent);}
        FillScroll(panel);
    }
    private Task OpenRecentResume(Resume resume){current=resume;dirty=false;return OpenEditor();}
    private UIElement HomeAgendaRow(AgendaEntry entry,DateTimeOffset now)
    {
        var row=new Grid {ColumnSpacing=12};row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(52)});row.ColumnDefinitions.Add(new ColumnDefinition());
        var date=Stack(2);var day=Text(entry.Date==DateTime.Today?"今天":entry.Date==DateTime.Today.AddDays(1)?"明天":entry.At.LocalDateTime.ToString("MM/dd"),15,true);day.Foreground=Brush(entry.Overdue(now)?WarningInk:Accent);date.Children.Add(day);date.Children.Add(Text(entry.AllDay?"全天":entry.At.LocalDateTime.ToString("HH:mm"),12));row.Children.Add(date);
        var content=Stack(6);var title=Text(entry.Title,15,true);title.MaxLines=2;content.Children.Add(title);
        var meta=Text(entry.Kind+(entry.Company.Length>0?"，"+entry.Company:"")+(entry.Overdue(now)?"，已到期待处理":"")+(entry.Application is not null?"，来自投递":""),12);meta.Opacity=.75;content.Children.Add(meta);
        var actions=Flow(6);actions.Children.Add(Button("查看安排",()=>entry.Item is not null?EditSchedule(entry.Item):EditApplication(entry.Application!,"home")));
        var complete=Button("标记完成",()=>CompleteAgenda(entry,true));complete.Tag="home-complete:"+entry.Id;Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(complete,"完成安排："+entry.Title);actions.Children.Add(complete);content.Children.Add(actions);Grid.SetColumn(content,1);row.Children.Add(content);
        return new Border {Child=row,Padding=new Thickness(0,14,0,14),BorderBrush=Brush(Stroke),BorderThickness=new Thickness(0,0,0,1)};
    }
}

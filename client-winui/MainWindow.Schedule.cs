using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private DateTime scheduleDate=DateTime.Today;
    private int scheduleView;
    private CalendarView? scheduleCalendar;
    private string scheduleQuery="",scheduleKind="全部类型",scheduleApplicationId="";
    private readonly HashSet<string> expandedSchedule=[];
    private async Task<List<AgendaEntry>> ReadAgenda()
    {
        var items=api.Get<List<ScheduleItem>>("schedule"); var records=api.Get<List<ApplicationRecord>>("applications");
        await Task.WhenAll(items,records); return ScheduleService.Merge(items.Result,records.Result);
    }
    private async Task ShowSchedule()
    {
        int generation=navigationRevision;
        Heading("求职日程","把笔试、面试、投递截止和自己的待办安排放在一起。");
        var entries=await ReadAgenda(); if(!PageIsCurrent("schedule",generation)) return;
        var panel=Stack(16);
        panel.Children.Add(Button("导出到系统日历（ICS）",async ()=>{var picker=new Windows.Storage.Pickers.FileSavePicker {SuggestedFileName="求职日程"};picker.FileTypeChoices.Add("日历文件",new[]{".ics"});InitPicker(picker);var file=await picker.PickSaveFileAsync();if(file is not null){await File.WriteAllTextAsync(file.Path,CalendarExport.Create(await ReadAgenda()),new System.Text.UTF8Encoding(false));Success("日历已导出","可导入系统日历，在应用关闭时由日历提醒。");}})); var tools=Row();
        tools.Children.Add(Button("＋ 新增日程",()=>EditSchedule(new ScheduleItem {StartAt=new DateTimeOffset(scheduleDate.AddHours(9),TimeZoneInfo.Local.GetUtcOffset(scheduleDate)).ToString("O")}),true));
        tools.Children.Add(Button("今天",async ()=> {scheduleDate=DateTime.Today; scheduleView=0; await Navigate("schedule");})); tools.Children.Add(Button("刷新",()=>Navigate("schedule"))); panel.Children.Add(tools);
        var metrics=new Grid {ColumnSpacing=10};for(int i=0;i<3;i++)metrics.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        var pending=entries.Where(e=>!e.Completed).ToList();int metricIndex=0;
        foreach(var stat in new[] {("今日安排",pending.Count(e=>e.Date==DateTime.Today),"#245AE8"),("近七天",pending.Count(e=>e.End>=DateTimeOffset.Now&&e.Date<=DateTime.Today.AddDays(6)),"#167D78"),("已到期待办",pending.Count(e=>e.Overdue(DateTimeOffset.Now)),"#CC7B20")}) {
            var content=Stack(4);var value=Text(stat.Item2.ToString(),26,true);value.Foreground=Brush(stat.Item3);content.Children.Add(value);content.Children.Add(Text(stat.Item1,12));var card=Card(content,new Thickness(16));Grid.SetColumn(card,metricIndex++);metrics.Children.Add(card);
        }panel.Children.Add(metrics);
        panel.Children.Add(Text("投递记录中的下一次安排会自动显示；在日程中可另外添加多次面试或自定义事项。运行时发送系统通知；关闭软件后的提醒可使用导出的系统日历，重新打开也会检查未处理事项。",12));
        var grid=new Grid {ColumnSpacing=18,RowSpacing=18}; grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto}); grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)}); grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto}); grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        scheduleCalendar=new CalendarView {SelectionMode=CalendarViewSelectionMode.Single,Language="zh-CN",FirstDayOfWeek=Windows.Globalization.DayOfWeek.Monday,HorizontalAlignment=HorizontalAlignment.Stretch,MinWidth=320,MinDate=new DateTimeOffset(new DateTime(2000,1,1)),MaxDate=new DateTimeOffset(new DateTime(2100,12,31))};
        var calendar=scheduleCalendar;
        calendar.SetDisplayDate(new DateTimeOffset(scheduleDate)); calendar.SelectedDates.Add(new DateTimeOffset(scheduleDate));
        scheduleCalendar.CalendarViewDayItemChanging+=(_,args)=> {var date=args.Item.Date.Date; args.Item.SetDensityColors(ScheduleService.ForDate(entries,date).Where(e=>!e.Completed).Take(10).Select(e=>((SolidColorBrush)Brush(e.Kind=="面试"?"#245AE8":e.Kind=="笔试"?"#167D78":e.Kind=="投递截止"?"#CC7B20":"#7955B4")).Color));};
        var month=Stack(10); month.Children.Add(Text("月历",18,true)); month.Children.Add(scheduleCalendar); month.Children.Add(Text("日期上的色条表示待办：蓝色面试 · 绿色笔试 · 橙色截止 · 紫色其他",11));
        var quick=new Grid {ColumnSpacing=5,RowSpacing=5};for(int i=0;i<2;i++){quick.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});quick.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});}int quickIndex=0;
        foreach(var type in new[] {"笔试","面试","投递截止","自定义"}) {var add=Button("＋ "+type,()=>EditSchedule(new ScheduleItem {Kind=type,StartAt=new DateTimeOffset(scheduleDate.AddHours(9),TimeZoneInfo.Local.GetUtcOffset(scheduleDate)).ToString("O")}));add.HorizontalAlignment=HorizontalAlignment.Stretch;ToolTipService.SetToolTip(add,$"在所选日期新建{type}");Grid.SetColumn(add,quickIndex%2);Grid.SetRow(add,quickIndex++/2);quick.Children.Add(add);}month.Children.Add(Text("在所选日期快速添加",12));month.Children.Add(quick);
        var monthCard=Card(month,new Thickness(16)); monthCard.MaxWidth=430; monthCard.VerticalAlignment=VerticalAlignment.Top; grid.Children.Add(monthCard);
        var list=Stack(12); var heading=Text("",18,true); list.Children.Add(heading);
        var mode=new ComboBox {Header="查看范围",ItemsSource=new[] {"所选日期","近期 30 天","全部未完成","全部日程（含已完成）"},SelectedIndex=scheduleView,HorizontalAlignment=HorizontalAlignment.Stretch}; list.Children.Add(mode);
        var types=new ComboBox {Header="日程类型筛选",ItemsSource=new[] {"全部类型"}.Concat(ScheduleService.Kinds).ToArray(),SelectedItem=scheduleKind,HorizontalAlignment=HorizontalAlignment.Stretch};list.Children.Add(types);
        var query=new TextBox {PlaceholderText="搜索事项、公司或备注",MaxLength=200,Text=scheduleQuery}; list.Children.Add(query); var cards=Stack(12);Entrance(cards);list.Children.Add(cards);
        var clear=Button("清空筛选",()=>{scheduleApplicationId="";query.Text="";types.SelectedIndex=0;return RefreshPage("schedule");});list.Children.Insert(list.Children.Count-1,clear);
        if(scheduleApplicationId.Length>0)list.Children.Insert(1,Text("仅显示当前投递关联的安排；清空筛选可查看所有日程。",12,true));
        void Refresh()
        {
            cards.Children.Clear(); scheduleView=mode.SelectedIndex; var now=DateTimeOffset.Now;
            heading.Text=scheduleView==0?$"{scheduleDate:yyyy年M月d日} · 日程":scheduleView==1?"近期日程":scheduleView==2?"全部待办":"全部日程";
            scheduleQuery=query.Text;scheduleKind=types.SelectedItem?.ToString()??"全部类型";
            var found=(scheduleView==0?ScheduleService.ForDate(entries,scheduleDate):scheduleView==1?ScheduleService.Upcoming(entries,now):scheduleView==2?entries.Where(e=>!e.Completed):entries).Where(e=>(scheduleApplicationId==""||e.ApplicationId==scheduleApplicationId)&&(scheduleKind=="全部类型"||e.Kind==scheduleKind)&&(e.Title+" "+e.Company+" "+e.Item?.Notes+" "+e.Application?.Notes).Contains(query.Text,StringComparison.OrdinalIgnoreCase)).ToList();
            heading.Text+=$" · {found.Count} 项";
            if(found.Count==0){var empty=Stack(10);empty.Children.Add(Text(scheduleView==0?"这一天暂无符合条件的安排。":"暂无符合条件的日程。",14));cards.Children.Add(Card(empty));}
            foreach(var entry in found) {
                var item=Stack(10);var summary=Stack(6);var title=Text(entry.Title,17,true);title.MaxLines=2;summary.Children.Add(title);
                var state=Text(entry.Kind+" · "+entry.When+" · "+(entry.Completed?"✓ 已完成":entry.Overdue(now)?"已到期":"待办"),12,true);state.Foreground=Brush(entry.Completed?"#167D78":entry.Overdue(now)?"#CC7B20":Accent);summary.Children.Add(state);
                if(entry.Company.Length>0)summary.Children.Add(Text(entry.Company,12));
                var details=Stack(9);string notes=entry.Item?.Notes??entry.Application?.Notes??"";details.Children.Add(Text(notes.Length>0?notes:"暂无备注，可在编辑中补充地点、准备事项或会议链接。",12));
                details.Children.Add(Text(entry.Application is not null?"来自投递记录 · 修改与完成状态同步":"独立日程"+(entry.Item?.ApplicationId.Length>0?" · 已关联投递":"")+(entry.ReminderEnabled?" · "+ScheduleService.ReminderLabel(entry.ReminderMinutes):" · 提醒已关闭"),11));
                var expand=Disclosure("",details,expandedSchedule.Contains(entry.Id),entry.Id);expand.Header=summary;expand.Expanding+=(_,_)=>expandedSchedule.Add(entry.Id);expand.Collapsed+=(_,_)=>expandedSchedule.Remove(entry.Id);item.Children.Add(expand);
                var actions=Row(7);
                actions.Children.Add(Button("编辑",()=>entry.Item is not null?EditSchedule(entry.Item):EditApplication(entry.Application!,"schedule")));
                var complete=Button(entry.Completed?"恢复待办":"标记完成",()=>CompleteAgenda(entry,!entry.Completed));
                complete.IsEnabled=entry.Item is not null||entry.Application?.Status is not "已录用" and not "已结束";actions.Children.Add(complete);
                if(entry.Item is not null) actions.Children.Add(Button("删除",async ()=> {if(await Confirm("删除这条日程？",entry.Title)){await api.Delete("schedule/"+entry.Item.Id);await Navigate("schedule");}}));
                if(entry.ApplicationId.Length>0) actions.Children.Add(Button("查看投递",()=>ViewApplication(entry.ApplicationId)));
                item.Children.Add(actions); cards.Children.Add(Card(item,new Thickness(16)));
            }
        }
        calendar.SelectedDatesChanged+=(_,_)=> {if(calendar.SelectedDates.Count>0){scheduleDate=calendar.SelectedDates[0].Date;mode.SelectedIndex=0;Refresh();}};
        mode.SelectionChanged+=(_,_)=>Refresh(); query.TextChanged+=(_,_)=>Refresh();types.SelectionChanged+=(_,_)=>Refresh(); Refresh();
        var listCard=Card(list,new Thickness(18)); Grid.SetColumn(listCard,1); grid.Children.Add(listCard);
        bool stacked=false; grid.SizeChanged+=(_,args)=> {bool next=args.NewSize.Width<780;if(next==stacked)return;stacked=next;grid.ColumnDefinitions[0].Width=next?new GridLength(1,GridUnitType.Star):GridLength.Auto;grid.ColumnDefinitions[1].Width=next?new GridLength(0):new GridLength(1,GridUnitType.Star);Grid.SetColumn(listCard,next?0:1);Grid.SetRow(listCard,next?1:0);};
        panel.Children.Add(grid); FillScroll(panel);
    }
    private async Task CompleteAgenda(AgendaEntry entry,bool completed)
    {
        bool previous=entry.Completed;await SetAgendaCompleted(entry,completed);await Navigate("schedule");
        Feedback(completed?"日程已完成":"已恢复为待办",entry.Title,async ()=> {
            // Read current data so undo never overwrites a later edit or recreates a deleted item.
            var latest=(await ReadAgenda()).FirstOrDefault(e=>e.Id==entry.Id);
            if(latest is null||latest.Completed!=completed||latest.Application?.Status is "已录用" or "已结束")throw new InvalidOperationException("日程状态已发生变化，无法撤销这次操作。");
            await SetAgendaCompleted(latest,previous);if(currentPage=="schedule")await Navigate("schedule");
        });
    }
    private async Task SetAgendaCompleted(AgendaEntry entry,bool completed)
    {
        var latest=(await ReadAgenda()).FirstOrDefault(e=>e.Id==entry.Id)??throw new InvalidOperationException("日程已删除，请刷新列表。");
        if(latest.At!=entry.At||latest.Application is not null&&JobWorkflow.Closed(latest.Application))throw new InvalidOperationException("日程安排或投递状态已变化，请刷新后重试。");
        if(latest.Item is not null){var copy=CloneDocument(latest.Item);copy.Completed=completed;await api.Save("schedule",copy);}
        else if(latest.Application is not null){var copy=CloneDocument(latest.Application);copy.ReminderDone=completed;await api.Save("applications",copy);}
    }
    private async Task EditSchedule(ScheduleItem original)
    {
        string returnPage=currentPage;
        if(original.UpdatedAt.Length>0)original=(await api.Get<List<ScheduleItem>>("schedule")).FirstOrDefault(i=>i.Id==original.Id)??throw new InvalidOperationException("日程已被删除，请刷新列表。");
        var item=CloneDocument(original);
        var applications=await api.Get<List<ApplicationRecord>>("applications");
        var links=new[] {new ApplicationRecord {Id="",Title="不关联投递"}}.Concat(applications).ToList();
        var panel=Stack(12); var title=new TextBox {Header="事项名称",Text=item.Title,MaxLength=200};
        var kind=new ComboBox {Header="日程类型",ItemsSource=ScheduleService.Kinds,SelectedItem=item.Kind,HorizontalAlignment=HorizontalAlignment.Stretch};
        var at=DateTimeOffset.TryParse(item.StartAt,out var parsed)?parsed.ToLocalTime():DateTimeOffset.Now;
        var date=new CalendarDatePicker {Header="日期",Date=at,HorizontalAlignment=HorizontalAlignment.Stretch};var time=new TimePicker {Header="时间",ClockIdentifier="24HourClock",Time=at.TimeOfDay,HorizontalAlignment=HorizontalAlignment.Stretch};
        var allDay=new CheckBox {Content="全天事项",IsChecked=item.AllDay}; LinkedChoice(allDay,time,inverse:true);
        var link=new ComboBox {Header="关联投递（可选）",ItemsSource=links,DisplayMemberPath="Title",SelectedItem=links.FirstOrDefault(r=>r.Id==item.ApplicationId)??links[0],HorizontalAlignment=HorizontalAlignment.Stretch};
        var reminders=new CheckBox {Content="软件内提醒",IsChecked=item.ReminderEnabled};int[] minutes=[1440,60,15,0];var ahead=new ComboBox {Header="提醒时间",ItemsSource=new[] {"提前一天","提前一小时","提前 15 分钟","到点提醒"},SelectedIndex=Math.Max(0,Array.IndexOf(minutes,item.ReminderMinutes)),HorizontalAlignment=HorizontalAlignment.Stretch};LinkedChoice(reminders,ahead);
        var notes=new TextBox {Header="备注／地点／会议链接",Text=item.Notes,MaxLength=10000,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=90};var completed=new CheckBox {Content="已完成",IsChecked=item.Completed};
        foreach(var element in new UIElement[] {title,kind,date,allDay,time,link,reminders,ahead,notes,completed}) panel.Children.Add(element);
        var dialog=SaveForm("编辑日程",panel,"保存日程",async ()=> {
        if(string.IsNullOrWhiteSpace(title.Text)||date.Date is null)throw new InvalidOperationException("请填写事项名称并选择日期。");
        var local=date.Date!.Value.Date+(allDay.IsChecked==true?TimeSpan.Zero:time.Time);
        item.Title=title.Text.Trim();item.Kind=(string)kind.SelectedItem;item.StartAt=new DateTimeOffset(local,TimeZoneInfo.Local.GetUtcOffset(local)).ToString("O");item.AllDay=allDay.IsChecked==true;item.Completed=completed.IsChecked==true;item.ReminderEnabled=reminders.IsChecked==true;item.ReminderMinutes=minutes[ahead.SelectedIndex];item.Notes=notes.Text;item.ApplicationId=(link.SelectedItem as ApplicationRecord)?.Id??"";
        await api.Save("schedule",item);
        });
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        scheduleDate=DateTimeOffset.Parse(item.StartAt).LocalDateTime.Date;await RefreshPage(returnPage);Feedback("日程已保存",item.Title);await CheckApplicationReminders();
    }
}

namespace ResumeStudio.Services;

public sealed record AgendaEntry(string Id,string Title,string Company,string Kind,DateTimeOffset At,bool AllDay,bool Completed,bool ReminderEnabled,int ReminderMinutes,ScheduleItem? Item,ApplicationRecord? Application)
{
    public DateTime Date=>At.LocalDateTime.Date;
    public string ApplicationId=>Item?.ApplicationId??Application?.Id??"";
    public DateTimeOffset End=>AllDay?new DateTimeOffset(Date.AddDays(1),TimeZoneInfo.Local.GetUtcOffset(Date.AddDays(1))):At;
    public bool Overdue(DateTimeOffset now)=>!Completed&&End<now;
    public string When=>AllDay?At.LocalDateTime.ToString("yyyy-MM-dd")+" · 全天":At.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
}

public static class ScheduleService
{
    public static readonly string[] Kinds=["笔试","面试","投递截止","跟进","自定义"];
    public static string ReminderLabel(int minutes)=>minutes switch {1440=>"提前一天提醒",60=>"提前一小时提醒",15=>"提前 15 分钟提醒",0=>"到点提醒",_=>$"提前 {minutes} 分钟提醒"};
    public static List<AgendaEntry> Merge(IEnumerable<ScheduleItem> items,IEnumerable<ApplicationRecord> applications)
    {
        var records=applications.ToList(); var result=new List<AgendaEntry>();
        foreach(var item in items) if(DateTimeOffset.TryParse(item.StartAt,out var at)) {
            var link=records.FirstOrDefault(r=>r.Id==item.ApplicationId);
            result.Add(new("schedule:"+item.Id,item.Title,link?.Company??"",item.Kind,at,item.AllDay,item.Completed,item.ReminderEnabled,item.ReminderMinutes,item,null));
        }
        // Existing application arrangements are projections, never copied to schedule files.
        foreach(var record in records) if(DateTimeOffset.TryParse(record.DueAt,out var at)) result.Add(new("application:"+record.Id,record.Title,record.Company,record.EventKind=="截止日期"?"投递截止":record.EventKind,at,false,record.ReminderDone||record.Status is "已录用" or "已结束",true,1440,null,record));
        return result.OrderBy(e=>e.At).ThenBy(e=>e.Id,StringComparer.Ordinal).ToList();
    }
    public static IEnumerable<AgendaEntry> ForDate(IEnumerable<AgendaEntry> entries,DateTime date)=>entries.Where(e=>e.Date==date.Date);
    public static List<AgendaEntry> Overview(IEnumerable<AgendaEntry> entries,DateTimeOffset now,int limit=5)
    {
        if(limit<=0)return [];
        var pending=entries.Where(e=>!e.Completed).ToList();
        // Reserve room for upcoming appointments even when old overdue items remain unresolved.
        return pending.Where(e=>e.Overdue(now)).OrderByDescending(e=>e.At).Take(Math.Min(2,limit))
            .Concat(pending.Where(e=>!e.Overdue(now)).OrderBy(e=>e.At)).Take(limit).ToList();
    }
    public static IEnumerable<AgendaEntry> Upcoming(IEnumerable<AgendaEntry> entries,DateTimeOffset now)=>entries.Where(e=>!e.Completed&&e.End>=now&&e.Date<=now.LocalDateTime.Date.AddDays(30));
    public static IEnumerable<AgendaEntry> Reminders(IEnumerable<AgendaEntry> entries,DateTimeOffset now)=>entries.Where(e=>!e.Completed&&e.ReminderEnabled&&e.At.AddMinutes(-e.ReminderMinutes)<=now);
}

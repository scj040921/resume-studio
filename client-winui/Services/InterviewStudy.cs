using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResumeStudio.Services;

public sealed class InterviewQuestion
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData {get;set;}
    public string Id {get;set;}=Guid.NewGuid().ToString();
    public string Title {get;set;}="";
    public string Answer {get;set;}="";
    public string Status {get;set;}="unseen";
    public string ReviewedAt {get;set;}="";
    public int ReviewCount {get;set;}
    public int IntervalDays {get;set;}
    public string NextReviewAt {get;set;}="";
    public string ReviewNote {get;set;}="";
}
public sealed class InterviewDeck
{
    [JsonExtensionData] public Dictionary<string,JsonElement>? ExtensionData {get;set;}
    public string Id {get;set;}=Guid.NewGuid().ToString();
    public string Title {get;set;}="";
    public string StudyType {get;set;}="interview";
    public string SourceName {get;set;}="";
    public string LastQuestionId {get;set;}="";
    public string UpdatedAt {get;set;}="";
    public List<InterviewQuestion> Questions {get;set;}=[];
    public override string ToString()=>Title;
}
public readonly record struct InterviewCounts(int Total,int Remembered,int Learning,int Unseen);
public static class InterviewProgress
{
    public static InterviewCounts Count(IEnumerable<InterviewQuestion> questions){var list=questions.ToList();return new(list.Count,list.Count(q=>q.Status=="remembered"),list.Count(q=>q.Status=="learning"),list.Count(q=>q.Status=="unseen"));}
    public static IEnumerable<InterviewQuestion> Filter(InterviewDeck deck,string status)=>deck.Questions.Where(q=>status=="all"||status=="due"&&(!DateTimeOffset.TryParse(q.NextReviewAt,out var due)||due<=DateTimeOffset.Now)||q.Status==status);
    public static void Review(InterviewQuestion question,string status,DateTimeOffset now){
        question.Status=status;question.ReviewedAt=now.ToString("O");question.ReviewCount=Math.Min(100000,question.ReviewCount+1);
        question.IntervalDays=status=="remembered"?(question.IntervalDays==0?1:Math.Min(90,question.IntervalDays*2+1)):0;
        question.NextReviewAt=(status=="remembered"?now.AddDays(question.IntervalDays):now.AddMinutes(10)).ToString("O");
    }
    public static string Label(string status)=>status switch {"remembered"=>"已记住","learning"=>"未掌握",_=>"未标记"};
}

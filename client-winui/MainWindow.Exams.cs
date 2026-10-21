using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using Windows.Storage.Pickers;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private string examDeckId="";
    private int examQuestionIndex;
    private InterviewDeck? examDraftDeck;
    private InterviewQuestion? examDraftQuestion;
    private string examDraftNote="";
    private bool examDraftDirty;
    private int examDraftRevision;
    private TextBlock? examSaveStatus;
    private readonly DispatcherTimer examSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(650)};
    private readonly SemaphoreSlim examSaveLock=new(1,1);

    private async Task ShowExamReviews()
    {
        int generation=navigationRevision;
        Heading("笔试题复盘","导入笔试题后逐题记录错因、知识缺口和下一步改进。内容自动保存到本机。");
        var all=await api.Get<List<InterviewDeck>>("interviews");if(!PageIsCurrent("exams",generation))return;
        var decks=all.Where(d=>d.StudyType=="exam").OrderBy(d=>d.Title).ToList();
        var root=Stack(14);var toolbar=Row();toolbar.Children.Add(Button("＋ 导入笔试题文件",ImportExamQuestions,true));
        var selected=decks.FirstOrDefault(d=>d.Id==examDeckId)??decks.FirstOrDefault();
        var picker=new ComboBox {Header="复盘题库",ItemsSource=decks,SelectedItem=selected,MinWidth=220};toolbar.Children.Add(picker);
        var count=selected?.Questions.Count??0;var done=selected?.Questions.Count(q=>q.Status=="remembered")??0;var pending=selected?.Questions.Count(q=>q.Status!="remembered")??0;
        toolbar.Children.Add(Text($"共 {count} 题 · 已复盘 {done} · 待复盘 {pending}",13,true));root.Children.Add(toolbar);
        var host=new Border {MinHeight=440,MaxWidth=980,HorizontalAlignment=HorizontalAlignment.Stretch};root.Children.Add(host);
        var pageButtons=Row();pageButtons.HorizontalAlignment=HorizontalAlignment.Center;root.Children.Add(pageButtons);
        void Draw()
        {
            selected=picker.SelectedItem as InterviewDeck;examDeckId=selected?.Id??"";
            count=selected?.Questions.Count??0;done=selected?.Questions.Count(q=>q.Status=="remembered")??0;pending=count-done;
            if(toolbar.Children.Count>2)toolbar.Children[2]=Text($"共 {count} 题 · 已复盘 {done} · 待复盘 {pending}",13,true);
            if(selected is null||count==0){host.Child=Card(Text("导入 Markdown、TXT、DOCX 或 PDF 笔试题，按标题拆分后逐题复盘。",16));pageButtons.Children.Clear();return;}
            examQuestionIndex=Math.Clamp(examQuestionIndex,0,count-1);var q=selected.Questions[examQuestionIndex];
            var card=Stack(14);card.Children.Add(Text($"第 {examQuestionIndex+1} / {count} 题  ·  {(q.Status=="remembered"?"已复盘":q.Status=="learning"?"需要再练":"待复盘")}",12,true));
            var title=Text(q.Title,23,true);title.IsTextSelectionEnabled=true;card.Children.Add(title);
            var prompt=new TextBox {Header="题目内容",Text=q.Answer,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=210,IsSpellCheckEnabled=false};card.Children.Add(prompt);
            var note=new TextBox {Header="本题复盘",Text=q.ReviewNote,PlaceholderText="记录作答过程、错因、遗漏知识、正确思路和下次改进……",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=160,MaxLength=20000};card.Children.Add(note);
            var saveState=Text("复盘内容尚未保存",11);examSaveStatus=saveState;card.Children.Add(saveState);
            bool saving=false;async Task Save(string status)
            {
                if(saving)return;saving=true;
                try
                {
                    examSaveTimer.Stop();q.ReviewNote=note.Text;q.Status=status;selected.LastQuestionId=q.Id;
                    examDraftDeck=selected;examDraftQuestion=q;examDraftNote=note.Text;examDraftDirty=true;examDraftRevision++;
                    int revision=examDraftRevision;await PersistExamDraft();
                    if(revision==examDraftRevision){saveState.Text="已保存 · "+DateTime.Now.ToString("HH:mm:ss");Draw();}
                }
                finally{saving=false;}
            }
            examDraftDeck=selected;examDraftQuestion=q;examDraftNote=q.ReviewNote;
            note.TextChanged+=(_,_)=>{q.ReviewNote=note.Text;examDraftNote=note.Text;examDraftDirty=true;examDraftRevision++;saveState.Text="正在保存…";examSaveTimer.Stop();examSaveTimer.Start();};
            var actions=Row();actions.Children.Add(Button("保存本题",()=>Save(q.Status),true));actions.Children.Add(Button("标记已复盘并下一题",async ()=>{await Save("remembered");if(examQuestionIndex<count-1){examQuestionIndex++;Draw();}},true));actions.Children.Add(Button("标记需要再练",()=>Save("learning")));actions.Children.Add(Button("清除复盘标记",()=>Save("unseen")));card.Children.Add(actions);host.Child=Card(card,new Thickness(22));
            pageButtons.Children.Clear();pageButtons.Children.Add(Button("← 上一题",async ()=>{await FlushExamDraft();examQuestionIndex=Math.Max(0,examQuestionIndex-1);Draw();}));pageButtons.Children.Add(Text($"{examQuestionIndex+1} / {count}",13));pageButtons.Children.Add(Button("下一题 →",async ()=>{await FlushExamDraft();examQuestionIndex=Math.Min(count-1,examQuestionIndex+1);Draw();}));
        }
        picker.SelectionChanged+=(_,_)=>_=Run(async ()=>{await FlushExamDraft();examQuestionIndex=0;Draw();});Draw();FillScroll(root);
    }

    private async Task FlushExamDraft()
    {
        examSaveTimer.Stop();await PersistExamDraft();
    }

    private async Task PersistExamDraft()
    {
        if(!examDraftDirty||examDraftDeck is null||examDraftQuestion is null)return;
        await examSaveLock.WaitAsync();
        try
        {
            if(!examDraftDirty||examDraftDeck is null||examDraftQuestion is null)return;
            var deck=examDraftDeck;int revision=examDraftRevision;
            examDraftQuestion.ReviewNote=examDraftNote;
            var saved=await api.Save("interviews",CloneDocument(deck));deck.UpdatedAt=saved.UpdatedAt;
            if(ReferenceEquals(deck,examDraftDeck)&&revision==examDraftRevision)
            {
                examDraftDirty=false;
                if(examSaveStatus is not null)examSaveStatus.Text="已自动保存 · "+DateTime.Now.ToString("HH:mm:ss");
            }
        }
        finally{examSaveLock.Release();}
    }

    private async Task ImportExamQuestions()
    {
        var picker=new FileOpenPicker();foreach(string extension in new[]{".md",".markdown",".txt",".docx",".pdf"})picker.FileTypeFilter.Add(extension);InitPicker(picker);
        var file=await picker.PickSingleFileAsync();if(file is not null)await ImportInterviewFile(file.Path,"exam");
    }
}

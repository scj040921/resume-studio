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
    private TextBox? examReviewInput;
    private readonly DispatcherTimer examSaveTimer=new(){Interval=TimeSpan.FromMilliseconds(650)};
    private readonly SemaphoreSlim examSaveLock=new(1,1);

    private async Task ShowExamReviews()
    {
        int generation=navigationRevision;
        Heading("笔试题复盘","导入笔试题后逐题记录错因、知识缺口和下一步改进。内容自动保存到本机。");
        var all=await api.Get<List<InterviewDeck>>("interviews");if(!PageIsCurrent("exams",generation))return;
        var decks=all.Where(d=>d.StudyType=="exam").OrderBy(d=>d.Title).ToList();
        var root=Stack(14);var toolbar=Row();toolbar.VerticalAlignment=VerticalAlignment.Top;
        toolbar.Children.Add(ToolbarField("",Button("＋ 导入笔试题文件",ImportExamQuestions,true)));
        var selected=decks.FirstOrDefault(d=>d.Id==examDeckId)??decks.FirstOrDefault();
        var picker=new ComboBox {MinWidth=220,MaxWidth=360,Tag="exam-deck"};
        foreach(var deck in decks){var item=new ComboBoxItem {Content=$"{deck.Title} · {deck.Questions.Count} 题",Tag=deck};picker.Items.Add(item);if(deck==selected)picker.SelectedItem=item;}
        toolbar.Children.Add(ToolbarField("复盘题库",picker));
        var count=selected?.Questions.Count??0;var done=selected?.Questions.Count(q=>q.Status=="remembered")??0;var pending=selected?.Questions.Count(q=>q.Status!="remembered")??0;
        var stats=Text($"共 {count} 题 · 已复盘 {done} · 待复盘 {pending}",13,true);stats.Tag="exam-stats";
        var statsCell=new Grid {MinHeight=40};stats.VerticalAlignment=VerticalAlignment.Center;statsCell.Children.Add(stats);
        toolbar.Children.Add(ToolbarField("",statsCell));root.Children.Add(toolbar);
        var remove=Button("删除复盘题库",async ()=>{if(selected is not null)await DeleteExamReview(selected);});remove.IsEnabled=selected is not null;toolbar.Children.Add(ToolbarField("",remove));
        var host=new Border {MinHeight=440,MaxWidth=980,HorizontalAlignment=HorizontalAlignment.Stretch};root.Children.Add(host);
        var pageButtons=Row();pageButtons.HorizontalAlignment=HorizontalAlignment.Center;root.Children.Add(pageButtons);
        void Draw()
        {
            selected=(picker.SelectedItem as ComboBoxItem)?.Tag as InterviewDeck;examDeckId=selected?.Id??"";remove.IsEnabled=selected is not null;
            count=selected?.Questions.Count??0;done=selected?.Questions.Count(q=>q.Status=="remembered")??0;pending=count-done;
            stats.Text=$"共 {count} 题 · 已复盘 {done} · 待复盘 {pending}";
            if(selected is null||count==0){host.Child=Card(Text("导入 Markdown、TXT、DOCX 或 PDF 笔试题，按标题拆分后逐题复盘。",16));pageButtons.Children.Clear();return;}
            examQuestionIndex=Math.Clamp(examQuestionIndex,0,count-1);var q=selected.Questions[examQuestionIndex];
            var card=Stack(14);card.Children.Add(Text($"第 {examQuestionIndex+1} / {count} 题  ·  {(q.Status=="remembered"?"已复盘":q.Status=="learning"?"需要再练":"待复盘")}",12,true));
            var title=Text(q.Title,23,true);title.IsTextSelectionEnabled=true;card.Children.Add(title);
            // Enable multiline before assigning text; WinUI otherwise keeps only its first line.
            var prompt=new TextBox {Header="题目内容",IsReadOnly=true,AcceptsReturn=true,Text=q.Answer,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=210,IsSpellCheckEnabled=false,Tag="exam-question-content"};card.Children.Add(prompt);
            var note=new TextBox {Header="本题复盘",AcceptsReturn=true,Text=q.ReviewNote,PlaceholderText="记录作答过程、错因、遗漏知识、正确思路和下次改进……",TextWrapping=TextWrapping.Wrap,MinHeight=160,MaxLength=20000,Tag="exam-review-note"};card.Children.Add(note);
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
            examDraftDeck=selected;examDraftQuestion=q;examDraftNote=q.ReviewNote;examReviewInput=note;
            note.TextChanged+=(_,_)=>{if(!ReferenceEquals(examReviewInput,note)||!ReferenceEquals(examDraftQuestion,q)||examDraftNote==note.Text)return;q.ReviewNote=note.Text;examDraftNote=note.Text;examDraftDirty=true;examDraftRevision++;saveState.Text="正在保存…";examSaveTimer.Stop();examSaveTimer.Start();};
            var actions=Row();actions.Children.Add(Button("保存本题",()=>Save(q.Status),true));actions.Children.Add(Button("标记已复盘并下一题",async ()=>{await Save("remembered");if(examQuestionIndex<count-1){examQuestionIndex++;Draw();}},true));actions.Children.Add(Button("标记需要再练",()=>Save("learning")));actions.Children.Add(Button("清除复盘标记",()=>Save("unseen")));card.Children.Add(actions);host.Child=Card(card,new Thickness(22));
            pageButtons.Children.Clear();pageButtons.Children.Add(Button("← 上一题",async ()=>{await FlushExamDraft();examQuestionIndex=Math.Max(0,examQuestionIndex-1);Draw();}));var position=Text($"{examQuestionIndex+1} / {count}",13);position.Tag="exam-position";pageButtons.Children.Add(position);pageButtons.Children.Add(Button("下一题 →",async ()=>{await FlushExamDraft();examQuestionIndex=Math.Min(count-1,examQuestionIndex+1);Draw();}));
        }
        picker.SelectionChanged+=(_,_)=>_=Run(async ()=>{await FlushExamDraft();examQuestionIndex=0;Draw();});Draw();FillScroll(root);
    }

    private async Task DeleteExamReview(InterviewDeck deck)
    {
        if(!await Confirm("删除这份笔试复盘？",$"{deck.Title} · {deck.Questions.Count} 道题，题目及复盘记录将一并删除。此操作无法撤销。"))return;
        shell.IsHitTestVisible=false;
        try
        {
            await FlushExamDraft();await examSaveLock.WaitAsync();
            try
            {
                await api.Delete("interviews/"+deck.Id);
                // Invalidate queued autosaves before releasing the lock so they cannot
                // recreate a deleted deck or redraw its stale question after deletion.
                if(examDraftDeck?.Id==deck.Id){examSaveTimer.Stop();examDraftDirty=false;examDraftRevision++;examDraftDeck=null;examDraftQuestion=null;examDraftNote="";examSaveStatus=null;examReviewInput=null;}
                if(examDeckId==deck.Id){examDeckId="";examQuestionIndex=0;}
            }
            finally{examSaveLock.Release();}
            await Navigate("exams");Success("笔试复盘已删除",deck.Title);
        }
        finally{shell.IsHitTestVisible=true;}
    }

    private async Task FlushExamDraft()
    {
        // TextChanged may still be queued when the user immediately switches pages.
        if(currentPage=="exams"&&examReviewInput is not null&&examDraftQuestion is not null&&examDraftNote!=examReviewInput.Text){examDraftNote=examReviewInput.Text;examDraftQuestion.ReviewNote=examDraftNote;examDraftDirty=true;examDraftRevision++;}
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

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
        var selected=decks.FirstOrDefault(d=>d.Id==examDeckId)??decks.OrderByDescending(d=>DateTimeOffset.TryParse(d.UpdatedAt,out var at)?at:DateTimeOffset.MinValue).FirstOrDefault();
        int SavedIndex(InterviewDeck? deck)=>deck is null?0:Math.Max(0,deck.Questions.FindIndex(q=>q.Id==deck.LastQuestionId));
        examQuestionIndex=SavedIndex(selected);
        var picker=new ComboBox {MinWidth=220,MaxWidth=360,Tag="exam-deck"};
        foreach(var deck in decks){var item=new ComboBoxItem {Content=$"{deck.Title} · {deck.Questions.Count} 题",Tag=deck};picker.Items.Add(item);if(deck==selected)picker.SelectedItem=item;}
        toolbar.Children.Add(ToolbarField("复盘题库",picker));
        var count=selected?.Questions.Count??0;var done=selected?.Questions.Count(q=>q.Status=="remembered")??0;var pending=selected?.Questions.Count(q=>q.Status!="remembered")??0;
        var stats=Text($"共 {count} 题 · 已复盘 {done} · 待复盘 {pending}",13,true);stats.Tag="exam-stats";
        var statsCell=new Grid {MinHeight=40};stats.VerticalAlignment=VerticalAlignment.Center;statsCell.Children.Add(stats);
        toolbar.Children.Add(ToolbarField("",statsCell));root.Children.Add(toolbar);
        var remove=Button("删除复盘题库",async ()=>{if(selected is not null)await DeleteExamReview(selected);});remove.IsEnabled=selected is not null;toolbar.Children.Add(ToolbarField("",remove));
        var questionLabels=new List<string>();string labelDeckId="";bool updatingJump=false,moving=false,saving=false,switching=false;
        var jump=new ComboBox {ItemsSource=questionLabels,MinWidth=230,MaxWidth=370,PlaceholderText="跳转到题目",Tag="exam-question-picker"};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(jump,"跳转到题目");ToolTipService.SetToolTip(jump,"跳转到题目");
        var jumpRow=Row();var jumpLabel=Text("跳转题目",13,true);jumpLabel.VerticalAlignment=VerticalAlignment.Center;jumpRow.Children.Add(jumpLabel);jumpRow.Children.Add(jump);root.Children.Add(jumpRow);
        var host=new Border {MinHeight=440,MaxWidth=980,HorizontalAlignment=HorizontalAlignment.Stretch};root.Children.Add(host);
        var pageButtons=Row();pageButtons.HorizontalAlignment=HorizontalAlignment.Center;root.Children.Add(pageButtons);
        Button previous=null!,next=null!;var actionButtons=new List<Button>();
        var position=Text("0 / 0",13);position.Tag="exam-position";position.VerticalAlignment=VerticalAlignment.Center;
        previous=Button("← 上一题",()=>MoveTo(examQuestionIndex-1));next=Button("下一题 →",()=>MoveTo(examQuestionIndex+1));
        pageButtons.Children.Add(previous);pageButtons.Children.Add(position);pageButtons.Children.Add(next);
        bool Alive()=>PageIsCurrent("exams",generation);
        void UpdateControls(){bool enabled=Alive()&&!moving&&!saving&&!switching;previous.IsEnabled=enabled&&count>0&&examQuestionIndex>0;next.IsEnabled=enabled&&examQuestionIndex+1<count;jump.IsEnabled=enabled&&count>1;picker.IsEnabled=enabled;remove.IsEnabled=enabled&&selected is not null;foreach(var action in actionButtons)action.IsEnabled=enabled;}
        void SelectJump(int index){updatingJump=true;try{jump.SelectedIndex=index;}finally{updatingJump=false;}}
        async Task MoveTo(int target)
        {
            if(!Alive()||moving||saving||switching||selected is null||target<0||target>=count||target==examQuestionIndex){SelectJump(count>0?examQuestionIndex:-1);return;}
            moving=true;UpdateControls();
            try{await FlushExamDraft();if(!Alive())return;examQuestionIndex=target;Draw();}
            finally{moving=false;if(Alive()){SelectJump(count>0?examQuestionIndex:-1);UpdateControls();}}
        }
        void Draw()
        {
            if(!Alive())return;
            selected=(picker.SelectedItem as ComboBoxItem)?.Tag as InterviewDeck;examDeckId=selected?.Id??"";remove.IsEnabled=selected is not null;
            count=selected?.Questions.Count??0;done=selected?.Questions.Count(q=>q.Status=="remembered")??0;pending=count-done;
            stats.Text=$"共 {count} 题 · 已复盘 {done} · 待复盘 {pending}";
            if(selected is null||count==0){host.Child=Card(Text("导入 Markdown、TXT、DOCX 或 PDF 笔试题，按标题拆分后逐题复盘。",16));jumpRow.Visibility=Visibility.Collapsed;pageButtons.Visibility=Visibility.Collapsed;updatingJump=true;try{questionLabels=[];jump.ItemsSource=questionLabels;jump.SelectedIndex=-1;labelDeckId="";}finally{updatingJump=false;}actionButtons.Clear();UpdateControls();return;}
            var currentDeck=selected;examQuestionIndex=Math.Clamp(examQuestionIndex,0,count-1);var q=currentDeck.Questions[examQuestionIndex];
            string QuestionLabel(int index){var question=currentDeck.Questions[index];string status=question.Status switch {"remembered"=>"已复盘","learning"=>"需要再练",_=>"待复盘"};return $"第 {index+1} 题 · {status} · {question.Title.Replace('\r',' ').Replace('\n',' ')}";}
            updatingJump=true;
            try{if(labelDeckId!=currentDeck.Id||questionLabels.Count!=count){questionLabels=Enumerable.Range(0,count).Select(QuestionLabel).ToList();jump.ItemsSource=questionLabels;labelDeckId=currentDeck.Id;}else if(questionLabels[examQuestionIndex]!=QuestionLabel(examQuestionIndex)){questionLabels[examQuestionIndex]=QuestionLabel(examQuestionIndex);jump.ItemsSource=null;jump.ItemsSource=questionLabels;}jump.SelectedIndex=examQuestionIndex;}
            finally{updatingJump=false;}
            jumpRow.Visibility=Visibility.Visible;pageButtons.Visibility=Visibility.Visible;position.Text=$"{examQuestionIndex+1} / {count}";
            var card=Stack(14);card.Children.Add(Text($"第 {examQuestionIndex+1} / {count} 题  ·  {(q.Status=="remembered"?"已复盘":q.Status=="learning"?"需要再练":"待复盘")}",12,true));
            var presentation=InterviewImport.ExamPresentation(q);
            if(presentation.Section.Length>0){var section=Text(presentation.Section,23,true);section.IsTextSelectionEnabled=true;section.Tag="exam-section-title";card.Children.Add(section);}
            var title=Text(presentation.Title,presentation.Section.Length>0?18:23,true);title.IsTextSelectionEnabled=true;title.Tag="exam-question-title";card.Children.Add(title);
            // Enable multiline before assigning text; WinUI otherwise keeps only its first line.
            if(!string.IsNullOrWhiteSpace(presentation.Content)){var prompt=new TextBox {Header="题目内容",IsReadOnly=true,AcceptsReturn=true,Text=presentation.Content,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=210,IsSpellCheckEnabled=false,Tag="exam-question-content"};card.Children.Add(prompt);}
            var note=new TextBox {Header="本题复盘",AcceptsReturn=true,Text=q.ReviewNote,PlaceholderText="记录作答过程、错因、遗漏知识、正确思路和下次改进……",TextWrapping=TextWrapping.Wrap,MinHeight=160,MaxLength=20000,Tag="exam-review-note"};card.Children.Add(note);
            var saveState=Text("自动保存已开启",11);examSaveStatus=saveState;card.Children.Add(saveState);
            async Task Save(string status)
            {
                if(saving||moving||switching||!Alive())return;saving=true;UpdateControls();
                try
                {
                    examSaveTimer.Stop();q.ReviewNote=note.Text;q.Status=status;currentDeck.LastQuestionId=q.Id;
                    examDraftDeck=currentDeck;examDraftQuestion=q;examDraftNote=note.Text;examDraftDirty=true;examDraftRevision++;
                    int revision=examDraftRevision;await PersistExamDraft();
                    if(revision==examDraftRevision&&Alive()){saveState.Text="已保存 · "+DateTime.Now.ToString("HH:mm:ss");Draw();}
                }
                finally{saving=false;if(Alive())UpdateControls();}
            }
            examDraftDeck=currentDeck;examDraftQuestion=q;examDraftNote=q.ReviewNote;examReviewInput=note;
            if(currentDeck.LastQuestionId!=q.Id){currentDeck.LastQuestionId=q.Id;examDraftDirty=true;examDraftRevision++;examSaveTimer.Stop();examSaveTimer.Start();}
            note.TextChanged+=(_,_)=>{if(!ReferenceEquals(examReviewInput,note)||!ReferenceEquals(examDraftQuestion,q)||examDraftNote==note.Text)return;q.ReviewNote=note.Text;examDraftNote=note.Text;examDraftDirty=true;examDraftRevision++;saveState.Text="正在保存…";examSaveTimer.Stop();examSaveTimer.Start();};
            var actions=Row();actionButtons.Clear();actionButtons.Add(Button("保存本题",()=>Save(q.Status),true));actionButtons.Add(Button("标记已复盘并下一题",async ()=>{await Save("remembered");if(Alive()&&examQuestionIndex<count-1)await MoveTo(examQuestionIndex+1);},true));actionButtons.Add(Button("标记需要再练",()=>Save("learning")));actionButtons.Add(Button("清除复盘标记",()=>Save("unseen")));foreach(var action in actionButtons)actions.Children.Add(action);card.Children.Add(actions);host.Child=Card(card,new Thickness(22));UpdateControls();
        }
        jump.SelectionChanged+=(_,_)=>{if(!updatingJump&&Alive())_=Run(()=>MoveTo(jump.SelectedIndex));};
        picker.SelectionChanged+=(_,_)=>_=Run(async ()=>{
            if(!Alive()||switching)return;switching=true;UpdateControls();
            try{await FlushExamDraft();if(!Alive())return;selected=(picker.SelectedItem as ComboBoxItem)?.Tag as InterviewDeck;examQuestionIndex=SavedIndex(selected);}
            catch{picker.SelectedItem=picker.Items.Cast<ComboBoxItem>().FirstOrDefault(i=>ReferenceEquals(i.Tag,selected));throw;}
            finally{switching=false;if(Alive())Draw();}
        });
        Draw();FillScroll(root);
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
        // TextChanged may still be queued or the user may type again while a save is in flight.
        for(;;){
            if(currentPage=="exams"&&examReviewInput is not null&&examDraftQuestion is not null&&examDraftNote!=examReviewInput.Text){examDraftNote=examReviewInput.Text;examDraftQuestion.ReviewNote=examDraftNote;examDraftDirty=true;examDraftRevision++;}
            examSaveTimer.Stop();if(!examDraftDirty||examDraftDeck is null||examDraftQuestion is null)return;
            await PersistExamDraft();
            if(!examDraftDirty&&(currentPage!="exams"||examReviewInput is null||examDraftQuestion is null||examDraftNote==examReviewInput.Text))return;
        }
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

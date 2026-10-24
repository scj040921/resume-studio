using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ResumeStudio.Services;
using Windows.Storage.Pickers;
using Windows.System;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private string interviewDeckId="",interviewFilter="all";
    private int interviewIndex;
    private readonly SemaphoreSlim interviewSaveLock=new(1,1);
    private async Task MarkInterview(InterviewDeck deck,InterviewQuestion question,string status)
    {
        await interviewSaveLock.WaitAsync();var previous=CloneDocument(question);string before=question.Status,at=question.ReviewedAt;
        try {InterviewProgress.Review(question,status,DateTimeOffset.UtcNow);deck.LastQuestionId=question.Id;var saved=await api.Save("interviews",deck);deck.UpdatedAt=saved.UpdatedAt;}
        catch {question.Status=before;question.ReviewedAt=at;question.ReviewCount=previous.ReviewCount;question.IntervalDays=previous.IntervalDays;question.NextReviewAt=previous.NextReviewAt;throw;}
        finally {interviewSaveLock.Release();}
    }
    private async Task ShowInterviews()
    {
        int generation=navigationRevision;
        Heading("面试题背诵","一次专注一道题，记录掌握情况；题库和进度自动保存在本机。");
        var decks=await api.Get<List<InterviewDeck>>("interviews");if(!PageIsCurrent("interviews",generation))return;
        decks=decks.Where(d=>d.StudyType!="exam").OrderBy(d=>d.Title).ToList();
        body.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var tools=Stack(9);var toolbar=Row();toolbar.VerticalAlignment=VerticalAlignment.Top;
        toolbar.Children.Add(ToolbarField("",Button("＋ 导入题目文档",ImportInterviews,true)));
        var select=new ComboBox {ItemsSource=decks,MinWidth=200,MaxWidth=360,Tag="interview-deck"};
        var filter=new ComboBox {ItemsSource=new[]{"全部题目","未掌握","已记住","未标记","今日待复习"},MinWidth=140,Tag="interview-filter"};
        string[] filters=["all","learning","remembered","unseen","due"];filter.SelectedIndex=Math.Max(0,Array.IndexOf(filters,interviewFilter));
        toolbar.Children.Add(ToolbarField("题库",select));toolbar.Children.Add(ToolbarField("练习范围",filter));
        InterviewDeck? deck=decks.FirstOrDefault(d=>d.Id==interviewDeckId)??decks.FirstOrDefault();select.SelectedItem=deck;
        var remove=Button("删除题库",async ()=> {var selected=deck;if(selected is null)return;if(await Confirm("删除这份题库？",selected.Title+"，题目及背诵进度将一并删除。")){await api.Delete("interviews/"+selected.Id);interviewDeckId="";interviewIndex=0;await Navigate("interviews");}});remove.IsEnabled=deck is not null;toolbar.Children.Add(ToolbarField("",remove));
        tools.Children.Add(toolbar);var stats=Text("",13,true);stats.Tag="interview-stats";tools.Children.Add(stats);tools.Margin=new Thickness(0,0,0,12);body.Children.Add(tools);
        var host=new Border {MaxWidth=1040,HorizontalAlignment=HorizontalAlignment.Stretch,Tag="interview-card-host"};Grid.SetRow(host,1);body.Children.Add(host);
        var footer=Stack(8);footer.Margin=new Thickness(0,12,0,0);
        var arrows=Row();arrows.HorizontalAlignment=HorizontalAlignment.Center;
        List<InterviewQuestion> visible=[];bool saving=false;
        Button previous=null!,next=null!;var position=Text("",13);position.VerticalAlignment=VerticalAlignment.Center;position.Tag="interview-position";
        previous=Button("← 上一题",()=>{Move(-1);return Task.CompletedTask;});
        next=Button("下一题 →",()=>{Move(1);return Task.CompletedTask;});arrows.Children.Add(previous);arrows.Children.Add(position);arrows.Children.Add(next);footer.Children.Add(arrows);
        var tip=Text("使用左右方向键或按钮切题；滚动鼠标滚轮可阅读答案。标记后自动进入下一题。",11);tip.HorizontalAlignment=HorizontalAlignment.Center;footer.Children.Add(tip);Grid.SetRow(footer,2);body.Children.Add(footer);
        toolbar.Children.Add(ToolbarField("",Button("随机抽题",()=>{if(visible.Count>0){interviewIndex=Random.Shared.Next(visible.Count);Draw();_=SaveInterviewBookmark(deck!,visible[interviewIndex].Id);}return Task.CompletedTask;})));
        bool Alive()=>PageIsCurrent("interviews",generation);
        bool Move(int delta){if(!Alive()||saving||visible.Count==0)return false;int target=Math.Clamp(interviewIndex+delta,0,visible.Count-1);if(target==interviewIndex)return false;interviewIndex=target;Draw();_=SaveInterviewBookmark(deck!,visible[interviewIndex].Id);return true;}
        void Draw()
        {
            if(!Alive())return;visible=deck is null?[]:InterviewProgress.Filter(deck,interviewFilter).ToList();interviewIndex=Math.Clamp(interviewIndex,0,Math.Max(0,visible.Count-1));
            var totals=InterviewProgress.Count(deck?.Questions??[]);stats.Text=$"共 {totals.Total} 题  ·  已记住 {totals.Remembered}  ·  未掌握 {totals.Learning}  ·  未标记 {totals.Unseen}";
            previous.IsEnabled=!saving&&interviewIndex>0;next.IsEnabled=!saving&&interviewIndex+1<visible.Count;select.IsEnabled=!saving;filter.IsEnabled=!saving;remove.IsEnabled=!saving&&deck is not null;
            position.Text=visible.Count==0?"0 / 0":$"第 {interviewIndex+1} / {visible.Count} 题";
            if(visible.Count==0){var empty=Stack(12);empty.VerticalAlignment=VerticalAlignment.Center;empty.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(Text(deck is null?"导入你的第一份面试题文档":"这个练习范围暂时没有题目",22,true));empty.Children.Add(Text(deck is null?"支持 Markdown、TXT、DOCX 和文字型 PDF，按小标题生成题目卡片。":"可以切换到“全部题目”，重新复习或调整掌握状态。",14));host.Child=Card(empty);return;}
            var q=visible[interviewIndex];var card=new Grid {RowSpacing=14};card.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});card.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});card.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            var heading=Stack(8);heading.Children.Add(Text(InterviewProgress.Label(q.Status)+" · "+deck!.Title,12));var title=Text(q.Title,24,true);title.IsTextSelectionEnabled=true;heading.Children.Add(title);card.Children.Add(heading);
            var answer=Stack(12);var toggle=new ToggleSwitch {Header="显示答案",IsOn=true};answer.Children.Add(toggle);
            var text=Text(string.IsNullOrWhiteSpace(q.Answer)?"这道题没有正文，可以先口述你的答案。":q.Answer,15);text.IsTextSelectionEnabled=true;text.LineHeight=25;answer.Children.Add(text);
            toggle.Toggled+=(_,_)=>text.Visibility=toggle.IsOn?Visibility.Visible:Visibility.Collapsed;
            var answerScroll=new ScrollViewer {Content=answer,HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalContentAlignment=HorizontalAlignment.Stretch};Grid.SetRow(answerScroll,1);card.Children.Add(answerScroll);
            var verdict=new Grid {ColumnSpacing=14};verdict.ColumnDefinitions.Add(new ColumnDefinition());verdict.ColumnDefinitions.Add(new ColumnDefinition());
            async Task Review(string status){if(saving||!Alive())return;saving=true;Draw();try {await MarkInterview(deck!,q,status);if(!Alive())return;var remains=InterviewProgress.Filter(deck!,interviewFilter).ToList();if(remains.Contains(q))interviewIndex=Math.Min(interviewIndex+1,Math.Max(0,remains.Count-1));}finally {saving=false;if(Alive())Draw();}}
            var remembered=Button("✓ 已记住",()=>Review("remembered"),true);remembered.Tag="interview-remembered";remembered.HorizontalAlignment=HorizontalAlignment.Stretch;remembered.IsEnabled=!saving;
            var learning=Button("未掌握",()=>Review("learning"));learning.Tag="interview-learning";learning.HorizontalAlignment=HorizontalAlignment.Stretch;learning.IsEnabled=!saving;Grid.SetColumn(learning,1);verdict.Children.Add(remembered);verdict.Children.Add(learning);Grid.SetRow(verdict,2);card.Children.Add(verdict);
            host.Child=Card(card,new Thickness(24));
        }
        select.SelectionChanged+=(_,_)=>{if(!Alive()||saving)return;deck=select.SelectedItem as InterviewDeck;interviewDeckId=deck?.Id??"";interviewIndex=0;Draw();};
        filter.SelectionChanged+=(_,_)=>{if(!Alive()||saving)return;interviewFilter=filters[Math.Max(0,filter.SelectedIndex)];interviewIndex=0;Draw();};
        foreach(var key in new[]{VirtualKey.Left,VirtualKey.Right}){var shortcut=new KeyboardAccelerator {Key=key};shortcut.Invoked+=(_,e)=>{e.Handled=true;Move(key==VirtualKey.Left?-1:1);};body.KeyboardAccelerators.Add(shortcut);}
        interviewDeckId=deck?.Id??"";if(deck is not null)interviewIndex=Math.Max(0,InterviewProgress.Filter(deck,interviewFilter).ToList().FindIndex(q=>q.Id==deck.LastQuestionId));Draw();
    }
    private async Task SaveInterviewBookmark(InterviewDeck deck,string id){
        await interviewSaveLock.WaitAsync();try{deck.LastQuestionId=id;var saved=await api.Save("interviews",deck);deck.UpdatedAt=saved.UpdatedAt;}catch(Exception ex){Feedback("断点保存未完成",ex.Message);}finally{interviewSaveLock.Release();}
    }
    private async Task ImportInterviews()
    {
        var picker=new FileOpenPicker();foreach(string extension in new[]{".md",".markdown",".txt",".docx",".pdf"})picker.FileTypeFilter.Add(extension);InitPicker(picker);
        var file=await picker.PickSingleFileAsync();if(file is not null)await ImportInterviewFile(file.Path,"interview");
    }
    private async Task ImportInterviewFile(string path,string studyType="interview")
    {
        var document=await Task.Run(()=>InterviewImport.Read(path));var content=Stack(12);
        var name=new TextBox {Header=studyType=="exam"?"复盘名称":"题库名称",Text=Path.GetFileNameWithoutExtension(document.SourceName),MaxLength=200};content.Children.Add(name);
        int[] levels=[0,..document.HeadingLevels];var mode=new ComboBox {Header="拆分方式",ItemsSource=levels.Select(l=>l==0?"自动识别小标题":$"按 {l} 级标题拆分").ToList(),SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};content.Children.Add(mode);
        var summary=Text("",12);content.Children.Add(summary);var titles=new ListView {Height=160,SelectionMode=ListViewSelectionMode.Single,Tag="interview-import-titles"};content.Children.Add(titles);
        var excerpt=new TextBox {Header="选中题目的正文预览",IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=150};content.Children.Add(excerpt);
        content.Children.Add(Text("只保存提取的题目文字；原文档不会复制进软件。题库和掌握进度仅保存在本机。",11));
        var dialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title=studyType=="exam"?"导入笔试题":"导入面试题",Content=new ScrollViewer {Content=content,MaxHeight=570},PrimaryButtonText=studyType=="exam"?"创建复盘":"导入题库",CloseButtonText="取消",DefaultButton=ContentDialogButton.Primary};List<InterviewQuestion> questions=[];
        void Refresh(){try {questions=InterviewImport.Split(document,levels[Math.Max(0,mode.SelectedIndex)]);titles.ItemsSource=questions.Select(q=>q.Title).ToList();titles.SelectedIndex=0;summary.Text=$"识别出 {questions.Count} 道题 · {document.Rule} · "+(document.HeadingLevels.Length==0?"未发现小标题，将全文作为一张卡片。":$"当前按 { (levels[mode.SelectedIndex]==0?InterviewImport.AutoLevel(document):levels[mode.SelectedIndex]) } 级标题拆分。请检查题目是否对应。" );dialog.IsPrimaryButtonEnabled=!string.IsNullOrWhiteSpace(name.Text);}catch(IOException ex){questions=[];titles.ItemsSource=null;excerpt.Text="";summary.Text=ex.Message;dialog.IsPrimaryButtonEnabled=false;}}
        titles.SelectionChanged+=(_,_)=>excerpt.Text=titles.SelectedIndex>=0&&titles.SelectedIndex<questions.Count?questions[titles.SelectedIndex].Answer:"";
        mode.SelectionChanged+=(_,_)=>Refresh();name.TextChanged+=(_,_)=>dialog.IsPrimaryButtonEnabled=questions.Count>0&&!string.IsNullOrWhiteSpace(name.Text);Refresh();
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        var saved=await api.Save("interviews",new InterviewDeck {Title=name.Text.Trim(),SourceName=document.SourceName,StudyType=studyType,Questions=questions});
        if(studyType=="exam"){examDeckId=saved.Id;examQuestionIndex=0;}
        else {interviewDeckId=saved.Id;interviewFilter="all";interviewIndex=0;}
        await Navigate(studyType=="exam"?"exams":"interviews");Success(studyType=="exam"?"笔试复盘已创建":"题库已导入",$"{saved.Title} · {saved.Questions.Count} 道题。进度会自动保存到本机。");
    }
}

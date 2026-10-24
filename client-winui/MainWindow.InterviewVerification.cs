using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task InterviewFeatureTest()
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"interview-test");
        string fixtures=Environment.GetEnvironmentVariable("RESUMESTUDIO_INTERVIEW_FIXTURES")??throw new IOException("Import fixtures missing");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
        async Task Click(string label){var button=FeatureButton(body,label)??throw new Exception("Action missing: "+label);((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();await Task.Delay(180);}
        async Task WaitForExamLayout(){for(int attempt=0;attempt<100;attempt++){body.UpdateLayout();if(InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="exam-deck") is not null&&FeatureButton(body,"删除复盘题库") is not null)return;await Task.Delay(50);}throw new Exception("Exam page did not finish native layout");}
        try {
            await api.Save("favorites",new Job {Id="compact-neighbor",Title="嵌入式测试工程师",Company="测试公司",Location="广州",Source="BOSS直聘",Url="https://example.com/neighbor"});
            await api.Save("favorites",new Job {Id="compact",Title="嵌入式软件工程师",Company="公司名称\n测试公司",Location="深圳",Source="BOSS直聘",Salary="15–20K",Url="https://example.com/compact",Description=string.Join("\n",Enumerable.Repeat("工作职责与任职资格：开发、调试、测试及文档。",30))});
            foreach(string style in new[]{"classic","haru"}){
                preferences.InterfaceStyle=style;preferences.Theme="light";BuildShell();await Navigate("favorites");await Task.Delay(220);
                // Virtualized containers can be realized after navigation returns, especially
                // on a busy CI desktop. Wait for layout instead of relying on a fixed delay.
                var list=visibleJobList??throw new Exception("Favorite list missing");
                FrameworkElement? first=null,second=null;
                for(int attempt=0;attempt<50;attempt++){
                    list.UpdateLayout();first=list.ContainerFromIndex(0) as FrameworkElement;second=list.ContainerFromIndex(1) as FrameworkElement;
                    if(first is not null&&second is not null&&first.ActualHeight>0&&second.ActualHeight>0)break;
                    await Task.Delay(100);
                }
                if(first is null||second is null)throw new Exception(style+": favorite containers were not realized within five seconds");
                var card=InteractionElement<Expander>(first,e=>e.Tag?.ToString()=="favorite-card")??throw new Exception("Compact favorite card missing");
                double Top(FrameworkElement element)=>element.TransformToVisual(visibleJobList).TransformPoint(new Windows.Foundation.Point()).Y;
                double secondTop=Top(second);
                Check(first.ActualHeight<190&&Math.Abs(secondTop-Top(first)-first.ActualHeight)<=16,style+": collapsed list containers have no reserved blank height");
                double compact=card.ActualHeight;
                Check(!card.IsExpanded&&compact<190,style+": favorite card starts compact without its long description");
                Check(((StackPanel)card.Header).Children.OfType<TextBlock>().Any(t=>t.Text=="测试公司 · 深圳"||t.Text=="测试公司 · 广州"),style+": compact summary folds line breaks and removes captured company labels");
                await SaveNativePreview(Path.Combine(output,"favorites-compact-"+style+".png"));
                card.IsExpanded=true;await Task.Delay(250);
                Check(card.ActualHeight>compact+100&&FeatureButton(body,"编辑收藏") is not null,style+": expanding reveals the full detail and actions");
                Check(Top(second)>secondTop+100&&Math.Abs(Top(second)-Top(first)-first.ActualHeight)<=16,style+": expanding pushes the neighboring card down without overlap");
                card.IsExpanded=false;await Task.Delay(250);Check(card.ActualHeight<190,style+": favorite can be collapsed again");
                Check(Math.Abs(Top(second)-secondTop)<=2,style+": collapsing restores the compact neighbor position");
            }
            string? userDocument=Environment.GetEnvironmentVariable("RESUMESTUDIO_INTERVIEW_DOCUMENT");
            var imports=new[]{"md","txt","docx","blank-headings.docx","numbered.docx","pdf","chinese.pdf"}.ToList();if(!string.IsNullOrWhiteSpace(userDocument))imports.Insert(0,"user-document");
            foreach(string extension in imports){
                string file=extension=="user-document"?userDocument!:Path.Combine(fixtures,extension=="chinese.pdf"?"interview-chinese.pdf":extension=="blank-headings.docx"?"interview-blank-headings.docx":extension=="numbered.docx"?"interview-numbered.docx":"interview."+extension);
                var import=ImportInterviewFile(file);var dialog=await FeatureDialog("导入面试题");
                ListView? list=null;
                for(int attempt=0;attempt<50;attempt++){
                    dialog.UpdateLayout();list=InteractionElement<ListView>(dialog,l=>l.Tag?.ToString()=="interview-import-titles");
                    if(list is not null&&FindTextField(dialog,"题库名称") is not null)break;
                    await Task.Delay(100);
                }
                if(list is null)throw new Exception(extension+": import preview did not finish loading");
                if(extension=="chinese.pdf")(FindTextField(dialog,"题库名称")??throw new Exception("Deck title field missing")).Text="嵌入式基础题";
                int expected=extension=="user-document"?82:2;
                Check(list.Items.Count==expected&&dialog.IsPrimaryButtonEnabled,extension+": native import preview exposes all expected questions");
                await FeatureClick(dialog,"导入题库");await import;await Task.Delay(180);
                Check(currentPage=="interviews"&&InterviewProgress.Count((await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==interviewDeckId).Questions).Total==expected,extension+": confirmed import saves a complete deck through Java");
            }
            Check((await api.Get<List<InterviewDeck>>("interviews")).Count==imports.Count,"importing another document preserves previous decks");
            await Click("✓ 已记住");var deck=(await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==interviewDeckId);
            Check(deck.Questions[0].Status=="remembered"&&interviewIndex==1,"remembered verdict persists and advances to the second card");
            await Click("未掌握");deck=(await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==interviewDeckId);
            Check(InterviewProgress.Count(deck.Questions)==new InterviewCounts(2,1,1,0),"both buttons produce separate persisted statistics");
            await Click("← 上一题");Check(interviewIndex==0,"mouse previous action returns to the first card");
            await Click("未掌握");deck=(await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==interviewDeckId);
            Check(InterviewProgress.Count(deck.Questions)==new InterviewCounts(2,0,2,0),"changing a remembered card to unmastered replaces rather than duplicates its count");
            var filter=InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="interview-filter")!;filter.SelectedIndex=2;await Task.Delay(100);
            Check(InteractionElement<TextBlock>(body,t=>t.Tag?.ToString()=="interview-position")!.Text=="0 / 0","empty remembered filter has no stale card");
            filter.SelectedIndex=1;await Task.Delay(100);
            Check(FeatureButton(body,"✓ 已记住") is not null,"unmastered practice filter shows the relevant question");
            await SaveNativePreview(Path.Combine(output,"interview-study.png"));
            string savedId=interviewDeckId;await Navigate("templates");await Navigate("interviews");await Task.Delay(120);
            Check(interviewDeckId==savedId&&InteractionElement<TextBlock>(body,t=>t.Tag?.ToString()=="interview-stats")!.Text.Contains("未掌握 2"),"returning to study restores saved verdicts and statistics");
            var answer=InteractionElement<ToggleSwitch>(body,t=>t.Header?.ToString()=="显示答案")!;answer.IsOn=false;await Task.Delay(100);answer.IsOn=true;
            Check(body.KeyboardAccelerators.Count==2,"question page exposes both direction shortcuts");
            string? actualExam=Environment.GetEnvironmentVariable("RESUMESTUDIO_EXAM_DOCUMENT");
            string examFile=string.IsNullOrWhiteSpace(actualExam)?Path.Combine(fixtures,"exam-mixed-numbering.docx"):actualExam;
            int examTotal=string.IsNullOrWhiteSpace(actualExam)?11:29;
            var oldExam=await api.Save("interviews",new InterviewDeck {Title=Path.GetFileNameWithoutExtension(examFile),StudyType="exam",Questions=Enumerable.Range(1,4).Select(i=>new InterviewQuestion {Title="旧题 "+i,ReviewNote="旧复盘记录"}).ToList()});
            examDeckId=oldExam.Id;await Navigate("exams");await WaitForExamLayout();
            Check(InteractionElement<TextBlock>(body,t=>t.Tag?.ToString()=="exam-position")!.Text=="1 / 4","exam regression begins with a selected four-card deck");
            var examImport=ImportInterviewFile(examFile,"exam");var examDialog=await FeatureDialog("导入笔试题");
            var examTitles=InteractionElement<ListView>(examDialog,l=>l.Tag?.ToString()=="interview-import-titles")!;
            Check(examTitles.Items.Count==examTotal&&examDialog.IsPrimaryButtonEnabled,"exam preview identifies every expected subquestion");
            await FeatureClick(examDialog,"创建复盘");await examImport;await WaitForExamLayout();
            var newExam=(await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==examDeckId);
            Check(newExam.Id!=oldExam.Id&&newExam.Questions.Count==examTotal&&interviewDeckId==savedId,"creating an exam selects the new complete deck without changing the interview deck");
            var examPicker=InteractionElement<ComboBox>(body,c=>c.Tag?.ToString()=="exam-deck")!;
            Check(((examPicker.SelectedItem as ComboBoxItem)?.Tag as InterviewDeck)?.Id==newExam.Id&&InteractionElement<TextBlock>(body,t=>t.Tag?.ToString()=="exam-position")!.Text==$"1 / {examTotal}","visible exam selector and card counter match the imported deck instead of the old four-card deck");
            Check(examPicker.Items.Cast<ComboBoxItem>().Any(i=>i.Content?.ToString()==$"{oldExam.Title} · 4 题")&&examPicker.Items.Cast<ComboBoxItem>().Any(i=>i.Content?.ToString()==$"{newExam.Title} · {examTotal} 题"),"same-name exam decks display their separate question counts");
            Check(InteractionElement<TextBox>(body,t=>t.Tag?.ToString()=="exam-question-content")!.Text.ReplaceLineEndings("\n")==newExam.Questions[0].Answer.ReplaceLineEndings("\n"),"visible exam content retains every line of the imported question and choices");
            await SaveNativePreview(Path.Combine(output,"exam-import-active.png"));
            await Click("下一题 →");Check(InteractionElement<TextBlock>(body,t=>t.Tag?.ToString()=="exam-position")!.Text==$"2 / {examTotal}","newly imported exam advances within its full card set");
            await Navigate("templates");await Navigate("exams");await WaitForExamLayout();Check(examDeckId==newExam.Id,"returning to exam reviews keeps the newly imported deck selected");
            var multiNote=InteractionElement<TextBox>(body,t=>t.Tag?.ToString()=="exam-review-note")!;multiNote.Text="复盘第一行\n复盘第二行";await FlushExamDraft();
            Check((await api.Get<List<InterviewDeck>>("interviews")).Single(d=>d.Id==newExam.Id).Questions.Any(q=>q.ReviewNote.ReplaceLineEndings("\n")=="复盘第一行\n复盘第二行"),"flushing an edit captures current text even before the deferred TextChanged event");
            await Navigate("templates");await Navigate("exams");await WaitForExamLayout();
            Check(InteractionElement<TextBox>(body,t=>t.Tag?.ToString()=="exam-review-note")!.Text.ReplaceLineEndings("\n")=="复盘第一行\n复盘第二行","multiline review notes remain complete after saving and reopening the exam page");
            Check(FeatureButton(body,"删除复盘题库")?.IsEnabled==true,"selected exam exposes an enabled delete action");
            var deleting=DeleteExamReview(newExam);var confirm=await FeatureDialog("删除这份笔试复盘？");await FeatureClick(confirm,"取消");await deleting;
            Check((await api.Get<List<InterviewDeck>>("interviews")).Any(d=>d.Id==newExam.Id)&&examDeckId==newExam.Id,"cancelling deletion preserves the selected exam and its questions");
            var examNote=InteractionElement<TextBox>(body,t=>t.Tag?.ToString()=="exam-review-note")!;examNote.Text="删除前仍在等待自动保存的复盘内容。";
            deleting=DeleteExamReview(newExam);confirm=await FeatureDialog("删除这份笔试复盘？");await FeatureClick(confirm,"确认");await deleting;await WaitForExamLayout();await Task.Delay(850);await FlushExamDraft();
            var remainingExams=(await api.Get<List<InterviewDeck>>("interviews")).Where(d=>d.StudyType=="exam").ToList();
            Check(remainingExams.Count==1&&remainingExams[0].Id==oldExam.Id&&remainingExams[0].Questions[0].ReviewNote=="旧复盘记录","confirmed deletion removes only the selected exam without an autosave recreating it");
            Check(examDeckId==oldExam.Id&&InteractionElement<TextBlock>(body,t=>t.Tag?.ToString()=="exam-position")!.Text=="1 / 4","deleting the selected exam falls back to the remaining deck");
            deleting=DeleteExamReview(oldExam);confirm=await FeatureDialog("删除这份笔试复盘？");await FeatureClick(confirm,"确认");await deleting;await WaitForExamLayout();await Task.Delay(850);await FlushExamDraft();
            Check(!(await api.Get<List<InterviewDeck>>("interviews")).Any(d=>d.StudyType=="exam")&&examDeckId==""&&examDraftDeck is null&&!examDraftDirty&&FeatureButton(body,"删除复盘题库")?.IsEnabled==false,"deleting the last exam clears stale drafts and shows the empty state with deletion disabled");
            Check((await api.Get<List<InterviewDeck>>("interviews")).Count==imports.Count,"exam deletion preserves every interview study deck");
            await File.WriteAllTextAsync(Path.Combine(output,"interview-result.json"),JsonSerializer.Serialize(new{status="passed",checks},BackendClient.Json));
        }catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(output,"interview-result.json"),JsonSerializer.Serialize(new{status="failed",checks,error=ex.ToString()},BackendClient.Json));}
        finally {await SaveCurrent();await DetachPreviewAsync(true);await api.StopAsync();closing=true;api.Dispose();Close();}
    }
}

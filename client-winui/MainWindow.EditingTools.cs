using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ResumeStudio.Services;
using System.Text.Json;
using Windows.System;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private readonly List<string> undoStates=[];
    private readonly List<string> redoStates=[];
    private string undoBaseline="",undoResumeId="";
    private bool restoringUndo,undoCapturePending;
    private static Resume CloneResume(Resume r)=>CloneDocument(r);
    private string EditorSnapshot() { if(current is null) return ""; var clone=CloneResume(current); clone.UpdatedAt=""; return JsonSerializer.Serialize(clone,BackendClient.Json); }
    private void InitializeUndo()
    {
        if(current is null||undoResumeId==current.Id) return;
        undoTimer.Stop();undoCapturePending=false;undoResumeId=current.Id; undoBaseline=EditorSnapshot(); undoStates.Clear(); redoStates.Clear();
    }
    private void CaptureUndo()
    {
        if(current is null||restoringUndo) return;
        if(undoResumeId!=current.Id) { InitializeUndo(); return; }
        if(!undoCapturePending) {
            string after=EditorSnapshot(); if(after==undoBaseline) return;
            // Keep the state before a typing burst; serialize the final state once after input settles.
            undoStates.Add(undoBaseline); if(undoStates.Count>40) undoStates.RemoveAt(0);
            undoCapturePending=true;
        }
        undoTimer.Stop();undoTimer.Start();redoStates.Clear();
    }
    private void FinalizeUndoBurst()
    {
        undoTimer.Stop();
        if(current is not null&&undoCapturePending&&!restoringUndo)undoBaseline=EditorSnapshot();
        undoCapturePending=false;
    }
    private async Task UndoEditor(bool redo)
    {
        var source=redo?redoStates:undoStates; var target=redo?undoStates:redoStates;
        if(source.Count==0) { Success("没有可"+(redo?"重做":"撤销")+"的修改","会话内最多保留 40 组编辑；跨次启动请使用历史版本。"); return; }
        FinalizeUndoBurst();
        editTimer.Stop(); previewTimer.Stop(); await SaveCurrent(); target.Add(EditorSnapshot()); string snapshot=source[^1]; source.RemoveAt(source.Count-1);
        restoringUndo=true;
        try { current=JsonSerializer.Deserialize<Resume>(snapshot,BackendClient.Json)!; undoBaseline=snapshot;undoCapturePending=false; Changed(); await SaveCurrent(); await OpenEditor(); }
        finally { restoringUndo=false; }
    }
    private UIElement FormattingBar(TextBox input)
    {
        var panel=new Grid {ColumnSpacing=5,Tag="editor-formatting-bar"}; for(int i=0;i<4;i++) panel.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        int index=0;
        foreach(var item in new[] {("加粗","bold"),("列表","bullet"),("小标题","heading"),("表格","table")}) {
            var button=Button(item.Item1,async ()=> {if(item.Item2=="table"){await InsertContent(input,5);return;}var result=ResumeEditing.Format(NormalizeLines(input.Text),input.SelectionStart,input.SelectionLength,item.Item2); FinalizeUndoBurst(); input.Text=result.Text; input.Focus(FocusState.Programmatic); input.Select(result.Start,result.Length); });
            button.Padding=new Thickness(4,7,4,7);button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Center;Grid.SetColumn(button,index++); panel.Children.Add(button);
            ToolTipService.SetToolTip(button,item.Item2=="bold"?"选中文本后加粗 · Ctrl+B":item.Item2=="bullet"?"将选中行切换为列表":item.Item2=="heading"?"插入经历标题与时间":"插入 Markdown 表格");
        }
        var bold=new KeyboardAccelerator {Key=VirtualKey.B,Modifiers=VirtualKeyModifiers.Control};
        bold.Invoked+=(_,e)=> {e.Handled=true;var result=ResumeEditing.Format(NormalizeLines(input.Text),input.SelectionStart,input.SelectionLength,"bold");FinalizeUndoBurst();input.Text=result.Text;input.Select(result.Start,result.Length);};input.KeyboardAccelerators.Add(bold);
        return panel;
    }
    private async Task SaveCheckpoint()
    {
        if(current is null) return; await SaveCurrent(); await api.Post<JsonElement>($"resumes/{current.Id}/history",new {}); Success("历史版本已保存","每份简历最多保留最近 20 个压缩版本。");
    }
    private async Task ShowHistory()
    {
        if(current is null) return; await SaveCurrent(); string resumeId=current.Id;
        var items=await api.Get<List<ResumeRevision>>($"resumes/{resumeId}/history"); var list=new ListView {ItemsSource=items.Select(i=>LocalDate(i.CreatedAt)+" · "+i.Label).ToList(),SelectionMode=ListViewSelectionMode.Single,MaxHeight=400};
        var content=Stack(); content.Children.Add(Text("恢复会覆盖当前编辑稿；恢复前会保存一个版本。也可复制成新简历。最多保留最近 20 版；自动版本每 5 分钟最多保存一次。",13)); content.Children.Add(list);
        if(items.Count==0) content.Children.Add(Text("暂无历史版本。可在工作台手动保存版本。",14));
        var choice=await new ContentDialog {XamlRoot=shell.XamlRoot,Title="简历历史版本",Content=content,PrimaryButtonText="恢复选中版本",SecondaryButtonText="复制为新简历",CloseButtonText="返回",IsPrimaryButtonEnabled=items.Count>0,IsSecondaryButtonEnabled=items.Count>0}.ShowAsync();
        if(choice==ContentDialogResult.None) return; if(list.SelectedIndex<0) throw new InvalidOperationException("请先选择一个版本。");
        var selected=await api.Get<ResumeRevision>($"resumes/{resumeId}/history/{items[list.SelectedIndex].Id}"); var r=selected.Resume??throw new IOException("历史版本无法读取。");
        await api.Post<JsonElement>($"resumes/{resumeId}/history",new {});
        if(choice==ContentDialogResult.Secondary) {r.Id=Guid.NewGuid().ToString(); r.Title+=" · 历史副本";}
        current=await api.Save("resumes",r); dirty=false; undoResumeId=""; await OpenEditor();
    }
    private void AddUndoKeys(UIElement target)
    {
        if(target.KeyboardAccelerators.Count>0) return;
        foreach(var key in new[] {VirtualKey.Z,VirtualKey.Y}) {
            var accelerator=new KeyboardAccelerator {Key=key,Modifiers=VirtualKeyModifiers.Control};
            accelerator.Invoked+=async (_,e)=> {if(currentPage!="editor") return; e.Handled=true; await Run(()=>UndoEditor(key==VirtualKey.Y));}; target.KeyboardAccelerators.Add(accelerator);
        }
    }
}

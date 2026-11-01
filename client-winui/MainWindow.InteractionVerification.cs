using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task InteractionOnlyTest(bool smoke=false)
    {
        string output=Environment.GetEnvironmentVariable("RESUMESTUDIO_TEST_OUTPUT")??Path.Combine(api.DataDirectory,"interactions");Directory.CreateDirectory(output);var checks=new List<string>();
        try{if(smoke)await VerifyInteractionPrimitives(checks);else await VerifyInteractions(output,checks);await File.WriteAllTextAsync(Path.Combine(output,"interaction-result.json"),System.Text.Json.JsonSerializer.Serialize(new {status="passed",checks},BackendClient.Json));}
        catch(Exception ex){await File.WriteAllTextAsync(Path.Combine(output,"interaction-result.json"),System.Text.Json.JsonSerializer.Serialize(new {status="failed",checks,error=ex.ToString()},BackendClient.Json));}
        finally{Title="拾简 · 交互验证已结束（隔离数据）";}
    }
    private static T? InteractionElement<T>(DependencyObject parent,Func<T,bool> predicate) where T:DependencyObject
    {
        if(parent is T match&&predicate(match))return match;
        for(int i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);i++) {
            var found=InteractionElement(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent,i),predicate);if(found is not null)return found;
        }return null;
    }
    private async Task VerifyInteractionPrimitives(List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        var gate=new TaskCompletionSource();int calls=0;var button=Button("交互测试",()=>Task.CompletedTask);body.Children.Add(button);
        var busy=ExecuteButton(button,()=> {calls++;return gate.Task;});await Task.Delay(230);
        Check(!button.IsEnabled&&button.Content is StackPanel,"slow native button shows progress and disables repeated submission");
        await ExecuteButton(button,()=> {calls++;return Task.CompletedTask;});Check(calls==1,"duplicate operation is suppressed while original action is running");
        gate.SetResult();await busy;Check(button.IsEnabled&&button.Content?.ToString()=="交互测试","native button restores its label and enabled state after completion");
        try {await ExecuteButton(button,()=>throw new IOException("fixture failure"));}catch(IOException){}
        Check(button.IsEnabled&&button.Content?.ToString()=="交互测试","failed action also restores the native button");
        await ExecuteButton(button,()=>Task.CompletedTask,true);
        Check(button.Content?.ToString()=="✓ 已完成","successful opt-in action displays completion on its button");
        await ExecuteButton(button,()=>Task.CompletedTask,true);await Task.Delay(1350);
        Check(button.Content?.ToString()=="交互测试","repeated completion feedback restores the original label");
        try {await ExecuteButton(button,()=>throw new IOException("fixture failure"),true);}catch(IOException){}
        Check(button.Content?.ToString()=="交互测试","failed opt-in action never shows completion");
        var choice=new CheckBox {IsChecked=false};var dependent=new Button();LinkedChoice(choice,dependent);Check(!dependent.IsEnabled,"unchecked linked option disables its dependent control");choice.IsChecked=true;Check(dependent.IsEnabled,"checked linked option enables its dependent control");choice.IsChecked=false;Check(!dependent.IsEnabled,"clearing linked option disables its dependent control again");
        body.UpdateLayout();await Task.Delay(80);var pageVisual=ElementCompositionPreview.GetElementVisual(body);RevealPage(body,new Windows.Foundation.Point(20,20));
        Check(!Animate||pageVisual.Clip is Microsoft.UI.Composition.CompositionGeometricClip,"native circular page reveal uses a geometric clip when motion is enabled");await Task.Delay(420);Check(pageVisual.Clip is null,"native page reveal releases its temporary clip");
        ScaleTo(button,1.035f);await Task.Delay(350);var scale=ElementCompositionPreview.GetElementVisual(button).Scale;
        Check(Math.Abs(scale.X-(Animate?1.035f:1))<.002,"actual composition scale animation reaches the requested hover state");
        motionEnabled=false;ScaleTo(button,1.035f);Check(ElementCompositionPreview.GetElementVisual(button).Scale.X==1,"reduced motion resets composition scale immediately");motionEnabled=preferences.MotionEnabled;
    }
    private async Task VerifyInteractions(string output,List<string> checks)
    {
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add(label);}
        await VerifyInteractionPrimitives(checks);
        current=await api.Save("resumes",new Resume {Title="交互验证简历",Profile=new Profile {Name="交互测试",Role="嵌入式实习",Email="test@example.com"},Sections=[new() {Title="项目",Content="- 实现通信测试"},new() {Title="项目",Content="- 验证第二个模块"}]});dirty=false;await OpenEditor();await Task.Delay(180);
        var contact=InteractionElement<Expander>(body,e=>e.Header?.ToString()=="联系方式")??throw new Exception("Contact disclosure missing");
        var email=FindTextField(body,"邮箱")!;email.Text="saved@example.com";contact.IsExpanded=false;await Task.Delay(100);contact.IsExpanded=true;await Task.Delay(200);
        Check(FindTextField(body,"邮箱")?.Text=="saved@example.com"&&current.Profile.Email=="saved@example.com","collapsing and reopening editor details preserves edited values");
        Check(editorProgress?.Value==5&&editorProgress.Maximum==5,"editor progress counts filled profile and visible content modules");
        editorParts!.SelectedIndex=1;await Task.Delay(150);Check(editorContentCount?.Text.Contains("字符")==true,"module editor shows live character and line counts");
        await FocusPreviewSection();Check(await preview!.CoreWebView2.ExecuteScriptAsync("document.querySelector('section[data-resume-index=\"1\"]').textContent.includes('第二个模块')")=="true","preview anchoring identifies the selected module even with duplicate titles");
        var input=FindTextField(body,"内容")!;Check(input.KeyboardAccelerators.Any(a=>a.Key==Windows.System.VirtualKey.B),"content field exposes Ctrl+B formatting shortcut");
        var format=InteractionElement<Grid>(body,g=>g.Tag?.ToString()=="editor-formatting-bar")??throw new Exception("Formatting toolbar missing");
        var formatButtons=format.Children.OfType<Button>().ToList();Check(formatButtons.Count==4&&formatButtons.All(b=>b.HorizontalAlignment==HorizontalAlignment.Stretch&&b.HorizontalContentAlignment==HorizontalAlignment.Center)&&formatButtons.Max(b=>b.ActualWidth)-formatButtons.Min(b=>b.ActualWidth)<2,"formatting controls fill aligned equal-width columns");
        await SaveNativePreview(Path.Combine(output,"interaction-editor-classic.png"));
        var scheduled=await api.Save("schedule",new ScheduleItem {Title="准备二面材料",Kind="面试",StartAt=DateTimeOffset.Now.AddDays(1).ToString("O"),Notes="准备项目演示与岗位问题"});scheduleView=2;scheduleKind="面试";await Navigate("schedule");await Task.Delay(200);
        var disclosure=InteractionElement<Expander>(body,e=>e.Tag?.ToString()=="schedule:"+scheduled.Id)??throw new Exception("Schedule card disclosure missing");
        Check(disclosure.Content is Grid layers&&layers.Children.Count==3,"expanded native details use a three-layer card without duplicating content");
        disclosure.IsExpanded=true;await Task.Delay(200);await Navigate("schedule");await Task.Delay(200);
        Check(InteractionElement<Expander>(body,e=>e.Tag?.ToString()=="schedule:"+scheduled.Id)?.IsExpanded==true,"expanded schedule details survive refresh");
        Check(FeatureButton(body,"＋ 笔试") is not null&&FeatureButton(body,"＋ 面试") is not null,"calendar exposes prefilled exam and interview quick creation actions");
        var entry=(await ReadAgenda()).Single(e=>e.Item?.Id==scheduled.Id);await CompleteAgenda(entry,true);
        Check(feedback.IsOpen&&feedback.ActionButton is Button&&(await api.Get<List<ScheduleItem>>("schedule")).Single(i=>i.Id==scheduled.Id).Completed,"schedule completion persists and presents a dedicated undo feedback action");
        var edited=(await api.Get<List<ScheduleItem>>("schedule")).Single(i=>i.Id==scheduled.Id);edited.Notes="完成后补充的地点必须保留";await api.Save("schedule",edited);
        var undo=(Button)feedback.ActionButton;((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(undo).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        for(int i=0;i<60&&feedback.IsOpen;i++)await Task.Delay(50);
        Check(!(await api.Get<List<ScheduleItem>>("schedule")).Single(i=>i.Id==scheduled.Id).Completed,"actual feedback undo button restores the completed schedule");
        Check((await api.Get<List<ScheduleItem>>("schedule")).Single(i=>i.Id==scheduled.Id).Notes=="完成后补充的地点必须保留","undo preserves edits made after schedule completion");
        await SaveNativePreview(Path.Combine(output,"interaction-schedule-classic.png"));
        var quick=EditSchedule(new ScheduleItem {Kind="笔试",StartAt=DateTimeOffset.Now.AddDays(2).ToString("O")});var dialog=await FeatureDialog("编辑日程");
        Check(FindCombo(dialog,"日程类型")?.SelectedItem?.ToString()=="笔试","quick schedule creation keeps the preselected event type");await FeatureClick(dialog,"取消");await quick;
        preferences.InterfaceStyle="haru";preferences.Theme="light";await ApplyPreferences();await Navigate("schedule");await VerifyGlassCaptionRegions();
        var originalSize=AppWindow.Size;try{AppWindow.Resize(new Windows.Graphics.SizeInt32(800,700));await Task.Delay(220);await VerifyGlassCaptionRegions();}finally{AppWindow.Resize(originalSize);await Task.Delay(220);}
        Check(true,"Haru header stays aligned, below caption buttons and draggable at regular and compact window sizes");await SaveNativePreview(Path.Combine(output,"interaction-schedule-haru.png"));
        await OpenEditor();editorParts!.SelectedIndex=1;await Task.Delay(180);await WaitForPreview();await SaveNativePreview(Path.Combine(output,"interaction-editor-haru.png"));
        var folder=await Windows.Storage.StorageFolder.GetFolderFromPathAsync(output);var browserImage=await folder.CreateFileAsync("interaction-live-preview.png",Windows.Storage.CreationCollisionOption.ReplaceExisting);
        using(var stream=await browserImage.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))await preview!.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,stream);
        preferences.Theme="dark";await ApplyPreferences();await SaveNativePreview(Path.Combine(output,"interaction-editor-dark.png"));
        await Navigate("settings");await Task.Delay(180);var motion=InteractionElement<ToggleSwitch>(body,t=>t.Header?.ToString()=="交互动效")??throw new Exception("Motion setting missing");motion.IsOn=false;await Task.Delay(250);
        Check(!AppPreferences.Load(api.DataDirectory).MotionEnabled&&body.ChildrenTransitions.Count==0,"motion preference persists and removes page entrance transitions");motion.IsOn=true;await Task.Delay(250);
        Check(AppPreferences.Load(api.DataDirectory).MotionEnabled,"motion can be restored without restarting the app");
        preferences.InterfaceStyle="classic";preferences.Theme="system";await ApplyPreferences();scheduleKind="全部类型";scheduleQuery="";
    }
}

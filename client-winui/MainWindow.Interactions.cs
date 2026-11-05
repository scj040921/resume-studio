using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;
using Windows.UI.ViewManagement;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private static bool motionEnabled=true;
    private static readonly UISettings motionSettings=new();
    private readonly InfoBar feedback=new() {IsClosable=true,Margin=new Thickness(0,12,0,0)};
    private readonly DispatcherTimer feedbackTimer=new() {Interval=TimeSpan.FromSeconds(8)};
    private static bool Animate=>motionEnabled&&motionSettings.AnimationsEnabled;
    private Windows.Foundation.Point? revealOrigin;
    private long revealOriginTime;
    private void RememberRevealOrigin(Windows.Foundation.Point point){revealOrigin=point;revealOriginTime=Environment.TickCount64;}
    private Windows.Foundation.Point? TakeRevealOrigin(){var point=Environment.TickCount64-revealOriginTime<1200?revealOrigin:null;revealOrigin=null;return point;}
    private static void RevealPage(FrameworkElement element,Windows.Foundation.Point? origin)
    {
        if(!Animate||origin is null)return;
        var visual=ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        var fade=visual.Compositor.CreateScalarKeyFrameAnimation();fade.Duration=TimeSpan.FromMilliseconds(180);
        fade.InsertKeyFrame(0,.35f);fade.InsertKeyFrame(1,1);visual.StartAnimation("Opacity",fade);
    }
    private static void InertialWheel(UIElement child,ScrollViewer scroll)
    {
        double target=0;long previous=0;
        child.PointerWheelChanged+=(_,e)=>{
            if(!Animate||e.KeyModifiers!=Windows.System.VirtualKeyModifiers.None)return;
            var point=e.GetCurrentPoint(scroll);if(point.Properties.IsHorizontalMouseWheel)return;
            int delta=point.Properties.MouseWheelDelta;if(delta==0||scroll.ScrollableHeight<=0)return;
            long now=Environment.TickCount64;target=Math.Clamp((now-previous<280?target:scroll.VerticalOffset)-delta*.8,0,scroll.ScrollableHeight);previous=now;
            if(Math.Abs(target-scroll.VerticalOffset)>.1&&scroll.ChangeView(null,target,null,false))e.Handled=true;
        };
    }

    private static void ScaleTo(FrameworkElement element,float scale,bool pressed=false)
    {
        var visual=ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint=new Vector3((float)element.ActualWidth/2,(float)element.ActualHeight/2,0);
        if(!Animate) {visual.StopAnimation("Scale");visual.Scale=Vector3.One;return;}
        var animation=visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Duration=TimeSpan.FromMilliseconds(pressed?90:160);
        animation.InsertKeyFrame(1,new Vector3(scale,scale,1),visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.18f,.8f),new Vector2(.3f,1)));
        visual.StartAnimation("Scale",animation);
    }
    private static void ButtonMotion(ButtonBase button)
    {
        bool over=false;
        button.PointerEntered+=(_,_)=> {over=true;if(button.IsEnabled)ScaleTo(button,1);};
        button.PointerExited+=(_,_)=> {over=false;ScaleTo(button,1);};
        button.PointerPressed+=(_,_)=> {if(button.IsEnabled)ScaleTo(button,.99f,true);};
        button.PointerReleased+=(_,_)=>ScaleTo(button,1);
        button.PointerCanceled+=(_,_)=>ScaleTo(button,1);
        button.PointerCaptureLost+=(_,_)=>ScaleTo(button,1);
        button.LostFocus+=(_,_)=> {if(!over)ScaleTo(button,1);};
        button.IsEnabledChanged+=(_,_)=> {if(!button.IsEnabled)ScaleTo(button,1);};
    }
    private static Border InteractiveCard(UIElement child,Thickness padding)
    {
        var card=new Border {Child=child,Padding=padding,Background=PanelMaterial,BorderBrush=EdgeMaterial,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(20)};
        // A reading panel is not an action: keep geometry and borders stable on hover.
        return card;
    }
    private static void Entrance(Panel panel)
    {
        panel.ChildrenTransitions=Animate?new TransitionCollection {new EntranceThemeTransition {IsStaggeringEnabled=false}}:new TransitionCollection();
    }
    private sealed class CompletionState {public object? Original;public int Revision;}
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ButtonBase,CompletionState> completions=new();
    private async Task ExecuteButton(ButtonBase button,Func<Task> action,bool showCompletion=false)
    {
        if(!button.IsEnabled)return;
        var completion=completions.GetValue(button,_=>new CompletionState());int revision=++completion.Revision;
        object? original=completion.Original??(button as Button)?.Content;completion.Original=null;
        button.IsEnabled=false;
        bool succeeded=false;
        try {
            var running=action();
            if(await Task.WhenAny(running,Task.Delay(180))!=running&&button is Button regular) {
                var status=Row(8);var label=Text(original?.ToString()??"处理中",14);label.Foreground=regular.Foreground;status.Children.Add(new ProgressRing {IsActive=true,Width=18,Height=18,Foreground=regular.Foreground});status.Children.Add(label);regular.Content=status;
            }
            await running;succeeded=true;
        }
        finally {if(button is Button regular)regular.Content=original;button.IsEnabled=true;}
        if(succeeded&&showCompletion&&button is Button completed){
            completion.Original=original;completed.Content="✓ 已完成";
            if(Animate){ScaleTo(completed,1.025f);_ = ResetCompletionScale(completed);}
            _=RestoreCompletion(completed,completion,revision);
        }
    }
    private static async Task ResetCompletionScale(Button button){await Task.Delay(160);ScaleTo(button,1);}
    private static async Task RestoreCompletion(Button button,CompletionState completion,int revision){await Task.Delay(1200);if(completion.Revision!=revision)return;button.Content=completion.Original;completion.Original=null;}
    private static void LinkedChoice(CheckBox choice,Control dependent,bool inverse=false)
    {
        void Apply(bool animate){bool enabled=(choice.IsChecked==true)!=inverse;dependent.IsEnabled=enabled;if(animate&&Animate){ScaleTo(dependent,enabled?1.015f:.985f);_ = ResetLinkedScale(dependent);}}
        choice.Checked+=(_,_)=>Apply(true);choice.Unchecked+=(_,_)=>Apply(true);choice.Indeterminate+=(_,_)=>Apply(true);Apply(false);
    }
    private static async Task ResetLinkedScale(Control control){await Task.Delay(150);ScaleTo(control,1);}
    private static void SetSelectionUnderline(Border underline,bool selected)
    {
        double target=selected?1:0;if(Math.Abs(underline.Opacity-target)<.001)return;
        var visual=ElementCompositionPreview.GetElementVisual(underline);visual.StopAnimation("Opacity");
        if(Animate){var animation=visual.Compositor.CreateScalarKeyFrameAnimation();animation.Duration=TimeSpan.FromMilliseconds(220);animation.InsertKeyFrame(1,(float)target);visual.StartAnimation("Opacity",animation);}else visual.Opacity=(float)target;
        underline.Opacity=target;
    }
    private void InitializeFeedback()
    {
        feedbackTimer.Tick+=(_,_)=> {feedbackTimer.Stop();feedback.IsOpen=false;};
        feedback.Closed+=(_,_)=>feedbackTimer.Stop();
        feedback.PointerEntered+=(_,_)=>feedbackTimer.Stop();
        feedback.PointerExited+=(_,_)=> {if(feedback.IsOpen)feedbackTimer.Start();};
    }
    private void Feedback(string title,string message="",Func<Task>? undo=null)
    {
        feedbackTimer.Stop();feedback.Title=title;feedback.Message=message;feedback.Severity=InfoBarSeverity.Success;
        feedback.ActionButton=undo is null?null:Button("撤销这次操作",async ()=> {feedbackTimer.Stop();await undo();feedback.IsOpen=false;});
        feedback.IsOpen=true;feedbackTimer.Start();
    }
    private Expander Disclosure(string title,UIElement content,bool expanded=false,string? key=null)
    {
        var stack=new Grid {Margin=new Thickness(0,0,0,8)};
        stack.Children.Add(new Border {Margin=new Thickness(12,6,12,-6),Background=PanelMaterial,BorderBrush=EdgeMaterial,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Opacity=.45,IsHitTestVisible=false});
        stack.Children.Add(new Border {Margin=new Thickness(6,3,6,-3),Background=PanelMaterial,BorderBrush=EdgeMaterial,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Opacity=.7,IsHitTestVisible=false});
        stack.Children.Add(new Border {Child=content,Padding=new Thickness(10),Background=PanelMaterial,BorderBrush=EdgeMaterial,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(20)});
        var expander=new Expander {Header=title,Content=stack,IsExpanded=expanded,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        expander.Expanding+=(_,_)=>{if(Animate){var visual=ElementCompositionPreview.GetElementVisual(stack);visual.Scale=new Vector3(.985f,.985f,1);ScaleTo(stack,1);}};
        if(key is not null)expander.Tag=key;
        ToolTipService.SetToolTip(expander,"点击展开或收起，也可以用空格键操作");
        return expander;
    }
    private void SaveIndicator(string label,bool pending=false)
    {
        saveState.Text=(pending?"◌  ":"✓  ")+label;
        saveState.Foreground=Brush(pending?(darkStyle?"#F0C77A":"#855500"):(darkStyle?"#87C9C0":"#236C65"));
    }
}

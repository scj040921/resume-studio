using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private static T CloneDocument<T>(T value)=>System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value,Services.BackendClient.Json),Services.BackendClient.Json)!;
    // Keep the form open, including all user input, until validation and persistence both succeed.
    private ContentDialog SaveForm(string title,StackPanel fields,string saveLabel,Func<Task> save)
    {
        var error=new InfoBar {IsClosable=true,Severity=InfoBarSeverity.Error,Title="请检查填写内容"};
        fields.Children.Insert(0,error);
        var form=new ContentControl {Content=fields,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        var dialog=new ContentDialog {XamlRoot=shell.XamlRoot,Title=title,Content=new ScrollViewer {Content=form,MaxHeight=580},PrimaryButtonText=saveLabel,CloseButtonText="取消",DefaultButton=ContentDialogButton.None};
        bool submitting=false;dialog.Closing+=(_,args)=>{if(submitting)args.Cancel=true;};
        dialog.PrimaryButtonClick+=async (_,args)=> {
            if(submitting){args.Cancel=true;return;}submitting=true;
            var deferral=args.GetDeferral();args.Cancel=true;
            dialog.IsPrimaryButtonEnabled=dialog.IsSecondaryButtonEnabled=false;form.IsEnabled=false;error.IsOpen=false;dialog.PrimaryButtonText="正在保存…";
            try {await save();args.Cancel=false;}
            catch(Exception ex) {error.Message=ex.Message;error.IsOpen=true;error.Focus(FocusState.Programmatic);}
            finally {dialog.PrimaryButtonText=saveLabel;submitting=false;form.IsEnabled=true;dialog.IsPrimaryButtonEnabled=true;dialog.IsSecondaryButtonEnabled=!string.IsNullOrEmpty(dialog.SecondaryButtonText);deferral.Complete();}
        };
        return dialog;
    }
    private bool PageIsCurrent(string page,int generation)=>currentPage==page&&navigationRevision==generation&&!closing;
    private Task RefreshPage(string page)=>currentPage==page?Navigate(page):Task.CompletedTask;
}

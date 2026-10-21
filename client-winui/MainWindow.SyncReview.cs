using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using System.Text.Json.Nodes;
using ResumeStudio.Services;
namespace ResumeStudio;
public sealed partial class MainWindow
{
    private async Task<JsonObject?> ReviewSyncRecords(SyncPlan plan)
    {
        var remote=SyncProtocol.Snapshot(plan.RemoteEnvelope!,plan.Local.Snapshot);
        var rows=await api.Post<JsonElement>("sync/review",new {local=plan.Local.Snapshot,remote});
        var panel=Stack(12);var selections=new Dictionary<string,ComboBox>();
        foreach(var row in rows.EnumerateArray()){
            string key=row.GetProperty("key").GetString()!;
            panel.Children.Add(Text(row.GetProperty("kind").GetString()+" · "+row.GetProperty("title").ToString(),15,true));
            panel.Children.Add(Disclosure("查看两端内容",new TextBox {Text="本机：\n"+row.GetProperty("local").GetString()+"\n\n云端：\n"+row.GetProperty("remote").GetString(),IsReadOnly=true,AcceptsReturn=true,TextWrapping=Microsoft.UI.Xaml.TextWrapping.Wrap,MaxHeight=200},false));
            var select=new ComboBox {PlaceholderText="请选择保留方向",ItemsSource=new[]{"保留本机（含删除）","保留云端（含删除）"},HorizontalAlignment=Microsoft.UI.Xaml.HorizontalAlignment.Stretch};selections[key]=select;panel.Children.Add(select);
        }
        var mergeDialog=new ContentDialog {XamlRoot=shell.XamlRoot,RequestedTheme=shell.RequestedTheme,Title="逐条合并 · 两端原资料将备份",Content=new ScrollViewer {Content=panel,MaxHeight=520},PrimaryButtonText="合并并同步",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close,IsPrimaryButtonEnabled=selections.Count==0};
        foreach(var select in selections.Values)select.SelectionChanged+=(_,_)=>mergeDialog.IsPrimaryButtonEnabled=selections.Values.All(s=>s.SelectedIndex>=0);
        syncConflictDialog=mergeDialog;ContentDialogResult choice;
        try{choice=await mergeDialog.ShowAsync();}finally{syncConflictDialog=null;}
        if(choice!=ContentDialogResult.Primary||closeRequested)return null;
        if(selections.Values.Any(s=>s.SelectedIndex<0))throw new IOException("请为每项差异选择保留方向后再合并。");
        var choices=selections.ToDictionary(p=>p.Key,p=>p.Value.SelectedIndex==0?"local":"remote");
        var merged=await api.Post<JsonElement>("sync/merge",new {local=plan.Local.Snapshot,remote,choices});return JsonNode.Parse(merged.GetRawText())!.AsObject();
    }
}

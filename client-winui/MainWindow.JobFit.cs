using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private async Task SelectFitJob()
    {
        if(current is null) return;
        var jobs=await api.Get<List<Job>>("favorites"); var panel=Stack(12);
        var choice=new ComboBox {Header="收藏职位（也可直接粘贴要求）",ItemsSource=jobs,DisplayMemberPath="Title",HorizontalAlignment=HorizontalAlignment.Stretch};
        var title=new TextBox {Header="目标职位",Text=current.Profile.Role,MaxLength=250}; var company=new TextBox {Header="公司",MaxLength=200};
        var description=new TextBox {Header="职位职责与任职要求",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=200,MaxLength=30000};
        choice.SelectionChanged+=(_,_)=> {if(choice.SelectedItem is Job j) {title.Text=j.Title; company.Text=j.Company; description.Text=j.Description;}};
        foreach(var element in new UIElement[] {choice,title,company,description}) panel.Children.Add(element);
        if(await new ContentDialog {XamlRoot=shell.XamlRoot,Title="选择目标岗位",Content=new ScrollViewer {Content=panel,MaxHeight=560},PrimaryButtonText="开始对照",CloseButtonText="返回"}.ShowAsync()!=ContentDialogResult.Primary) return;
        var job=choice.SelectedItem is Job selected?CloneDocument(selected):new Job();
        job.Title=title.Text; job.Company=company.Text; job.Description=description.Text;
        await ShowJobFit(job,current);
    }
    private async Task FitJob(Job job)
    {
        await SaveCurrent(); var resumes=await api.Get<List<Resume>>("resumes");
        if(resumes.Count==0) {Success("请先创建一份简历","在模板中心填写真实经历后，可针对收藏岗位制作独立版本。"); return;}
        var choice=new ComboBox {Header="选择基础简历",ItemsSource=resumes,DisplayMemberPath="Title",SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
        var description=new TextBox {Header="职位要求（信息不完整时请从官网补充）",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Text=job.Description,MinHeight=200,MaxLength=30000};
        var content=Stack(); content.Children.Add(choice); content.Children.Add(description);
        if(await new ContentDialog {XamlRoot=shell.XamlRoot,Title="针对岗位制作简历",Content=content,PrimaryButtonText="开始对照",CloseButtonText="返回"}.ShowAsync()!=ContentDialogResult.Primary) return;
        job=CloneDocument(job); job.Description=description.Text;
        await ShowJobFit(job,(Resume)choice.SelectedItem);
    }
    private async Task ShowJobFit(Job job,Resume original)
    {
        if(string.IsNullOrWhiteSpace(job.Title)||string.IsNullOrWhiteSpace(job.Description)) throw new InvalidOperationException("请补充职位名称与任职要求。");
        original=CloneResume(original);
        var suggestions=JobFitAdvisor.LocalSuggestions(original,job); var selected=new HashSet<int>(); string aiFeedback="";
        while(true)
        {
            var panel=Stack(12); panel.Children.Add(Text(job.Company+" · "+job.Title,19,true));
            panel.Children.Add(Text("本地对照只识别已支持的技术与学历关键词。出现相关表述不代表已满足岗位要求，请结合完整职位描述核对熟练度、经验和职责。",12));
            var requirements=JobFitAdvisor.Analyze(original,job);
            foreach(var r in requirements) {var c=Stack(5); c.Children.Add(Text(r.Name+" · "+r.Status,14,true)); c.Children.Add(Text("职位原文："+r.Requirement,12)); c.Children.Add(Text("简历依据："+r.Evidence,12)); panel.Children.Add(Card(c,new Thickness(12)));}
            if(requirements.Count==0) panel.Children.Add(Text("未识别出支持的要求关键词；可以继续阅读原文或获取 AI 建议。",13));
            panel.Children.Add(new Expander {Header="完整职位要求",Content=Text(job.Description,13),HorizontalAlignment=HorizontalAlignment.Stretch});
            panel.Children.Add(Text("选择要采用的修改；所有修改需你核对事实，生成后原简历保留。",14,true));
            if(aiFeedback!="") panel.Children.Add(Text(aiFeedback,13,true));
            foreach(var s in suggestions) {
                var block=Stack(7); var toggle=new CheckBox {Content=original.Sections[s.SectionIndex].Title+" · 采用此项",IsChecked=selected.Contains(s.SectionIndex)};
                toggle.Checked+=(_,_)=>selected.Add(s.SectionIndex); toggle.Unchecked+=(_,_)=>selected.Remove(s.SectionIndex);
                block.Children.Add(toggle); block.Children.Add(Text("原文：\n"+s.Before,12)); block.Children.Add(Text("建议：\n"+s.After,12)); block.Children.Add(Text("理由："+s.Reason,12)); panel.Children.Add(Card(block,new Thickness(14)));
            }
            if(suggestions.Count==0) panel.Children.Add(Text("本地没有可自动调整的技能顺序。可直接复制为岗位简历并自行编辑。",12));
            string host=Uri.TryCreate(preferences.AiEndpoint,UriKind.Absolute,out var endpoint)?endpoint.Host:"尚未设置";
            panel.Children.Add(Text("获取 AI 建议会向 "+host+" 发送职位要求、求职意向和可见模块内容；姓名、电话、邮箱、照片字段不发送。模块内自行填写的个人信息仍会包含在请求中。",12));
            var result=await new ContentDialog {XamlRoot=shell.XamlRoot,Title="岗位对照与简历建议",Content=new ScrollViewer {Content=panel,MaxHeight=590},PrimaryButtonText="生成新的岗位简历",SecondaryButtonText="获取 AI 建议",CloseButtonText="返回"}.ShowAsync();
            if(result==ContentDialogResult.None) return;
            if(result==ContentDialogResult.Primary) {await SaveCurrent(); current=await api.Save("resumes",JobFitAdvisor.Apply(original,job,suggestions.Where(s=>selected.Contains(s.SectionIndex)))); dirty=false; await OpenEditor(); Success("岗位简历已创建","原稿保留。请核对事实并检查 PDF 分页后再投递。"); return;}
            using var cancellation=new CancellationTokenSource(); var loading=new ContentDialog {XamlRoot=shell.XamlRoot,Title="正在生成 AI 建议",Content="请求已发送到你配置的服务，最长等待 90 秒。",CloseButtonText="取消等待"};
            loading.CloseButtonClick+=(_,_)=>cancellation.Cancel(); var shown=loading.ShowAsync();
            try {
                var ai=await AiResumeClient.Suggest(preferences,await AiSecret.Read(api.DataDirectory),original,job,cancellation.Token);
                foreach(var suggestion in ai) {suggestions.RemoveAll(s=>s.SectionIndex==suggestion.SectionIndex); selected.Remove(suggestion.SectionIndex); suggestions.Add(suggestion);}
                aiFeedback=ai.Count==0?"AI 没有提出修改建议。":"已获取 AI 建议，请逐项核对并选择。";
                if(ai.Count==0) Success("AI 未提出修改","可以直接生成岗位简历，自行编辑。" );
            }
            catch(OperationCanceledException) {aiFeedback="已取消等待，服务端可能仍在处理本次请求。";}
            catch(Exception ex) when(ex is IOException or InvalidOperationException or JsonException or HttpRequestException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException) {aiFeedback="AI 建议未获取："+(ex is JsonException or KeyNotFoundException or IndexOutOfRangeException?"服务返回的内容不符合建议格式。":ex.Message);}
            finally {loading.Hide(); await shown;}
        }
    }
    private UIElement AiSettingsCard()
    {
        var panel=Stack(12); panel.Children.Add(Text("在线 AI",20,true));
        var address=new TextBox {Header="完整 Chat Completions 接口地址",Text=preferences.AiEndpoint,PlaceholderText="https://你的服务/v1/chat/completions",MaxLength=2000};
        var model=new TextBox {Header="模型名称",Text=preferences.AiModel,MaxLength=200};
        var key=new PasswordBox {Header="API Key",PlaceholderText=AiSecret.Exists(api.DataDirectory)?"已加密保存；留空保留现有密钥":"填写服务提供的密钥，本机无鉴权模型可留空"};
        panel.Children.Add(address); panel.Children.Add(model); panel.Children.Add(key);
        panel.Children.Add(Text("密钥由 Windows 按当前用户加密保存。更换接口地址时需重新填写密钥。只有主动获取建议时才发送简历模块与职位描述；请求可能产生服务费用。",12));
        var actions=Flow(); actions.Children.Add(Button("保存 AI 设置",async ()=> {
            AiResumeClient.Endpoint(address.Text.Trim()); if(string.IsNullOrWhiteSpace(model.Text)) throw new InvalidOperationException("请填写模型名称。");
            if(address.Text.Trim()!=preferences.AiEndpoint&&string.IsNullOrEmpty(key.Password)) AiSecret.Delete(api.DataDirectory);
            if(!string.IsNullOrEmpty(key.Password)) await AiSecret.Save(api.DataDirectory,key.Password);
            preferences.AiEndpoint=address.Text.Trim(); preferences.AiModel=model.Text.Trim(); await preferences.SaveAsync(api.DataDirectory); key.Password=""; key.PlaceholderText=AiSecret.Exists(api.DataDirectory)?"已加密保存；留空保留现有密钥":"未保存密钥"; Success("AI 设置已保存","可从工作台或职位卡片进入岗位对照。");
        },true)); actions.Children.Add(Button("删除已存密钥",()=> {AiSecret.Delete(api.DataDirectory); key.Password=""; key.PlaceholderText="未保存密钥"; return Task.CompletedTask;})); panel.Children.Add(actions); return Card(panel);
    }
}

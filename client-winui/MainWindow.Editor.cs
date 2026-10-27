using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ResumeStudio.Services;
using Windows.System;
using System.Collections.ObjectModel;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private int selectedEditorPart;
    private string editorResumeId="";
    private ListView? editorParts;
    private ObservableCollection<ResumeSection> editorModules=[];
    private bool draggingEditorModule,updatingDirectory;
    private AppBarButton? layoutButton;
    private Grid? editorLayoutPanel;
    private bool layoutExpanded;
    private ToggleSwitch? examplePreviewToggle;
    private bool showPreviewExamples=true;
    private Grid? editorFields;
    private TextBlock? editorProgressText,editorContentCount;
    private ProgressBar? editorProgress;

    private AppBarButton EditorAction(string label,Symbol icon,Func<Task> action)
    {
        var button=new AppBarButton { Label=label,Icon=new SymbolIcon(icon) };
        ButtonMotion(button);ToolTipService.SetToolTip(button,label=="保存"?"保存 · Ctrl+S":label=="撤销"?"撤销 · Ctrl+Z":label=="重做"?"重做 · Ctrl+Y":label);
        button.Click+=async (_,_)=>await Run(()=>ExecuteButton(button,action)); return button;
    }
    private async Task OpenEditor()
    {
        if(current is null) return;
        int generation=++navigationRevision;
        await Task.Yield();
        await pageTransitionLock.WaitAsync();
        try
        {
            if(generation!=navigationRevision||current is null)return;
            // Some entry points open the editor directly rather than through Navigate.
            if(currentPage=="exams")await FlushExamDraft();
            if(generation!=navigationRevision||current is null)return;
            await OpenEditorCore(generation);
        }
        finally { pageTransitionLock.Release(); }
    }
    private async Task OpenEditorCore(int generation)
    {
        if(current is null) return;
        CancelJobSearch();
        InitializeUndo();
        if(editorResumeId!=current.Id) { selectedEditorPart=0; editorResumeId=current.Id; }
        currentPage="editor";await DetachPreviewAsync();if(!PageIsCurrent("editor",generation))return;ResetPageBody();
        Heading("简历工作台","模块目录 · A4 实时预览 · 内容编辑；自动保存到本机。"); UpdateNavigation("editor");
        body.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });body.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto}); body.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(480) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(16) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        var toolbar=new CommandBar { DefaultLabelPosition=CommandBarDefaultLabelPosition.Right,IsOpen=false,Margin=new Thickness(0,0,0,12),Background=PanelMaterial };
        toolbar.PrimaryCommands.Add(EditorAction("我的简历",Symbol.Back,()=>Navigate("resumes")));
        toolbar.PrimaryCommands.Add(EditorAction("保存",Symbol.Save,async ()=> {await SaveCurrent();Feedback("简历已保存","修改已写入本机。");}));
        toolbar.PrimaryCommands.Add(EditorAction("导出 PDF",Symbol.Download,ExportPdf));
        toolbar.SecondaryCommands.Add(EditorAction("导出可编辑 Word",Symbol.Document,ExportWord));
        layoutButton=EditorAction("排版设置",Symbol.Font,()=> {ToggleLayoutSettings();return Task.CompletedTask;});
        ToolTipService.SetToolTip(layoutButton,"展开模板、字体、颜色、字号与行距设置");toolbar.PrimaryCommands.Add(layoutButton);
        toolbar.PrimaryCommands.Add(EditorAction("刷新预览",Symbol.Refresh,RenderPreview));
        toolbar.PrimaryCommands.Add(EditorAction("撤销",Symbol.Undo,()=>UndoEditor(false)));
        toolbar.PrimaryCommands.Add(EditorAction("重做",Symbol.Redo,()=>UndoEditor(true)));
        toolbar.SecondaryCommands.Add(EditorAction("备份 JSON",Symbol.SaveLocal,ExportJson));
        toolbar.SecondaryCommands.Add(EditorAction("查看打印版",Symbol.Print,async ()=> {
            showPreviewExamples=false;if(examplePreviewToggle is not null)examplePreviewToggle.IsOn=false;
            await WaitForPreview();preview!.CoreWebView2.ShowPrintUI(Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser);
        }));
        toolbar.SecondaryCommands.Add(EditorAction("检查与建议",Symbol.Help,ShowResumeCheck));
        toolbar.SecondaryCommands.Add(EditorAction("AI 提示词",Symbol.Copy,CopyAiPrompt));
        toolbar.SecondaryCommands.Add(EditorAction("保存历史版本",Symbol.SaveLocal,SaveCheckpoint));
        toolbar.SecondaryCommands.Add(EditorAction("历史版本",Symbol.Clock,ShowHistory));
        toolbar.SecondaryCommands.Add(EditorAction("岗位对照",Symbol.Find,()=>SelectFitJob()));
        toolbar.SecondaryCommands.Add(EditorAction("检查 PDF 分页",Symbol.Document,()=>CheckPdf(false)));
        AddUndoKeys(body);
        var saveKey=new KeyboardAccelerator { Key=VirtualKey.S,Modifiers=VirtualKeyModifiers.Control };
        saveKey.Invoked+=async (_,e)=> { e.Handled=true; await Run(SaveCurrent); }; toolbar.KeyboardAccelerators.Add(saveKey);
        Grid.SetColumnSpan(toolbar,3); body.Children.Add(toolbar);
        editorLayoutPanel=new Grid {Visibility=layoutExpanded?Visibility.Visible:Visibility.Collapsed,Margin=new Thickness(0,0,0,12)};
        editorLayoutPanel.Children.Add(Card(LayoutSettings(current),new Thickness(14)));Grid.SetRow(editorLayoutPanel,1);Grid.SetColumnSpan(editorLayoutPanel,3);body.Children.Add(editorLayoutPanel);

        editorWorkspace=new Grid();var workspace=editorWorkspace;
        workspace.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});workspace.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
        foreach(double width in new[] {156d,12,1,12,360})workspace.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(width,width==1?GridUnitType.Star:GridUnitType.Pixel)});
        var viewBar=new Grid {ColumnSpacing=12,Margin=new Thickness(0,0,0,10)};viewBar.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});viewBar.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        editorView=new ComboBox {Header="工作台视图",ItemsSource=new[] {"编辑内容","A4 预览"},SelectedIndex=0,MinWidth=140};viewBar.Children.Add(editorView);
        compactModulePicker=new ComboBox {Header="当前模块",ItemsSource=new[] {"基本信息"}.Concat(current.Sections.Select(s=>s.Title)).ToList(),SelectedIndex=selectedEditorPart>=2?selectedEditorPart-1:0,HorizontalAlignment=HorizontalAlignment.Stretch};
        compactModulePicker.SelectionChanged+=(_,_)=>{if(!PageIsCurrent("editor",generation)||updatingCompactModulePicker||compactModulePicker.SelectedIndex<0)return;if(compactModulePicker.SelectedIndex==0)SelectBasicInformation();else editorParts!.SelectedIndex=compactModulePicker.SelectedIndex-1;};
        Grid.SetColumn(compactModulePicker,1);viewBar.Children.Add(compactModulePicker);Grid.SetColumnSpan(viewBar,5);workspace.Children.Add(viewBar);editorViewBar=viewBar;
        editorView.SelectionChanged+=(_,_)=>UpdateWorkbenchLayout();
        var moduleColumn=Stack(10); moduleColumn.Children.Add(Text("内容目录",14,true));
        editorProgressText=Text("",11);editorProgress=new ProgressBar {Minimum=0,Maximum=1,Height=5,HorizontalAlignment=HorizontalAlignment.Stretch};
        moduleColumn.Children.Add(editorProgressText);moduleColumn.Children.Add(editorProgress);
        moduleColumn.Children.Add(Button("基本信息",()=> {SelectBasicInformation();return Task.CompletedTask;}));
        moduleColumn.Children.Add(Text("拖动模块调整顺序",11));
        editorModules=new ObservableCollection<ResumeSection>(current.Sections);
        editorParts=new ListView {HorizontalAlignment=HorizontalAlignment.Stretch,BorderThickness=new Thickness(0),Background=PanelMaterial,SelectionMode=ListViewSelectionMode.Single,CanDragItems=true,CanReorderItems=true,AllowDrop=true,ItemsSource=editorModules};
        editorParts.ItemTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Spacing='3' Padding='0,6'><TextBlock Text='{Binding Title}' TextWrapping='Wrap'/><TextBlock Text='{Binding EditorStatus}' Foreground='{Binding StatusColor}' FontSize='11'/></StackPanel></DataTemplate>");
        var parts=editorParts;
        ToolTipService.SetToolTip(editorParts,"按住模块并拖动；也可在模块设置中使用上移／下移");
        editorParts.SelectionChanged+=(_,_)=> {
            if(!PageIsCurrent("editor",generation)||parts!=editorParts||draggingEditorModule||updatingDirectory)return;
            if(parts.SelectedIndex>=0){selectedEditorPart=parts.SelectedIndex+2;ShowEditorPart();}
        };
        editorParts.DragItemsStarting+=(_,_)=>{if(PageIsCurrent("editor",generation)&&parts==editorParts)draggingEditorModule=true;};
        editorParts.DragItemsCompleted+=async (_,args)=>await Run(async ()=> {
            if(!PageIsCurrent("editor",generation)||parts!=editorParts)return;
            draggingEditorModule=false;
            var selected=parts.SelectedItem as ResumeSection??args.Items.OfType<ResumeSection>().FirstOrDefault();
            if(CommitEditorOrder(selected)){ShowEditorPart();await SaveCurrent();await RenderPreview();Feedback("模块顺序已保存","预览与导出将使用新的顺序。");}
        });moduleColumn.Children.Add(editorParts);
        moduleColumn.Children.Add(Button("＋ 添加模块",AddEditorModule));
        var modulesScroll=new ScrollViewer { Content=moduleColumn,HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        editorDirectory=modulesScroll;Grid.SetRow(modulesScroll,1);workspace.Children.Add(modulesScroll);
        editorFields=new Grid();Grid.SetColumn(editorFields,4);Grid.SetRow(editorFields,1);workspace.Children.Add(editorFields);
        Grid.SetRow(workspace,2);Grid.SetColumnSpan(workspace,3);body.Children.Add(workspace);
        if(selectedEditorPart>=2&&current.Sections.Count>0)editorParts.SelectedIndex=Math.Clamp(selectedEditorPart-2,0,current.Sections.Count-1);
        else SelectBasicInformation();
        UpdateEditorProgress();

        if(preview is null){preview=new WebView2 { HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch };preview.SizeChanged+=async (_,_)=>await Run(UpdatePreviewScale);}
        preview.DefaultBackgroundColor=haruStyle?Microsoft.UI.Colors.Transparent:Microsoft.UI.Colors.White;
        var previewPanel=new Grid(); previewPanel.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); previewPanel.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        var previewHeader=Stack(4);previewHeader.Margin=new Thickness(12,9,12,9);
        examplePreviewToggle=new ToggleSwitch {Header="未填写处显示模板示例",IsOn=showPreviewExamples};
        var examples=examplePreviewToggle;
        examples.Toggled+=async (_,_)=>await Run(async ()=> {if(!PageIsCurrent("editor",generation)||examples!=examplePreviewToggle)return;showPreviewExamples=examples.IsOn;await RenderPreview();});previewHeader.Children.Add(examples);
        capacityText=Text("正在估算 A4 内容容量…",11);previewHeader.Children.Add(capacityText);previewPanel.Children.Add(previewHeader); Grid.SetRow(preview,1); previewPanel.Children.Add(preview);
        previewHost=previewPanel;
        var frame=Card(previewPanel,new Thickness(0));editorPreviewFrame=frame;Grid.SetColumn(frame,2);Grid.SetRow(frame,1);workspace.Children.Add(frame);
        workspace.SizeChanged+=(_,_)=>{if(PageIsCurrent("editor",generation)&&workspace==editorWorkspace)UpdateWorkbenchLayout();};
        UpdateWorkbenchLayout();
        var editorWeb=preview;
        await renderLock.WaitAsync();
        try {if(PageIsCurrent("editor",generation))await InitializeWeb(editorWeb);}
        finally {renderLock.Release();}
        if(!PageIsCurrent("editor",generation))return;await RenderPreview();if(PageIsCurrent("editor",generation))await UpdateCapacity(editorWeb);
    }
    private void ShowEditorPart()
    {
        if(current is null||editorFields is null) return;
        var resume=current; var fields=Stack(13);Entrance(fields); editorFields.Children.Clear();editorContentCount=null;
        if(selectedEditorPart==0)
        {
            fields.Children.Add(Text("基本信息",20,true));
            var identity=Stack(12);identity.Children.Add(Field("简历名称",resume.Title,v=>resume.Title=v));
            identity.Children.Add(Field("姓名",resume.Profile.Name,v=>resume.Profile.Name=v));identity.Children.Add(Field("求职意向",resume.Profile.Role,v=>resume.Profile.Role=v));fields.Children.Add(Disclosure("姓名与求职方向",identity,true));
            var contact=Stack(12);contact.Children.Add(Field("电话",resume.Profile.Phone,v=>resume.Profile.Phone=v));contact.Children.Add(Field("邮箱",resume.Profile.Email,v=>resume.Profile.Email=v));
            contact.Children.Add(Field("城市",resume.Profile.City,v=>resume.Profile.City=v));contact.Children.Add(Field("个人主页 / 作品链接",resume.Profile.Link,v=>resume.Profile.Link=v));fields.Children.Add(Disclosure("联系方式",contact,true));
            fields.Children.Add(Disclosure("自定义基本信息",ProfileItemsEditor(resume.Profile),true));
            fields.Children.Add(Field("个人简介",resume.Profile.Summary,v=>resume.Profile.Summary=v,true));
            fields.Children.Add(Button("添加证件照",ImportPhoto)); fields.Children.Add(Button("移除照片",()=> { resume.Photo=""; Changed(); return Task.CompletedTask; }));
        }
        else if(selectedEditorPart-2>=0&&selectedEditorPart-2<resume.Sections.Count)
        {
            var section=resume.Sections[selectedEditorPart-2]; fields.Children.Add(Text(section.Title,20,true));
            var anchor=Button("在预览中定位模块",FocusPreviewSection);ToolTipService.SetToolTip(anchor,"滚动到当前模块并短暂高亮；不会改变 PDF");fields.Children.Add(anchor);
            fields.Children.Add(Field("模块标题",section.Title,v=> {section.Title=v;RefreshDirectoryLabel(section);}));
            var input=Field("内容",section.Content,v=>{section.Content=v;RefreshDirectoryLabel(section);},true,"## 项目 / 经历名称 | 时间\n- 技术、职责与真实成果"); input.MinHeight=260; fields.Children.Add(FormattingBar(input));fields.Children.Add(ContentTools(input,section));fields.Children.Add(input);
            editorContentCount=Text("",11);fields.Children.Add(editorContentCount);UpdateEditorProgress();
            fields.Children.Add(Text("支持经历、列表、目录子板块、图片与表格。将光标放在表格内，可通过“表格”逐格编辑；图片标记决定图片位置。",12));
            var options=Stack(10);var visible=new ToggleSwitch { Header="显示模块",IsOn=section.Visible }; visible.Toggled+=(_,_)=> { section.Visible=visible.IsOn;RefreshDirectoryLabel(section); Changed(); }; options.Children.Add(visible);
            var moves=Row(6);var up=Button("上移模块",()=>MoveEditorSection(section,-1));up.IsEnabled=selectedEditorPart>2;var down=Button("下移模块",()=>MoveEditorSection(section,1));down.IsEnabled=selectedEditorPart-2<resume.Sections.Count-1;moves.Children.Add(up);moves.Children.Add(down);options.Children.Add(moves);
            options.Children.Add(Button("移除模块",async ()=> { if(await Confirm("移除模块？",section.Title)) { resume.Sections.Remove(section); selectedEditorPart=Math.Max(0,selectedEditorPart-1); Changed(); await SaveCurrent(); await OpenEditor(); } }));fields.Children.Add(Disclosure("模块显示与顺序",options,true));
        }
        var scroll=new ScrollViewer { Content=Card(fields,new Thickness(18)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollMode=ScrollMode.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch };
        scroll.SizeChanged+=(_,e)=>fields.Width=Math.Max(0,e.NewSize.Width-52); editorFields.Children.Add(scroll);
        RefreshCompactModulePicker();
    }
    private void UpdateEditorProgress()
    {
        if(current is null)return;
        var visible=current.Sections.Where(s=>s.Visible).ToList();int total=3+visible.Count;
        int filled=(string.IsNullOrWhiteSpace(current.Profile.Name)?0:1)+(string.IsNullOrWhiteSpace(current.Profile.Role)?0:1)+(string.IsNullOrWhiteSpace(current.Profile.Phone)&&string.IsNullOrWhiteSpace(current.Profile.Email)?0:1)+visible.Count(s=>!string.IsNullOrWhiteSpace(s.Content));
        if(editorProgress is not null){editorProgress.Maximum=total;editorProgress.Value=filled;}
        if(editorProgressText is not null)editorProgressText.Text=$"已填写 {filled} / {total} 项";
        if(editorContentCount is not null&&selectedEditorPart>=2&&selectedEditorPart-2<current.Sections.Count){var text=current.Sections[selectedEditorPart-2].Content;editorContentCount.Text=$"{text.Length:N0} 字符 · {text.Split('\n').Length} 行";}
    }
    private async Task FocusPreviewSection()
    {
        if(current is null||selectedEditorPart<2||selectedEditorPart-2>=current.Sections.Count)return;
        if(!current.Sections[selectedEditorPart-2].Visible){Feedback("当前模块已隐藏","打开“显示模块”后可以在预览中定位。");return;}
        if(editorWorkspace?.ActualWidth<1100&&editorView is not null)editorView.SelectedIndex=1;
        await WaitForPreview();int index=selectedEditorPart-2;
        string result=await preview!.CoreWebView2.ExecuteScriptAsync($$"""
        (()=>{const section=document.querySelector('section[data-resume-index="{{index}}"]');if(!section)return false;
          section.scrollIntoView({behavior:{{(Animate?"'smooth'":"'instant'")}},block:'center'});
          {{(Animate?"section.animate([{backgroundColor:'#dfe7ff'},{backgroundColor:'transparent'}],{duration:1000});":"")}}
          return true;})()
        """);
        if(result!="true")Feedback("预览正在更新","请稍后重新定位模块。");
    }
    private async Task MoveEditorSection(ResumeSection section,int offset)
    {
        if(current is null) return; int index=current.Sections.IndexOf(section),target=index+offset;
        if(target<0||target>=current.Sections.Count) return;
        current.Sections.RemoveAt(index); current.Sections.Insert(target,section); selectedEditorPart=target+2;
        Changed(); await SaveCurrent(); await OpenEditor();
    }

    private void SelectBasicInformation()
    {
        if(editorParts is not null)editorParts.SelectedIndex=-1;
        selectedEditorPart=0;ShowEditorPart();
    }

    private void RefreshDirectoryLabel(ResumeSection section)
    {
        int index=editorModules.IndexOf(section);if(index<0||editorParts is null)return;
        updatingDirectory=true;
        try {editorModules[index]=section;editorParts.SelectedItem=section;}
        finally {updatingDirectory=false;}
        RefreshCompactModulePicker();
    }

    private bool CommitEditorOrder(ResumeSection? selected)
    {
        if(current is null||current.Sections.SequenceEqual(editorModules))return false;
        // Native reordering changes the collection first; persist the final permutation once.
        if(editorModules.Count!=current.Sections.Count||editorModules.Distinct().Count()!=editorModules.Count||editorModules.Any(section=>!current.Sections.Contains(section)))return false;
        current.Sections=editorModules.ToList();
        selectedEditorPart=selected is null?0:current.Sections.IndexOf(selected)+2;
        RefreshCompactModulePicker();
        lastUndoCapture=DateTime.MinValue;Changed();return true;
    }

    private UIElement LayoutSettings(Resume resume)
    {
        int generation=navigationRevision;
        bool Active()=>PageIsCurrent("editor",generation)&&ReferenceEquals(current,resume);
        void UpdateLayout(){if(!Active())return;Changed();previewTimer.Stop();previewTimer.Start();}
        var fields=new Grid {ColumnSpacing=18,RowSpacing=8};
        for(int column=0;column<3;column++)fields.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
        fields.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});fields.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        fields.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});fields.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        void Add(FrameworkElement control,int row,int column){Grid.SetRow(control,row);Grid.SetColumn(control,column);fields.Children.Add(control);}
        var choices=new ComboBox {Header="模板",ItemsSource=templates,SelectedItem=templates.First(t=>t.Id==resume.TemplateId),HorizontalAlignment=HorizontalAlignment.Stretch};
        choices.SelectionChanged+=async (_,_)=>await Run(async ()=> {if(Active()&&choices.SelectedItem is Template t){resume.TemplateId=t.Id;resume.Color=t.Color;Changed();await RenderPreview();}});Add(choices,0,0);
        var font=new ComboBox {Header="简历字体",ItemsSource=ResumeTypography.Labels,SelectedIndex=Array.IndexOf(ResumeTypography.Names,ResumeTypography.Normalize(resume.FontFamily)),HorizontalAlignment=HorizontalAlignment.Stretch};
        font.SelectionChanged+=async (_,_)=>await Run(async ()=> {if(Active()&&font.SelectedIndex>=0){resume.FontFamily=ResumeTypography.Names[font.SelectedIndex];Changed();await RenderPreview();}});Add(font,0,1);
        var palette=Stack(4);palette.Children.Add(Text("主题色",13));
        var colors=Row(5);foreach(string color in new[] {"#245AE8","#167D78","#353D59","#222222","#7955B4","#1E668A"}) {
            var button=Button("●",()=> {if(Active()){resume.Color=color;UpdateLayout();}return Task.CompletedTask;});button.Foreground=Brush(color);button.Padding=new Thickness(6,4,6,4);ToolTipService.SetToolTip(button,"主题色 "+color);colors.Children.Add(button);
        }palette.Children.Add(colors);Add(palette,0,2);
        var size=new Slider {Header="正文字号",Minimum=10,Maximum=16,StepFrequency=.5,Value=resume.FontSize};size.ValueChanged+=(_,_)=> {if(Active()){resume.FontSize=size.Value;UpdateLayout();}};Add(size,1,0);
        var spacing=new Slider {Header="行间距",Minimum=1.2,Maximum=2,StepFrequency=.1,Value=resume.LineHeight};spacing.ValueChanged+=(_,_)=> {if(Active()){resume.LineHeight=spacing.Value;UpdateLayout();}};Add(spacing,1,1);
        Add(Text("MiSans 随软件提供；其他字体使用本机字体。\nPDF 使用白色纸张，仅包含你填写的内容。",11),1,2);
        var vertical=new Slider {Header="上下页边距（mm）",Minimum=8,Maximum=25,StepFrequency=1,Value=resume.PageMarginVertical};vertical.ValueChanged+=(_,_)=>{if(Active()){resume.PageMarginVertical=vertical.Value;UpdateLayout();}};Add(vertical,2,0);
        var horizontal=new Slider {Header="左右页边距（mm）",Minimum=8,Maximum=25,StepFrequency=1,Value=resume.PageMarginHorizontal};horizontal.ValueChanged+=(_,_)=>{if(Active()){resume.PageMarginHorizontal=horizontal.Value;UpdateLayout();}};Add(horizontal,2,1);
        var gap=new Slider {Header="模块间距（mm）",Minimum=0,Maximum=12,StepFrequency=.5,Value=resume.SectionSpacing};gap.ValueChanged+=(_,_)=>{if(Active()){resume.SectionSpacing=gap.Value;UpdateLayout();}};Add(gap,2,2);
        var alignment=new ComboBox {Header="页眉对齐",ItemsSource=new[] {"跟随模板","左对齐","居中"},SelectedIndex=resume.HeaderAlignment=="left"?1:resume.HeaderAlignment=="center"?2:0,HorizontalAlignment=HorizontalAlignment.Stretch};alignment.SelectionChanged+=(_,_)=>{if(Active()){resume.HeaderAlignment=alignment.SelectedIndex==1?"left":alignment.SelectedIndex==2?"center":"template";UpdateLayout();}};Add(alignment,3,0);
        Add(Button("恢复默认排版",()=>{if(Active()){resume.PageMarginVertical=14;resume.PageMarginHorizontal=16;resume.SectionSpacing=4.8;resume.HeaderAlignment="template";Changed();editorLayoutPanel!.Children.Clear();editorLayoutPanel.Children.Add(Card(LayoutSettings(resume),new Thickness(14)));previewTimer.Stop();previewTimer.Start();}return Task.CompletedTask;}),3,1);
        Add(Text("预览保持 A4 纸张比例；窗口变窄时可切换编辑和预览。",11),3,2);
        return fields;
    }
    private void ToggleLayoutSettings()
    {
        layoutExpanded=!layoutExpanded;
        if(editorLayoutPanel is not null)editorLayoutPanel.Visibility=layoutExpanded?Visibility.Visible:Visibility.Collapsed;
    }
}

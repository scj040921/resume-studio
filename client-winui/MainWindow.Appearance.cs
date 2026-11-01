using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using ResumeStudio.Services;
using Windows.UI.ViewManagement;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private AppPreferences preferences = new();
    private static bool haruStyle, darkStyle;
    private Grid? contentHost;
    private Border? headerFrame;
    private NavigationView? modernNavigation;
    private readonly UISettings systemSettings = new();
    private bool changingNavigation;
    private static string Accent => haruStyle ? "#5B6EAE" : "#245AE8";
    private static string Ink => darkStyle ? "#ECECF5" : haruStyle ? "#292D3E" : "#22304A";
    private static string Surface => darkStyle ? "#24242D" : "#FFFFFF";
    private static string Stroke => darkStyle ? "#41414D" : haruStyle ? "#E3E3EC" : "#E3E8F1";
    private void BuildContentHost()
    {
        contentHost=new Grid();
        contentHost.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        contentHost.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        contentHost.RowDefinitions.Add(new RowDefinition { Height=new GridLength(1,GridUnitType.Star) });
        contentHost.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var header=new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var heading=Stack(5); heading.Children.Add(pageTitle); heading.Children.Add(pageSubtitle); header.Children.Add(heading);
        saveState.HorizontalAlignment=HorizontalAlignment.Right; saveState.VerticalAlignment=VerticalAlignment.Center; saveState.Margin=new Thickness(16,0,0,0); Grid.SetColumn(saveState,1); header.Children.Add(saveState);
        headerFrame=new Border { Child=header,Margin=new Thickness(0,0,0,20) }; contentHost.Children.Add(headerFrame);
        headerFrame.LayoutUpdated+=(_,_)=>UpdateGlassDragRegions();
        shell.Loaded+=(_,_)=>shell.XamlRoot.Changed+=(_,_)=>UpdateGlassDragRegions();
        shell.SizeChanged+=(_,e)=>UpdateClassicNavigationLayout(e.NewSize.Width);
        Grid.SetRow(notice,1); contentHost.Children.Add(notice);
        pageHost.Child=body; Grid.SetRow(pageHost,2); contentHost.Children.Add(pageHost);
        Grid.SetRow(feedback,3); contentHost.Children.Add(feedback);
    }
    private void ResetPageBody()
    {
        // Retire the whole page instead of clearing a live collection containing focused
        // ComboBox/CommandBar controls. WinUI can AV in Clear after a native dropdown closes.
        body=new Grid(); Entrance(body); pageHost.Child=body;
        editorParts=null; editorFields=null; editorLayoutPanel=null; layoutButton=null;
        examplePreviewToggle=null; editorProgressText=null; editorContentCount=null;
        editorProgress=null; capacityText=null; editorModules=[];
        editorWorkspace=null;editorViewBar=null;editorDirectory=null;editorPreviewFrame=null;editorView=null;compactModulePicker=null;
        draggingEditorModule=false; updatingDirectory=false;
    }
    private void BuildShell()
    {
        haruStyle=preferences.InterfaceStyle=="haru";
        motionEnabled=preferences.MotionEnabled; Entrance(body);
        var systemColor=systemSettings.GetColorValue(UIColorType.Background);
        darkStyle=haruStyle&&(preferences.Theme=="dark"||preferences.Theme=="system"&&systemColor.R+systemColor.G+systemColor.B<384);
        shell.RequestedTheme=darkStyle?ElementTheme.Dark:ElementTheme.Light;
        if(modernNavigation is not null) { modernNavigation.Content=null; modernNavigation=null; }
        shell.Children.Clear(); shell.ColumnDefinitions.Clear(); navigation.Clear();
        modernNavigationItems.Clear(); classicChildMenus.Clear(); classicLabels.Clear(); classicUnderlines.Clear(); classicChevrons.Clear();
        classicSidebar=null; classicBrandName=null; classicBrandSubtitle=null; classicFooter=null;
        contentHost!.Resources.Clear();
        if(haruStyle)
        {
            foreach(var type in new[] {typeof(TextBox),typeof(ComboBox)})
            {
                var style=new Style(type); style.Setters.Add(new Setter(Control.CornerRadiusProperty,new CornerRadius(12))); style.Setters.Add(new Setter(Control.FontFamilyProperty,ResumeTypography.InterfaceFont)); contentHost.Resources.Add(type,style);
            }
        }
        ConfigureWindowMaterial();
        contentHost!.Margin=haruStyle?new Thickness(22,18,22,18):new Thickness(30,26,30,22);
        pageTitle.Foreground=Brush(Ink); pageSubtitle.Foreground=Brush(darkStyle?"#B1B1C3":"#697086"); saveState.Foreground=Brush(darkStyle?"#9FB2E5":"#167D78");
        headerFrame!.Padding=haruStyle?new Thickness(20,15,20,15):new Thickness(0);
        headerFrame.CornerRadius=new CornerRadius(haruStyle?22:0);
        headerFrame.BorderThickness=new Thickness(haruStyle?1:0); headerFrame.BorderBrush=EdgeMaterial;
        headerFrame.Background=haruStyle?PanelMaterial:new SolidColorBrush(Colors.Transparent);
        previewTimer.Interval=TimeSpan.FromMilliseconds(preferences.PreviewDelayMs);
        if(haruStyle)
        {
            Grid.SetColumn(contentHost,0);
            modernNavigation=new NavigationView { IsBackButtonVisible=NavigationViewBackButtonVisible.Collapsed,IsSettingsVisible=false,OpenPaneLength=210,CompactPaneLength=48,PaneDisplayMode=NavigationViewPaneDisplayMode.Auto,Content=contentHost };
            ConfigureGlassNavigation(modernNavigation);
            var brand=Row(10); brand.Margin=new Thickness(15,6,15,12); brand.Children.Add(BrandLogo(34)); var name=Text("拾简",22,true); name.VerticalAlignment=VerticalAlignment.Center; brand.Children.Add(name); modernNavigation.PaneHeader=brand;
            foreach(var page in NavigationCatalog.Roots)
            {
                var item=CreateModernNavigationItem(page);
                if(page.IsFooter) modernNavigation.FooterMenuItems.Add(item); else modernNavigation.MenuItems.Add(item);
            }
            modernNavigation.SelectionChanged+=async (_,e)=> { if(!changingNavigation&&e.SelectedItem is NavigationViewItem item) await Run(()=>Navigate((string)item.Tag)); };
            shell.Children.Add(modernNavigation);
        }
        else
        {
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(222) }); shell.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            BuildSidebar(); Grid.SetColumn(contentHost,1); shell.Children.Add(contentHost);
            UpdateClassicNavigationLayout(shell.ActualWidth);
        }
        UpdateNavigation(currentPage);
        ConfigureGlassTitleBar();
    }
    private void UpdateNavigation(string page)
    {
        string selected=NavigationCatalog.SelectedKey(page);
        string? parent=NavigationCatalog.ParentKey(page);
        while(parent is not null)
        {
            expandedNavigationGroups.Add(parent);
            if(modernNavigationItems.TryGetValue(parent,out var group)) group.IsExpanded=true;
            parent=NavigationCatalog.ParentKey(parent);
        }
        string? activeParent=NavigationCatalog.ParentKey(page);
        foreach(var button in navigation)
        {
            string key=(string)button.Tag;
            button.Background=Brush(key==selected?"#294267":key==activeParent?"#203653":"#14233F");
            if(classicUnderlines.TryGetValue(key,out var underline))SetSelectionUnderline(underline,key==selected);
        }
        UpdateClassicNavigationGroups();
        if(modernNavigation is null) return;
        changingNavigation=true;
        try { modernNavigation.SelectedItem=modernNavigationItems.GetValueOrDefault(selected); }
        finally { changingNavigation=false; }
    }
    private async Task ApplyPreferences()
    {
        await SaveCurrent(); await preferences.SaveAsync(api.DataDirectory);await DetachPreviewAsync(true);BuildShell();
        if(currentPage=="editor") await OpenEditor(); else await Navigate(currentPage);
    }
    private void ShowSettings()
    {
        Heading("设置","切换界面外观与预览节奏，简历和求职记录共用同一份本机数据。");
        var panel=Stack(18);
        panel.Children.Add(UpdateSettingsCard());
        panel.Children.Add(AiSettingsCard());
        panel.Children.Add(Button("全局搜索资料",()=>ShowWorkspaceTools()));
        AddSyncSettings(panel);
        var appearance=Stack(14); appearance.Children.Add(Text("界面与主题",20,true));
        appearance.Children.Add(Text("界面字体：MiSans，由小米提供，随软件分发。",12));
        var motion=new ToggleSwitch {Header="交互动效",IsOn=preferences.MotionEnabled};
        motion.Toggled+=async (_,_)=>await Run(async ()=> {preferences.MotionEnabled=motion.IsOn;motionEnabled=motion.IsOn;Entrance(body);await preferences.SaveAsync(api.DataDirectory);});appearance.Children.Add(motion);
        appearance.Children.Add(Text("页面入场、按钮按压与卡片悬停会使用动态反馈；关闭后保留展开、状态提示与快捷操作。系统关闭动画时也会减少动效。",12));
        var style=new ComboBox { Header="界面版本",ItemsSource=new[] {"经典界面 · 当前默认","Haru 玻璃界面 · haruphoto 整体亚克力"},SelectedIndex=haruStyle?1:0,HorizontalAlignment=HorizontalAlignment.Stretch };
        style.SelectionChanged+=async (_,_)=>await Run(async ()=> { preferences.InterfaceStyle=style.SelectedIndex==1?"haru":"classic"; await ApplyPreferences(); }); appearance.Children.Add(style);
        var theme=new ComboBox { Header="Haru 界面主题",ItemsSource=new[] {"跟随系统","浅色","深色"},SelectedIndex=preferences.Theme=="light"?1:preferences.Theme=="dark"?2:0,IsEnabled=haruStyle,HorizontalAlignment=HorizontalAlignment.Stretch };
        theme.SelectionChanged+=async (_,_)=>await Run(async ()=> { preferences.Theme=theme.SelectedIndex==1?"light":theme.SelectedIndex==2?"dark":"system"; await ApplyPreferences(); }); appearance.Children.Add(theme);
        appearance.Children.Add(Text("Haru 的整窗背景、侧栏、顶栏和内容卡片使用亚克力玻璃与高光描边，透出窗口背后的模糊颜色。Windows 关闭透明效果或高对比度模式下会自动回退为清晰底色。切换无需重启。",13)); panel.Children.Add(Card(appearance));
        var performance=Stack(14); performance.Children.Add(Text("编辑与性能",20,true));
        var live=new ToggleSwitch { Header="实时预览",IsOn=preferences.LivePreview };
        live.Toggled+=async (_,_)=>await Run(async ()=> { preferences.LivePreview=live.IsOn; if(!live.IsOn) previewTimer.Stop(); await preferences.SaveAsync(api.DataDirectory); }); performance.Children.Add(live);
        var delay=new ComboBox { Header="停止输入后的预览刷新",ItemsSource=new[] {"及时 · 350 ms","均衡 · 900 ms","省电 · 1500 ms"},SelectedIndex=preferences.PreviewDelayMs<650?0:preferences.PreviewDelayMs<1200?1:2,HorizontalAlignment=HorizontalAlignment.Stretch };
        delay.SelectionChanged+=async (_,_)=>await Run(async ()=> { preferences.PreviewDelayMs=delay.SelectedIndex==0?350:delay.SelectedIndex==2?1500:900; previewTimer.Interval=TimeSpan.FromMilliseconds(preferences.PreviewDelayMs); await preferences.SaveAsync(api.DataDirectory); }); performance.Children.Add(delay);
        performance.Children.Add(Text("自动保存独立运行，停止输入后约 650 ms 保存。关闭实时预览后可在工作台手动刷新，导出 PDF 时仍会同步最新内容。",13)); panel.Children.Add(Card(performance));
        var diagnostics=Stack(10); diagnostics.Children.Add(Text("本次会话",18,true));
        diagnostics.Children.Add(Text($"模板首屏：{timings.GetValueOrDefault("firstPageReady"):0} ms · 本地服务：{timings.GetValueOrDefault("backendReady"):0} ms\n编辑预览实例：{webInitializations} · 整页加载：{previewNavigations} · 文档更新：{previewPatches}",13));
        diagnostics.Children.Add(Text("耗时从窗口创建起计算，供比较同机启动表现；不包含启动进程前的磁盘与系统调度时间。",11)); panel.Children.Add(Card(diagnostics)); FillScroll(panel);
        var storage=Stack(12); storage.Children.Add(Text("本机存储",20,true));
        var storageInfo=Text(StorageUsage(),13); storage.Children.Add(storageInfo);
        storage.Children.Add(Text("简历、职位收藏和投递记录保存在本机。上一版备份与最近 20 个历史版本压缩保存；预览磁盘缓存可随时重建。",12));
        var actions=Row(8);
        actions.Children.Add(Button("清理预览磁盘缓存",async ()=> { await ClearPreviewDiskCache(); storageInfo.Text=StorageUsage(); Success("缓存已清理","只清除了预览磁盘缓存，简历、备份、职位收藏和投递记录均保留。"); }));
        actions.Children.Add(Button("优化存储占用",async ()=> {await OptimizeStorage();storageInfo.Text=StorageUsage();Feedback("存储优化完成","已处理完成更新的临时载荷，保留最近一份完整程序备份及个人额外文件。");}));
        actions.Children.Add(Button("刷新占用",()=> { storageInfo.Text=StorageUsage(); return Task.CompletedTask; })); storage.Children.Add(actions);
        panel.Children.Add(Card(storage));
    }
    private string StorageUsage()
    {
        string root=api.DataDirectory;
        if(!Directory.Exists(root)) return "当前数据目录尚未创建。";
        long records=0,backups=0,logs=0,cache=0,other=0,updates=0;
        var pending=new Stack<string>(); pending.Push(root);
        while(pending.Count>0)
        {
            string directory=pending.Pop(); string[] files,subdirectories;
            try { files=Directory.GetFiles(directory); subdirectories=Directory.GetDirectories(directory); } catch(IOException) { continue; } catch(UnauthorizedAccessException) { continue; }
            foreach(string child in subdirectories)if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)pending.Push(child);
            foreach(string path in files)
            {
                long size; try {if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)continue;size=Services.StorageMaintenance.DiskBytes(path); } catch(IOException) { continue; } catch(UnauthorizedAccessException) { continue; }
                string relative=Path.GetRelativePath(root,path);
                if(relative.StartsWith("updates"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))updates+=size;
                else if(relative.StartsWith("WebView2"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) cache+=size;
                else if(relative.StartsWith("history"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path).EndsWith(".bak",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(path).EndsWith(".bak.gz",StringComparison.OrdinalIgnoreCase)) backups+=size;
                else if(Path.GetFileName(path).StartsWith("backend.log",StringComparison.OrdinalIgnoreCase)) logs+=size;
                else if(Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase)) records+=size;
                else other+=size;
            }
        }
        static string Size(long bytes)=>bytes<1024?$"{bytes} B":bytes<1024*1024?$"{bytes/1024d:0.0} KB":$"{bytes/1024d/1024d:0.00} MB";
        var programSize=Services.StorageMaintenance.DirectoryBytes(AppContext.BaseDirectory);long program=programSize.Disk,logical=programSize.Logical,oldPrograms=0;
        foreach(var directory in Directory.GetDirectories(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!,".resumestudio-backup-*"))
            oldPrograms+=Services.StorageMaintenance.DirectoryBytes(directory).Disk;
        return $"程序磁盘占用：{Size(program)}（逻辑大小 {Size(logical)}）\n更新临时缓存：{Size(updates)}  ·  旧程序备份：{Size(oldPrograms)}\n简历与求职记录：{Size(records)}  ·  历史与文档备份：{Size(backups)}\n浏览器数据（含登录状态）：{Size(cache)}  ·  日志：{Size(logs)}  ·  设置与其他：{Size(other)}\n合计：{Size(program+oldPrograms+updates+records+backups+cache+logs+other)}\n浏览结束后自动维护超过 64 MB 的 HTTP 磁盘缓存，保留登录状态。\n个人数据目录：{root}";
    }
    private async Task ClearPreviewDiskCache()
    {
        var core=preview?.CoreWebView2;
        if(core is not null) { await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache); return; }
        var temporary=new Microsoft.UI.Xaml.Controls.WebView2 { Width=1,Height=1,Visibility=Microsoft.UI.Xaml.Visibility.Collapsed };
        body.Children.Add(temporary);
        try
        {
            webEnvironment??=CreateWebEnvironment();
            await temporary.EnsureCoreWebView2Async(await webEnvironment);
            await temporary.CoreWebView2!.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
        }
        finally { body.Children.Remove(temporary); temporary.Close(); }
    }
}

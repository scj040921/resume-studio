using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    // Share materials across cards rather than creating a blur brush for every list item.
    private static Brush panelMaterial = new SolidColorBrush(Colors.White);
    private static Brush edgeMaterial = new SolidColorBrush(Colors.Transparent);
    private static Brush navigationMaterial = new SolidColorBrush(Colors.Transparent);
    private static Brush PanelMaterial => haruStyle ? panelMaterial : Brush(Surface);
    private static Brush EdgeMaterial => haruStyle ? edgeMaterial : Brush(Stroke);
    private bool nativeGlass;
    private RectInt32[] glassDragRegions=[];

    private void ConfigureWindowMaterial()
    {
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        bool acrylic=haruStyle&&Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported();
        SystemBackdrop=acrylic?new DesktopAcrylicBackdrop():new MicaBackdrop();
        nativeGlass=haruStyle&&NativeWindowGlass.Enable(handle,darkStyle);
        if(!haruStyle) NativeWindowGlass.Disable(handle);
        if (!haruStyle) { shell.Background=Brush(darkStyle?"#1B1E1C":"#F3F5F2"); return; }
        // Transparent hosts are essential: opaque NavigationView/card backgrounds cover the DWM acrylic.
        shell.Background=Brush(acrylic?(darkStyle?"#18191B25":"#14F7F7FA"):(darkStyle?"#252732":"#F7F7FA"));
        // One real window blur, with translucent tint on top. Per-card AcrylicBrush can fall back to
        // an opaque fill in desktop windows, hiding the glass; layering tints also avoids repeated blur work.
        // Blur belongs to the window and navigation; reading surfaces remain stable.
        panelMaterial=Brush(darkStyle?"#F2252827":"#F5FFFFFF");
        edgeMaterial=Brush(darkStyle?"#444946":"#DDE1DD");
        navigationMaterial=Brush(darkStyle?"#C8202422":"#C9EFF2EE");
    }

    private void ConfigureGlassNavigation(NavigationView nav)
    {
        foreach(string key in new[] {"NavigationViewContentBackground","NavigationViewExpandedPaneBackground","NavigationViewTopPaneBackground"})
            nav.Resources[key]=new SolidColorBrush(Colors.Transparent);
        nav.Resources["NavigationViewDefaultPaneBackground"]=navigationMaterial;
        nav.Resources["NavigationViewContentGridBorderBrush"]=edgeMaterial;
        nav.Loaded+=(_,_)=>ApplyGlassHosts(nav);
        nav.PaneOpened+=(_,_)=>ApplyGlassHosts(nav);
        nav.DisplayModeChanged+=(_,_)=>ApplyGlassHosts(nav);
    }

    private void ConfigureGlassTitleBar()
    {
        // The page header lives inside NavigationView; SetTitleBar(headerFrame) leaves
        // the visible window's top edge outside the drag region. Own both native caption
        // rectangles after layout, including changes to the navigation pane and DPI.
        SetTitleBar(null);
        ExtendsContentIntoTitleBar=haruStyle;
        glassDragRegions=[];
        if(!haruStyle) InputNonClientPointerSource.GetForWindowId(AppWindow.Id).ClearRegionRects(NonClientRegionKind.Caption);
        UpdateGlassHeaderMargin();
        if(Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar=AppWindow.TitleBar;
            if(haruStyle)
            {
                titleBar.ButtonBackgroundColor=Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor=Colors.Transparent;
                titleBar.ButtonForegroundColor=((SolidColorBrush)Brush(Ink)).Color;
                titleBar.ButtonInactiveForegroundColor=((SolidColorBrush)Brush(darkStyle?"#ADB6CC":"#697086")).Color;
            }
            else { titleBar.ButtonBackgroundColor=null; titleBar.ButtonInactiveBackgroundColor=null; titleBar.ButtonForegroundColor=null; titleBar.ButtonInactiveForegroundColor=null; }
        }
        UpdateGlassDragRegions();
    }

    private void UpdateGlassHeaderMargin()
    {
        if(headerFrame is null||contentHost is null)return;
        double scale=shell.XamlRoot?.RasterizationScale??1;
        double titleHeight=AppWindow.TitleBar.Height>0?AppWindow.TitleBar.Height/scale:32;
        double top=haruStyle?Math.Max(10,titleHeight-contentHost.Margin.Top+8):0;
        var margin=new Thickness(0,top,0,20);
        if(headerFrame.Margin!=margin)headerFrame.Margin=margin;
    }

    private void UpdateGlassDragRegions()
    {
        if(!haruStyle||shell.XamlRoot is null||headerFrame is null||!headerFrame.IsLoaded) return;
        double scale=shell.XamlRoot.RasterizationScale;
        int right=(int)Math.Floor(shell.ActualWidth*scale)-AppWindow.TitleBar.RightInset;
        var regions=new List<RectInt32>();
        // Leave the NavigationView menu button at the left and native caption buttons
        // at the right interactive. The otherwise empty top edge also moves the window.
        int left=Math.Max(AppWindow.TitleBar.LeftInset,(int)Math.Ceiling(48*scale));
        if(right>left) regions.Add(new RectInt32(left,0,right-left,(int)Math.Ceiling(32*scale)));
        var bounds=headerFrame.TransformToVisual(shell).TransformBounds(new Windows.Foundation.Rect(0,0,headerFrame.ActualWidth,headerFrame.ActualHeight));
        int x=Math.Max(0,(int)Math.Ceiling(bounds.X*scale)),y=Math.Max(0,(int)Math.Ceiling(bounds.Y*scale));
        int headerRight=y>=AppWindow.TitleBar.Height?(int)Math.Floor(shell.ActualWidth*scale):right;
        int end=Math.Min(headerRight,(int)Math.Floor((bounds.X+bounds.Width)*scale)),bottom=(int)Math.Floor((bounds.Y+bounds.Height)*scale);
        if(end>x&&bottom>y) regions.Add(new RectInt32(x,y,end-x,bottom-y));
        var next=regions.ToArray();
        if(glassDragRegions.SequenceEqual(next)) return;
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Caption,next);
        glassDragRegions=next;
    }

    private static void ApplyGlassHosts(DependencyObject parent)
    {
        // Set actual template hosts too: visual states can override the default resource setter.
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is Grid grid && grid.Name=="ContentGrid") grid.Background=new SolidColorBrush(Colors.Transparent);
            if(child is SplitView split && split.Pane is Panel pane) pane.Background=navigationMaterial;
            ApplyGlassHosts(child);
        }
    }
}

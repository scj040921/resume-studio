using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ResumeStudio;

// Natural-width controls retain reading and tab order when a toolbar wraps.
public sealed class FlowPanel : Panel
{
    public double Spacing {get;set;}=8;
    protected override Size MeasureOverride(Size available)
    {
        double limit=double.IsFinite(available.Width)?Math.Max(0,available.Width):double.MaxValue;
        double x=0,y=0,line=0,width=0;
        foreach(var child in Children){
            if(child.Visibility==Visibility.Collapsed)continue;
            child.Measure(new Size(limit,double.PositiveInfinity));var size=child.DesiredSize;
            if(x>0&&x+size.Width>limit){width=Math.Max(width,x-Spacing);y+=line+Spacing;x=0;line=0;}
            x+=size.Width+Spacing;line=Math.Max(line,size.Height);
        }
        return new Size(Math.Min(limit,Math.Max(width,Math.Max(0,x-Spacing))),y+line);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x=0,y=0,line=0;
        foreach(var child in Children){
            if(child.Visibility==Visibility.Collapsed)continue;
            var size=child.DesiredSize;
            if(x>0&&x+size.Width>finalSize.Width){y+=line+Spacing;x=0;line=0;}
            child.Arrange(new Rect(x,y,Math.Min(size.Width,finalSize.Width),size.Height));
            x+=size.Width+Spacing;line=Math.Max(line,size.Height);
        }
        return finalSize;
    }
}

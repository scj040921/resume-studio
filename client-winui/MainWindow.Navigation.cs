using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, NavigationViewItem> modernNavigationItems = new();
    private readonly Dictionary<string, StackPanel> classicChildMenus = new();
    private readonly Dictionary<string, TextBlock> classicLabels = new();
    private readonly Dictionary<string, Border> classicUnderlines = new();
    private readonly Dictionary<string, FontIcon> classicChevrons = new();
    private readonly HashSet<string> expandedNavigationGroups = new() { "discovery" };
    private Grid? classicSidebar;
    private FrameworkElement? classicBrandName, classicBrandSubtitle, classicFooter;
    private bool classicCompact;

    private NavigationViewItem CreateModernNavigationItem(NavigationNode node)
    {
        var item = new NavigationViewItem
        {
            Content = node.Label, Tag = node.Key,
            Icon = new FontIcon { Glyph = node.Glyph, FontSize = 16 },
            SelectsOnInvoked = !node.IsGroup,
            IsExpanded = expandedNavigationGroups.Contains(node.Key)
        };
        modernNavigationItems.Add(node.Key, item);
        foreach(var child in node.Children) item.MenuItems.Add(CreateModernNavigationItem(child));
        return item;
    }

    private void BuildClassicNavigationMenu(StackPanel menu, IEnumerable<NavigationNode> nodes, int depth = 0)
    {
        foreach(var node in nodes)
        {
            var key = node.Key;
            var button = Button(node.Label, () => node.IsGroup ? ToggleClassicNavigationGroup(key) : Navigate(key));
            button.Tag = key; button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Foreground = Brush(Ink); button.Background = Brush(darkStyle?"#202422":"#EDF1EC");
            button.BorderThickness = new Thickness(0); button.Padding = new Thickness(12, 12, 12, 12);
            var content = new Grid { ColumnSpacing = 10 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(new FontIcon { Glyph = node.Glyph, FontSize = 16 });
            var label = Text(node.Label, 14); label.Foreground = Brush(Ink);
            label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(label, 1); content.Children.Add(label); classicLabels.Add(key, label);
            var underline=new Border {Height=2,VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,0,0,-5),CornerRadius=new CornerRadius(1),Background=Brush(Accent),Opacity=0,IsHitTestVisible=false};
            Grid.SetColumn(underline,1);content.Children.Add(underline);classicUnderlines.Add(key,underline);
            if(node.IsGroup)
            {
                var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 10 };
                Grid.SetColumn(chevron, 2); content.Children.Add(chevron); classicChevrons.Add(key, chevron);
                var flyout = new MenuFlyout();
                AddNavigationFlyoutItems(flyout.Items, node.Children);
                // The attached flyout opens only in compact mode. The full sidebar uses inline children.
                Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.SetAttachedFlyout(button, flyout);
            }
            button.Content = content; navigation.Add(button); menu.Children.Add(button);
            if(!node.IsGroup) continue;
            var children = Stack(5); children.Margin = new Thickness(20, 0, 0, 0);
            classicChildMenus.Add(key, children); menu.Children.Add(children);
            BuildClassicNavigationMenu(children, node.Children, depth + 1);
        }
    }

    private void AddNavigationFlyoutItems(IList<MenuFlyoutItemBase> items, IEnumerable<NavigationNode> nodes)
    {
        foreach(var node in nodes)
        {
            if(node.IsGroup)
            {
                var group = new MenuFlyoutSubItem { Text = node.Label };
                AddNavigationFlyoutItems(group.Items, node.Children); items.Add(group);
            }
            else
            {
                string key = node.Key;
                var item = new MenuFlyoutItem { Text = node.Label, Tag = key, Icon = new FontIcon { Glyph = node.Glyph } };
                item.Click += async (_, _) => await Run(() => Navigate(key)); items.Add(item);
            }
        }
    }

    private Task ToggleClassicNavigationGroup(string key)
    {
        if(classicCompact)
        {
            var button = navigation.Single(x => (string)x.Tag == key);
            Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(button);
        }
        else
        {
            if(!expandedNavigationGroups.Add(key)) expandedNavigationGroups.Remove(key);
            UpdateClassicNavigationGroups();
        }
        return Task.CompletedTask;
    }

    private void UpdateClassicNavigationGroups()
    {
        foreach(var (key, menu) in classicChildMenus)
        {
            bool expanded = expandedNavigationGroups.Contains(key);
            menu.Visibility = expanded && !classicCompact ? Visibility.Visible : Visibility.Collapsed;
            classicChevrons[key].Glyph = expanded ? "\uE70E" : "\uE70D";
            classicChevrons[key].Visibility = classicCompact ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void UpdateClassicNavigationLayout(double width)
    {
        if(classicSidebar is null || shell.ColumnDefinitions.Count < 2) return;
        classicCompact = width > 0 && width < 900;
        shell.ColumnDefinitions[0].Width = new GridLength(classicCompact ? 64 : 222);
        classicSidebar.Padding = classicCompact ? new Thickness(7, 20, 7, 12) : new Thickness(17, 28, 17, 20);
        foreach(var label in classicLabels.Values) label.Visibility = classicCompact ? Visibility.Collapsed : Visibility.Visible;
        foreach(var element in new[] { classicBrandName, classicBrandSubtitle, classicFooter })
            if(element is not null) element.Visibility = classicCompact ? Visibility.Collapsed : Visibility.Visible;
        UpdateClassicNavigationGroups();
    }
}

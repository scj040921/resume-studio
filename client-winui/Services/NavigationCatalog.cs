namespace ResumeStudio.Services;

public sealed record NavigationNode(string Key, string Label, string Glyph, bool IsFooter = false, params NavigationNode[] Children)
{
    public bool IsGroup => Children.Length > 0;
}

/// <summary>Shared routes for the classic sidebar and Haru NavigationView.</summary>
public static class NavigationCatalog
{
    public static IReadOnlyList<NavigationNode> Roots { get; } = new NavigationNode[]
    {
        new("home", "求职首页", "\uE80F"),
        new("templates", "模板中心", "\uE8A5"),
        new("resumes", "我的简历", "\uE8A5"),
        new("discovery", "发现职位", "\uE721", false,
            new("jobs", "主页面", "\uE80F"),
            new("sources", "公司来源管理", "\uE774")),
        new("favorites", "职位收藏", "\uE734"),
        new("applications", "投递记录", "\uE73E"),
        new("schedule", "求职日程", "\uE787"),
        new("interviews", "面试题背诵", "\uE82D"),
        new("exams", "笔试题复盘", "\uE9D9"),
        new("about", "使用说明", "\uE946", true),
        new("settings", "设置", "\uE713", true)
    };

    public static string SelectedKey(string page) => page == "editor" ? "resumes" : page;

    public static string? ParentKey(string page)
    {
        string selected = SelectedKey(page);
        return FindParent(Roots, selected);
    }

    private static string? FindParent(IEnumerable<NavigationNode> nodes, string key)
    {
        foreach(var node in nodes)
        {
            if(node.Children.Any(child => child.Key == key)) return node.Key;
            var parent = FindParent(node.Children, key);
            if(parent is not null) return parent;
        }
        return null;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private ListView? visibleJobList;
    private string favoriteQuery="",favoriteSource="全部平台";
    private int favoritePage=1,favoritePageSize=20,favoriteSort;
    private void ResetFavoriteView(){favoriteQuery="";favoriteSource="全部平台";favoritePage=1;favoriteSort=0;}
    private async Task ShowFavorites()
    {
        int generation=navigationRevision;
        Heading("职位收藏","跨平台收藏到拾简，按平台与关键词整理。");
        var jobs=await api.Get<List<Job>>("favorites"); if(!PageIsCurrent("favorites",generation)) return;
        var searchIndex=jobs.Select(job=>(Job:job,Text:string.Concat(job.Title," ",job.Company," ",job.Notes," ",job.Tags))).ToList();
        body.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto}); body.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});body.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
        var controls=Stack(12); controls.Margin=new Thickness(0,0,0,14); controls.Children.Add(Button("＋ 从 BOSS 等平台链接收藏",ImportExternalFavorite,true));
        var browsing=Flow();browsing.Children.Add(Button("BOSS 浏览并收藏",()=>OpenPlatformCaptureAsync("boss")));browsing.Children.Add(Button("牛客浏览并收藏",()=>OpenPlatformCaptureAsync("nowcoder")));controls.Children.Add(browsing);
        var filters=new Grid {ColumnSpacing=8,RowSpacing=8};filters.ColumnDefinitions.Add(new ColumnDefinition());filters.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});filters.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});filters.RowDefinitions.Add(new RowDefinition());filters.RowDefinitions.Add(new RowDefinition()); var query=new TextBox { PlaceholderText="搜索职位、公司、标签或备注",Text=favoriteQuery,HorizontalAlignment=HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(query,"搜索职位收藏");
        var sources=new[] {"全部平台"}.Concat(jobs.Select(j=>j.Source).Where(s=>s.Length>0).Distinct()).ToList();
        var source=new ComboBox { ItemsSource=sources,SelectedItem=sources.Contains(favoriteSource)?favoriteSource:sources[0],Width=180 }; filters.Children.Add(query);Grid.SetColumn(source,1);filters.Children.Add(source);
        var reset=Button("清空筛选",()=>{query.Text="";source.SelectedIndex=0;query.Focus(FocusState.Programmatic);return Task.CompletedTask;});Grid.SetColumn(reset,2);filters.Children.Add(reset);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(source,"按收藏来源筛选");
        void ArrangeFilters(double width){bool compact=width<620;Grid.SetColumnSpan(query,compact?3:1);Grid.SetRow(source,compact?1:0);Grid.SetColumn(source,compact?0:1);Grid.SetRow(reset,compact?1:0);Grid.SetColumn(reset,2);source.HorizontalAlignment=HorizontalAlignment.Left;}
        filters.SizeChanged+=(_,e)=>ArrangeFilters(e.NewSize.Width);ArrangeFilters(0);controls.Children.Add(filters);
        var organize=Flow(10);organize.Children.Add(Button("标签／批量管理／截止提示",()=>ShowWorkspaceTools("favorites")));
        var sort=new ComboBox {ItemsSource=new[] {"最近更新","按公司名称","按职位名称","截止时间优先"},SelectedIndex=favoriteSort,Width=170};sort.Tag="favorite-sort";Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(sort,"收藏排序");organize.Children.Add(sort);controls.Children.Add(organize);
        var count=Text("",12); controls.Children.Add(count); body.Children.Add(controls);
        var list=CreateJobList(true); Grid.SetRow(list,1); body.Children.Add(list);
        var emptyText=Text("",14);var emptyTitle=Text("",18,true);var emptyContent=Stack(10);emptyContent.Children.Add(emptyTitle);emptyContent.Children.Add(emptyText);
        var emptyAction=Button("清空筛选",()=>{query.Text="";source.SelectedIndex=0;return Task.CompletedTask;});emptyContent.Children.Add(emptyAction);
        var empty=Card(emptyContent);empty.VerticalAlignment=VerticalAlignment.Top;empty.Visibility=Visibility.Collapsed;Grid.SetRow(empty,1);body.Children.Add(empty);
        // Keep page controls outside the scrolling list, so old favorites remain reachable.
        var footer=Stack(6);footer.Margin=new Thickness(0,12,0,0);
        var pager=Row(8);pager.HorizontalAlignment=HorizontalAlignment.Center;
        var previous=new Button {Content="上一页",Padding=new Thickness(12,8,12,8),FontFamily=ResumeTypography.InterfaceFont};
        var next=new Button {Content="下一页",Padding=new Thickness(12,8,12,8),FontFamily=ResumeTypography.InterfaceFont};ButtonMotion(previous);ButtonMotion(next);
        var pages=new ComboBox {Width=90,Tag="favorite-pages"};ToolTipService.SetToolTip(pages,"跳转到指定页");
        int[] sizes=[10,20,50];var size=new ComboBox {ItemsSource=sizes.Select(n=>$"{n} 条/页").ToList(),SelectedIndex=Array.IndexOf(sizes,favoritePageSize),Width=115,Tag="favorite-page-size"};ToolTipService.SetToolTip(size,"每页显示的收藏数");
        pager.Children.Add(previous);pager.Children.Add(pages);pager.Children.Add(next);pager.Children.Add(size);footer.Children.Add(pager);
        var pageSummary=Text("",12);pageSummary.HorizontalAlignment=HorizontalAlignment.Center;footer.Children.Add(pageSummary);Grid.SetRow(footer,2);body.Children.Add(footer);
        bool refreshing=false;
        void Refresh(bool resetPage=false)
        {
            if(!PageIsCurrent("favorites",generation))return;
            string selected=source.SelectedItem as string??"全部平台";
            favoriteQuery=query.Text;favoriteSource=selected;
            string text=query.Text;
            var found=searchIndex.Where(entry=>(selected=="全部平台"||entry.Job.Source==selected)&&(text.Length==0||entry.Text.Contains(text,StringComparison.OrdinalIgnoreCase))).Select(entry=>entry.Job).ToList();
            favoriteSort=sort.SelectedIndex;
            if(favoriteSort==1)found=found.OrderBy(j=>j.Company,StringComparer.CurrentCultureIgnoreCase).ThenBy(j=>j.Title,StringComparer.CurrentCultureIgnoreCase).ToList();
            else if(favoriteSort==2)found=found.OrderBy(j=>j.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(j=>j.Company,StringComparer.CurrentCultureIgnoreCase).ToList();
            else if(favoriteSort==3)found=found.OrderBy(j=>DateTimeOffset.TryParse(j.DeadlineAt,out var deadline)?deadline:DateTimeOffset.MaxValue).ToList();
            bool hasFilter=query.Text.Length>0||selected!="全部平台";
            empty.Visibility=found.Count==0?Visibility.Visible:Visibility.Collapsed;list.Visibility=found.Count==0?Visibility.Collapsed:Visibility.Visible;
            emptyTitle.Text=jobs.Count==0?"保留值得投递的机会":"没有找到匹配的职位";
            emptyText.Text=jobs.Count==0?"从上方粘贴职位链接，或浏览 BOSS、牛客的职位详情后收藏。":"试试公司名或更短的关键词，也可以清空筛选查看全部收藏。";
            emptyAction.Visibility=hasFilter?Visibility.Visible:Visibility.Collapsed;reset.IsEnabled=hasFilter;
            int pageCount=(found.Count+favoritePageSize-1)/favoritePageSize;
            footer.Visibility=found.Count==0?Visibility.Collapsed:Visibility.Visible;
            favoritePage=resetPage?1:Math.Clamp(favoritePage,1,Math.Max(1,pageCount));
            int offset=(favoritePage-1)*favoritePageSize;var pageJobs=found.Skip(offset).Take(favoritePageSize).ToList();
            list.ItemsSource=pageJobs;if(pageJobs.Count>0)list.ScrollIntoView(pageJobs[0]);
            count.Text=$"共 {jobs.Count} 条收藏 · 筛选结果 {found.Count} 条 · {selected}"+(found.Count==0?(jobs.Count==0?"。可浏览职位详情或粘贴链接添加。":"。可清空筛选查看全部收藏。") : "");
            refreshing=true;
            try {pages.ItemsSource=Enumerable.Range(1,pageCount).Select(n=>$"第 {n} 页").ToList();pages.SelectedIndex=pageCount==0?-1:favoritePage-1;pages.IsEnabled=pageCount>1;}
            finally {refreshing=false;}
            previous.IsEnabled=favoritePage>1;next.IsEnabled=favoritePage<pageCount;
            pageSummary.Text=pageCount==0?"没有匹配的收藏":$"第 {favoritePage} / {pageCount} 页 · 显示第 {offset+1}–{offset+pageJobs.Count} 条";
        }
        previous.Click+=(_,_)=>{if(favoritePage>1){favoritePage--;Refresh();}};
        next.Click+=(_,_)=>{favoritePage++;Refresh();};
        pages.SelectionChanged+=(_,_)=>{if(!refreshing&&pages.SelectedIndex>=0){favoritePage=pages.SelectedIndex+1;Refresh();}};
        size.SelectionChanged+=(_,_)=>{if(size.SelectedIndex>=0){favoritePageSize=sizes[size.SelectedIndex];Refresh(true);}};
        sort.SelectionChanged+=(_,_)=>Refresh(true);query.TextChanged+=(_,_)=>Refresh(true); source.SelectionChanged+=(_,_)=>Refresh(true); Refresh();
    }
    private ListView CreateJobList(bool favorite)
    {
        var style=new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(0,0,0,12)));
        var list=new ListView { SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=false,ItemContainerStyle=style,HorizontalContentAlignment=HorizontalAlignment.Stretch,
            ItemTemplate=(DataTemplate)Application.Current.Resources["JobCardTemplate"],Tag=new JobCardRenderer(job=>JobCard(job,favorite)) };
        // ItemsStackPanel inside ListView realizes cards only around the viewport.
        // Never wrap this list in another ScrollViewer: that would disable virtualization.
        visibleJobList=list;
        return list;
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResumeStudio.Services;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ResumeStudio;

public sealed partial class MainWindow
{
    private static readonly string[] ContentKinds=["自由文本","条目列表","经历列表","目录板块","图片","表格"];
    private async Task AddEditorModule()
    {
        if(current is null)return;
        var target=current;
        if(target.Sections.Count>=30)throw new IOException("每份简历最多 30 个模块。");
        var fields=Stack();var name=new TextBox {Header="模块标题",Text="自定义模块",MaxLength=100};
        var kind=new ComboBox {Header="内容类型",ItemsSource=ContentKinds,SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
        fields.Children.Add(name);fields.Children.Add(kind);
        fields.Children.Add(Text("可在同一模块组合文字、经历、子板块、图片和表格。正文始终可以直接编辑。",12));
        var result=await SaveForm("添加模块",fields,"添加",()=> {
            if(current!=target)throw new IOException("简历已切换，请重新添加。");
            if(string.IsNullOrWhiteSpace(name.Text))throw new IOException("请填写模块标题。");
            target.Sections.Add(new ResumeSection {Title=name.Text.Trim()});selectedEditorPart=target.Sections.Count+1;Changed();return SaveCurrent();
        }).ShowAsync();
        if(result!=ContentDialogResult.Primary)return;
        await OpenEditor();
        var input=FindTextField(editorFields!,"内容");
        if(input is not null&&kind.SelectedIndex>0)await InsertContent(input,kind.SelectedIndex);
    }
    private UIElement ProfileItemsEditor(Profile profile)
    {
        var fields=Stack(10);
        fields.Children.Add(Text("补充学历、政治面貌、语言或其他信息；空内容不会出现在简历上。",12));
        foreach(var item in profile.Items.ToList()) {
            var row=Stack(6);
            var emoji=Field("图标 / Emoji",item.Emoji,v=>item.Emoji=v);emoji.MaxLength=16;row.Children.Add(emoji);
            var label=Field("信息标签",item.Label,v=>item.Label=v);label.MaxLength=60;row.Children.Add(label);
            var value=Field("信息内容",item.Value,v=>item.Value=v);value.MaxLength=500;row.Children.Add(value);
            row.Children.Add(Button("删除此信息项",()=> {profile.Items.Remove(item);Changed();ShowEditorPart();return Task.CompletedTask;}));
            fields.Children.Add(new Expander {Header=string.IsNullOrWhiteSpace(item.Label)?"新信息项":item.Label,Content=row,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch});
        }
        fields.Children.Add(Button("＋ 自定义信息项",()=> {
            if(profile.Items.Count>=16)throw new IOException("最多添加 16 个信息项。");
            profile.Items.Add(new ProfileItem());Changed();ShowEditorPart();return Task.CompletedTask;
        }));return fields;
    }
    private UIElement ContentTools(TextBox input,ResumeSection section)
    {
        var tools=Stack(8);
        var bar=new CommandBar {DefaultLabelPosition=CommandBarDefaultLabelPosition.Right,Background=PanelMaterial,IsOpen=false};
        for(int i=1;i<ContentKinds.Length;i++) {
            int kind=i;var button=EditorAction(ContentKinds[i],i==4?Symbol.Pictures:i==5?Symbol.List:Symbol.Add,()=>InsertContent(input,kind));
            ToolTipService.SetToolTip(button,i==5?"光标在表格内时编辑该表格，否则插入新表格":i==3?"添加一层子板块，可包含文字、列表、经历、图片或表格":"插入"+ContentKinds[i]);
            if(i<3)bar.PrimaryCommands.Add(button);else bar.SecondaryCommands.Add(button);
        }
        tools.Children.Add(bar);
        foreach(var image in section.Images.ToList()) {
            var fields=Stack(8);var alt=Field("图片替代说明",image.Alt,v=>image.Alt=v);alt.MaxLength=500;fields.Children.Add(alt);
            var caption=Field("图片标题",image.Caption,v=>image.Caption=v);caption.MaxLength=1000;fields.Children.Add(caption);
            var width=new Slider {Header="图片最大宽度（%）",Minimum=10,Maximum=100,StepFrequency=5,Value=image.WidthPercent};width.ValueChanged+=(_,_)=>{image.WidthPercent=width.Value;Changed();};fields.Children.Add(width);
            var height=new Slider {Header="图片最大高度（mm）",Minimum=20,Maximum=170,StepFrequency=5,Value=image.MaxHeightMm};height.ValueChanged+=(_,_)=>{image.MaxHeightMm=height.Value;Changed();};fields.Children.Add(height);
            fields.Children.Add(Button("替换图片",async ()=> {var data=await PickModuleImage();if(data is null)return;ResumeLayout.ReplaceImage(current??throw new IOException("请先打开简历。"),image,data,BackendClient.Json);Changed();}));
            fields.Children.Add(Button("移除图片",()=> {section.Images.Remove(image);section.Content=section.Content.Replace(ResumeLayout.ImageToken(image),"");Changed();ShowEditorPart();return Task.CompletedTask;}));
            tools.Children.Add(new Expander {Header=string.IsNullOrWhiteSpace(image.Alt)?"模块图片":image.Alt,Content=fields,HorizontalAlignment=HorizontalAlignment.Stretch});
        }
        return tools;
    }
    private async Task InsertContent(TextBox input,int kind)
    {
        if(current is null||selectedEditorPart<2)return;
        var target=current;var section=target.Sections[selectedEditorPart-2];
        var fields=Stack(10);var title=new TextBox {Header="子板块标题",MaxLength=100};
        var childKind=new ComboBox {Header="子板块内容",ItemsSource=new[] {"自由文本","条目列表","经历列表","图片","表格"},SelectedIndex=0,HorizontalAlignment=HorizontalAlignment.Stretch};
        bool directory=kind==3;
        if(directory){fields.Children.Add(title);fields.Children.Add(childKind);}
        var editor=Stack(10);fields.Children.Add(editor);
        var text=new TextBox {Header="内容（每行一项）",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=150,MaxLength=28000};
        var name=new TextBox {Header="名称 / 单位 / 学校",MaxLength=250};var role=new TextBox {Header="角色 / 专业 / 学历",MaxLength=300};
        var start=new TextBox {Header="开始时间",MaxLength=60};var end=new TextBox {Header="结束时间",MaxLength=60};
        var description=new TextBox {Header="职责与成果（每行一项）",AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=120,MaxLength=28000};
        var alt=new TextBox {Header="图片替代说明",MaxLength=500};var caption=new TextBox {Header="图片标题",MaxLength=1000};
        string? imageData=null;
        var imageState=Text("请选择本机 PNG、JPEG 或 WebP 图片，导入后自动压缩。",12);
        var pick=Button("选择图片",async ()=> {var data=await PickModuleImage();if(data is not null){imageData=data;imageState.Text="图片已就绪；点击插入后写入简历。";}});
        var tableRange=directory?null:ResumeLayout.ReadTable(NormalizeLines(input.Text),input.SelectionStart);
        List<List<string>> rows=tableRange?.Rows??[["类别","内容"],["",""]];
        if(rows.Count==0)rows=[["",""]];int columns=rows.Max(row=>row.Count);foreach(var row in rows)while(row.Count<columns)row.Add("");
        var tableEditor=Stack(8);
        void DrawTable() {
            tableEditor.Children.Clear();
            var commands=new CommandBar {DefaultLabelPosition=CommandBarDefaultLabelPosition.Right,Background=PanelMaterial,IsOpen=false};
            commands.PrimaryCommands.Add(EditorAction("添加行",Symbol.Add,()=> {if(rows.Count>=20)throw new IOException("最多 20 行。");rows.Add(Enumerable.Repeat("",rows[0].Count).ToList());DrawTable();return Task.CompletedTask;}));
            commands.PrimaryCommands.Add(EditorAction("添加列",Symbol.Add,()=> {if(rows[0].Count>=8)throw new IOException("最多 8 列。");foreach(var row in rows)row.Add("");DrawTable();return Task.CompletedTask;}));
            tableEditor.Children.Add(commands);
            var grid=new Grid {ColumnSpacing=6,RowSpacing=6};
            for(int col=0;col<rows[0].Count;col++)grid.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(155)});
            grid.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
            for(int r=0;r<rows.Count;r++) {
                int rowIndex=r;grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
                for(int c=0;c<rows[r].Count;c++) {
                    int columnIndex=c;var cell=new TextBox {Header=r==0?"表头 "+(c+1):null,Text=rows[r][c],AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinWidth=145,MaxLength=1500};
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cell,$"第 {r+1} 行第 {c+1} 列");
                    cell.TextChanged+=(_,_)=>rows[rowIndex][columnIndex]=NormalizeLines(cell.Text);Grid.SetRow(cell,r);Grid.SetColumn(cell,c);grid.Children.Add(cell);
                }
                var remove=Button("删除行",()=> {rows.RemoveAt(rowIndex);DrawTable();return Task.CompletedTask;});remove.IsEnabled=rows.Count>1;Grid.SetRow(remove,r);Grid.SetColumn(remove,rows[r].Count);grid.Children.Add(remove);
            }
            int footer=rows.Count;grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
            for(int c=0;c<rows[0].Count;c++){int column=c;var remove=Button("删除列",()=> {foreach(var row in rows)row.RemoveAt(column);DrawTable();return Task.CompletedTask;});remove.IsEnabled=rows[0].Count>1;Grid.SetRow(remove,footer);Grid.SetColumn(remove,c);grid.Children.Add(remove);}
            tableEditor.Children.Add(new ScrollViewer {Content=grid,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollMode=ScrollMode.Enabled,VerticalScrollMode=ScrollMode.Disabled,MaxWidth=650});
            tableEditor.Children.Add(Text("第一行为表头；可增删行列和编辑单元格。插入后仍可在正文中修改，或将光标放回表格再次打开。",12));
        }
        int SelectedKind()=>directory?childKind.SelectedIndex switch {3=>4,4=>5,var value=>value}:kind;
        void Draw() {
            editor.Children.Clear();int selected=SelectedKind();
            if(selected==2){foreach(var field in new UIElement[] {name,role,start,end,description})editor.Children.Add(field);}
            else if(selected==4){foreach(var field in new UIElement[] {imageState,pick,alt,caption})editor.Children.Add(field);}
            else if(selected==5){DrawTable();editor.Children.Add(tableEditor);}
            else {text.Header=selected==1?"条目（每行一项）":"正文";editor.Children.Add(text);}
        }
        childKind.SelectionChanged+=(_,_)=>Draw();Draw();
        bool addedImage=false;
        await SaveForm(directory?"添加目录子板块":kind==5&&tableRange is not null?"编辑表格":"插入"+ContentKinds[kind],fields,"插入 / 应用",()=> {
            if(current!=target||!target.Sections.Contains(section))throw new IOException("简历或模块已切换，请重新操作。");
            int selected=SelectedKind();string content;
            ResumeImage? image=null;
            if(directory&&string.IsNullOrWhiteSpace(title.Text))throw new IOException("请填写子板块标题。");
            if(selected==4){if(imageData is null)throw new IOException("请先选择图片。");if(section.Images.Count>=12)throw new IOException("每个模块最多 12 张图片。");EnsureDocumentBudget(imageData.Length);image=new ResumeImage {DataUrl=imageData,Alt=alt.Text,Caption=caption.Text};content=ResumeLayout.ImageToken(image);}
            else if(selected==5)content=ResumeLayout.Table(rows);
            else if(selected==2){if(string.IsNullOrWhiteSpace(name.Text))throw new IOException("请填写经历名称。");content=ResumeLayout.Experience(name.Text,role.Text,start.Text,end.Text,description.Text);}
            else content=selected==1?ResumeLayout.List(NormalizeLines(text.Text)):NormalizeLines(text.Text).Trim();
            if(string.IsNullOrWhiteSpace(content))throw new IOException("请填写内容。");
            if(directory)content="### "+title.Text.Trim()+"\n"+content;
            string source=NormalizeLines(input.Text);int from=input.SelectionStart,length=input.SelectionLength;
            if(selected==5&&tableRange is not null&&!directory){from=tableRange.Start;length=tableRange.Length;}
            string inserted=(from>0&&source[from-1]!='\n'?"\n":"")+content+(from+length<source.Length&&source[from+length]!='\n'?"\n":"");
            string result=source[..from]+inserted+source[(from+length)..];
            if(result.Length>30000)throw new IOException("模块正文不能超过 3 万字符，请拆分成多个模块。");
            FinalizeUndoBurst();
            if(image is not null){section.Images.Add(image);addedImage=true;}
            input.Text=result;input.Select(from,inserted.Length);return Task.CompletedTask;
        }).ShowAsync();
        // Refresh the image editors after the modal has released its visual tree.
        if(addedImage&&current==target&&selectedEditorPart>=2&&target.Sections.ElementAtOrDefault(selectedEditorPart-2)==section)ShowEditorPart();
    }
    private void EnsureDocumentBudget(int extraCharacters)
    {
        if(current is not null&&System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(current,BackendClient.Json))+extraCharacters>1_850_000)
            throw new IOException("简历接近 2 MB 上限，请先移除不用的图片或减少图片大小。");
    }
    private async Task<string?> PickModuleImage()
    {
        var path=DesktopFileDialog.Open(WinRT.Interop.WindowNative.GetWindowHandle(this),"插入模块图片",[".png",".jpg",".jpeg",".webp"]);
        return path is null?null:await ReadModuleImage(path);
    }
    private static async Task<string> ReadModuleImage(string path)
    {
        if(new FileInfo(path).Length>20*1024*1024)throw new IOException("请选择小于 20 MiB 的图片。");
        var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(path);using var input=await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
        var decoder=await BitmapDecoder.CreateAsync(input);if((long)decoder.PixelWidth*decoder.PixelHeight>40_000_000)throw new IOException("图片不能超过 4000 万像素。");
        double scale=Math.Min(1,Math.Min(1200d/decoder.OrientedPixelWidth,900d/decoder.OrientedPixelHeight));
        uint width=(uint)Math.Max(1,Math.Round(decoder.OrientedPixelWidth*scale)),height=(uint)Math.Max(1,Math.Round(decoder.OrientedPixelHeight*scale));
        // WIC scales in source coordinates before applying EXIF rotation; the encoder
        // receives the final oriented dimensions so rows retain their correct stride.
        uint sourceWidth=(uint)Math.Max(1,Math.Round(decoder.PixelWidth*scale)),sourceHeight=(uint)Math.Max(1,Math.Round(decoder.PixelHeight*scale));
        var decoded=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,new BitmapTransform {ScaledWidth=sourceWidth,ScaledHeight=sourceHeight,InterpolationMode=BitmapInterpolationMode.Fant},ExifOrientationMode.RespectExifOrientation,ColorManagementMode.DoNotColorManage);
        var pixels=decoded.DetachPixelData();bool transparent=false;for(int i=3;i<pixels.Length;i+=4)if(pixels[i]<255){transparent=true;break;}
        if(transparent){string png=await EncodeModuleImage(pixels,width,height,true);if(ResumeLayout.ValidImage(png))return png;}
        // JPEG has no alpha channel. Flatten on white when a transparent PNG exceeds the budget.
        if(transparent)for(int i=0;i<pixels.Length;i+=4){int alpha=pixels[i+3];for(int c=0;c<3;c++)pixels[i+c]=(byte)((pixels[i+c]*alpha+255*(255-alpha))/255);pixels[i+3]=255;}
        string data=await EncodeModuleImage(pixels,width,height,false);if(!ResumeLayout.ValidImage(data))throw new IOException("压缩后图片仍超过上限，请缩小图片后重试。");return data;
    }
    private static async Task<string> EncodeModuleImage(byte[] pixels,uint width,uint height,bool png)
    {
        using var output=new InMemoryRandomAccessStream();var properties=new BitmapPropertySet();if(!png)properties["ImageQuality"]=new BitmapTypedValue(.78f,Windows.Foundation.PropertyType.Single);
        var encoder=await BitmapEncoder.CreateAsync(png?BitmapEncoder.PngEncoderId:BitmapEncoder.JpegEncoderId,output,properties);encoder.SetPixelData(BitmapPixelFormat.Bgra8,png?BitmapAlphaMode.Straight:BitmapAlphaMode.Ignore,width,height,96,96,pixels);await encoder.FlushAsync();output.Seek(0);
        using var reader=new DataReader(output.GetInputStreamAt(0));await reader.LoadAsync((uint)output.Size);var bytes=new byte[(int)output.Size];reader.ReadBytes(bytes);
        return "data:image/"+(png?"png":"jpeg")+";base64,"+Convert.ToBase64String(bytes);
    }
}

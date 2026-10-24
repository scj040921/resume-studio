using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace ResumeStudio.Services;

public sealed record InterviewBlock(string Text,int Level=0);
public sealed record InterviewImportDocument(string SourceName,IReadOnlyList<InterviewBlock> Blocks,string Rule="文档小标题")
{
    public int[] HeadingLevels=>Blocks.Where(b=>b.Level>0&&!string.IsNullOrWhiteSpace(b.Text)).Select(b=>b.Level).Distinct().Order().ToArray();
}

/// <summary>Reads text only; document macros, images and original files are not retained.</summary>
public static partial class InterviewImport
{
    public const int MaxTextLength=500000;
    private static readonly XNamespace W="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    [GeneratedRegex(@"^\s*(#{1,6})\s+(.+?)\s*#*\s*$")] private static partial Regex MarkdownHeading();
    [GeneratedRegex(@"^\s*(?:\d{1,4}(?:\.\d{1,3})*[.、．)）:：]\s*|[（(][\d一二三四五六七八九十百]+[)）]\s*|[一二三四五六七八九十百]+[、.．)]\s*|[①-⑳]\s*|(?:Q|Question|问题|题目|问)\s*\d*\s*[:：.、-]\s*|Q\s*\d+\s*|第[一二三四五六七八九十百\d]+题\s*[:：、]?\s*).+",RegexOptions.IgnoreCase)] private static partial Regex NumberedQuestion();
    [GeneratedRegex(@"(?:heading|标题)\s*([1-9])",RegexOptions.IgnoreCase)] private static partial Regex HeadingStyle();
    [GeneratedRegex(@"^\s*(?:(?:[一二三四五六七八九十百\d]+[、.．:：]|第[一二三四五六七八九十百\d]+部分)\s*)?(?:单项选择题|多项选择题|不定项选择题|选择题|单选题|多选题|填空题|判断题|是非题|简答题|问答题|计算题|解答题|应用题|案例分析题|分析题|编程题|程序设计题|程序阅读题|代码阅读题|综合题)\s*(?:[（(:：]|$)")] private static partial Regex ExamSection();
    [GeneratedRegex(@"^\s*(?:\d{1,4}[.、．)）:：]|[（(]\d{1,4}[)）]|[①-⑳])\s*\S")] private static partial Regex ExamQuestionNumber();
    private static bool AnswerHeading(string text)=>Regex.IsMatch(text.Trim().TrimEnd(':','：'),@"^(?:答案|解答|解析|答题要点|注意事项|示例|代码示例|补充|参考答案|answer|explanation)$",RegexOptions.IgnoreCase);
    private static bool QuestionTitle(string text)=>text.Length<=300&&(NumberedQuestion().IsMatch(text)||text.TrimEnd().EndsWith('？')||text.TrimEnd().EndsWith('?'));
    private static bool StrongQuestion(string text)=>text.Length<=300&&(text.TrimEnd().EndsWith('？')||text.TrimEnd().EndsWith('?')||Regex.IsMatch(text.Trim(),@"^(?:(?:\d+[.、．)）:]|[一二三四五六七八九十]+、|[①-⑳])\s*)?(?:Q\s*\d+|Question\s*\d*\s*:|问题\s*\d*\s*[:：]|题目\s*\d*\s*[:：]|问\s*[:：]|什么|如何|怎样|为什么|简述|请简述)",RegexOptions.IgnoreCase));
    public static InterviewImportDocument Read(string path)
    {
        var file=new FileInfo(path);if(!file.Exists)throw new IOException("导入文件不存在。");
        if(file.Length>20*1024*1024)throw new IOException("文档不能超过 20 MiB，请拆分后导入。");
        string extension=file.Extension.ToLowerInvariant();
        string rule=extension==".pdf"?"PDF 字号与题目文字":"Markdown 标题、题号与独立问题段落";
        var blocks=extension==".docx"?ReadDocx(path,out rule):extension switch {".md" or ".markdown" or ".txt"=>ParseText(ReadText(path)),".pdf"=>ReadPdf(path),_=>throw new IOException("请导入 Markdown、TXT、DOCX 或 PDF 文档。")};
        if(blocks.Sum(b=>b.Text.Length)>MaxTextLength)throw new IOException("文档文字超过 50 万字符，请拆分后导入。");
        if(!blocks.Any(b=>!string.IsNullOrWhiteSpace(b.Text)))throw new IOException(extension==".pdf"?"PDF 中没有可提取的文字，可能是扫描件。请先用 OCR 转成文字文档，再导入。":"文档中没有可导入的文字。");
        return new(file.Name,blocks,rule);
    }
    private static string ReadText(string path)
    {
        byte[] bytes=File.ReadAllBytes(path);
        if(bytes.Length>=2&&((bytes[0]==255&&bytes[1]==254)||(bytes[0]==254&&bytes[1]==255)))return File.ReadAllText(path);
        try{return new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');}
        catch(DecoderFallbackException){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);return Encoding.GetEncoding(54936).GetString(bytes);}
    }
    public static List<InterviewBlock> ParseText(string text)
    {
        if(text.Length>MaxTextLength)throw new IOException("文档文字超过 50 万字符，请拆分后导入。");
        var result=new List<InterviewBlock>();var protectedLines=new HashSet<int>();string? fence=null;
        foreach(string raw in text.Replace("\r\n","\n").Replace('\r','\n').Split('\n')){
            string line=raw.TrimEnd(),trim=line.TrimStart();
            if(trim.StartsWith("```",StringComparison.Ordinal)||trim.StartsWith("~~~",StringComparison.Ordinal)){string marker=trim[..3];if(fence is null)fence=marker;else if(fence==marker)fence=null;protectedLines.Add(result.Count);result.Add(new(line));continue;}
            if(fence is not null||raw.StartsWith("    ",StringComparison.Ordinal)||raw.StartsWith('\t')||trim.StartsWith('|')){protectedLines.Add(result.Count);result.Add(new(line));continue;}
            var match=MarkdownHeading().Match(line);
            if(match.Success){result.Add(new(match.Groups[2].Value,match.Groups[1].Length));continue;}
            if(Regex.IsMatch(trim,@"^(?:={3,}|-{3,})$")&&result.Count>0&&result[^1].Level==0&&!string.IsNullOrWhiteSpace(result[^1].Text)){
                result[^1]=result[^1] with {Level=trim[0]=='='?1:2};continue;
            }
            result.Add(new(line));
        }
        // Explicit document headings take precedence over numbered answer lists.
        if(!result.Any(b=>b.Level>0)){
            var candidates=Enumerable.Range(0,result.Count).Where(i=>!protectedLines.Contains(i)&&!AnswerHeading(result[i].Text)&&result[i].Text.Length<=300).ToList();
            var strong=candidates.Where(i=>StrongQuestion(result[i].Text.Trim('*','_',' '))).ToList();
            var bold=candidates.Where(i=>Regex.IsMatch(result[i].Text,@"^\s*(?:\*\*[^*]+\*\*|__[^_]+__)\s*$")).ToList();
            var selected=strong.Count>=2?strong:bold.Count>=2?bold:candidates.Where(i=>(i==0||string.IsNullOrWhiteSpace(result[i-1].Text))&&QuestionTitle(result[i].Text)).ToList();
            foreach(int i in selected)result[i]=new(result[i].Text.Trim('*','_',' '),1);
        }
        return result;
    }
    public static int AutoLevel(InterviewImportDocument document)
    {
        var headings=document.Blocks.Where(b=>b.Level>0&&!string.IsNullOrWhiteSpace(b.Text)&&!AnswerHeading(b.Text)).ToList();
        var questions=headings.Where(b=>!ExamSection().IsMatch(b.Text)).ToList();
        return (questions.Count>0?questions:headings).GroupBy(b=>b.Level)
            .OrderByDescending(g=>g.Count(b=>StrongQuestion(b.Text))).ThenByDescending(g=>g.Count(b=>QuestionTitle(b.Text))).ThenByDescending(g=>g.Count()).ThenBy(g=>g.Key).Select(g=>g.Key).FirstOrDefault();
    }
    public static List<InterviewQuestion> Split(InterviewImportDocument document,int level=0)
    {
        if(level==0)level=AutoLevel(document);
        var questions=new List<InterviewQuestion>();InterviewQuestion? current=null;var answer=new StringBuilder();
        void Flush(){if(current is null)return;current.Answer=answer.ToString().Trim();if(current.Answer.Length>50000)throw new IOException("单题正文超过 50000 个字符，请增加题目标题分段。");questions.Add(current);current=null;answer.Clear();}
        foreach(var block in document.Blocks){
            // Word often leaves empty paragraphs with a heading style. They are spacing,
            // not question boundaries, and must not discard the following answer text.
            if(string.IsNullOrWhiteSpace(block.Text)){if(current is not null)answer.AppendLine();continue;}
            if(level>0&&block.Level==level&&!AnswerHeading(block.Text)){
                Flush();string title=block.Text.Trim();if(title.Length>300)throw new IOException("题目标题超过 300 个字符，请整理小标题。");current=new(){Title=title};
            }else if(level>0&&block.Level>0&&block.Level<level&&!AnswerHeading(block.Text)){Flush();}
            else if(current is not null){answer.AppendLine(block.Level>0?new string('#',Math.Min(block.Level,6))+" "+block.Text:block.Text);}
        }
        Flush();
        if(level==0){string content=string.Join("\n",document.Blocks.Select(b=>b.Text)).Trim();if(content.Length>50000)throw new IOException("未发现小标题，无法拆分长文档。请添加小标题后导入。");string title=Path.GetFileNameWithoutExtension(document.SourceName).Trim();questions.Add(new(){Title=string.IsNullOrWhiteSpace(title)?"导入的面试题":title,Answer=content});}
        if(questions.Count==0||questions.Count>2000)throw new IOException("该标题层级应识别出 1–2000 道题，请调整拆分层级。");
        if(questions.Any(q=>string.IsNullOrWhiteSpace(q.Title)||q.Title.Length>300))throw new IOException("题目标题须为 1–300 个字符，请整理小标题后导入。");
        if(questions.Sum(q=>q.Title.Length+q.Answer.Length)>MaxTextLength)throw new IOException("拆分后的题库超过 50 万字符，请拆分文档后导入。");
        return questions;
    }
    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        if(entry.Length>20*1024*1024)throw new IOException("Word 文档展开后过大，请拆分后导入。");
        using var stream=entry.Open();using var reader=XmlReader.Create(stream,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20*1024*1024});return XDocument.Load(reader);
    }
    private static List<InterviewBlock> ReadDocx(string path,out string rule)
    {
        rule="Word 标题样式与大纲层级";
        using var zip=ZipFile.OpenRead(path);var entry=zip.GetEntry("word/document.xml")??throw new IOException("DOCX 文档结构无效。");
        var styles=new Dictionary<string,XElement>();if(zip.GetEntry("word/styles.xml") is {} styleEntry)foreach(var style in LoadXml(styleEntry).Descendants(W+"style"))if(style.Attribute(W+"styleId") is {} id)styles[id.Value]=style;
        int StyleLevel(string? id,HashSet<string> seen){if(id is null||!seen.Add(id)||!styles.TryGetValue(id,out var style))return 0;var outline=style.Element(W+"pPr")?.Element(W+"outlineLvl")?.Attribute(W+"val")?.Value;if(int.TryParse(outline,out int n)&&n is >=0 and <=8)return n+1;var match=HeadingStyle().Match(id+" "+style.Element(W+"name")?.Attribute(W+"val")?.Value);if(match.Success)return int.Parse(match.Groups[1].Value);return StyleLevel(style.Element(W+"basedOn")?.Attribute(W+"val")?.Value,seen);}
        XElement? StyleProperty(string? id,params string[] path){var seen=new HashSet<string>();while(id is not null&&seen.Add(id)&&styles.TryGetValue(id,out var style)){XElement? node=style;foreach(string part in path)node=node?.Element(W+part);if(node is not null)return node;id=style.Element(W+"basedOn")?.Attribute(W+"val")?.Value;}return null;}
        var numberedLevels=new Dictionary<(string,int),string>();
        var numberStarts=new Dictionary<(string,int),int>();var numberPatterns=new Dictionary<(string,int),string>();
        if(zip.GetEntry("word/numbering.xml") is {} numberingEntry){
            var numbering=LoadXml(numberingEntry);
            var abstracts=numbering.Descendants(W+"abstractNum").Where(e=>e.Attribute(W+"abstractNumId") is not null).ToDictionary(e=>e.Attribute(W+"abstractNumId")!.Value);
            foreach(var num in numbering.Descendants(W+"num")){
                string? id=num.Attribute(W+"numId")?.Value,abstractId=num.Element(W+"abstractNumId")?.Attribute(W+"val")?.Value;
                if(id is null||id=="0"||abstractId is null||!abstracts.TryGetValue(abstractId,out var definition))continue;
                void ReadLevel(int n,XElement lvl){numberedLevels[(id,n)]=lvl.Element(W+"numFmt")?.Attribute(W+"val")?.Value??"";numberStarts[(id,n)]=int.TryParse(lvl.Element(W+"start")?.Attribute(W+"val")?.Value,out int start)?start:1;numberPatterns[(id,n)]=lvl.Element(W+"lvlText")?.Attribute(W+"val")?.Value??$"%{n+1}、";}
                foreach(var lvl in definition.Elements(W+"lvl"))if(int.TryParse(lvl.Attribute(W+"ilvl")?.Value,out int n))ReadLevel(n,lvl);
                foreach(var replacement in num.Elements(W+"lvlOverride"))if(int.TryParse(replacement.Attribute(W+"ilvl")?.Value,out int n)){
                    if(replacement.Element(W+"lvl") is {} lvl)ReadLevel(n,lvl);
                    if(int.TryParse(replacement.Element(W+"startOverride")?.Attribute(W+"val")?.Value,out int start))numberStarts[(id,n)]=start;
                }
            }
        }
        var blocks=new List<InterviewBlock>();var numberedQuestions=new List<(int Index,int Level)>();var visual=new List<(int Index,bool Bold,double Size)>();var sizes=new Dictionary<double,int>();int length=0;
        var labels=new Dictionary<int,string>();var examNumbers=new Dictionary<int,int>();var outsideParagraphs=new HashSet<int>();var counters=new Dictionary<(string,int),int>();
        foreach(var paragraph in LoadXml(entry).Descendants(W+"p")){
            string text=string.Concat(paragraph.Descendants().Where(e=>e.Name==W+"t"||e.Name==W+"tab"||e.Name==W+"br").Select(e=>e.Name==W+"t"?e.Value:e.Name==W+"tab"?"\t":"\n"));
            length+=text.Length;if(length>MaxTextLength)throw new IOException("Word 文档文字超过 50 万字符。");
            var properties=paragraph.Element(W+"pPr");string? styleId=properties?.Element(W+"pStyle")?.Attribute(W+"val")?.Value;int level=StyleLevel(styleId,[]);
            if(int.TryParse(properties?.Element(W+"outlineLvl")?.Attribute(W+"val")?.Value,out int direct)&&direct is >=0 and <=8)level=direct+1;
            if(direct==9)level=0;
            blocks.Add(new(text,string.IsNullOrWhiteSpace(text)?0:level));
            // Automatic numbering is stored in numPr, not in paragraph text. A top-level
            // decimal list can be the question structure even with List Paragraph style.
            var numProperties=properties?.Element(W+"numPr");string? numId=(numProperties?.Element(W+"numId")??StyleProperty(styleId,"pPr","numPr","numId"))?.Attribute(W+"val")?.Value;
            int listLevel=int.TryParse((numProperties?.Element(W+"ilvl")??StyleProperty(styleId,"pPr","numPr","ilvl"))?.Attribute(W+"val")?.Value,out int ilvl)?ilvl:0;
            bool outsideTable=!paragraph.Ancestors(W+"tbl").Any();
            int index=blocks.Count-1;if(outsideTable)outsideParagraphs.Add(index);
            if(numId is not null&&numId!="0"&&numberedLevels.TryGetValue((numId,listLevel),out string? format)){
                var key=(numId,listLevel);int number=counters.TryGetValue(key,out int previous)?previous+1:numberStarts.GetValueOrDefault(key,1);counters[key]=number;
                foreach(var child in counters.Keys.Where(k=>k.Item1==numId&&k.Item2>listLevel).ToList())counters.Remove(child);
                string marker=numberPatterns.GetValueOrDefault(key,$"%{listLevel+1}、");
                for(int depth=0;depth<=listLevel;depth++)marker=marker.Replace($"%{depth+1}",WordNumber(counters.GetValueOrDefault((numId,depth),numberStarts.GetValueOrDefault((numId,depth),1)),numberedLevels.GetValueOrDefault((numId,depth),format)));
                if(format!="bullet"&&format!="none")labels[index]=marker+" "+text;
                if(outsideTable&&format is "decimal" or "decimalEnclosedCircle" or "chineseCounting" or "chineseCountingThousand")examNumbers[index]=listLevel;
            }
            if(level==0&&numId is not null&&numberedLevels.GetValueOrDefault((numId,listLevel)) is "decimal" or "decimalEnclosedCircle" or "chineseCounting" or "chineseCountingThousand"&&text.Trim().Length is >0 and <=300&&outsideTable&&!AnswerHeading(text))numberedQuestions.Add((blocks.Count-1,listLevel));
            var runs=paragraph.Descendants(W+"r").Where(r=>!string.IsNullOrWhiteSpace(string.Concat(r.Descendants(W+"t").Select(t=>t.Value)))).ToList();
            bool bold=runs.Count>0&&runs.All(r=>{var b=r.Element(W+"rPr")?.Element(W+"b")??StyleProperty(styleId,"rPr","b");return b is not null&&b.Attribute(W+"val")?.Value is not ("0" or "false" or "off");});
            double size=runs.Select(r=>double.TryParse((r.Element(W+"rPr")?.Element(W+"sz")??StyleProperty(styleId,"rPr","sz"))?.Attribute(W+"val")?.Value,out double half)?half/2:12).DefaultIfEmpty(12).Average();
            if(!string.IsNullOrWhiteSpace(text))sizes[size]=sizes.GetValueOrDefault(size)+text.Length;
            if(outsideTable&&text.Trim().Length is >0 and <=150&&!text.Contains('\n')&&!AnswerHeading(text))visual.Add((blocks.Count-1,bold,size));
        }
        if(ExpandExamSections(blocks,labels,examNumbers,outsideParagraphs) is {} exam){rule="Word 试卷题型、小题题号与自动编号";return exam;}
        if(!blocks.Any(b=>b.Level>0)&&numberedQuestions.Count>=2){int outer=numberedQuestions.Min(p=>p.Level);var outerQuestions=numberedQuestions.Where(p=>p.Level==outer).ToList();if(outerQuestions.Count>=2){rule="Word 自动编号（最外层题目）";foreach(var candidate in outerQuestions)blocks[candidate.Index]=blocks[candidate.Index] with {Level=1};return blocks;}}
        if(!blocks.Any(b=>b.Level>0)&&sizes.Count>0){double bodySize=sizes.MaxBy(p=>p.Value).Key;var large=visual.Where(v=>v.Size>bodySize*1.2).ToList();var emphasized=visual.Where(v=>v.Bold&&(StrongQuestion(blocks[v.Index].Text)||!Regex.IsMatch(blocks[v.Index].Text,@"[。；;!]"))).ToList();var candidates=large.Count>=2?large:emphasized;if(candidates.Count>=2){rule=large.Count>=2?"Word 独立大字号标题":"Word 独立加粗标题";foreach(var candidate in candidates)blocks[candidate.Index]=blocks[candidate.Index] with {Level=1};return blocks;}}
        if(!blocks.Any(b=>b.Level>0)){rule="题号、问题标记与独立问题段落";return ParseText(string.Join("\n",blocks.Select(b=>b.Text)));}
        return blocks;
    }
    private static string WordNumber(int number,string format)
    {
        if(format is "upperLetter" or "lowerLetter"){
            var letters="";for(int n=number;n>0;n=(n-1)/26)letters=(char)((format=="upperLetter"?'A':'a')+(n-1)%26)+letters;return letters;
        }
        if(format=="decimalEnclosedCircle"&&number is >=1 and <=20)return ((char)('①'+number-1)).ToString();
        return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private static List<InterviewBlock>? ExpandExamSections(List<InterviewBlock> blocks,Dictionary<int,string> labels,Dictionary<int,int> automaticNumbers,HashSet<int> outsideParagraphs)
    {
        var sections=Enumerable.Range(0,blocks.Count).Where(i=>outsideParagraphs.Contains(i)&&blocks[i].Text.Length<=120&&ExamSection().IsMatch(blocks[i].Text)).ToList();
        if(sections.Count==0)return null;
        int sectionLevel=Math.Clamp(sections.Max(i=>blocks[i].Level),1,8),questionLevel=sectionLevel+1;
        var result=blocks.Take(sections[0]).ToList();bool found=false;
        for(int s=0;s<sections.Count;s++){
            int begin=sections[s],end=s+1<sections.Count?sections[s+1]:blocks.Count;
            int outer=automaticNumbers.Where(p=>p.Key>begin&&p.Key<end).Select(p=>p.Value).DefaultIfEmpty(0).Min();
            var questions=Enumerable.Range(begin+1,end-begin-1).Where(i=>outsideParagraphs.Contains(i)&&!string.IsNullOrWhiteSpace(blocks[i].Text)&&
                (automaticNumbers.TryGetValue(i,out int depth)?depth==outer:!labels.ContainsKey(i)&&ExamQuestionNumber().IsMatch(blocks[i].Text))).ToList();
            var explicitHeadings=blocks.Skip(begin+1).Take(end-begin-1).Where(b=>b.Level>blocks[begin].Level).ToList();
            int explicitLevel=AutoLevel(new("exam",explicitHeadings));
            if(explicitLevel>0)questions=questions.Concat(Enumerable.Range(begin+1,end-begin-1).Where(i=>outsideParagraphs.Contains(i)&&blocks[i].Level==explicitLevel&&!string.IsNullOrWhiteSpace(blocks[i].Text)&&!AnswerHeading(blocks[i].Text))).Distinct().Order().ToList();
            if(questions.Count==0){result.Add(blocks[begin] with {Level=sectionLevel});result.Add(blocks[begin] with {Level=questionLevel});result.AddRange(blocks.Skip(begin+1).Take(end-begin-1).Select(b=>b with {Level=0}));continue;}
            found=true;result.Add(blocks[begin] with {Level=sectionLevel});
            // A shared stem/instruction belongs to every subquestion. Preserve original text,
            // including choices, user-entered answers and code, without using it as a boundary.
            var shared=blocks.Skip(begin+1).Take(questions[0]-begin-1).Select(b=>b with {Level=0}).ToList();
            for(int q=0;q<questions.Count;q++){
                int index=questions[q],next=q+1<questions.Count?questions[q+1]:end;string text=labels.GetValueOrDefault(index,blocks[index].Text).Trim();
                int newline=text.IndexOf('\n');string title=(newline<0?text:text[..newline]).Trim();
                result.Add(new(title.Length<=300?title:title[..297]+"…",questionLevel));result.Add(new(blocks[begin].Text));result.AddRange(shared);
                if(title.Length>300)result.Add(new(text));else if(newline>=0)result.Add(new(text[(newline+1)..]));
                for(int i=index+1;i<next;i++)result.Add(new(labels.GetValueOrDefault(i,blocks[i].Text)));
            }
        }
        return found?result:null;
    }
    private static List<InterviewBlock> ReadPdf(string path)
    {
        using var pdf=PdfDocument.Open(path);if(pdf.NumberOfPages>200)throw new IOException("PDF 超过 200 页，请拆分题库后导入。");
        var sizes=new Dictionary<double,int>();int characters=0;
        foreach(var page in pdf.GetPages())foreach(var letter in page.Letters){
            characters+=letter.Value.Length;if(characters>MaxTextLength)throw new IOException("PDF 文字超过 50 万字符，请拆分后导入。");
            if(!string.IsNullOrWhiteSpace(letter.Value)){double point=Math.Round(letter.PointSize,1);sizes[point]=sizes.GetValueOrDefault(point)+letter.Value.Length;}
        }
        double bodySize=sizes.Count==0?12:sizes.MaxBy(pair=>pair.Value).Key;var headings=sizes.Keys.Where(s=>s>bodySize*1.1).OrderDescending().Take(6).ToArray();
        var result=new List<InterviewBlock>();int length=0;
        foreach(var page in pdf.GetPages()){
            // Use text baselines rather than glyph rectangles: some CJK fonts have oversized
            // glyph bounds that make layout analysis merge a heading with the next answer line.
            var lines=page.Letters.GroupBy(l=>Math.Round(l.Location.Y,1)).OrderByDescending(row=>row.Key);
            foreach(var row in lines){
                var letters=row.OrderBy(l=>l.Location.X).ToList();var line=new StringBuilder();
                for(int i=0;i<letters.Count;i++){
                    var letter=letters[i];if(i>0&&!string.IsNullOrWhiteSpace(letter.Value)&&!string.IsNullOrWhiteSpace(letters[i-1].Value)&&letter.BoundingBox.Left-letters[i-1].BoundingBox.Right>letter.PointSize*.2)line.Append(' ');
                    line.Append(letter.Value);
                }
                string text=line.ToString().Trim();length+=text.Length;if(length>MaxTextLength)throw new IOException("PDF 文字超过 50 万字符，请拆分后导入。");
                double size=letters.Where(l=>!string.IsNullOrWhiteSpace(l.Value)).Select(l=>Math.Round(l.PointSize,1)).DefaultIfEmpty(bodySize).Average();
                int level=text.Length<=300&&size>bodySize*1.1?Math.Clamp(Array.FindIndex(headings,s=>Math.Abs(s-size)<.6)+1,1,6):0;
                if(level==0&&QuestionTitle(text))level=Math.Min(6,headings.Length+1);
                result.Add(new(text,level));
            }
            result.Add(new(""));
        }
        return result;
    }
}

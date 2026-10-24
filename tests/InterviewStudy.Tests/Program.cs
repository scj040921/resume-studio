using ResumeStudio.Services;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Content;

string output=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(Path.GetTempPath(),"InterviewImport-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
void Reject(Action action,string name){try{action();}catch(Exception ex)when(ex is IOException or System.Xml.XmlException){Check(true,name);return;}throw new Exception(name);}
string md="# C 面试题\n\n## 1. 指针是什么？\n指针保存地址。\n### 答案\n保留解答小标题。\n1. 答案列表不会变成题目。\n\n## 2. 什么是互斥锁？\n保护共享数据。\n";
File.WriteAllText(Path.Combine(output,"interview.md"),md,new UTF8Encoding(false));
var document=InterviewImport.Read(Path.Combine(output,"interview.md"));var questions=InterviewImport.Split(document);
Check(InterviewImport.AutoLevel(document)==2&&questions.Count==2,"Markdown categories and answer subheadings are excluded from question boundaries");
Check(questions[0].Answer.Contains("### 答案")&&questions[0].Answer.Contains("答案列表"),"question answers retain lists and lower headings");
Check(InterviewImport.Split(document,1).Count==1,"manual heading-level selection can merge a category into one card");
Check(InterviewImport.Split(new("answers.md",[new("1. 指针是什么？",2),new("答案",1),new("完整解释"),new("1. 第一步",3),new("2. 第二步",3),new("3. 第三步",3),new("2. 任务是什么？",2),new("任务解释")])).Count==2,"question-like headings take priority over numerous answer-list headings and answer labels cannot discard text");
string fence=new((char)96,3);
var code=InterviewImport.ParseText("## 1. Code?\n"+fence+"c\n# include\n## still code\n"+fence+"\n## 2. Next?\nAnswer");
Check(InterviewImport.Split(new("code.md",code)).Count==2,"headings inside fenced code are preserved as answer text");
var setext=InterviewImport.ParseText("One question?\n---\nAnswer\nTwo questions?\n---\nAnswer");
Check(InterviewImport.Split(new("setext.md",setext)).Count==2,"setext Markdown headings split correctly");
foreach(string content in new[]{"一、什么是中断？\n答案\n\n二、什么是任务？\n答案","① 什么是中断？\n答案\n\n② 什么是任务？\n答案","问题：中断是什么\n答案\n\nQ2: 任务是什么\n答案","**中断机制**\n答案\n\n**任务调度**\n答案"})Check(InterviewImport.Split(new("extended.txt",InterviewImport.ParseText(content))).Count==2,"Chinese/circled/labeled/bold question headings split");
var plainCode=InterviewImport.Split(new("code.txt",InterviewImport.ParseText("Q1: 指针\n答案\n"+fence+"c\nQ99: example\n1. code line\n"+fence+"\n1. 答案步骤\n2. 第二步骤\n\nQ2: 内存\n答案")));
Check(plainCode.Count==2&&plainCode[0].Answer.Contains("Q99")&&plainCode[0].Answer.Contains("第二步骤"),"plain text fallback preserves fenced code and numbered answer steps");
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
string plain="1. 中断是什么？\n响应异步事件。\n\n2. 什么是任务？\n调度单位。";
File.WriteAllBytes(Path.Combine(output,"interview.txt"),Encoding.GetEncoding(54936).GetBytes(plain));
Check(InterviewImport.Split(InterviewImport.Read(Path.Combine(output,"interview.txt"))).Count==2,"Chinese GB18030 TXT imports numbered question titles");
void Docx(string path,string styles,string body){
    using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
    foreach(var entry in new[]{("word/styles.xml",styles),("word/document.xml",body)}){
        using var stream=new StreamWriter(zip.CreateEntry(entry.Item1).Open(),new UTF8Encoding(false));stream.Write(entry.Item2);
    }
}
string ns="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
string styles=$"""<w:styles xmlns:w="{ns}"><w:style w:styleId="Heading1"><w:pPr><w:outlineLvl w:val="0"/></w:pPr></w:style><w:style w:styleId="Question"><w:pPr><w:outlineLvl w:val="1"/></w:pPr></w:style><w:style w:styleId="Derived"><w:basedOn w:val="Question"/></w:style></w:styles>""";
string body=$"""<w:document xmlns:w="{ns}"><w:body><w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>嵌入式题库</w:t></w:r></w:p><w:p><w:pPr><w:pStyle w:val="Derived"/></w:pPr><w:r><w:t>1. 什么是栈？</w:t></w:r></w:p><w:p><w:r><w:t>自动管理的数据区。</w:t></w:r></w:p><w:p><w:pPr><w:outlineLvl w:val="1"/></w:pPr><w:r><w:t>2. 什么是堆？</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>显式分配与释放。</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>""";
Docx(Path.Combine(output,"interview.docx"),styles,body);
var word=InterviewImport.Split(InterviewImport.Read(Path.Combine(output,"interview.docx")));
Check(word.Count==2&&word[0].Title.Contains("栈")&&word[1].Answer.Contains("显式"),"DOCX custom inherited styles, outline levels and table text import");
string blankHeading="<w:p><w:pPr><w:pStyle w:val=\"Derived\"/></w:pPr><w:r><w:t>   </w:t></w:r></w:p>";
string spacedBody=body.Replace("<w:body>","<w:body>"+blankHeading).Replace("自动管理的数据区。</w:t></w:r></w:p>","自动管理的数据区。</w:t></w:r></w:p>"+blankHeading+"<w:p><w:r><w:t>空白标题后的补充答案。</w:t></w:r></w:p>").Replace("</w:body>",blankHeading+"</w:body>");
Docx(Path.Combine(output,"interview-blank-headings.docx"),styles,spacedBody);
var spacedWord=InterviewImport.Split(InterviewImport.Read(Path.Combine(output,"interview-blank-headings.docx")));
Check(spacedWord.Count==2&&spacedWord.All(q=>!string.IsNullOrWhiteSpace(q.Title))&&spacedWord[0].Answer.Contains("补充答案"),"empty Word heading paragraphs create no invalid questions and retain subsequent answer text");
Check(InterviewImport.Split(new("blank.docx",[new("",1),new("1. Question?",2),new("Answer"),new("  ",1),new("continued answer")]))[0].Answer.Contains("continued answer"),"blank category headings do not terminate the current question");
Check(InterviewImport.AutoLevel(new("blank.docx",[new("",1),new(" ",1),new("Question",2)]))==2,"automatic heading selection ignores empty formatted paragraphs");
string numberedBody=$"""<w:document xmlns:w="{ns}"><w:body><w:p><w:pPr><w:numPr><w:ilvl w:val="0"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>TCP 与 UDP 的区别</w:t></w:r></w:p><w:p><w:r><w:t>保留第一题答案。</w:t></w:r></w:p><w:p><w:pPr><w:numPr><w:ilvl w:val="1"/><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>答案中的嵌套列表</w:t></w:r></w:p><w:p><w:pPr><w:numPr><w:numId w:val="0"/></w:numPr></w:pPr><w:r><w:t>取消编号的正文</w:t></w:r></w:p><w:p><w:pPr><w:numPr><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>线程与进程的区别</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:pPr><w:numPr><w:numId w:val="1"/></w:numPr></w:pPr><w:r><w:t>表格中的编号内容</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>""";
string numberedPath=Path.Combine(output,"interview-numbered.docx");Docx(numberedPath,styles,numberedBody);
using(var zip=ZipFile.Open(numberedPath,ZipArchiveMode.Update)){using var stream=new StreamWriter(zip.CreateEntry("word/numbering.xml").Open());stream.Write($"""<w:numbering xmlns:w="{ns}"><w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:numFmt w:val="decimal"/></w:lvl><w:lvl w:ilvl="1"><w:numFmt w:val="decimal"/></w:lvl></w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num></w:numbering>""");}
var numbered=InterviewImport.Split(InterviewImport.Read(numberedPath));
Check(numbered.Count==2&&numbered[0].Title=="TCP 与 UDP 的区别"&&numbered[0].Answer.Contains("嵌套列表")&&numbered[0].Answer.Contains("取消编号")&&numbered[1].Answer.Contains("表格中的编号"),"Word automatic numbering splits questions without treating nested lists, disabled numbering or tables as questions");
foreach(string emphasis in new[]{"<w:b/>","<w:sz w:val=\"36\"/>"}){
    string visualPath=Path.Combine(output,"visual-"+(emphasis.Contains("w:b")?"bold":"size")+".docx");
    string visualBody=$"""<w:document xmlns:w="{ns}"><w:body><w:p><w:r><w:rPr>{emphasis}</w:rPr><w:t>指针与内存</w:t></w:r></w:p><w:p><w:r><w:t>正常正文，保存完整解释。</w:t></w:r></w:p><w:p><w:r><w:rPr>{emphasis}</w:rPr><w:t>任务与调度</w:t></w:r></w:p><w:p><w:r><w:t>正常正文，说明任务的机制。</w:t></w:r></w:p></w:body></w:document>""";
    Docx(visualPath,styles,visualBody);Check(InterviewImport.Split(InterviewImport.Read(visualPath)).Count==2,"Word standalone bold or large-font headings are recognized without heading styles");
}
string inheritedPath=Path.Combine(output,"inherited-numbering.docx");Docx(inheritedPath,styles.Replace("</w:styles>","<w:style w:styleId=\"AutoList\"><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr></w:pPr></w:style></w:styles>"),numberedBody.Replace("<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>","<w:pStyle w:val=\"AutoList\"/>").Replace("<w:numPr><w:numId w:val=\"1\"/></w:numPr>","<w:pStyle w:val=\"AutoList\"/>"));
using(var target=ZipFile.Open(inheritedPath,ZipArchiveMode.Update)){using var origin=ZipFile.OpenRead(numberedPath);using var sourceStream=origin.GetEntry("word/numbering.xml")!.Open();using var destinationStream=target.CreateEntry("word/numbering.xml").Open();sourceStream.CopyTo(destinationStream);}
Check(InterviewImport.Split(InterviewImport.Read(inheritedPath)).Count==2,"Word numbering inherited from paragraph styles is recognized");
string ExamParagraph(string text,string properties="")=>$"<w:p><w:pPr>{properties}</w:pPr><w:r><w:t>{System.Security.SecurityElement.Escape(text)}</w:t></w:r></w:p>";
string examSection="<w:pStyle w:val=\"Question\"/>",examDecimal="<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>",examOption="<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"2\"/></w:numPr>";
string longExamStem="1、分析以下代码并给出输出："+new string('x',310);
string examBody=$"<w:document xmlns:w=\"{ns}\"><w:body>"+ExamParagraph("测试试卷","<w:pStyle w:val=\"Heading1\"/>")+
    ExamParagraph("一、选择题（每题2分）",examSection)+ExamParagraph("请选择一个正确选项。")+ExamParagraph("1、指针保存什么？")+ExamParagraph("地址",examOption)+ExamParagraph("B、数值")+
    ExamParagraph("结构体的特点",examDecimal)+ExamParagraph("用户填写的答案：内存布局相关。")+ExamParagraph("答案中的编号步骤","<w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"1\"/></w:numPr>")+
    ExamParagraph("二、填空题（每题5分）",examSection)+ExamParagraph("1、文件关闭函数是____。")+ExamParagraph("2、栈由____管理。")+
    ExamParagraph("三、简答题（每题5分）",examSection)+ExamParagraph("1、简述任务调度。")+ExamParagraph("说明互斥机制",examDecimal).Replace("说明互斥机制</w:t>","说明互斥机制</w:t><w:br/><w:t>用户填写的答案正文</w:t>")+
    ExamParagraph("四、编程题（每题10分）",examSection)+ExamParagraph(longExamStem)+ExamParagraph("for (int i=0; i&lt;3; i++) {}".Replace("&lt;","<"))+ExamParagraph("2、实现字符串拼接。")+
    ExamParagraph("五、综合题",examSection)+ExamParagraph("未编号的完整题干及作答要求。")+
    ExamParagraph("六、程序阅读题",examSection)+ExamParagraph("理解中断流程","<w:outlineLvl w:val=\"2\"/>")+ExamParagraph("程序正文一。")+ExamParagraph("分析调度流程","<w:outlineLvl w:val=\"2\"/>")+ExamParagraph("程序正文二。")+"</w:body></w:document>";
string examPath=Path.Combine(output,"exam-mixed-numbering.docx");Docx(examPath,styles,examBody);
using(var zip=ZipFile.Open(examPath,ZipArchiveMode.Update)){using var stream=new StreamWriter(zip.CreateEntry("word/numbering.xml").Open());stream.Write($"""<w:numbering xmlns:w="{ns}"><w:abstractNum w:abstractNumId="0"><w:lvl w:ilvl="0"><w:start w:val="2"/><w:numFmt w:val="decimal"/><w:lvlText w:val="%1、"/></w:lvl><w:lvl w:ilvl="1"><w:numFmt w:val="decimal"/></w:lvl></w:abstractNum><w:abstractNum w:abstractNumId="1"><w:lvl w:ilvl="0"><w:numFmt w:val="upperLetter"/><w:lvlText w:val="%1、"/></w:lvl></w:abstractNum><w:num w:numId="1"><w:abstractNumId w:val="0"/></w:num><w:num w:numId="2"><w:abstractNumId w:val="1"/></w:num></w:numbering>""");}
var examDocument=InterviewImport.Read(examPath);var examQuestions=InterviewImport.Split(examDocument);
Check(examQuestions.Count==11&&examQuestions.All(q=>q.Title.Length<=300),"exam section headings coexist with manual and automatic subquestion numbers");
Check(examQuestions[0].Answer.Contains("A、 地址")&&examQuestions[0].Answer.Contains("B、数值")&&examQuestions[1].Answer.Contains("编号步骤"),"exam options and nested answer lists stay in the current question");
Check(examQuestions[0].Answer.Contains("请选择")&&examQuestions[1].Answer.Contains("请选择")&&examQuestions[1].Answer.Contains("用户填写的答案"),"shared exam instructions and entered answers are preserved without becoming questions");
Check(examQuestions[5].Title.Contains("互斥")&&!examQuestions[5].Title.Contains("答案正文")&&examQuestions[5].Answer.Contains("答案正文"),"a Word line break separates an automatically numbered question from its entered answer");
Check(examQuestions[6].Answer.Contains(longExamStem)&&examQuestions[6].Answer.Contains("for (int")&&examQuestions[8].Answer.Contains("未编号"),"long exam stems, code and sections without subquestion numbers are not discarded");
Check(examQuestions[9].Title=="理解中断流程"&&examQuestions[10].Answer.Contains("正文二"),"explicit subquestion headings remain recognized alongside inferred exam numbers");
Check(InterviewImport.AutoLevel(new("exam.docx",[new("一、选择题",2),new("二、填空题",2),new("理解指针",3),new("理解数组",3),new("三、简答题",2)]))==3,"exam category counts cannot override explicit subquestion headings");
Check(InterviewImport.Split(examDocument,2).Count==6,"manual exam section-level splitting remains available");
var legacyExamQuestion=new InterviewQuestion {Title="3、自定义函数实现字符串拼接",Answer="四、编程题（每题10分，共30分）\r\n  for (int i=0; i<3; i++) {}\r\n下一行代码",ReviewNote="已有复盘"};
var examPresentation=InterviewImport.ExamPresentation(legacyExamQuestion);
Check(examPresentation.Section=="四、编程题（每题10分，共30分）"&&examPresentation.Title==legacyExamQuestion.Title&&examPresentation.Content=="  for (int i=0; i<3; i++) {}\n下一行代码"&&legacyExamQuestion.Answer.StartsWith("四、编程题")&&legacyExamQuestion.ReviewNote=="已有复盘","existing exam cards expose section, subquestion and indented content separately without rewriting saved data");
Check(InterviewImport.ExamPresentation(new(){Title="3、实现字符串拼接",Answer="四、编程题（每题10分，共30分）"}).Content=="","title-only subquestions have no duplicate section in their content");
Check(InterviewImport.ExamPresentation(new(){Title="普通题目",Answer="四、这是答案中的步骤\n保留内容"})==("","普通题目","四、这是答案中的步骤\n保留内容"),"ordinary answer lists are not promoted to exam section headings");
string? actualExam=Environment.GetEnvironmentVariable("RESUMESTUDIO_EXAM_DOCUMENT");
if(!string.IsNullOrWhiteSpace(actualExam)){
    var actual=InterviewImport.Split(InterviewImport.Read(actualExam));
    Check(actual.Count==29&&actual.Take(20).All(q=>q.Answer.StartsWith("一、选择题"))&&actual.Skip(20).Take(4).All(q=>q.Answer.StartsWith("二、填空题"))&&actual.Skip(24).Take(2).All(q=>q.Answer.StartsWith("三、简答题"))&&actual.Skip(26).All(q=>q.Answer.StartsWith("四、编程题")),"actual exam has 20 choice, 4 fill-in, 2 short-answer and 3 programming question cards");
    Check(actual[19].Answer.Contains("A、 编译-")&&actual[25].Title.Contains("顺序表")&&actual[25].Answer.Contains("随机访问")&&actual[26].Answer.Contains("count++")&&actual[28].Title.Contains("字符串拼接"),"actual exam retains automatic letter options, same-paragraph entered answers and programming code");
    using var zip=ZipFile.OpenRead(actualExam);using var stream=zip.GetEntry("word/document.xml")!.Open();var xml=XDocument.Load(stream);XNamespace w=ns;
    string Compact(string text)=>new(text.Where(c=>!char.IsWhiteSpace(c)).ToArray());
    string imported=Compact(string.Join("\n",actual.Select(q=>q.Title+"\n"+q.Answer)));
    var original=xml.Descendants(w+"p").Skip(1).Select(p=>string.Concat(p.Descendants().Where(e=>e.Name==w+"t"||e.Name==w+"br"||e.Name==w+"tab").Select(e=>e.Name==w+"t"?e.Value:e.Name==w+"br"?"\n":"\t"))).SelectMany(t=>t.Split('\n')).Select(Compact).Where(t=>t.Length>0).ToList();
    Check(original.All(imported.Contains),"every nonblank source line after the exam title is retained in the imported cards");
    File.WriteAllText(Path.Combine(output,"actual-exam-result.json"),System.Text.Json.JsonSerializer.Serialize(actual));
}
if(args.Length>2){var userDocument=InterviewImport.Read(args[2]);var userQuestions=InterviewImport.Split(userDocument);Check(userQuestions.Count==82&&userQuestions[0].Title=="tcp和udp的区别"&&userQuestions[^1].Title=="extern关键字的作用？","actual user Word document splits all 82 numbered question titles in order");Check(userQuestions[0].Answer.Contains("连接方式")&&userQuestions[0].Answer.Contains("无流量控制")&&userQuestions[0].Answer.Contains("TCP"),"actual document keeps comparison table text in the first answer");File.WriteAllText(Path.Combine(output,"user-import-result.json"),System.Text.Json.JsonSerializer.Serialize(userQuestions));}
if(args.Length>2){using var zip=ZipFile.OpenRead(args[2]);using var stream=zip.GetEntry("word/document.xml")!.Open();var xml=XDocument.Load(stream);XNamespace w=ns;var expected=xml.Descendants(w+"p").Where(p=>p.Element(w+"pPr")?.Element(w+"numPr")?.Element(w+"numId")?.Attribute(w+"val")?.Value=="1").Select(p=>string.Concat(p.Descendants(w+"t").Select(t=>t.Value)).Trim()).ToArray();Check(InterviewImport.Split(InterviewImport.Read(args[2])).Select(q=>q.Title).SequenceEqual(expected),"every actual document question title matches the original 82 numbered paragraphs exactly");}
var builder=new PdfDocumentBuilder();var font=builder.AddStandard14Font(Standard14Font.Helvetica);var page=builder.AddPage(PageSize.A4);
page.AddText("1. What is a mutex?",16,new PdfPoint(40,740),font);page.AddText("Mutual exclusion protects shared data.",11,new PdfPoint(40,710),font);
page.AddText("2. What is a semaphore?",16,new PdfPoint(40,640),font);page.AddText("A counter limits concurrent access.",11,new PdfPoint(40,610),font);
File.WriteAllBytes(Path.Combine(output,"interview.pdf"),builder.Build());
var pdf=InterviewImport.Split(InterviewImport.Read(Path.Combine(output,"interview.pdf")));
Check(pdf.Count==2&&pdf[0].Answer.Contains("Mutual exclusion")&&pdf[1].Title.Contains("semaphore"),"real PDF typography and reading order produce two question cards");
string fontPath=args.Length>1?args[1]:Path.Combine(Directory.GetCurrentDirectory(),"client-winui","Assets","Fonts","MiSans-Regular.ttf");
var chineseBuilder=new PdfDocumentBuilder();var chineseFont=chineseBuilder.AddTrueTypeFont(File.ReadAllBytes(fontPath));var chinesePage=chineseBuilder.AddPage(PageSize.A4);
chinesePage.AddText("1. 什么是中断？",16,new PdfPoint(40,740),chineseFont);
chinesePage.AddText("中断响应外部或内部的异步事件，通常由硬件或软件触发。",11,new PdfPoint(40,710),chineseFont);
chinesePage.AddText("2. 什么是任务？",16,new PdfPoint(40,640),chineseFont);
chinesePage.AddText("任务是调度器管理的执行单位，具有自己的状态与执行上下文。",11,new PdfPoint(40,610),chineseFont);
File.WriteAllBytes(Path.Combine(output,"interview-chinese.pdf"),chineseBuilder.Build());
var chinese=InterviewImport.Split(InterviewImport.Read(Path.Combine(output,"interview-chinese.pdf")));
if(chinese.Count!=2||!chinese[0].Title.Replace(" ","").Contains("中断")||!chinese[1].Answer.Replace(" ","").Contains("调度器"))Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{blocks=InterviewImport.Read(Path.Combine(output,"interview-chinese.pdf")).Blocks,questions=chinese},new System.Text.Json.JsonSerializerOptions{Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
Check(chinese.Count==2&&chinese[0].Title.Replace(" ","").Contains("中断")&&chinese[1].Answer.Replace(" ","").Contains("调度器"),"actual Chinese PDF with embedded MiSans extracts headings and answer glyphs");
var emptyPdf=new PdfDocumentBuilder();emptyPdf.AddPage(PageSize.A4);File.WriteAllBytes(Path.Combine(output,"empty.pdf"),emptyPdf.Build());
Reject(()=>InterviewImport.Read(Path.Combine(output,"empty.pdf")),"image-only/empty PDF gives an explicit no-text error");
Docx(Path.Combine(output,"unsafe.docx"),styles,$"""<!DOCTYPE x [<!ENTITY external SYSTEM "file:///nonexistent">]><w:document xmlns:w="{ns}"><w:body><w:p><w:r><w:t>&external;</w:t></w:r></w:p></w:body></w:document>""");
Reject(()=>InterviewImport.Read(Path.Combine(output,"unsafe.docx")),"DOCX external XML entities cannot be read");
Reject(()=>InterviewImport.ParseText(new string('a',InterviewImport.MaxTextLength+1)),"oversized text is rejected before creating cards");
var deck=new InterviewDeck {Questions=questions};questions[0].Status="remembered";questions[1].Status="learning";
Check(InterviewProgress.Count(deck.Questions)==new InterviewCounts(2,1,1,0),"remembered and unmastered counts are separate");
questions[0].Status="learning";questions[0].Status="learning";
Check(InterviewProgress.Count(deck.Questions)==new InterviewCounts(2,0,2,0),"changing or repeating a verdict never double counts");
Check(InterviewProgress.Filter(deck,"remembered").Count()==0&&InterviewProgress.Filter(deck,"learning").Count()==2,"practice filters follow current states");
var reviewed=new InterviewQuestion {Title="间隔复习",Status="unseen"};var reviewAt=DateTimeOffset.Parse("2026-10-17T09:00:00+08:00");
InterviewProgress.Review(reviewed,"remembered",reviewAt);Check(reviewed.IntervalDays==1&&DateTimeOffset.Parse(reviewed.NextReviewAt)==reviewAt.AddDays(1),"first remembered review schedules one day");
InterviewProgress.Review(reviewed,"remembered",reviewAt.AddDays(1));Check(reviewed.IntervalDays==3&&reviewed.ReviewCount==2,"remembered interval increases with repetitions");
InterviewProgress.Review(reviewed,"learning",reviewAt);Check(reviewed.IntervalDays==0&&DateTimeOffset.Parse(reviewed.NextReviewAt)==reviewAt.AddMinutes(10),"unmastered question returns after ten minutes");
Console.WriteLine($"PASS: {checks} interview import and progress checks. Fixtures: {output}");

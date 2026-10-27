package com.resumestudio;

import java.io.*;
import java.nio.*;
import java.nio.charset.*;
import java.util.*;
import java.util.regex.*;
import java.util.zip.*;
import javax.xml.parsers.*;
import org.w3c.dom.*;
import com.tom_roush.pdfbox.pdmodel.PDDocument;
import com.tom_roush.pdfbox.text.PDFTextStripper;

final class MobileImport {
    static Map<String,Object> read(String name,byte[] bytes)throws Exception {
        List<Object> blocks=new ArrayList<>();String lower=name.toLowerCase(Locale.ROOT);
        if(lower.endsWith(".docx")){
            Map<String,byte[]> xml=new HashMap<>();try(ZipInputStream zip=new ZipInputStream(new ByteArrayInputStream(bytes))){ZipEntry e;while((e=zip.getNextEntry())!=null){if(e.getName().equals("word/document.xml")||e.getName().equals("word/styles.xml")||e.getName().equals("word/numbering.xml")){byte[] content=MobileCompat.readNBytes(zip,20*1024*1024+1);if(content.length>20*1024*1024)throw new IOException("Word 展开内容过大");xml.put(e.getName(),content);}}}
            if(!xml.containsKey("word/document.xml"))throw new IOException("DOCX 格式无效");
            Map<String,Element> styles=new HashMap<>();if(xml.containsKey("word/styles.xml")){NodeList nodes=parse(xml.get("word/styles.xml")).getElementsByTagNameNS("*","style");for(int i=0;i<nodes.getLength();i++){Element s=(Element)nodes.item(i);styles.put(s.getAttribute("w:styleId"),s);}}
            Map<String,Map<Integer,Numbering>> numbering=xml.containsKey("word/numbering.xml")?numbering(parse(xml.get("word/numbering.xml"))):new HashMap<>();
            Map<String,Integer> counters=new HashMap<>();Map<Integer,String> labels=new HashMap<>();Map<Integer,Integer> automatic=new HashMap<>();Set<Integer> outside=new HashSet<>();
            NodeList paragraphs=parse(xml.get("word/document.xml")).getElementsByTagNameNS("*","p");
            for(int i=0;i<paragraphs.getLength();i++){Element p=(Element)paragraphs.item(i);StringBuilder text=new StringBuilder();collect(p,text);int level=outline(p);String style=attr(p,"pStyle","w:val");String styleId=style;if(level==0){Set<String> seen=new HashSet<>();while(level==0&&styles.containsKey(style)&&seen.add(style)){Element s=styles.get(style);level=outline(s);Matcher m=Pattern.compile("(?i)(?:heading|标题)\\s*([1-6])").matcher(style+" "+attr(s,"name","w:val"));if(level==0&&m.find())level=Integer.parseInt(m.group(1));style=attr(s,"basedOn","w:val");}}blocks.add(MobileCompat.map("text",text.toString(),"level",text.toString().trim().isEmpty()?0:level));
                boolean inTable=false;for(Node n=p.getParentNode();n!=null;n=n.getParentNode())if("tbl".equals(n.getLocalName())){inTable=true;break;}if(!inTable)outside.add(i);
                Element pr=child(p,"pPr"),num=child(pr,"numPr");Set<String> seen=new HashSet<>();while(num==null&&styles.containsKey(styleId)&&seen.add(styleId)){Element s=styles.get(styleId);num=child(child(s,"pPr"),"numPr");styleId=attr(s,"basedOn","w:val");}
                String id=val(child(num,"numId")),depthText=val(child(num,"ilvl"));int depth=integer(depthText,0);Numbering def=numbering.getOrDefault(id,Collections.emptyMap()).get(depth);
                if(def!=null&&!text.toString().trim().isEmpty()){String key=id+":"+depth;int value=counters.getOrDefault(key,def.start-1)+1;counters.put(key,value);for(int d=depth+1;d<9;d++)counters.remove(id+":"+d);String marker=def.pattern;for(int d=0;d<=depth;d++){Numbering parent=numbering.get(id).get(d);int count=counters.getOrDefault(id+":"+d,parent==null?1:parent.start);marker=marker.replace("%"+(d+1),wordNumber(count,parent==null?def.format:parent.format));}if(!def.format.equals("bullet")&&!def.format.equals("none"))labels.put(i,marker+" "+text);if(!inTable&&Arrays.asList("decimal","decimalEnclosedCircle","chineseCounting","chineseCountingThousand").contains(def.format))automatic.put(i,depth);}
            }
            List<Object> exam=expandExam(blocks,labels,automatic,outside);if(exam!=null)blocks=exam;
        }else if(lower.endsWith(".pdf")){
            try(PDDocument pdf=PDDocument.load(bytes)){
                if(pdf.getNumberOfPages()>200)throw new IOException("PDF 超过 200 页，请拆分后导入");
                PDFTextStripper stripper=new PDFTextStripper();stripper.setSortByPosition(true);StringBuilder all=new StringBuilder();
                for(int page=1;page<=pdf.getNumberOfPages();page++){stripper.setStartPage(page);stripper.setEndPage(page);all.append(stripper.getText(pdf)).append('\n');if(all.length()>500000)throw new IOException("文字超过 50 万字符");}blocks=text(all.toString());
            }
        }else if(lower.endsWith(".md")||lower.endsWith(".markdown")||lower.endsWith(".txt")){
            String text;if(bytes.length>=2&&((bytes[0]==(byte)255&&bytes[1]==(byte)254)||(bytes[0]==(byte)254&&bytes[1]==(byte)255)))text=new String(bytes,Charset.forName("UTF-16"));
            else try{text=StandardCharsets.UTF_8.newDecoder().decode(ByteBuffer.wrap(bytes)).toString();}catch(CharacterCodingException e){text=new String(bytes,Charset.forName("GB18030"));}blocks=text(text.replace("\uFEFF",""));
        }else throw new IOException("请选择 Markdown、TXT、DOCX 或文字型 PDF");
        int length=blocks.stream().map(Json::object).mapToInt(b->Json.str(b,"text").length()).sum();if(length==0)throw new IOException("没有可导入的文字，扫描 PDF 请先做 OCR");if(length>500000)throw new IOException("文字超过 50 万字符，请拆分后导入");
        return MobileCompat.map("sourceName",name,"blocks",blocks);
    }
    private static List<Object> text(String text){
        List<Object> blocks=new ArrayList<>();String fence="";
        for(String line:text.replace("\r\n","\n").replace('\r','\n').split("\n",-1)){
            String trimmed=line.trim();if(trimmed.startsWith("```")||trimmed.startsWith("~~~")){String marker=trimmed.substring(0,3);if(fence.isEmpty())fence=marker;else if(fence.equals(marker))fence="";blocks.add(MobileCompat.map("text",line,"level",0));continue;}
            Matcher m=Pattern.compile("^\\s*(#{1,6})\\s+(.+?)\\s*#*\\s*$").matcher(line);
            if(fence.isEmpty()&&m.matches())blocks.add(MobileCompat.map("text",m.group(2),"level",m.group(1).length()));
            else if(fence.isEmpty()&&trimmed.matches("={3,}|-{3,}")&&!blocks.isEmpty()&&!Json.str(Json.object(blocks.get(blocks.size()-1)),"text").trim().isEmpty()){var previous=new LinkedHashMap<>(Json.object(blocks.get(blocks.size()-1)));previous.put("level",trimmed.charAt(0)=='='?1:2);blocks.set(blocks.size()-1,previous);}
            else blocks.add(MobileCompat.map("text",line,"level",0));
        }
        boolean headings=blocks.stream().map(Json::object).anyMatch(b->((Number)b.get("level")).intValue()>0);
        if(!headings)for(int i=0;i<blocks.size();i++){Map<String,Object>b=Json.object(blocks.get(i));String t=Json.str(b,"text").trim();if(t.length()<=300&&t.matches("(?i)(?:(?:\\d{1,3}[.、．)）:]|[（(]\\d{1,3}[)）]|Q\\s*\\d+|第.+题).+|.+[？?])"))blocks.set(i,new LinkedHashMap<>(MobileCompat.map("text",Json.str(b,"text"),"level",1)));}
        return blocks;
    }
    private static Document parse(byte[] bytes)throws Exception {
        // Android's Harmony parser does not implement Xerces' disallow-doctype feature.
        // DOCX needs no DTD: reject its declaration, including UTF-16/32 byte layouts,
        // and deny every external resolution instead of relying on a vendor feature.
        String markup=new String(bytes,StandardCharsets.ISO_8859_1).replace("\u0000","");
        if(Pattern.compile("(?i)<!\\s*(DOCTYPE|ENTITY)\\b").matcher(markup).find())throw new IOException("Word XML 不允许 DTD 或实体声明");
        DocumentBuilderFactory f=DocumentBuilderFactory.newInstance();f.setNamespaceAware(true);f.setExpandEntityReferences(false);
        DocumentBuilder builder=f.newDocumentBuilder();builder.setEntityResolver((publicId,systemId)->{throw new org.xml.sax.SAXException("不允许外部 XML 资源");});return builder.parse(new ByteArrayInputStream(bytes));
    }
    private static String attr(Element e,String tag,String name){NodeList n=e.getElementsByTagNameNS("*",tag);return n.getLength()==0?"":((Element)n.item(0)).getAttribute(name);}
    private static final class Numbering {String format,pattern;int start;Numbering(String f,String p,int s){format=f;pattern=p;start=s;}}
    private static Element child(Element e,String name){if(e==null)return null;for(Node n=e.getFirstChild();n!=null;n=n.getNextSibling())if(n instanceof Element&&name.equals(n.getLocalName()))return (Element)n;return null;}
    private static String val(Element e){return e==null?"":e.getAttributeNS("http://schemas.openxmlformats.org/wordprocessingml/2006/main","val");}
    private static int integer(String text,int fallback){try{return Integer.parseInt(text);}catch(NumberFormatException e){return fallback;}}
    private static Map<String,Map<Integer,Numbering>> numbering(Document doc){
        Map<String,Element> definitions=new HashMap<>();NodeList abstracts=doc.getElementsByTagNameNS("*","abstractNum");String ns="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        for(int i=0;i<abstracts.getLength();i++){Element e=(Element)abstracts.item(i);definitions.put(e.getAttributeNS(ns,"abstractNumId"),e);}
        Map<String,Map<Integer,Numbering>> result=new HashMap<>();NodeList instances=doc.getElementsByTagNameNS("*","num");
        for(int i=0;i<instances.getLength();i++){Element n=(Element)instances.item(i);String id=n.getAttributeNS(ns,"numId");Element definition=definitions.get(val(child(n,"abstractNumId")));if(id.isEmpty()||id.equals("0")||definition==null)continue;Map<Integer,Numbering> levels=new HashMap<>();for(Node x=definition.getFirstChild();x!=null;x=x.getNextSibling())if(x instanceof Element&&"lvl".equals(x.getLocalName())){Element e=(Element)x;int depth=integer(e.getAttributeNS(ns,"ilvl"),0);levels.put(depth,readLevel(e,depth));}for(Node x=n.getFirstChild();x!=null;x=x.getNextSibling())if(x instanceof Element&&"lvlOverride".equals(x.getLocalName())){Element e=(Element)x;int depth=integer(e.getAttributeNS(ns,"ilvl"),0);Element replacement=child(e,"lvl");if(replacement!=null)levels.put(depth,readLevel(replacement,depth));Element start=child(e,"startOverride");if(start!=null&&levels.containsKey(depth))levels.get(depth).start=integer(val(start),1);}result.put(id,levels);}return result;
    }
    private static Numbering readLevel(Element e,int depth){String pattern=val(child(e,"lvlText"));return new Numbering(val(child(e,"numFmt")),pattern.isEmpty()?"%"+(depth+1)+"、":pattern,integer(val(child(e,"start")),1));}
    private static String wordNumber(int n,String format){if(format.equals("upperLetter")||format.equals("lowerLetter")){String result="";for(;n>0;n=(n-1)/26)result=(char)((format.equals("upperLetter")?'A':'a')+(n-1)%26)+result;return result;}if(format.equals("decimalEnclosedCircle")&&n>=1&&n<=20)return String.valueOf((char)('①'+n-1));return Integer.toString(n);}
    private static boolean section(String text){return text.length()<=120&&text.matches("^\\s*(?:(?:[一二三四五六七八九十百\\d]+[、.．:：]|第[一二三四五六七八九十百\\d]+部分)\\s*)?(?:单项选择题|多项选择题|不定项选择题|选择题|单选题|多选题|填空题|判断题|是非题|简答题|问答题|计算题|解答题|应用题|案例分析题|分析题|编程题|程序设计题|程序阅读题|代码阅读题|综合题)\\s*(?:[（(:：].*|$)");}
    private static Map<String,Object> block(String text,int level){return MobileCompat.map("text",text,"level",level);}
    private static List<Object> expandExam(List<Object> blocks,Map<Integer,String> labels,Map<Integer,Integer> automatic,Set<Integer> outside){
        List<Integer> sections=new ArrayList<>();int sectionLevel=1;for(int i=0;i<blocks.size();i++){Map<String,Object>b=Json.object(blocks.get(i));if(outside.contains(i)&&section(Json.str(b,"text"))){sections.add(i);sectionLevel=Math.max(sectionLevel,((Number)b.get("level")).intValue());}}if(sections.isEmpty())return null;sectionLevel=Math.min(8,sectionLevel);int questionLevel=sectionLevel+1;List<Object> result=new ArrayList<>(blocks.subList(0,sections.get(0)));boolean found=false;
        for(int s=0;s<sections.size();s++){int begin=sections.get(s),end=s+1<sections.size()?sections.get(s+1):blocks.size(),outer=Integer.MAX_VALUE;for(int i=begin+1;i<end;i++)if(automatic.containsKey(i))outer=Math.min(outer,automatic.get(i));List<Integer> questions=new ArrayList<>();for(int i=begin+1;i<end;i++){String t=Json.str(Json.object(blocks.get(i)),"text");if(outside.contains(i)&&!t.trim().isEmpty()&&(automatic.containsKey(i)?automatic.get(i)==outer:!labels.containsKey(i)&&t.matches("^\\s*(?:\\d{1,4}[.、．)）:：]|[（(]\\d{1,4}[)）]|[①-⑳])\\s*\\S[\\s\\S]*")))questions.add(i);}
            String heading=Json.str(Json.object(blocks.get(begin)),"text");result.add(block(heading,sectionLevel));if(questions.isEmpty()){result.add(block(heading,questionLevel));for(int i=begin+1;i<end;i++)result.add(block(labels.getOrDefault(i,Json.str(Json.object(blocks.get(i)),"text")),0));continue;}found=true;List<Object> shared=new ArrayList<>();for(int i=begin+1;i<questions.get(0);i++)shared.add(block(labels.getOrDefault(i,Json.str(Json.object(blocks.get(i)),"text")),0));
            for(int q=0;q<questions.size();q++){int index=questions.get(q),next=q+1<questions.size()?questions.get(q+1):end;String text=labels.getOrDefault(index,Json.str(Json.object(blocks.get(index)),"text")).trim();int newline=text.indexOf('\n');String title=(newline<0?text:text.substring(0,newline)).trim();result.add(block(title.length()<=300?title:title.substring(0,297)+"…",questionLevel));result.add(block(heading,0));result.addAll(shared);if(title.length()>300)result.add(block(text,0));else if(newline>=0)result.add(block(text.substring(newline+1),0));for(int i=index+1;i<next;i++)result.add(block(labels.getOrDefault(i,Json.str(Json.object(blocks.get(i)),"text")),0));}
        }return found?result:null;
    }
    private static int outline(Element e){try{int n=Integer.parseInt(attr(e,"outlineLvl","w:val"));return n>=0&&n<=5?n+1:0;}catch(NumberFormatException ex){return 0;}}
    private static void collect(Node n,StringBuilder b){if(n instanceof Element){String name=n.getLocalName();if("t".equals(name)){b.append(n.getTextContent());return;}if("tab".equals(name))b.append('\t');if("br".equals(name))b.append('\n');}for(Node c=n.getFirstChild();c!=null;c=c.getNextSibling())collect(c,b);}
}

package com.resumestudio;

import java.nio.file.*;
import java.time.*;
import java.util.*;
import java.util.regex.*;

/** Shared data operations for the Android UI. Unknown JSON fields never round-trip through JavaScript. */
public final class MobileApi {
    final Path root; final Store store; private CompanyJobs jobs;
    private record UndoEntry(String id,Map<String,Object> document) {}
    private final Deque<UndoEntry> undo=new ArrayDeque<>();
    void clearUndo(){undo.clear();}
    public MobileApi(Path root)throws Exception{this.root=root;store=MobileData.open(root);if(!Files.exists(root.resolve("device-name.txt")))MobileCompat.writeString(root.resolve("device-name.txt"),"安卓设备");jobs=new CompanyJobs(store);}
    private Map<String,Object> find(String kind,String id)throws Exception {
        return store.list(kind).stream().filter(d->id.equals(Json.str(d,"id"))).findFirst().orElseThrow(()->new IllegalArgumentException("资料不存在，请刷新"));
    }
    public Object call(String method,Map<String,Object> request)throws Exception {
        String kind=Json.str(request,"kind"),id=Json.str(request,"id");
        switch(method){
            case "workspace":return WorkspaceTools.call(store,request);
            case "backupCatalog":return BackupCatalog.call(root,store,request);
            case "templates":return Templates.all();
            case "resumeImportCreate":{var resume=ResumeImport.create(request);resume.remove("id");return save("resumes",resume,"");}
            case "list":return store.list(kind);
            case "get":return find(kind,id);
            case "save":return save(kind,Json.object(request.get("doc")),Json.str(request,"expectedAt"));
            case "undoResume":{
                Map<String,Object> current=find("resumes",id);if(!Json.str(request,"expectedAt").equals(Json.str(current,"updatedAt")))throw new IllegalArgumentException("资料已更新，请重新打开");
                for(var iterator=undo.descendingIterator();iterator.hasNext();){UndoEntry entry=iterator.next();if(entry.id.equals(id)){iterator.remove();return store.save("resumes",entry.document);}}
                throw new IllegalArgumentException("没有可撤销的修改，可查看历史版本");
            }
            case "delete":store.delete(kind,id);return true;
            case "clone":{
                var doc=new LinkedHashMap<>(find("resumes",id));doc.put("id",UUID.randomUUID().toString());doc.put("title",Json.str(doc,"title")+" · 副本");return store.save("resumes",doc);
            }
            case "history":return store.history(id);
            case "checkpoint":store.checkpoint(id,"手动保存");return true;
            case "restore":{
                var doc=Json.object(store.revision(id,Json.str(request,"version")).get("resume"));store.checkpoint(id,"恢复历史前");return store.save("resumes",doc);
            }
            case "check":return ResumeAdvisor.check(find("resumes",id));
            case "capture":return JobCapture.preview(request);
            case "collect":{var doc=Json.object(request.get("doc"));Server.validate("favorites",doc);return store.collectFavorite(doc);}
            case "collectDetail":{var doc=Json.object(request.get("doc"));Server.validate("favorites",doc);return store.collectFavoriteDetail(doc);}
            case "openApplication":return store.openApplication(Json.object(request.get("doc")));
            case "sources":return jobs.sources();
            case "jobs":return jobs.search(Json.str(request,"provider"),Json.str(request,"keyword"),Json.str(request,"city"),((Number)request.getOrDefault("page",1)).intValue());
            case "snapshot":return store.syncExtendedSnapshot();
            case "fit":return fit(find("resumes",id),Json.object(request.get("job")));
            case "applySuggestions":{
                var original=find("resumes",id);var clone=Json.object(Json.parse(Json.write(original)));var sections=Json.array(clone.get("sections"));Set<Integer> used=new HashSet<>();
                for(Object value:Json.array(request.get("suggestions"))){var s=Json.object(value);int i=((Number)s.get("sectionIndex")).intValue();
                    if(i<0||i>=sections.size()||!used.add(i))throw new IllegalArgumentException("建议索引无效");var section=Json.object(sections.get(i));
                    if(!Boolean.TRUE.equals(section.get("visible"))||!Json.str(section,"content").equals(Json.str(s,"before"))||Json.str(s,"after").length()>30000)throw new IllegalArgumentException("简历已经变化，请重新生成建议");
                    section.put("content",Json.str(s,"after"));
                }
                clone.put("id",UUID.randomUUID().toString());clone.put("title",Json.str(original,"title")+" · 岗位版");return store.save("resumes",clone);
            }
            case "settings":return Files.exists(root.resolve("settings.json"))?Json.parse(MobileCompat.readString(root.resolve("settings.json"))):new LinkedHashMap<>();
            case "saveSettings":{
                Map<String,Object> prefs=Json.object(call("settings",Collections.emptyMap()));Map<String,Object> patch=Json.object(request.get("doc"));
                for(String key:SyncStore.SETTINGS)if(patch.containsKey(key))prefs.put(key,patch.get(key));
                var snapshot=Json.object(store.syncSnapshot().get("snapshot"));var safe=new LinkedHashMap<String,Object>();prefs.forEach((k,v)->{if(SyncStore.SETTINGS.contains(k))safe.put(k,v);});snapshot.put("settings",safe);SyncStore.validate(snapshot);
                Path temp=root.resolve("settings.tmp");MobileCompat.writeString(temp,Json.write(prefs));Files.move(temp,root.resolve("settings.json"),StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE);return true;
            }
            default:throw new IllegalArgumentException("未知操作："+method);
        }
    }
    private static void copyFields(Map<String,Object> target,Map<String,Object> patch,String keys){for(String key:keys.split(","))if(patch.containsKey(key))target.put(key,patch.get(key));}
    private Object save(String kind,Map<String,Object> patch,String expectedAt)throws Exception {
        String id=Json.str(patch,"id");Map<String,Object> previous=id.isEmpty()?new LinkedHashMap<>():find(kind,id);
        if(kind.equals("sources")&&id.isEmpty()&&!Json.str(patch,"_builtinId").isEmpty()){
            String builtin=Json.str(patch,"_builtinId");if(new CompanySources(store).all().stream().noneMatch(s->builtin.equals(Json.str(s,"id"))&&Boolean.TRUE.equals(s.get("builtin"))))throw new IllegalArgumentException("内置来源无效");id=builtin;
        }
        if(!previous.isEmpty()&&!expectedAt.equals(Json.str(previous,"updatedAt")))throw new IllegalArgumentException("资料已更新，请重新打开后编辑，避免覆盖其他修改");
        var doc=new LinkedHashMap<>(previous);if(!id.isEmpty())doc.put("id",id);
        switch(kind){
            case "resumes":{
                copyFields(doc,patch,"title,templateId,color,fontFamily,fontSize,lineHeight,photo");
                for(String field:Arrays.asList("fontSize","lineHeight"))if(doc.containsKey(field)){
                    Object number=doc.get(field);double low=field.equals("fontSize")?10:1.2,high=field.equals("fontSize")?16:2;
                    if(!(number instanceof Number n)||!Double.isFinite(n.doubleValue())||n.doubleValue()<low||n.doubleValue()>high)throw new IllegalArgumentException("字号应为 10–16，行距应为 1.2–2");
                }
                if(patch.containsKey("profile")){var profile=new LinkedHashMap<>(Json.object(previous.getOrDefault("profile",new LinkedHashMap<>())));copyFields(profile,Json.object(patch.get("profile")),"name,role,phone,email,city,link,summary");doc.put("profile",profile);}
                if(patch.containsKey("sections")){var old=Json.array(previous.getOrDefault("sections",Collections.emptyList()));var sections=new ArrayList<>();var indices=new HashSet<Integer>();
                    for(Object value:Json.array(patch.get("sections"))){var s=Json.object(value);int i=((Number)s.getOrDefault("_sourceIndex",-1)).intValue();
                        if(i>=old.size()||i< -1||i>=0&&!indices.add(i))throw new IllegalArgumentException("栏目顺序已变化，请重新打开");
                        var section=i<0?new LinkedHashMap<String,Object>():new LinkedHashMap<>(Json.object(old.get(i)));copyFields(section,s,"title,content,visible");sections.add(section);
                    }doc.put("sections",sections);
                }break;
            }
            case "favorites":copyFields(doc,patch,"title,company,location,salary,description,url,notes,source,canonicalUrl,infoStatus,tags,deadlineAt,lastFollowedAt");break;
            case "applications":{
                copyFields(doc,patch,"title,company,url,status,notes,dueAt,eventKind,reminderDone,review,tags,deadlineAt,lastFollowedAt");
                String resumeId=Json.str(patch,"_resumeId");if(!resumeId.isEmpty()&&previous.get("resumeSnapshot")==null){var resume=find("resumes",resumeId);doc.put("resumeSnapshot",resume);doc.put("resumeTitle",Json.str(resume,"title"));}break;
            }
            case "schedule":copyFields(doc,patch,"title,kind,startAt,notes,applicationId,allDay,completed,reminderEnabled,reminderMinutes");break;
            case "sources":copyFields(doc,patch,"name,adapter,slug,url,enabled,region");break;
            case "interviews":{
                copyFields(doc,patch,"title,sourceName,lastQuestionId,studyType");var old=Json.array(previous.getOrDefault("questions",Collections.emptyList()));var questions=new ArrayList<>();
                for(Object v:Json.array(patch.get("questions"))){var q=Json.object(v);var saved=old.stream().map(Json::object).filter(s->Json.str(s,"id").equals(Json.str(q,"id"))).findFirst().orElse(new LinkedHashMap<>());var copy=new LinkedHashMap<>(saved);copyFields(copy,q,"id,title,answer,status,reviewedAt,reviewCount,intervalDays,nextReviewAt,reviewNote");questions.add(copy);}doc.put("questions",questions);break;
            }
            default:throw new IllegalArgumentException("集合无效");
        }
        if(Json.write(doc).getBytes(java.nio.charset.StandardCharsets.UTF_8).length>(kind.equals("interviews")?4_000_000:2_000_000))throw new IllegalArgumentException("资料过大，请减少照片或正文");
        Server.validate(kind,doc);var saved=store.save(kind,doc);
        // Keep original native JSON, so undo does not round unknown numbers in JavaScript
        // or attach an unknown section field to a different section after sorting.
        if(kind.equals("resumes")&&!previous.isEmpty()){if(undo.size()>=30)undo.removeFirst();undo.addLast(new UndoEntry(id,previous));}
        return saved;
    }
    public static Object fit(Map<String,Object> resume,Map<String,Object> job){
        List<Object> results=new ArrayList<>(),suggestions=new ArrayList<>();String description=Json.str(job,"description");
        String evidence=Json.array(resume.get("sections")).stream().map(Json::object).filter(s->Boolean.TRUE.equals(s.get("visible"))).map(s->Json.str(s,"content")).collect(java.util.stream.Collectors.joining("\n"));
        List<String> skills=Arrays.asList("C/C++","Linux","RTOS","STM32","ARM","驱动","UART","SPI","I2C","CAN","TCP","UDP","socket","GDB","LVGL","Git","Python","Java","Qt","英语");
        for(String skill:skills){String pattern=skill.equals("C/C++")?"(?i)C/C\\+\\+|C\\+\\+|C语言":"(?i)"+Pattern.quote(skill);Pattern rx=Pattern.compile(pattern);
            if(rx.matcher(description).find())results.add(MobileCompat.map("name",skill,"requirement",Arrays.stream(description.split("\n")).filter(l->rx.matcher(l).find()).findFirst().orElse(""),"evidence",Arrays.stream(evidence.split("\n")).filter(l->rx.matcher(l).find()).findFirst().orElse("简历未找到明确表述，需核实")));}
        List<Object> sections=Json.array(resume.get("sections"));for(int i=0;i<sections.size();i++){Map<String,Object> s=Json.object(sections.get(i));if(!Boolean.TRUE.equals(s.get("visible"))||!Json.str(s,"title").contains("技能"))continue;
            String before=Json.str(s,"content");if(before.contains("## ")||before.contains("|"))continue;
            List<String> lines=new ArrayList<>(Arrays.asList(before.split("\n")));lines.sort(Comparator.comparingInt((String l)->-(int)skills.stream().filter(k->description.toLowerCase(Locale.ROOT).contains(k.toLowerCase(Locale.ROOT))&&l.toLowerCase(Locale.ROOT).contains(k.toLowerCase(Locale.ROOT))).count()));String after=String.join("\n",lines);
            if(!after.equals(before))suggestions.add(MobileCompat.map("sectionIndex",i,"before",before,"after",after,"reason","将职位相关技能前置，保留原有事实"));}
        return MobileCompat.map("requirements",results,"suggestions",suggestions);
    }
}

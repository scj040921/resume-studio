package com.resumestudio;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.*;
import java.util.zip.GZIPOutputStream;

/** Snapshot transaction under Store's monitor. A durable journal rolls back interrupted imports. */
final class SyncStore {
    static final int MAX_BYTES=64*1024*1024;
    static final List<String> KINDS=List.of("resumes","favorites","applications","sources","schedule");
    static final Set<String> SETTINGS=Set.of("interfaceStyle","theme","livePreview","motionEnabled","previewDelayMs","aiEndpoint","aiModel","updateChannel","updateRoute","checkUpdatesOnStartup");
    private static String id(Object value) {
        if(!(value instanceof String s)||!s.matches("[a-zA-Z0-9_-]{1,100}")) throw new IllegalArgumentException("同步标识无效"); return s;
    }
    private static void safe(Path root,Path target) throws IOException {
        if(!target.toAbsolutePath().normalize().startsWith(root.toAbsolutePath().normalize())) throw new IOException("同步路径越界");
        for(Path p=target;p!=null&&p.startsWith(root);p=p.getParent()) if(Files.isSymbolicLink(p)) throw new IOException("同步目录不允许符号链接");
    }
    private static Object canonical(Object value) {
        if(value instanceof Map<?,?> map) { var result=new TreeMap<String,Object>(); map.forEach((k,v)->result.put(k.toString(),canonical(v))); return result; }
        if(value instanceof List<?> list) return list.stream().map(SyncStore::canonical).toList();
        if(value instanceof java.math.BigDecimal number) return number.stripTrailingZeros(); return value;
    }
    static String hash(Map<String,Object> snapshot) {
        try { return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(Json.write(canonical(snapshot)).getBytes(StandardCharsets.UTF_8))); }
        catch(java.security.NoSuchAlgorithmException ex) { throw new IllegalStateException(ex); }
    }
    /** Latest actual file time of the selected data, excluding credentials, caches and baselines. */
    static String modifiedAt(Path root,Map<String,Object> snapshot)throws IOException {
        var paths=new ArrayList<Path>();paths.add(root.resolve("settings.json"));
        var kinds=new ArrayList<>(KINDS);if(snapshot.containsKey("interviews"))kinds.add("interviews");
        for(String kind:kinds)for(Object value:Json.array(snapshot.get(kind)))paths.add(root.resolve(kind).resolve(id(Json.object(value).get("id"))+".json"));
        for(Object value:Json.array(snapshot.get("history"))){var entry=Json.object(value);paths.add(root.resolve("history").resolve(id(entry.get("resumeId"))).resolve(id(entry.get("id"))+".json.gz"));}
        java.time.Instant latest=null;
        for(Path path:paths){safe(root,path);if(Files.exists(path)){var time=Files.getLastModifiedTime(path).toInstant();if(latest==null||time.isAfter(latest))latest=time;}}
        return latest==null?"":latest.toString();
    }
    /** Shared comparison-size JSON: ordinal keys, exact coefficient/exponent numbers, UTF-8 strings. */
    static long dataSize(Map<String,Object> snapshot){return comparisonJson(snapshot).getBytes(StandardCharsets.UTF_8).length;}
    private static String comparisonJson(Object value){
        if(value instanceof Map<?,?> map){var sorted=new TreeMap<String,Object>();map.forEach((k,v)->sorted.put(k.toString(),v));var result=new StringJoiner(",","{","}");sorted.forEach((k,v)->result.add(Json.write(k)+":"+comparisonJson(v)));return result.toString();}
        if(value instanceof List<?> list){var result=new StringJoiner(",","[","]");list.forEach(v->result.add(comparisonJson(v)));return result.toString();}
        if(value instanceof Number){var number=new java.math.BigDecimal(value.toString()).stripTrailingZeros();if(number.signum()==0)return "0";String coefficient=number.unscaledValue().toString();long exponent=-(long)number.scale();return exponent==0?coefficient:coefficient+"e"+exponent;}
        return Json.write(value);
    }
    private static Map<String,Object> settings(Path root) throws IOException {
        Path path=root.resolve("settings.json"); safe(root,path);
        if(!Files.exists(path)) return new LinkedHashMap<>();
        if(Files.size(path)>64*1024) throw new IOException("设置文件过大");
        return Json.object(Json.parse(Files.readString(path)));
    }
    static Map<String,Object> snapshot(Path root,Store store) throws IOException {
        var result=new LinkedHashMap<String,Object>();
        for(String kind:KINDS) { var records=new ArrayList<>(store.list(kind)); records.sort(Comparator.comparing(r->Json.str(r,"id"))); result.put(kind,records); }
        var prefs=new LinkedHashMap<String,Object>(); settings(root).forEach((k,v)->{if(SETTINGS.contains(k)) prefs.put(k,v);}); result.put("settings",prefs);
        var revisions=new ArrayList<Map<String,Object>>();
        for(var resume:Json.array(result.get("resumes"))) {
            String resumeId=Json.str(Json.object(resume),"id");
            for(var revision:store.history(resumeId)) {
                var entry=new LinkedHashMap<>(store.revision(resumeId,Json.str(revision,"id"))); entry.put("resumeId",resumeId); revisions.add(entry);
            }
        }
        revisions.sort(Comparator.comparing(r->Json.str(r,"resumeId")+":"+Json.str(r,"id"))); result.put("history",revisions);
        if(Json.write(result).getBytes(StandardCharsets.UTF_8).length>MAX_BYTES) throw new IOException("同步资料超过 64 MiB，请减少历史或照片后重试");
        return result;
    }
    static Map<String,Object> extendedSnapshot(Path root,Store store) throws IOException {
        var result=snapshot(root,store);var decks=new ArrayList<>(store.list("interviews"));decks.sort(Comparator.comparing(d->Json.str(d,"id")));result.put("interviews",decks);
        if(Json.write(result).getBytes(StandardCharsets.UTF_8).length>MAX_BYTES)throw new IOException("同步资料超过 64 MiB");return result;
    }
    static void validate(Map<String,Object> snapshot) {
        var keys=new HashSet<>(Set.of("resumes","favorites","applications","sources","schedule","settings","history"));if(snapshot.containsKey("interviews"))keys.add("interviews");
        if(!snapshot.keySet().equals(keys)) throw new IllegalArgumentException("同步资料集合不完整或协议不兼容");
        if(Json.write(snapshot).getBytes(StandardCharsets.UTF_8).length>MAX_BYTES) throw new IllegalArgumentException("同步资料超过 64 MiB");
        Set<String> resumes=new HashSet<>();
        for(String kind:KINDS) {
            var records=Json.array(snapshot.get(kind)); if(records.size()>5000) throw new IllegalArgumentException("同步记录过多");
            Set<String> ids=new TreeSet<>(String.CASE_INSENSITIVE_ORDER);
            for(Object value:records) {
                var doc=Json.object(value); String recordId=id(doc.get("id")); if(!ids.add(recordId)) throw new IllegalArgumentException("同步记录标识重复");
                if(kind.equals("resumes")) resumes.add(recordId);
                if(Json.write(doc).getBytes(StandardCharsets.UTF_8).length>2_000_000) throw new IllegalArgumentException("同步单条资料超过 2 MB");
                validateTypes(kind,doc);
                Server.validate(kind,doc);
            }
        }
        var prefs=Json.object(snapshot.get("settings"));
        integerFields(prefs,"previewDelayMs");
        for(var pair:prefs.entrySet()) {
            String key=pair.getKey(); Object value=pair.getValue();
            if(!SETTINGS.contains(key)) throw new IllegalArgumentException("同步设置包含不允许的字段");
            boolean valid=switch(key) {
                case "livePreview","motionEnabled","checkUpdatesOnStartup" -> value instanceof Boolean;
                case "previewDelayMs" -> value instanceof Number n&&n.doubleValue()==n.intValue()&&n.intValue()>=350&&n.intValue()<=2000;
                case "interfaceStyle" -> Set.of("classic","haru").contains(value);
                case "theme" -> Set.of("system","light","dark").contains(value);
                case "updateChannel" -> Set.of("stable","nightly").contains(value);
                case "updateRoute" -> Set.of("auto","direct","proxy").contains(value);
                case "aiEndpoint","aiModel" -> value instanceof String text&&text.length()<=2048;
                default -> false;
            }; if(!valid) throw new IllegalArgumentException("同步设置字段无效："+key);
        }
        Map<String,Integer> counts=new HashMap<>(); Set<String> ids=new TreeSet<>(String.CASE_INSENSITIVE_ORDER);
        for(Object value:Json.array(snapshot.get("history"))) {
            var entry=Json.object(value); String resumeId=id(entry.get("resumeId")),version=id(entry.get("id"));
            if(!resumes.contains(resumeId)||!ids.add(resumeId+":"+version)||counts.merge(resumeId,1,Integer::sum)>20) throw new IllegalArgumentException("同步历史关联、数量或版本无效");
            if(!(entry.get("label") instanceof String label)||label.length()>100||!(entry.get("automatic") instanceof Boolean)) throw new IllegalArgumentException("同步历史格式无效");
            try { Instant.parse(Json.str(entry,"createdAt")); } catch(Exception ex) { throw new IllegalArgumentException("同步历史时间无效"); }
            var resume=Json.object(entry.get("resume")); if(!resumeId.equals(Json.str(resume,"id"))) throw new IllegalArgumentException("同步历史简历标识无效"); validateTypes("resumes",resume); Server.validate("resumes",resume);
            if(Json.write(entry).getBytes(StandardCharsets.UTF_8).length>2_100_000) throw new IllegalArgumentException("同步历史过大");
        }
        if(snapshot.containsKey("interviews")){
            var decks=Json.array(snapshot.get("interviews"));if(decks.size()>5000)throw new IllegalArgumentException("同步题库过多");Set<String> deckIds=new TreeSet<>(String.CASE_INSENSITIVE_ORDER);
            for(Object value:decks){var deck=Json.object(value);if(!deckIds.add(id(deck.get("id"))))throw new IllegalArgumentException("同步题库标识重复");textFields(deck,"id,title,sourceName,updatedAt");InterviewStudy.validate(deck);
                for(Object question:Json.array(deck.get("questions")))textFields(Json.object(question),"id,title,answer,status,reviewedAt");
                if(Json.write(deck).getBytes(StandardCharsets.UTF_8).length>4_000_000)throw new IllegalArgumentException("同步题库超过 4 MB");
            }
        }
    }
    private static void textFields(Map<String,Object> doc,String fields) {
        for(String field:fields.split(",")) if(doc.containsKey(field)&&!(doc.get(field) instanceof String)) throw new IllegalArgumentException("同步文本字段无效："+field);
    }
    private static void booleanFields(Map<String,Object> doc,String fields) {
        for(String field:fields.split(",")) if(doc.containsKey(field)&&!(doc.get(field) instanceof Boolean)) throw new IllegalArgumentException("同步状态字段无效："+field);
    }
    private static void integerFields(Map<String,Object> doc,String fields) {
        for(String field:fields.split(",")) if(doc.containsKey(field)) {
            Object value=doc.get(field);
            try {
                // C# Int32 rejects fractional/exponent tokens even when their mathematical value is an integer.
                if(!(value instanceof Number)||!value.toString().matches("-?(0|[1-9][0-9]*)")) throw new NumberFormatException();
                Integer.parseInt(value.toString());
            } catch(NumberFormatException ex) { throw new IllegalArgumentException("同步整数字段无效："+field); }
        }
    }
    private static void validateTypes(String kind,Map<String,Object> doc) {
        textFields(doc,"id,updatedAt");
        switch(kind) {
            case "resumes" -> {
                textFields(doc,"title,templateId,color,fontFamily,photo");
                for(String field:List.of("fontSize","lineHeight")) if(doc.containsKey(field)&&(!(doc.get(field) instanceof Number n)||!Double.isFinite(n.doubleValue()))) throw new IllegalArgumentException("同步数字字段无效："+field);
                textFields(Json.object(doc.get("profile")),"name,role,phone,email,city,link,summary");
                for(Object value:Json.array(doc.get("sections"))) { var section=Json.object(value); textFields(section,"title,content"); booleanFields(section,"visible"); }
            }
            case "favorites" -> textFields(doc,"title,company,location,description,url,canonicalUrl,source,publishedAt,experience,salary,notes,infoStatus");
            case "applications" -> {
                textFields(doc,"jobId,title,company,url,status,resumeTitle,notes,createdAt,eventKind,dueAt,review"); booleanFields(doc,"reminderDone");
                if(doc.containsKey("timeline")) for(Object value:Json.array(doc.get("timeline"))) textFields(Json.object(value),"status,at,note");
                if(doc.get("resumeSnapshot")!=null) validateTypes("resumes",Json.object(doc.get("resumeSnapshot")));
            }
            case "sources" -> { textFields(doc,"name,adapter,slug,url,region,status,message,lastQueryAt"); booleanFields(doc,"enabled,builtin"); integerFields(doc,"lastCount"); }
            case "schedule" -> { textFields(doc,"title,kind,startAt,notes,applicationId"); booleanFields(doc,"allDay,completed,reminderEnabled"); integerFields(doc,"reminderMinutes"); }
            default -> throw new IllegalArgumentException("同步集合无效");
        }
    }
    private static void atomic(Path root,Path path,byte[] bytes) throws IOException {
        safe(root,path); Files.createDirectories(path.getParent()); Path temp=path.resolveSibling(path.getFileName()+".sync-tmp"); safe(root,temp);
        try(var out=new FileOutputStream(temp.toFile())) { out.write(bytes); out.getFD().sync(); }
        try { Files.move(temp,path,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE); }
        catch(AtomicMoveNotSupportedException ex) { throw new IOException("数据磁盘不支持原子替换，同步已停止",ex); }
    }
    private static void apply(Path root,Map<String,Object> snapshot) throws IOException {
        var kinds=new ArrayList<>(KINDS);if(snapshot.containsKey("interviews"))kinds.add("interviews");
        for(String kind:kinds) {
            // Use the filesystem's Path equality, including Windows case-insensitive names.
            Path dir=root.resolve(kind); safe(root,dir); Files.createDirectories(dir); Set<Path> names=new HashSet<>();
            for(Object value:Json.array(snapshot.get(kind))) {
                var doc=Json.object(value); Path path=dir.resolve(id(doc.get("id"))+".json"); names.add(path); atomic(root,path,Json.write(doc).getBytes(StandardCharsets.UTF_8));
            }
            try(var files=Files.list(dir)) { for(Path file:files.filter(p->p.getFileName().toString().endsWith(".json")).toList()) { safe(root,file); if(!names.contains(file)) Files.delete(file); } }
        }
        var prefs=new LinkedHashMap<>(settings(root)); SETTINGS.forEach(prefs::remove); prefs.putAll(Json.object(snapshot.get("settings")));
        atomic(root,root.resolve("settings.json"),Json.write(prefs).getBytes(StandardCharsets.UTF_8));
        Path history=root.resolve("history"); safe(root,history); Files.createDirectories(history); Set<Path> names=new HashSet<>();
        for(Object value:Json.array(snapshot.get("history"))) {
            var entry=new LinkedHashMap<>(Json.object(value)); String resumeId=id(entry.remove("resumeId")); Path path=history.resolve(resumeId).resolve(id(entry.get("id"))+".json.gz"); names.add(path);
            var bytes=new ByteArrayOutputStream(); try(var out=new GZIPOutputStream(bytes)) { out.write(Json.write(entry).getBytes(StandardCharsets.UTF_8)); } atomic(root,path,bytes.toByteArray());
        }
        try(var directories=Files.list(history)) { for(Path dir:directories.toList()) { safe(root,dir); if(!Files.isDirectory(dir)) continue;
            try(var files=Files.list(dir)) { for(Path path:files.filter(p->p.getFileName().toString().endsWith(".json.gz")).toList()) { safe(root,path); if(!names.contains(path)) Files.delete(path); } }
            try(var remaining=Files.list(dir)) { if(remaining.findAny().isEmpty()) Files.delete(dir); }
        } }
    }
    static void recover(Path root) throws IOException {
        Path journal=root.resolve("sync-recovery.json"); safe(root,journal);
        if(!Files.exists(journal)) return;
        if(Files.size(journal)>MAX_BYTES) throw new IOException("同步恢复日志过大，需要手动恢复备份");
        var previous=Json.object(Json.parse(Files.readString(journal))); validate(previous); apply(root,previous); Files.delete(journal);
    }
    static Map<String,Object> replace(Path root,Store store,String expectedHash,Map<String,Object> incoming) throws IOException {
        validate(incoming); boolean extended=incoming.containsKey("interviews");var previous=extended?extendedSnapshot(root,store):snapshot(root,store);
        if(!hash(previous).equals(expectedHash)) throw new ConcurrentModificationException("本机资料已改变，请重新检查同步");
        String backupName=Instant.now().toString().replace(':','-')+"-"+UUID.randomUUID()+".json";
        atomic(root,root.resolve("sync-backups").resolve(backupName),Json.write(previous).getBytes(StandardCharsets.UTF_8));
        Path journal=root.resolve("sync-recovery.json"); atomic(root,journal,Json.write(previous).getBytes(StandardCharsets.UTF_8));
        try { apply(root,incoming); Files.delete(journal); }
        catch(IOException|RuntimeException ex) { try { recover(root); } catch(Exception rollback) { ex.addSuppressed(rollback); } throw ex; }
        var current=extended?extendedSnapshot(root,store):snapshot(root,store); return Map.of("snapshot",current,"hash",hash(current),"backup",backupName);
    }
}

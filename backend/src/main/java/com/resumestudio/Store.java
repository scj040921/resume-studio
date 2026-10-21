package com.resumestudio;

import java.nio.file.*;
import java.io.IOException;
import java.io.OutputStream;
import java.time.Instant;
import java.util.*;
import java.util.zip.GZIPOutputStream;

/** Each document is atomically replaced. One compressed previous revision is kept for recovery. */
public final class Store {
    private final Path root;
    private final ResumeHistory history;
    public Store(Path root) throws IOException { this.root=root.toAbsolutePath().normalize(); Files.createDirectories(this.root); SyncStore.recover(this.root); history=new ResumeHistory(this.root); }
    public synchronized Map<String,Object> syncSnapshot() throws IOException {
        var snapshot=SyncStore.snapshot(root,this); return Map.of("snapshot",snapshot,"hash",SyncStore.hash(snapshot),"modifiedAt",SyncStore.modifiedAt(root,snapshot));
    }
    public synchronized Map<String,Object> syncExtendedSnapshot() throws IOException {
        var snapshot=SyncStore.extendedSnapshot(root,this); return Map.of("snapshot",snapshot,"hash",SyncStore.hash(snapshot),"modifiedAt",SyncStore.modifiedAt(root,snapshot));
    }
    public synchronized Map<String,Object> syncImport(String expectedHash,Map<String,Object> snapshot) throws IOException { return SyncStore.replace(root,this,expectedHash,snapshot); }
    private Path path(String kind,String id) throws IOException {
        if (!Set.of("resumes","favorites","applications","sources","schedule","interviews").contains(kind)) throw new IllegalArgumentException("Invalid collection");
        if (!id.matches("[a-zA-Z0-9_-]{1,100}")) throw new IllegalArgumentException("Invalid identifier");
        Path dir=root.resolve(kind); Files.createDirectories(dir); return dir.resolve(id+".json");
    }
    public synchronized Map<String,Object> save(String kind, Map<String,Object> doc) throws IOException {
        String id=Json.str(doc,"id"); if (id.isBlank()) id=UUID.randomUUID().toString();
        Map<String,Object> copy=new LinkedHashMap<>(doc); copy.put("id",id); copy.put("updatedAt",Instant.now().toString());
        Path p=path(kind,id), temp=p.resolveSibling(p.getFileName()+".tmp");
        if(kind.equals("favorites")) for(var saved:list(kind)) {
            if(!id.equals(Json.str(saved,"id"))&&JobWorkflow.sameJob(copy,saved,false)) throw new IllegalArgumentException("该职位已收藏，请编辑已有收藏，避免覆盖备注。");
        }
        Map<String,Object> previous=Files.exists(p)?Json.object(Json.parse(Files.readString(p))):new LinkedHashMap<>();
        if(kind.equals("resumes")&&!previous.isEmpty()) {
            var before=new LinkedHashMap<>(previous); var after=new LinkedHashMap<>(copy); before.remove("updatedAt"); after.remove("updatedAt");
            if(!Json.write(before).equals(Json.write(after))) history.capture(id,previous,"自动保存前",true);
        }
        if(kind.equals("applications")) {
            // A submitted snapshot and its timeline are retained even when an older client saves the record.
            for(String field:List.of("dueAt","eventKind","reminderDone","review","createdAt")) if(!copy.containsKey(field)&&previous.containsKey(field)) copy.put(field,previous.get(field));
            if(previous.get("resumeSnapshot")!=null) { copy.put("resumeSnapshot",previous.get("resumeSnapshot")); copy.put("resumeTitle",previous.get("resumeTitle")); }
            var events=new ArrayList<>(Json.array(previous.getOrDefault("timeline",List.of())));
            if(!Json.str(previous,"status").equals(Json.str(copy,"status"))) events.add(Map.of("status",Json.str(copy,"status"),"at",Instant.now().toString()));
            if(!Json.str(previous,"dueAt").equals(Json.str(copy,"dueAt"))) events.add(Map.of("status",Json.str(copy,"status"),"at",Instant.now().toString(),"note",Json.str(copy,"dueAt").isBlank()?"已清除下一次安排":Json.str(copy,"eventKind")+"："+Json.str(copy,"dueAt")));
            copy.put("timeline",events.stream().skip(Math.max(0,events.size()-100)).toList());
        }
        Path backup=p.resolveSibling(p.getFileName()+".bak.gz"), backupTemp=p.resolveSibling(p.getFileName()+".bak.gz.tmp");
        if (Files.exists(p)) {
            try (OutputStream out=new GZIPOutputStream(Files.newOutputStream(backupTemp))) { Files.copy(p,out); }
            try { Files.move(backupTemp,backup,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE); }
            catch(AtomicMoveNotSupportedException e) { Files.move(backupTemp,backup,StandardCopyOption.REPLACE_EXISTING); }
            Files.deleteIfExists(p.resolveSibling(p.getFileName()+".bak"));
        }
        Files.writeString(temp,Json.write(copy));
        try { Files.move(temp,p,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE); }
        catch(AtomicMoveNotSupportedException e) { Files.move(temp,p,StandardCopyOption.REPLACE_EXISTING); }
        return copy;
    }
    public synchronized List<Map<String,Object>> list(String kind) throws IOException {
        Path dir=path(kind,"probe").getParent(); List<Map<String,Object>> docs=new ArrayList<>();
        try (var files=Files.list(dir)) {
            for(Path p:files.filter(x->x.toString().endsWith(".json")).toList()) docs.add(Json.object(Json.parse(Files.readString(p))));
        }
        docs.sort(Comparator.comparing((Map<String,Object> m)->Json.str(m,"updatedAt")).reversed()); return docs;
    }
    public synchronized Map<String,Object> collectFavorite(Map<String,Object> job) throws IOException {
        for(var saved:list("favorites")) if(JobWorkflow.sameJob(job,saved,false)) return Map.of("record",saved,"created",false);
        return Map.of("record",save("favorites",job),"created",true);
    }
    /** An explicit current-detail capture may fill gaps, but cannot overwrite user content. */
    public synchronized Map<String,Object> collectFavoriteDetail(Map<String,Object> job) throws IOException {
        if(Json.str(job,"description").trim().length()<20)throw new IllegalArgumentException("职位正文未加载完整，请打开详情后重试。");
        for(var saved:list("favorites")) if(JobWorkflow.sameJob(job,saved,false)) {
            var enriched=new LinkedHashMap<String,Object>(saved);boolean changed=false;
            for(String field:List.of("company","location","salary","description","experience","publishedAt")) {
                String before=Json.str(saved,field),after=Json.str(job,field);
                boolean unreadable=!Json.str(saved,"infoStatus").equals("用户核对")&&before.matches("(?s).*[\\uE000-\\uF8FF\\uFFFD].*");
                if((before.isBlank()||unreadable)&&!after.isBlank()) {enriched.put(field,after);changed=true;}
            }
            return Map.of("record",changed?save("favorites",enriched):saved,"created",false,"updated",changed);
        }
        return Map.of("record",save("favorites",job),"created",true,"updated",false);
    }
    public synchronized Map<String,Object> openApplication(Map<String,Object> job) throws IOException {
        for(var saved:list("applications")) if(JobWorkflow.sameJob(job,saved,true)) return Map.of("record",saved,"created",false);
        var record=new LinkedHashMap<String,Object>();
        record.put("jobId",Json.str(job,"id"));
        for(String field:List.of("title","company","url")) record.put(field,Json.str(job,field));
        record.put("status","已打开");record.put("createdAt",Instant.now().toString());
        return Map.of("record",save("applications",record),"created",true);
    }
    public synchronized Map<String,Object> findApplication(Map<String,Object> job) throws IOException {
        for(var saved:list("applications")) if(JobWorkflow.sameJob(job,saved,true)) return Map.of("record",saved);
        return Map.of();
    }
    public synchronized void delete(String kind,String id) throws IOException {
        Path p=path(kind,id); Files.deleteIfExists(p); Files.deleteIfExists(p.resolveSibling(p.getFileName()+".bak")); Files.deleteIfExists(p.resolveSibling(p.getFileName()+".bak.gz"));
        if(kind.equals("resumes")) history.delete(id);
    }
    public synchronized List<Map<String,Object>> history(String id) throws IOException { path("resumes",id); return history.list(id); }
    public synchronized Map<String,Object> revision(String id,String version) throws IOException { path("resumes",id); return history.get(id,version); }
    public synchronized void checkpoint(String id,String label) throws IOException {
        var doc=Json.object(Json.parse(Files.readString(path("resumes",id))));
        history.capture(id,doc,label.isBlank()?"手动保存":label.substring(0,Math.min(100,label.length())),false);
    }
}

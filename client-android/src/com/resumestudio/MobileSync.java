package com.resumestudio;

import java.io.*;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.time.*;
import java.util.*;

/** v1 interoperable whole-snapshot synchronization. Every changed plan requires explicit direction. */
public final class MobileSync {
    public interface Remote { Fetched read() throws IOException; String write(byte[] bytes,String etag) throws IOException; }
    public static final class Fetched {
        public final byte[] bytes; public final String etag,modifiedAt;
        public Fetched(byte[] bytes,String etag) {this(bytes,etag,"");}
        public Fetched(byte[] bytes,String etag,String modifiedAt) {this.bytes=bytes;this.etag=etag;this.modifiedAt=modifiedAt;}
    }
    public static final class Plan {
        final String action,localHash,etag,target; final Map<String,Object> envelope,comparison;
        Plan(String action,String localHash,String etag,String target,Map<String,Object> envelope,Map<String,Object> comparison){this.action=action;this.localHash=localHash;this.etag=etag;this.target=target;this.envelope=envelope;this.comparison=comparison;}
        public Map<String,Object> display(){var result=new LinkedHashMap<>(comparison);result.putAll(MobileCompat.map("action",action,"requiresConfirmation",!action.equals("unchanged"),"remoteExists",envelope!=null,"contentCheck",etag!=null&&etag.startsWith("sha256:"),"remoteDevice",envelope==null?"":Json.str(envelope,"deviceId")));return result;}
    }
    private final Store store; private final Path root; private final Remote remote; private final String target,device;
    public MobileSync(Store store,Path root,Remote remote,String target,String device){this.store=store;this.root=root;this.remote=remote;this.target=target;this.device=device;}
    static Map<String,Object> envelope(byte[] bytes) throws IOException {
        if(bytes.length>SyncStore.MAX_BYTES)throw new IOException("同步包超过 64 MiB");
        String text=StandardCharsets.UTF_8.newDecoder().decode(java.nio.ByteBuffer.wrap(bytes)).toString();
        Map<String,Object> value=Json.object(Json.parse(text));
        if(!"resume-studio-sync".equals(value.get("protocol"))||!"1".equals(Objects.toString(value.get("schemaVersion"))))throw new IOException("同步协议不兼容，请更新两端软件");
        for(String key:Arrays.asList("snapshotId","deviceId"))if(!(value.get(key) instanceof String s)||s.isEmpty()||s.length()>100)throw new IOException("同步包标识无效");
        try{OffsetDateTime.parse(Json.str(value,"createdAt"));}catch(Exception ex){throw new IOException("同步包时间无效",ex);}
        Map<String,Object> extensions=Json.object(value.get("extensions"));SyncStore.validate(Json.object(value.get("data")));
        if(extensions.containsKey("interviewStudy")){var study=Json.object(extensions.get("interviewStudy"));if(!"1".equals(Objects.toString(study.get("version"))))throw new IOException("面试题同步扩展版本不兼容");var extended=new LinkedHashMap<>(Json.object(value.get("data")));extended.put("interviews",Json.array(study.get("decks")));SyncStore.validate(extended);}return value;
    }
    private static Map<String,Object> snapshot(Map<String,Object> envelope,Map<String,Object> local){var data=new LinkedHashMap<>(Json.object(envelope.get("data")));var extensions=Json.object(envelope.get("extensions"));data.put("interviews",extensions.containsKey("interviewStudy")?Json.object(extensions.get("interviewStudy")).get("decks"):local.get("interviews"));return data;}
    public Plan inspect() throws IOException {
        Map<String,Object> local=store.syncExtendedSnapshot();Fetched fetched=remote.read();String baseline=null;Map<String,Object> saved=null;
        Path file=root.resolve("sync-baseline-android.json");
        if(Files.exists(file)) {Map<String,Object> b=Json.object(Json.parse(MobileCompat.readString(file)));if(target.equals(b.get("target"))){baseline=Json.str(b,"hash");saved=b;}}
        String action;Map<String,Object> envelope=fetched==null?null:envelope(fetched.bytes);
        if(envelope==null)action=baseline==null?"push":"conflict";
        else {String l=SyncStore.hash(Json.object(local.get("snapshot"))),r=SyncStore.hash(snapshot(envelope,Json.object(local.get("snapshot"))));
            action=l.equals(r)?"unchanged":baseline==null?"conflict":l.equals(baseline)?"pull":r.equals(baseline)?"push":"conflict";
            if(action.equals("unchanged")&&Json.array(Json.object(local.get("snapshot")).get("interviews")).size()>0&&!Json.object(envelope.get("extensions")).containsKey("interviewStudy"))action="push";}
        String baselineText=saved==null?"无历史基线":!(saved.get("dataBytes") instanceof Number)?"上次同步未记录大小":"上次成功同步："+Json.str(saved,"at");
        Long previous=saved!=null&&saved.get("dataBytes") instanceof Number n?n.longValue():null;
        var data=Json.object(local.get("snapshot"));var remoteData=envelope==null?null:snapshot(envelope,data);
        boolean legacy=envelope!=null&&!Json.object(envelope.get("extensions")).containsKey("interviewStudy");
        String summary=envelope==null?"云端同步包不存在，请确认是否上传本机资料。":switch(action){case "push"->"本机资料发生变化，请选择最终保留的版本。";case "pull"->"云端资料发生变化，请选择最终保留的版本。";case "unchanged"->"两端资料一致，无需同步。";default->"两端资料不同，请选择最终保留的版本。";};
        Map<String,Object> comparison=MobileCompat.map("local",version(data,Json.str(local,"modifiedAt"),previous),"remote",version(remoteData,fetched==null?"":fetched.modifiedAt,previous),"summary",summary,"baselineText",baselineText,"note",legacy?"云端旧格式未包含面试题库。云端版本的比较大小包含将保留的本机题库；选择下载也会保留这些题库，选择上传会补齐云端扩展。":"");
        return new Plan(action,Json.str(local,"hash"),fetched==null?null:fetched.etag,target,envelope,comparison);
    }
    private static Map<String,Object> version(Map<String,Object> data,String modified,Long previous){
        long bytes=data==null?0:SyncStore.dataSize(data);String counts=data==null?"":Json.array(data.get("resumes")).size()+" 份简历 / "+Json.array(data.get("favorites")).size()+" 个收藏 / "+Json.array(data.get("applications")).size()+" 条投递 / "+Json.array(data.get("schedule")).size()+" 条日程 / "+Json.array(data.get("interviews")).size()+" 个题库";
        return MobileCompat.map("exists",data!=null,"dataBytes",bytes,"deltaBytes",data==null||previous==null?null:bytes-previous,"modifiedAt",modified,"counts",counts);
    }
    /** Only the configured background job may accept a non-conflicting baseline direction. */
    public Plan inspectAuto() throws IOException {return inspect();}
    public String executeAuto(Plan plan) throws IOException {
        if(plan.action.equals("conflict"))throw new IOException("两端资料均有变化，请打开设置确认保留版本");
        return execute(plan,plan.action);
    }
    public String execute(Plan plan,String direction) throws IOException {
        if(!plan.target.equals(target))throw new IOException("同步目标改变，请重新检查");
        String action=plan.action.equals("unchanged")?"unchanged":direction;
        if(action==null||!Arrays.asList("push","pull","unchanged").contains(action)||(!plan.action.equals("unchanged")&&action.equals("unchanged")))throw new IOException("请先确认要保留的版本，再执行同步");
        synchronized(store){
            Map<String,Object> current=store.syncExtendedSnapshot();if(!plan.localHash.equals(current.get("hash")))throw new IOException("本机资料已改变，请重新检查同步");
            Fetched observed=remote.read();if(!Objects.equals(plan.etag,observed==null?null:observed.etag))throw new IOException("云端资料已改变，请重新检查同步");
            String hash=Json.str(current,"hash"),etag=plan.etag;long dataBytes=SyncStore.dataSize(Json.object(current.get("snapshot")));
            if(action.equals("push")){
                Map<String,Object> outgoing=plan.envelope==null?new LinkedHashMap<>():new LinkedHashMap<>(plan.envelope);
                outgoing.put("protocol","resume-studio-sync");outgoing.put("schemaVersion",1);outgoing.put("snapshotId",UUID.randomUUID().toString());outgoing.put("deviceId",device);outgoing.put("deviceName",BackupCatalog.device(root));outgoing.put("createdAt",Instant.now().toString());
                var data=new LinkedHashMap<>(Json.object(current.get("snapshot")));Object decks=data.remove("interviews");outgoing.put("data",data);
                var extensions=new LinkedHashMap<>(Json.object(outgoing.getOrDefault("extensions",new LinkedHashMap<>())));var study=new LinkedHashMap<>(Json.object(extensions.getOrDefault("interviewStudy",new LinkedHashMap<>())));study.put("version",1);study.put("decks",decks);extensions.put("interviewStudy",study);outgoing.put("extensions",extensions);
                byte[] bytes=Json.write(outgoing).getBytes(StandardCharsets.UTF_8);if(bytes.length>SyncStore.MAX_BYTES)throw new IOException("同步包超过 64 MiB");etag=remote.write(bytes,plan.etag);
            }else if(action.equals("pull")){
                if(plan.envelope==null)throw new IOException("云端文件不存在，无法下载");
                Map<String,Object> data=snapshot(plan.envelope,Json.object(current.get("snapshot")));Map<String,Object> result=store.syncImport(plan.localHash,data);hash=Json.str(result,"hash");
                if(!hash.equals(SyncStore.hash(data)))throw new IOException("导入校验不一致，请检查备份");
                dataBytes=SyncStore.dataSize(Json.object(result.get("snapshot")));
            }
            Path file=root.resolve("sync-baseline-android.json"),tmp=root.resolve("sync-baseline-android.tmp");
            if(action.equals("unchanged")&&Files.exists(file)){var previous=Json.object(Json.parse(MobileCompat.readString(file)));if(target.equals(previous.get("target"))&&hash.equals(previous.get("hash"))&&Objects.equals(etag,previous.get("etag"))&&previous.get("dataBytes") instanceof Number n&&n.longValue()==dataBytes)return action;}
            MobileCompat.writeString(tmp,Json.write(MobileCompat.map("target",target,"hash",hash,"etag",etag,"at",Instant.now().toString(),"dataBytes",dataBytes)));Files.move(tmp,file,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE);return action;
        }
    }
    public List<Object> review(Plan plan)throws IOException{return SyncReview.differences(Json.object(store.syncExtendedSnapshot().get("snapshot")),snapshot(plan.envelope,Json.object(store.syncExtendedSnapshot().get("snapshot"))));}
    public String merge(Plan plan,Map<String,Object> choices)throws IOException{
        synchronized(store){
            Map<String,Object> current=store.syncExtendedSnapshot();if(!plan.localHash.equals(current.get("hash")))throw new IOException("本机资料已改变，请重新检查");
            Fetched observed=remote.read();if(!Objects.equals(plan.etag,observed==null?null:observed.etag))throw new IOException("云端资料已改变，请重新检查");
            var remoteData=snapshot(plan.envelope,Json.object(current.get("snapshot")));var merged=SyncReview.merge(Json.object(current.get("snapshot")),remoteData,choices);
            Path backups=root.resolve("sync-backups");Files.createDirectories(backups);String backupName="merge-remote-"+UUID.randomUUID()+".json";MobileCompat.writeString(backups.resolve(backupName),Json.write(remoteData));BackupCatalog.info(root,backupName,Json.str(plan.envelope,plan.envelope.containsKey("deviceName")?"deviceName":"deviceId"),"云端合并前");
            var applied=store.syncImport(plan.localHash,merged);
            return execute(new Plan("push",Json.str(applied,"hash"),plan.etag,plan.target,plan.envelope,plan.comparison),"push");
        }
    }
    public static URI address(String text){
        URI uri=URI.create(text.trim());String path=uri.getPath();
        boolean loopback="http".equals(uri.getScheme())&&Arrays.asList("127.0.0.1","localhost").contains(uri.getHost());
        if((!"https".equals(uri.getScheme())&&!loopback)||uri.getHost()==null||uri.getUserInfo()!=null||uri.getQuery()!=null||uri.getFragment()!=null||path==null||path.contains("\\")||Arrays.stream(path.split("/",-1)).anyMatch(p->p.equals(".")||p.equals("..")))throw new IllegalArgumentException("请填写无参数、无账号信息的最终 HTTPS WebDAV 目录地址");
        return URI.create(uri.toASCIIString().replaceAll("/+$","")+"/");
    }
    public static final class Dav implements Remote {
        private final URI file; private final Map<String,String> auth=new HashMap<>();
        public Dav(String directory,String user,String password){
            file=address(directory).resolve("resume-studio.sync.json");if(user.contains(":")||user.contains("\r")||user.contains("\n")||user.length()>1024)throw new IllegalArgumentException("WebDAV 用户名无效");
            if(!user.isEmpty()||!password.isEmpty())auth.put("Authorization","Basic "+Base64.getEncoder().encodeToString((user+":"+password).getBytes(StandardCharsets.UTF_8)));
            auth.put("Cache-Control","no-cache");
        }
        private static void ensure(int code)throws IOException {if(code==409||code==412)throw new IOException("云端资料已改变，条件上传被拒绝，请重新检查");if(code>=300&&code<400)throw new IOException("WebDAV 返回重定向，请填写最终目录地址");if(code<200||code>=300)throw new IOException("WebDAV 请求失败（HTTP "+code+"），请检查目录和凭据");}
        static boolean isStrong(String etag){return etag!=null&&etag.matches("\"[^\"\\r\\n]*\"");}
        static String fingerprint(byte[] bytes)throws IOException {
            try{return "sha256:"+MobileCompat.hex(java.security.MessageDigest.getInstance("SHA-256").digest(bytes));}
            catch(java.security.NoSuchAlgorithmException ex){throw new IOException("无法校验同步内容",ex);}
        }
        public Fetched read()throws IOException {MobileHttp.Result r=MobileHttp.request(file,"GET",auth,null,SyncStore.MAX_BYTES);if(r.status==404)return null;ensure(r.status);return new Fetched(r.bytes,isStrong(r.etag)?r.etag:fingerprint(r.bytes),r.modifiedAt);}
        public String write(byte[] bytes,String etag)throws IOException {
            if(bytes.length>SyncStore.MAX_BYTES)throw new IOException("同步包超过 64 MiB");
            Map<String,String> headers=new HashMap<>(auth);headers.put("Content-Type","application/json; charset=utf-8");
            if(etag==null)headers.put("If-None-Match","*");
            else if(etag.startsWith("sha256:")){
                if(!etag.matches("sha256:[0-9a-f]{64}"))throw new IOException("云端版本标识无效");
                Fetched observed=read();
                if(observed==null||!fingerprint(observed.bytes).equals(etag))throw new IOException("云端资料已改变，内容校验已拒绝上传，请重新检查");
                // A content fingerprint is local only. Prefer a recovered strong ETag; otherwise require existence.
                headers.put("If-Match",isStrong(observed.etag)?observed.etag:"*");
            }else {
                if(!isStrong(etag))throw new IOException("云端 ETag 无效");
                headers.put("If-Match",etag);
            }
            MobileHttp.Result r=MobileHttp.request(file,"PUT",headers,bytes,SyncStore.MAX_BYTES);
            if(r.status==412&&etag!=null&&etag.startsWith("sha256:")&&"*".equals(headers.get("If-Match"))){
                Fetched latest=read();
                if(latest==null||!fingerprint(latest.bytes).equals(etag))throw new IOException("云端资料已改变，内容校验已拒绝上传，请重新检查");
                // Retry only the content-check mode; retain a strong validator if one becomes available.
                if(isStrong(latest.etag))headers.put("If-Match",latest.etag);else headers.remove("If-Match");
                r=MobileHttp.request(file,"PUT",headers,bytes,SyncStore.MAX_BYTES);
            }
            ensure(r.status);Fetched committed=read();
            if(committed==null||!Arrays.equals(bytes,committed.bytes))throw new IOException("上传回读不一致，未建立同步基线，请重新检查");return committed.etag;
        }
    }
}

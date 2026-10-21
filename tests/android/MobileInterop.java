package com.resumestudio;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

/** Line-oriented test adapter for the actual Android data/sync classes, running on the host JVM. */
public final class MobileInterop {
    public static void main(String[] args)throws Exception{
        System.setOut(new PrintStream(System.out,true,StandardCharsets.UTF_8));
        Path root=Paths.get(new String(Base64.getDecoder().decode(args[0]),StandardCharsets.UTF_8));MobileApi api=new MobileApi(root);
        MobileSync sync=new MobileSync(api.store,root,new MobileSync.Dav(args[1],"user","password"),args[1],"android-test");MobileSync.Plan plan=null;
        BufferedReader input=new BufferedReader(new InputStreamReader(System.in,StandardCharsets.UTF_8));String line;
        while((line=input.readLine())!=null){try{var r=Json.object(Json.parse(line));String method=Json.str(r,"method");Object result;
            switch(method){
                case "inspect":plan=sync.inspect();result=plan.display();break;
                case "cancel":plan=null;result=true;break;
                case "size":result=SyncStore.dataSize(Json.object(r.get("snapshot")));break;
                case "inspectAuto":plan=sync.inspectAuto();result=plan.display();break;
                case "executeAuto":result=sync.executeAuto(Objects.requireNonNull(plan));break;
                case "reviewSync":result=sync.review(Objects.requireNonNull(plan));break;
                case "mergeSync":result=sync.merge(Objects.requireNonNull(plan),Json.object(r.get("choices")));break;
                case "execute":result=sync.execute(Objects.requireNonNull(plan),Json.str(r,"direction"));break;
                case "seed":{var doc=Json.object(r.get("doc"));Server.validate(Json.str(r,"kind"),doc);result=api.store.save(Json.str(r,"kind"),doc);break;}
                case "import":result=api.store.syncImport(Json.str(api.store.syncExtendedSnapshot(),"hash"),Json.object(r.get("snapshot")));break;
                case "envelope":result=MobileSync.envelope(Json.str(r,"text").getBytes(StandardCharsets.UTF_8));break;
                case "address":result=MobileSync.address(Json.str(r,"url")).toString();break;
                case "backupCount":{Path p=root.resolve("sync-backups");if(!Files.exists(p))result=0;else try(var paths=Files.list(p)){result=paths.filter(file->file.getFileName().toString().endsWith(".json")).count();}break;}
                default:result=api.call(method,r);
            }
            System.out.println(Json.write(MobileCompat.map("ok",true,"result",result)));
        }catch(Exception ex){System.out.println(Json.write(MobileCompat.map("ok",false,"error",Objects.toString(ex.getMessage(),ex.toString()))));}}
    }
}

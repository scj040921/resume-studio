package com.resumestudio;

import android.app.job.*;
import android.content.*;
import java.io.IOException;
import java.util.*;
import java.util.concurrent.*;

/** Background checks never select a version on the user's behalf. */
public final class MobileSyncJob extends JobService {
    static final int JOB_ID=903;
    private static final long PERIOD=30L*60L*1000L;
    private static final ExecutorService WORKER=Executors.newSingleThreadExecutor(r->{Thread t=new Thread(r,"mobile-sync-job");t.setDaemon(true);return t;});
    private volatile boolean stopped;

    public static void schedule(Context context,boolean enabled){
        JobScheduler scheduler=(JobScheduler)context.getSystemService(JOB_SCHEDULER_SERVICE);
        if(!enabled){scheduler.cancel(JOB_ID);return;}
        JobInfo job=new JobInfo.Builder(JOB_ID,new ComponentName(context,MobileSyncJob.class))
            .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setPersisted(true).setPeriodic(PERIOD,5L*60L*1000L).build();
        scheduler.schedule(job);
    }

    @Override public boolean onStartJob(JobParameters params){
        if(MainActivity.isForeground()){jobFinished(params,false);return false;}
        stopped=false;
        WORKER.execute(()->{
            String status="同步尚未配置";
            try{
                MobileSecrets secrets=new MobileSecrets(this);String stored=secrets.read("dav");
                if(!stored.isEmpty()){
                    Map<String,Object> config=Json.object(Json.parse(stored));
                    String directory=Json.str(config,"url"),user=Json.str(config,"user"),password=Json.str(config,"password");
                    if(!directory.isEmpty()){
                        MobileApi api=new MobileApi(getFilesDir().toPath().resolve("data"));
                        var preferences=getSharedPreferences(MainActivity.class.getSimpleName(),0);
                        String device=preferences.getString("deviceId","");
                        if(device.isEmpty()){device=UUID.randomUUID().toString();preferences.edit().putString("deviceId",device).apply();}
                        MobileSync sync=new MobileSync(api.store,api.root,new MobileSync.Dav(directory,user,password),directory+"|"+user,device);
                        MobileSync.Plan plan=sync.inspect();
                        if(!plan.action.equals("unchanged"))status="两端资料有差异，请打开设置比较并确认保留版本";
                        else {sync.execute(plan,null);status="两端资料一致，无需同步";}
                    }
                }
            }catch(Exception ex){status="自动同步未完成："+Objects.toString(ex.getMessage(),"网络或配置异常");}
            if(!stopped)storeStatus(status);
            jobFinished(params,false);
        });
        return true;
    }
    private void storeStatus(String status){
        getSharedPreferences("resume-studio",0).edit().putString("autoSyncStatus",status).putLong("autoSyncAt",System.currentTimeMillis()).apply();
        if(MainActivity.isForeground())MainActivity.postSyncStatus(this,status);
    }
    @Override public boolean onStopJob(JobParameters params){stopped=true;return true;}
}

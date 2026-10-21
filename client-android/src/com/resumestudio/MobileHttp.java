package com.resumestudio;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.nio.file.*;
import java.security.*;
import java.io.FileOutputStream;

/** WebDAV never redirects. Update GETs follow bounded HTTPS release redirects only. */
public final class MobileHttp {
    public interface Transfer {void progress(long bytes,long total);boolean cancelled();default void connected(HttpURLConnection connection) {}}
    interface UpdateConnectionOpener {HttpURLConnection open(URI uri,long offset)throws IOException;}
    public static final class Result {
        public final int status; public final byte[] bytes; public final String etag,modifiedAt;
        Result(int status,byte[] bytes,String etag,String modifiedAt) {this.status=status;this.bytes=bytes;this.etag=etag;this.modifiedAt=modifiedAt;}
        public String text() {return new String(bytes,StandardCharsets.UTF_8);}
    }
    public static Result request(URI uri,String method,Map<String,String> headers,byte[] body,int limit) throws IOException {
        if(!"https".equals(uri.getScheme())&&!("http".equals(uri.getScheme())&&MobileCompat.set("127.0.0.1","localhost","[::1]").contains(uri.getHost()))) throw new IOException("仅支持 HTTPS 地址");
        HttpURLConnection c=(HttpURLConnection)uri.toURL().openConnection();
        c.setInstanceFollowRedirects(false);c.setConnectTimeout(15000);c.setReadTimeout(90000);c.setUseCaches(false);c.setRequestMethod(method);
        headers.forEach(c::setRequestProperty);
        try {
            if(body!=null) {c.setDoOutput(true);c.setFixedLengthStreamingMode(body.length);try(OutputStream out=c.getOutputStream()){out.write(body);}}
            int status=c.getResponseCode(); if(c.getContentLengthLong()>limit) throw new IOException("响应内容过大");
            InputStream stream=status>=400?c.getErrorStream():c.getInputStream();byte[] bytes;
            if(stream==null) bytes=new byte[0];else try(InputStream in=stream){bytes=MobileCompat.readNBytes(in,limit+1);}
            if(bytes.length>limit)throw new IOException("响应内容过大");
            String modified="";try{String header=c.getHeaderField("Last-Modified");if(header!=null)modified=java.time.ZonedDateTime.parse(header,java.time.format.DateTimeFormatter.RFC_1123_DATE_TIME).toInstant().toString();}catch(java.time.format.DateTimeParseException ignored){}
            return new Result(status,bytes,c.getHeaderField("ETag"),modified);
        } finally {c.disconnect();}
    }
    public static String publicRequest(URI uri,String payload,int limit) throws IOException {
        Map<String,String> headers=new HashMap<>();headers.put("User-Agent","ResumeStudio-Android/0.9.3");headers.put("Accept","application/json,text/html");
        if(payload!=null)headers.put("Content-Type","application/json; charset=utf-8");
        Result r=request(uri,payload==null?"GET":"POST",headers,payload==null?null:payload.getBytes(StandardCharsets.UTF_8),limit);
        if(r.status!=200)throw new IOException("官网请求失败（HTTP "+r.status+"），可打开官网浏览");return r.text();
    }
    private static HttpURLConnection openUpdateConnection(URI uri,long offset)throws IOException {
        for(int redirects=0;redirects<=5;redirects++) {
            String host=uri.getHost()==null?"":uri.getHost().toLowerCase(Locale.ROOT);
            if(!"https".equals(uri.getScheme())||uri.getUserInfo()!=null||(uri.getPort()!=-1&&uri.getPort()!=443)||
                !(host.equals("github.com")||host.equals("gh-proxy.org")||host.endsWith(".githubusercontent.com")))
                throw new IOException("更新跳转地址不受信任");
            HttpURLConnection c=(HttpURLConnection)uri.toURL().openConnection();
            c.setInstanceFollowRedirects(false);c.setConnectTimeout(20000);c.setReadTimeout(60000);c.setUseCaches(false);
            c.setRequestProperty("User-Agent","ResumeStudio-Android");c.setRequestProperty("Cache-Control","no-cache");
            if(offset>0)c.setRequestProperty("Range","bytes="+offset+"-");
            try {
                int status=c.getResponseCode();
                if(status!=301&&status!=302&&status!=303&&status!=307&&status!=308)return c;
                String location=c.getHeaderField("Location");
                if(location==null)throw new IOException("更新跳转缺少目标地址");
                uri=uri.resolve(location);
            } catch(IOException|IllegalArgumentException ex){c.disconnect();throw new IOException("无法读取更新地址",ex);}
            c.disconnect();
        }
        throw new IOException("更新跳转次数过多");
    }
    public static byte[] readUpdate(URI uri,int limit)throws IOException {
        HttpURLConnection c=openUpdateConnection(uri,0);
        try {
            int status=c.getResponseCode();if(status!=200)throw new IOException("GitHub 更新通道返回 HTTP "+status);
            if(c.getContentLengthLong()>limit)throw new IOException("更新索引过大");
            try(InputStream input=c.getInputStream()) {
                byte[] bytes=MobileCompat.readNBytes(input,limit+1);
                if(bytes.length>limit)throw new IOException("更新索引过大");return bytes;
            }
        } finally {c.disconnect();}
    }
    public static String download(URI uri,Path target,long expectedSize,long maximum)throws IOException{
        return download(uri,target,expectedSize,maximum,new Transfer(){public void progress(long bytes,long total){}public boolean cancelled(){return false;}});
    }
    public static String download(URI uri,Path target,long expectedSize,long maximum,Transfer transfer)throws IOException{
        return download(uri,target,expectedSize,maximum,transfer,MobileHttp::openUpdateConnection);
    }
    static String download(URI uri,Path target,long expectedSize,long maximum,Transfer transfer,UpdateConnectionOpener opener)throws IOException{
        if(transfer.cancelled())throw new InterruptedIOException("下载已取消");
        Path temp=target.resolveSibling(target.getFileName()+".tmp");
        IOException failure=null;
        try{
            Files.createDirectories(temp.getParent());
            for(int attempt=0;attempt<3;attempt++){
                if(transfer.cancelled())throw new InterruptedIOException("下载已取消");
                long offset=Files.exists(temp)?Files.size(temp):0;
                if(offset>expectedSize){Files.deleteIfExists(temp);offset=0;}
                if(offset==expectedSize){String hash=hashFile(temp);Files.move(temp,target,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE);return hash;}
                HttpURLConnection c=null;
                try{
                    c=opener.open(uri,offset);transfer.connected(c);
                    int status=c.getResponseCode();
                    if(status==416&&offset>0){Files.deleteIfExists(temp);continue;}
                    if(offset==0&&status!=200)throw new IOException("APK 下载失败（HTTP "+status+")");
                    if(offset>0&&status==200){Files.deleteIfExists(temp);offset=0;}
                    else if(offset>0&&(status!=206||!validContentRange(c.getHeaderField("Content-Range"),offset,expectedSize)))throw new IOException("更新服务器不支持有效的断点续传");
                    long length=c.getContentLengthLong(),remaining=expectedSize-offset;
                    if(length>maximum||length>=0&&length!=remaining)throw new IOException("APK 大小与签名信息不一致");
                    MessageDigest digest=MessageDigest.getInstance("SHA-256");
                    if(offset>0)try(InputStream prefix=Files.newInputStream(temp)){byte[] block=new byte[32768];int read;while((read=prefix.read(block))!=-1)digest.update(block,0,read);}
                    long total=offset;transfer.progress(total,expectedSize);
                    try(InputStream input=c.getInputStream();OutputStream output=new FileOutputStream(temp.toFile(),offset>0)){
                        byte[] buffer=new byte[32768];int count;
                        while((count=input.read(buffer))!=-1){if(transfer.cancelled())throw new InterruptedIOException("下载已取消");total+=count;if(total>maximum||total>expectedSize)throw new IOException("APK 超过签名大小限制");digest.update(buffer,0,count);output.write(buffer,0,count);transfer.progress(total,expectedSize);}
                        output.flush();
                    }
                    if(transfer.cancelled())throw new InterruptedIOException("下载已取消");
                    if(total!=expectedSize)throw new EOFException("APK 下载不完整（已接收 "+total+" / "+expectedSize+" 字节）");
                    return moveVerifiedBytes(digest,temp,target);
                }catch(IOException ex){
                    if(transfer.cancelled()){Files.deleteIfExists(temp);throw new InterruptedIOException("下载已取消");}
                    failure=ex;
                    if(attempt==2)break;
                    try{Thread.sleep(400L*(attempt+1));}catch(InterruptedException interrupted){Thread.currentThread().interrupt();throw new InterruptedIOException("下载已中断");}
                }catch(GeneralSecurityException ex){throw new IOException("无法校验 APK",ex);}
                finally{if(c!=null)c.disconnect();}
            }
            throw failure==null?new IOException("APK 下载未完成"):failure;
        }catch(IOException ex){if(transfer.cancelled())Files.deleteIfExists(temp);throw ex;}
    }
    private static boolean validContentRange(String value,long offset,long expected){return value!=null&&value.matches("(?i)bytes\\s+"+offset+"-\\d+/"+expected);}
    private static String hashFile(Path path)throws IOException{
        try{MessageDigest digest=MessageDigest.getInstance("SHA-256");try(InputStream input=Files.newInputStream(path)){byte[] block=new byte[32768];int read;while((read=input.read(block))!=-1)digest.update(block,0,read);}return hex(digest.digest());}
        catch(GeneralSecurityException ex){throw new IOException("无法校验 APK",ex);}
    }
    private static String moveVerifiedBytes(MessageDigest digest,Path temp,Path target)throws IOException{
        String hash=hex(digest.digest());Files.move(temp,target,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE);return hash;
    }
    private static String hex(byte[] bytes){StringBuilder value=new StringBuilder();for(byte b:bytes)value.append(String.format(Locale.ROOT,"%02x",b&255));return value.toString();}
}

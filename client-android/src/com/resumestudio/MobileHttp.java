package com.resumestudio;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.nio.file.*;
import java.security.MessageDigest;
import java.io.FileOutputStream;

/** No redirects, cookies or permissive TLS. All response bodies are bounded. */
public final class MobileHttp {
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
    public static String download(URI uri,Path target,long expectedSize,long maximum)throws IOException{
        if(!"https".equals(uri.getScheme()))throw new IOException("更新包地址必须使用 HTTPS");
        HttpURLConnection c=(HttpURLConnection)uri.toURL().openConnection();c.setInstanceFollowRedirects(false);c.setConnectTimeout(15000);c.setReadTimeout(90000);c.setUseCaches(false);c.setRequestProperty("User-Agent","ResumeStudio-Android");
        Path temp=target.resolveSibling(target.getFileName()+".tmp");
        try{
            int status=c.getResponseCode();if(status!=200)throw new IOException("APK 下载失败（HTTP "+status+")");long length=c.getContentLengthLong();if(length>maximum||length>=0&&length!=expectedSize)throw new IOException("APK 大小与签名信息不一致");
            MessageDigest digest;try{digest=MessageDigest.getInstance("SHA-256");}catch(Exception ex){throw new IOException("无法校验 APK",ex);}
            long total=0;try(InputStream input=c.getInputStream();OutputStream output=new FileOutputStream(temp.toFile())){byte[] buffer=new byte[32768];int count;while((count=input.read(buffer))!=-1){total+=count;if(total>maximum||total>expectedSize)throw new IOException("APK 超过签名大小限制");digest.update(buffer,0,count);output.write(buffer,0,count);}output.flush();}
            if(total!=expectedSize)throw new IOException("APK 下载不完整");
            StringBuilder actual=new StringBuilder();for(byte b:digest.digest())actual.append(String.format(Locale.ROOT,"%02x",b&255));
            Files.move(temp,target,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE);return actual.toString();
        }catch(IOException ex){Files.deleteIfExists(temp);throw ex;}finally{c.disconnect();}
    }
}

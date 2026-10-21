package com.resumestudio;

import java.nio.file.*;
import java.util.*;

public final class InterviewStudyTest {
    private InterviewStudyTest() {}
    public static void main(String[] args) throws Exception {
        var question=new LinkedHashMap<String,Object>(Map.of("id","q1","title","1. 什么是指针？","answer","保存地址","status","unseen","reviewNote","首轮错误原因"));
        var deck=new LinkedHashMap<String,Object>(Map.of("id","deck1","title","嵌入式题库","studyType","exam","questions",List.of(question)));
        Server.validate("interviews",deck);
        var store=new Store(Files.createTempDirectory("InterviewStudyTest-"));store.save("interviews",deck);
        question.put("status","remembered");store.save("interviews",deck);
        var loaded=store.list("interviews").getFirst();var loadedQuestion=Json.object(Json.array(loaded.get("questions")).getFirst());
        if(!Json.str(loadedQuestion,"status").equals("remembered")||!Json.str(loadedQuestion,"reviewNote").equals("首轮错误原因")||!Json.str(loaded,"studyType").equals("exam"))throw new AssertionError("Exam review data did not persist");
        var snapshot=store.syncSnapshot();store.syncImport(Json.str(snapshot,"hash"),Json.object(snapshot.get("snapshot")));
        if(store.list("interviews").size()!=1)throw new AssertionError("Existing local study data was lost during sync");
        question.put("status","bad");
        boolean rejected=false;try{Server.validate("interviews",deck);}catch(IllegalArgumentException ex){rejected=true;}
        if(!rejected)throw new AssertionError("Invalid progress was accepted");
        question.put("status","learning");deck.put("studyType","unknown");rejected=false;
        try{Server.validate("interviews",deck);}catch(IllegalArgumentException ex){rejected=true;}
        if(!rejected)throw new AssertionError("Invalid study type was accepted");
        deck.put("studyType","exam");question.put("reviewNote","x".repeat(20001));rejected=false;
        try{Server.validate("interviews",deck);}catch(IllegalArgumentException ex){rejected=true;}
        if(!rejected)throw new AssertionError("Oversized exam review note was accepted");
        question.put("reviewNote","首轮错误原因");
        question.put("status","learning");deck.put("questions",List.of(question,question));rejected=false;
        try{Server.validate("interviews",deck);}catch(IllegalArgumentException ex){rejected=true;}
        if(!rejected)throw new AssertionError("Duplicate question IDs accepted");
        System.out.println("PASS InterviewStudy: atomic persistence, state changes, sync preservation and validation");
    }
}

package com.resumestudio;

import java.util.*;

/** Imported text and review state stay local; source documents are never retained. */
final class InterviewStudy {
    private InterviewStudy() {}
    static void validate(Map<String,Object> deck) {
        if(!(deck.get("title") instanceof String title)||title.isBlank()||title.length()>200)throw new IllegalArgumentException("题库名称应为 1–200 个字符");
        if(deck.containsKey("studyType")&&!Set.of("interview","exam").contains(Json.str(deck,"studyType")))throw new IllegalArgumentException("题库类型无效");
        var questions=Json.array(deck.get("questions"));
        if(questions.isEmpty()||questions.size()>2000)throw new IllegalArgumentException("一个题库应包含 1–2000 道题");
        var ids=new HashSet<String>();int length=0;
        for(Object value:questions){
            var q=Json.object(value);String id=Json.str(q,"id");
            if(!id.matches("[a-zA-Z0-9_-]{1,100}")||!ids.add(id))throw new IllegalArgumentException("题目标识重复或无效");
            if(!(q.get("title") instanceof String t)||t.isBlank()||t.length()>300)throw new IllegalArgumentException("题目标题无效");
            if(!(q.get("answer") instanceof String answer)||answer.length()>50000)throw new IllegalArgumentException("单题正文不能超过 50000 个字符");
            if(q.containsKey("reviewNote")&&(!(q.get("reviewNote") instanceof String note)||note.length()>20000))throw new IllegalArgumentException("单题复盘不能超过 20000 个字符");
            if(!Set.of("unseen","remembered","learning").contains(Json.str(q,"status")))throw new IllegalArgumentException("掌握状态无效");
            for(String number:List.of("reviewCount","intervalDays"))if(q.containsKey(number)&&(!(q.get(number) instanceof Number n)||n.doubleValue()!=n.intValue()||n.intValue()<0||n.intValue()>100000))throw new IllegalArgumentException("复习进度数字无效");
            if(q.containsKey("nextReviewAt")&&!Json.str(q,"nextReviewAt").isEmpty())try{java.time.OffsetDateTime.parse(Json.str(q,"nextReviewAt"));}catch(Exception ex){throw new IllegalArgumentException("下次复习时间无效");}
            length+=t.length()+answer.length();
        }
        if(length>500000)throw new IllegalArgumentException("一个题库的文字不能超过 50 万字符，请拆分导入");
    }
}

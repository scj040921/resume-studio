window.acceptanceResult=null;
(async()=>{
 const passed=[];const ok=(v,name)=>{if(!v)throw new Error(name);passed.push(name);};
 try{
  await navigate('home');ok(document.querySelector('#content h1').textContent.includes('你好'),'home loads in production WebView');
  const fontResponse=await fetch('fonts/MiSans-Regular.ttf');const fontBytes=await fontResponse.arrayBuffer();ok(fontBytes.byteLength===8122324,'bundled full MiSans font available');
  await navigate('templates');ok(document.querySelectorAll('.template-art').length===14,'all 14 templates');
  await actions.useTemplate({id:'gh-embedded-billryan'});ok(state.current.id,'create native persisted resume');
  const rid=state.current.id;state.current.title='安卓验收简历';state.current.profile.name='安卓测试';state.current.profile.role='嵌入式软件工程师';state.current.sections[0].content='## 测试大学 | 2023 - 2027\n- C/C++、Linux、LVGL 开发';state.dirty=true;await saveCurrent();
  await api('checkpoint',{id:rid});await actions.moveSection({index:'0',delta:'1'});await flush();
  let stored=await api('get',{kind:'resumes',id:rid});ok(stored.sections[1].title==='教育背景'&&stored.sections[1].content.includes('LVGL'),'touch reorder and save');
  state.current.profile.name='自动保存后的姓名';state.dirty=true;await navigate('resumes');await edit('resumes',rid);ok(state.current.profile.name==='自动保存后的姓名','navigation flushes edits');
  state.current.profile.name='应被撤销的姓名';state.dirty=true;await flush();await actions.undo();ok(state.current.profile.name==='自动保存后的姓名','native resume undo restores saved content');
  const versions=await api('history',{id:rid});ok(versions.length>=1,'native history persisted');
  for(const t of state.templates){const doc={...state.current,templateId:t.id};const html=await resumeHtml(doc,false,true);ok(html.includes('自动保存后的姓名')&&!html.includes('{{'),'template renderer '+t.id);}
  await preview(rid);await new Promise(r=>setTimeout(r,600));ok(document.querySelector('#resume-frame')?.srcdoc.includes('自动保存后的姓名'),'sandbox resume preview renders');
  window.exportFixtureHtml=await resumeHtml(state.previewDoc,false,true);
  await edit('favorites');Object.assign(state.current,{title:'嵌入式开发工程师',company:'测试公司',url:'https://www.nowcoder.com/jobs/detail/android-'+crypto.randomUUID(),description:'负责 Linux 与 C/C++ 嵌入式应用开发，熟悉 LVGL、TCP 协议和 Git。',notes:'用户手写备注'});state.dirty=true;await saveCurrent();const favorite=await api('get',{kind:'favorites',id:state.current.id});
  const duplicate=await api('collect',{doc:favorite});ok(!duplicate.created&&duplicate.record.notes==='用户手写备注','duplicate favorites preserve notes');
  const applied=await api('openApplication',{doc:favorite});await edit('applications',applied.record.id);state.current.status='已投递';state.current._resumeId=rid;state.current.dueAt='2026-10-20T10:00:00+08:00';state.current.eventKind='面试';state.dirty=true;await saveCurrent();
  let application=await api('get',{kind:'applications',id:state.current.id});ok(application.resumeSnapshot.profile.name==='自动保存后的姓名'&&application.timeline.length>=2,'application snapshot and timeline');
  await actions.scheduleApplication();state.dirty=true;await saveCurrent();ok(state.current.applicationId===application.id,'application links schedule');
  await edit('schedule',state.current.id);state.current.completed=true;state.dirty=true;await saveCurrent();ok((await api('get',{kind:'schedule',id:state.current.id})).completed,'schedule completion persists');
  await navigate('interviews');state.imported={sourceName:'验收题库.md',blocks:[{text:'基础题',level:1},{text:'什么是 TCP？',level:2},{text:'TCP 是可靠的字节流协议。',level:0},{text:'什么是互斥锁？',level:2},{text:'用于保护共享资源。',level:0}]};renderImport();state.importLevel=2;renderImportPreview();document.querySelector('#import-title').value='安卓面试验收';await actions.confirmImport();ok(state.current.questions.length===2,'import preview split into questions');
  await actions.answer();ok(document.querySelector('#answer').textContent.includes('TCP'),'read and hide answer');await actions.review({status:'remembered'});ok(state.current.questions[0].status==='remembered','interview progress persisted');await actions.question({delta:'1'});ok(state.deckIndex===1,'interview question navigation');
  await navigate('settings');document.querySelector('#pref-theme').value='dark';document.querySelector('#pref-style').value='classic';await actions.savePrefs();ok(document.body.classList.contains('dark')&&document.body.classList.contains('classic'),'theme and interface settings');
  await api('saveSettings',{doc:{theme:'light',interfaceStyle:'haru'}});await api('saveCredentials',{url:'https://example.org/resume/',user:'local-test-user',password:'secret-for-test'});let boot=await api('boot');ok(boot.davPasswordSaved&&!JSON.stringify(boot).includes('secret-for-test'),'keystore credentials never returned');let snapshot=await api('snapshot');ok(!JSON.stringify(snapshot).includes('secret-for-test'),'secrets excluded from sync snapshot');await api('clearCredentials');
  if(window.testDavPort){
   await api('saveCredentials',{url:`http://127.0.0.1:${window.testDavPort}/dav/`,user:'test',password:'native-secret'});
   const plan=await api('inspectSync');ok(plan.action==='push'&&plan.requiresConfirmation,'production Android DAV requires confirmation for empty server');
   let rejected=false;try{await api('executeSync');}catch(e){rejected=e.message.includes('确认');}ok(rejected,'unconfirmed first upload rejected by native core');
   const waitModal=async()=>{for(let i=0;i<100&&document.querySelector('#overlay').hidden;i++)await new Promise(r=>setTimeout(r,50));ok(!document.querySelector('#overlay').hidden,'actual comparison modal opens');};
   let syncAction=actions.sync();await waitModal();ok(document.querySelector('#modal-body').textContent.includes('无历史基线')&&document.querySelector('#modal-body').textContent.includes('文件修改时间'),'comparison renders first baseline and file time');
   ok(document.querySelectorAll('#modal-buttons button').length===2&&document.activeElement.textContent==='取消','missing cloud has no download and cancel focused');await window.appBack();await syncAction;ok((await api('inspectSync')).remoteExists===false,'Android Back cancels without creating cloud');
   syncAction=actions.sync();await waitModal();document.querySelectorAll('#modal-buttons button')[1].click();await syncAction;ok((await api('inspectSync')).action==='unchanged','production Android explicit upload and GET verification');
   await actions.sync();ok(document.querySelector('#overlay').hidden&&document.querySelector('#sync-result').textContent.includes('无需同步'),'equal data skips comparison modal');
   const uploaded=await api('get',{kind:'resumes',id:rid});await api('save',{kind:'resumes',expectedAt:uploaded.updatedAt,doc:{id:rid,title:'同步覆盖验收'}});
   syncAction=actions.sync();await waitModal();ok(document.querySelectorAll('#modal-buttons button').length===3&&document.querySelector('#modal-body').textContent.includes('相对上次同步'),'local-only change shows both versions and size delta');document.querySelectorAll('#modal-buttons button')[1].click();await syncAction;
   ok((await api('inspectSync')).action==='unchanged','production Android replaces and verifies existing DAV file');await api('cancelSync');
   const pending=await api('get',{kind:'resumes',id:rid});await api('save',{kind:'resumes',expectedAt:pending.updatedAt,doc:{id:rid,title:'后台自动同步验收'}});await api('setAutoSync',{enabled:true});
  }
  await navigate('favorites');document.querySelector('#search').value='不存在的职位';document.querySelector('#search').dispatchEvent(new Event('input',{bubbles:true}));ok(document.querySelector('#list-items').textContent.includes('还没有资料'),'favorites search');
  await navigate('home');ok(document.documentElement.scrollWidth<=innerWidth+2,'phone layout has no horizontal overflow');ok(!document.querySelector('#notice').textContent.includes('页面错误'),'no uncaught UI errors');
  window.acceptanceResult={passed,count:passed.length,autoSyncFixture:!!window.testDavPort};
 }catch(e){window.acceptanceResult={passed,error:e.stack||e.message};}
})();

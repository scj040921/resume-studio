// Exercise real animation frames and competing navigation, not keyframe declarations.
window.verifyPageMotion=async()=>{
 const passed=[],ok=(value,name)=>{if(!value)throw new Error(name);passed.push(name);},wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
 const content=document.querySelector('#content'),savedTransition=document.startViewTransition,savedAnimate=content.animate,savedRender=renderPage,savedApi=api;
 let snapshots=0,animations=0,changes=[],sampling=true,sharedError=null,lastFrame=0,wasMoving=false;const frameGaps=[],viewportFrames=[];window.motionFrameSamples=[];
 const observer=new MutationObserver(()=>changes.push({phase:content.dataset.motionPhase,opacity:Number(getComputedStyle(content).opacity)}));
 const sample=time=>{if(!sampling)return;const moving=!!content.dataset.motionPhase||content.classList.contains('shared-transition');if(moving&&wasMoving&&lastFrame)frameGaps.push(time-lastFrame);lastFrame=time;wasMoving=moving;if(moving)viewportFrames.push({viewport:innerWidth,width:document.documentElement.scrollWidth,nav:navElement.clientWidth});if(content.dataset.motionPhase)window.motionFrameSamples.push({page:state.page,phase:content.dataset.motionPhase,opacity:Number(getComputedStyle(content).opacity)});requestAnimationFrame(sample);};
 try{
  await motionQueue;await wait(300);await motionQueue;
  setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:true});await navigate('home');
  observer.observe(content,{childList:true});requestAnimationFrame(sample);
  document.startViewTransition=callback=>{snapshots++;return savedTransition.call(document,callback);};
  content.animate=function(frames,options){animations++;return savedAnimate.call(this,frames,options);};
  await navigate('jobs');await wait(250);await navigate('more');await wait(250);await navigate('interviews');
  if(snapshots!==0||animations!==6)throw new Error('route animation counts '+JSON.stringify({snapshots,animations,motion:document.body.className}));ok(true,'ordinary routes animate one live content layer without old-page snapshots');
  ok(changes.length>=3&&changes.every(change=>change.phase==='updating'&&change.opacity<.01),'all asynchronous page replacements occur while the live content layer is invisible');
  ok(window.motionFrameSamples.some(frame=>frame.opacity>.01&&frame.opacity<.99),'animation frames traverse real intermediate opacity values');ok(viewportFrames.every(frame=>frame.width<=frame.viewport+1)&&Math.max(...viewportFrames.map(frame=>frame.nav))-Math.min(...viewportFrames.map(frame=>frame.nav))<=1,'route offsets never widen the viewport or resize the fixed navigation');
  observer.disconnect();content.animate=savedAnimate;
  let active=0,maximum=0,rendered=[];
  renderPage=async page=>{active++;maximum=Math.max(maximum,active);try{await wait(90);rendered.push(page);await savedRender(page);}finally{active--;}};
  const slow=navigate('jobs');for(let n=0;n<100&&!active;n++)await wait(20);if(!active)throw new Error('Delayed route never started');
  const burst=['templates','home','more','interviews'].map(page=>navigate(page));await Promise.all([slow,...burst]);
  ok(maximum===1&&state.page==='interviews'&&rendered.join(',')==='jobs,interviews','rapid taps serialize rendering and discard superseded pending pages');
  ok(content.querySelector('h1')?.textContent==='面试题背诵'&&!content.dataset.motionPhase&&content.getAnimations().length===0,'the last selected page owns the content with no leftover page animation');
  renderPage=savedRender;await navigate('home');await wait(250);
  let sharedReady;const sharedStarted=new Promise(resolve=>sharedReady=resolve);
  document.startViewTransition=callback=>{
   snapshots++;const transition=savedTransition.call(document,callback);
   transition.ready.then(async()=>{
    try{ok(getComputedStyle(document.documentElement).viewTransitionName==='none','card expansion does not capture a full-page root snapshot');ok([...content.querySelectorAll('.card,.hero')].every(card=>getComputedStyle(card).animationName==='none'&&getComputedStyle(card).transform==='none'),'card morph never competes with a second card entry transform');await wait(100);}
    catch(error){sharedError=error;}finally{sharedReady();}
   },sharedReady);return transition;
  };
  const source=content.querySelector('.grid .card[data-page="favorites"]');
  const card=sharedMotion(source,()=>renderPage('favorites'),()=>content.querySelector('.card')||content.querySelector('h1'));
  await sharedStarted;const latest=navigate('jobs');await Promise.all([card,latest]);if(sharedError)throw sharedError;
  ok(snapshots===1&&state.page==='jobs'&&!document.querySelector('[style*="view-transition-name"]')&&!content.classList.contains('shared-transition'),'a bottom-tab request during card expansion completes without nested snapshots or stale card names');
  document.startViewTransition=undefined;content.animate=undefined;await navigate('more');
  ok(state.page==='more'&&!content.dataset.motionPhase&&!content.classList.contains('page-leave'),'older WebViews complete the sequential CSS fallback and restore interaction');
  content.animate=savedAnimate;setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:false});await navigate('home');
  ok(state.page==='home'&&content.getAnimations().length===0,'disabled motion switches pages without waiting for an animation');
  setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:true});
  api=async(method,input)=>{if(method==='sources')await wait(180);return savedApi(method,input);};
  const delayed=navigate('jobs');await wait(20);const responded=navElement.querySelector('button.active')?.dataset.page==='jobs';await delayed;api=savedApi;
  ok(responded,'bottom navigation responds to the tap before slow destination data finishes loading');
  let repeatedChanges=0;const repeatedObserver=new MutationObserver(()=>repeatedChanges++);repeatedObserver.observe(content,{childList:true});navElement.querySelector('[data-page="jobs"]').click();await wait(40);await motionQueue;repeatedObserver.disconnect();
  ok(repeatedChanges===0,'tapping the current bottom tab leaves the page and scroll position intact');
  await navigate('home');const first=navigate('jobs');await wait(40);const activeButton=navElement.querySelector('[data-page="jobs"]'),lensBefore=navIndicator.getBoundingClientRect();
  activeButton.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,pointerId:81,pointerType:'touch',clientX:lensBefore.left+lensBefore.width/2,clientY:lensBefore.top+10}));
  document.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,cancelable:true,pointerId:81,pointerType:'touch',clientX:lensBefore.left+lensBefore.width/2+12,clientY:lensBefore.top+10}));
  ok(Math.abs(navIndicator.getBoundingClientRect().left-lensBefore.left-12)<2,'grabbing the moving glass pill continues from its current visual position without jumping');
  document.dispatchEvent(new PointerEvent('pointercancel',{bubbles:true,pointerId:81,pointerType:'touch'}));await first;
  await navigate('home');const old=navigate('jobs');await wait(50);const startX=navIndicator.getBoundingClientRect().left,targetButton=navElement.querySelector('[data-page="templates"]'),targetX=navElement.getBoundingClientRect().left+navElement.clientLeft+targetButton.offsetLeft,sign=Math.sign(targetX-startX),positions=[],geometry=[];let track=true;
  const trackPill=()=>{if(!track)return;positions.push(navIndicator.getBoundingClientRect().left);geometry.push({bar:navElement.getBoundingClientRect().left,w:navElement.clientWidth,active:navElement.querySelector('button.active')?.dataset.page,left:targetButton.offsetLeft,transform:navIndicator.style.transform,phase:content.dataset.motionPhase});requestAnimationFrame(trackPill);};requestAnimationFrame(trackPill);const last=navigate('templates');await Promise.all([old,last]);await wait(360);track=false;
  if(positions.length<3||positions.some((x,index)=>index>1&&((x-positions[index-1])*sign<-.3||(x-targetX)*sign>1))||Math.abs(positions.at(-1)-targetX)>=2||navElement.querySelector('button.active')!==targetButton)throw new Error('Pill retarget path '+JSON.stringify({startX,targetX,positions,geometry}));ok(true,'rapid bottom-tab changes retarget the pill without bouncing back to an older selection');
  const deck=await api('save',{kind:'interviews',expectedAt:'',doc:{title:'动效交互验收',sourceName:'motion-test',questions:[{id:'motion-q',title:'回答展开测试',answer:'答案内容用于检查卡片展开时的平滑高度变化。'.repeat(12),status:'unseen'},{id:'motion-q2',title:'下一题',answer:'第二题答案',status:'unseen'}]}});
  await study(deck.id);const studyHeading=content.querySelector('h1'),studyCard=content.querySelector('.question').closest('.card');await actions.answer();await wait(40);
  ok(studyHeading.isConnected&&studyCard.isConnected&&!!document.querySelector('#answer'),'revealing an answer updates the existing card instead of recreating the page');
  ok(content.getAnimations().length===0,'local card updates never restart an animation on the whole page');
  const slot=document.querySelector('#study-answer-slot'),heightBefore=slot.getBoundingClientRect().height;await actions.answer();
  ok(Math.abs(slot.getBoundingClientRect().height-heightBefore)<2,'reversing an answer expansion starts at its current height without snapping');for(let n=0;n<80&&(slot.style.height||slot.style.overflow);n++)await wait(25);
  ok(!slot.style.height&&!slot.style.overflow,'answer height animation releases its temporary layout constraints');
  await actions.question({delta:'1'});ok(studyHeading.isConnected&&studyCard.isConnected&&studyCard.querySelector('.question').textContent==='下一题','changing questions keeps the existing page and card shell');await wait(300);
  await actions.answer();ok(studyCard.classList.contains('expanded'),'answer expansion changes the stacked-card state');await actions.answer();ok(!studyCard.classList.contains('expanded'),'collapsing the answer resets the stacked-card state');
  await actions.answer();await actions.question({delta:'-1'});ok(!state.answer&&!studyCard.classList.contains('expanded'),'changing questions collapses the stack and hides the previous answer');
  await navigate('home');
  const testButton=document.createElement('button');testButton.dataset.action='savePrefs';testButton.textContent='保存测试';content.append(testButton);const savePrefs=actions.savePrefs;
  try{actions.savePrefs=async()=>{};testButton.click();await wait(60);ok(testButton.classList.contains('completed'),'successful save receives completion feedback');await wait(1450);ok(!testButton.classList.contains('completed'),'completion feedback clears itself');actions.savePrefs=async()=>{throw new Error('fixture failure')};testButton.click();await wait(60);ok(!testButton.classList.contains('completed'),'failed save never receives completion feedback');}finally{actions.savePrefs=savePrefs;testButton.remove();clearTimeout(noticeTimer);document.querySelector('#notice').hidden=true;}
  const label=document.createElement('label');label.className='check';label.innerHTML='<input type="checkbox">选择建议';content.append(label);const checkbox=label.querySelector('input');checkbox.checked=true;ok(label.matches(':has(input:checked)'),'checked selection updates the linked label state');checkbox.checked=false;ok(!label.matches(':has(input:checked)'),'clearing selection removes the linked state');label.remove();
  let revealFrames=null;content.animate=function(frames,options){if(frames.some(frame=>frame.clipPath))revealFrames=frames;return savedAnimate.call(this,frames,options);};const navButton=navElement.querySelector('[data-page="jobs"]'),box=navButton.getBoundingClientRect();navButton.dispatchEvent(new MouseEvent('click',{bubbles:true,detail:1,clientX:box.left+box.width/2,clientY:box.top+box.height/2}));await wait(30);await motionQueue;content.animate=savedAnimate;ok(revealFrames?.[0].clipPath.startsWith('circle(0px')&&revealFrames[1].clipPath.startsWith('circle('),'navigation reveals the destination from the activated button');
  const spacer=document.createElement('div');spacer.style.height='2500px';content.append(spacer);window.scrollTo(0,0);const wheel=new WheelEvent('wheel',{deltaY:120,bubbles:true,cancelable:true});content.dispatchEvent(wheel);await wait(130);ok(wheel.defaultPrevented&&scrollY>0,'coarse mouse wheel advances with bounded inertia');cancelWheelInertia();spacer.remove();window.scrollTo(0,0);
  setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:false});const reduced=new WheelEvent('wheel',{deltaY:120,bubbles:true,cancelable:true});content.dispatchEvent(reduced);ok(!reduced.defaultPrevented,'disabled motion leaves wheel scrolling native');setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:true});
  await navigate('home');
  return passed;
 }finally{sampling=false;const sorted=frameGaps.slice().sort((a,b)=>a-b);window.motionTiming={samples:sorted.length,p50:sorted[Math.floor(sorted.length*.5)]||0,p95:sorted[Math.floor(sorted.length*.95)]||0,over50ms:sorted.filter(value=>value>50).length};observer.disconnect();renderPage=savedRender;api=savedApi;content.animate=savedAnimate;document.startViewTransition=savedTransition;setTheme({theme:'light',interfaceStyle:'haru',motionEnabled:true});}
};

'use strict';
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
// Exercise the actual WebView action and dialog with a minimal DOM, without native/network services.
const nodes=new Map();let activeElement;
function element(){return {hidden:true,children:[],textContent:'',_html:'',set innerHTML(value){this._html=value;this.children=[];},get innerHTML(){return this._html;},append(child){this.children.push(child);},focus(){activeElement=this;},click(){this.onclick?.();}};}
for(const id of ['#overlay','#modal-body','#modal-buttons','#sync-result','#notice'])nodes.set(id,element());
const document={querySelector:s=>s==='#modal-buttons button'?nodes.get('#modal-buttons').children[0]:nodes.get(s),createElement:element,addEventListener(){},get activeElement(){return activeElement;}};
const context=vm.createContext({document,setTimeout,clearTimeout,console,window:{},addEventListener(){}});
const source=fs.readFileSync(path.join(__dirname,'../../client-android/assets/app.js'),'utf8');
const bootstrap=source.lastIndexOf('\n(async()=>{try{state.boot');assert(bootstrap>0);vm.runInContext(source.slice(0,bootstrap),context);
const version={exists:true,dataBytes:1234,deltaBytes:234,modifiedAt:'2026-01-02T03:04:05Z',counts:'1 份简历'};
const plan={action:'push',requiresConfirmation:true,remoteExists:true,summary:'本机变化',baselineText:'上次成功同步',local:version,remote:{...version,deltaBytes:0},note:'',contentCheck:false};
let current=plan,calls=[];
context.api=async(method,input)=>{calls.push({method,input});if(method==='inspectSync')return current;if(method==='executeSync')return input.direction;return true;};
const run=()=>vm.runInContext('actions.sync()',context);
const tick=()=>new Promise(resolve=>setImmediate(resolve));
(async()=>{
 let pending=run();await tick();assert.equal(nodes.get('#overlay').hidden,false);assert.equal(activeElement.textContent,'取消');
 assert.deepEqual(nodes.get('#modal-buttons').children.map(b=>b.textContent),['取消','保留本机并上传','保留云端并下载']);
 const html=nodes.get('#modal-body').innerHTML;for(const label of ['资料大小','相对上次同步','文件修改时间','本机版本','云端版本','+234'])assert(html.includes(label),label);
 await context.window.appBack();await pending;assert.equal(calls.filter(c=>c.method==='executeSync').length,0);assert(calls.some(c=>c.method==='cancelSync'));
 calls=[];pending=run();await tick();nodes.get('#modal-buttons').children[2].click();await pending;assert.equal(calls.find(c=>c.method==='executeSync').input.direction,'pull','local-only plan can keep cloud');
 current={...plan,action:'pull'};calls=[];pending=run();await tick();nodes.get('#modal-buttons').children[1].click();await pending;assert.equal(calls.find(c=>c.method==='executeSync').input.direction,'push','cloud-only plan can keep local');
 current={...plan,remoteExists:false,remote:{exists:false},baselineText:'无历史基线'};calls=[];pending=run();await tick();assert.equal(nodes.get('#modal-buttons').children.length,2);assert(nodes.get('#modal-body').innerHTML.includes('不存在'));nodes.get('#modal-buttons').children[0].click();await pending;assert(!calls.some(c=>c.method==='executeSync'));
 current={...plan,action:'unchanged',requiresConfirmation:false};calls=[];await run();assert.equal(nodes.get('#overlay').hidden,true);assert.equal(calls.find(c=>c.method==='executeSync').input.direction,'unchanged');assert(nodes.get('#sync-result').innerHTML.includes('无需同步'));
 current={...plan,remote:{...version,modifiedAt:'',deltaBytes:null},baselineText:'上次同步未记录大小'};pending=run();await tick();assert(nodes.get('#modal-body').innerHTML.includes('未提供 / 未知'));assert(nodes.get('#modal-body').innerHTML.includes('未记录大小'));nodes.get('#modal-buttons').children[0].click();await pending;
 console.log('PASS Android sync dialog: actual action/modal, comparison metadata, Cancel focus/Back, either choice, missing cloud, equality, unknown metadata');
})().catch(error=>{console.error(error);process.exitCode=1;});

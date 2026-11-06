const fs=require('fs'),vm=require('vm'),crypto=require('crypto');
const base=fs.readFileSync('core.test.js','utf8');
// reuse El / doc construction from smoke.js by evaluating its prelude up to "let fails"
const pre=base.slice(0,base.indexOf('const files={},calls=[];'));
const mk=new Function('require','return (function(){'+pre.replace("const fs=require('fs'),vm=require('vm');","const fs=require('fs'),vm=require('vm');")+';return {El,root,doc,reg,html,code};})()');
const relay={};let mid=0;
function hm(k,l){return crypto.createHmac('sha256',k).update(l).digest();}
function client(name){
  const E=mk(require);const files={};const ctxRef={};
  const keys={};
  const X={Data:n=>files[n]||"",Save:(n,t)=>{files[n]=t;return "ok";},Delete:n=>{delete files[n];return "ok";},Legacy:()=>"",Info:()=>"",Paths:()=>"C:\\dl|C:\\data",Hash:(s,t)=>"H"+s+t,
   FileMeta:()=>"",GameSize:()=>"ok",GameIcon:()=>"",SetTray:()=>"ok",StartupGet:()=>"0",Open:()=>"ok",Http:()=>"ERR:x",ItchKeySource:()=>"none",
   EcNew:()=>{const e=crypto.createECDH('prime256v1');e.generateKeys();const pub=e.getPublicKey();const blob=Buffer.concat([Buffer.from('ECK1'),Buffer.from([32,0,0,0]),pub.slice(1)]);const id=e.getPrivateKey('base64');keys[id]=e;return id+"|"+blob.toString('base64');},
   PubCode:p=>{const b=Buffer.from(p,'base64');if(b.length!==72||b.slice(0,4).toString()!=='ECK1')return "";const h=crypto.createHash('sha256').update(b).digest();return (h.readBigUInt64BE(0)%1000000000n).toString().padStart(9,'0');},
   EcShared:(priv,pub)=>{const e=keys[priv];const b=Buffer.from(pub,'base64');const s=e.computeSecret(Buffer.concat([Buffer.from([4]),b.slice(8)]));return crypto.createHash('sha256').update(s).digest('hex');},
   RandHex:n=>crypto.randomBytes(+n).toString('hex'),
   Topic:(k,l)=>hm(Buffer.from(k,'hex'),'xaly-topic-'+l).toString('hex').slice(0,32),
   Seal:(k,t)=>{k=Buffer.from(k,'hex');const ek=hm(k,'xaly-enc'),mk2=hm(k,'xaly-mac'),iv=crypto.randomBytes(16);const c=crypto.createCipheriv('aes-256-cbc',ek,iv);const body=Buffer.concat([iv,c.update(t,'utf8'),c.final()]);return Buffer.concat([body,hm(mk2,body).slice(0,16)]).toString('base64');},
   Unseal:(k,s)=>{try{k=Buffer.from(k,'hex');const ek=hm(k,'xaly-enc'),mk2=hm(k,'xaly-mac'),a=Buffer.from(s,'base64'),bl=a.length-16;if(!hm(mk2,a.slice(0,bl)).slice(0,16).equals(a.slice(bl)))return "ERR:sig";const d=crypto.createDecipheriv('aes-256-cbc',ek,a.slice(0,16));return Buffer.concat([d.update(a.slice(16,bl)),d.final()]).toString('utf8');}catch(e){return "ERR:"+e.message;}},
   Relay:(id,topics,since,body)=>{setTimeout(()=>{let out="";if(body){(relay[topics]=relay[topics]||[]).push({id:"m"+(++mid),time:Math.floor(Date.now()/1000),event:"message",topic:topics,message:body});}
      else{topics.split(",").forEach(t=>(relay[t]||[]).forEach(m=>out+=JSON.stringify(m)+"\n"));}ctxRef.c.onHttp(id,"1",out,"200");},2);return "ok";}};
  const sb={document:E.doc,external:X,console,setTimeout,clearTimeout,setInterval:()=>0,clearInterval:()=>{},Date,Math,JSON,parseInt,parseFloat,encodeURIComponent,decodeURIComponent,RegExp,Array,Object,String,Number,Boolean,Error,isNaN,navigator:{}};
  sb.window=sb;const c=vm.createContext(sb);ctxRef.c=c;vm.runInContext(E.code,c);
  c.ONLINE_ACCOUNTS=0;c.createAcc();const ins=E.reg.mbox.find(x=>x.tag==="input");ins[0].value=name;ins[1].value=name.toLowerCase()+"@gmail.com";ins[2].value="pw1234";ins[3].value="pw1234";E.reg.mbox.find(x=>x.tag==="button"&&x._text==="Create account")[0].onclick({});
  return {c,E,X};
}
const sleep=ms=>new Promise(r=>setTimeout(r,ms));let fails=0;const ok=(v,m)=>{if(!v){fails++;console.log("FAIL:",m);}else console.log("ok  :",m);};
(async()=>{
  const A=client("Alice"),B=client("Bob");
  ok(/^\d{9}$/.test(A.c.S.user.code)&&A.c.S.fr.pub,"Alice has keys + code "+A.c.S.user.code);
  A.c.addFriend(B.c.S.user.code,"Bobby");await sleep(20);ok(A.c.S.fr.out.length===1,"request pending");
  B.c.frPoll();await sleep(20);ok(B.c.S.fr.inc.length===1&&B.c.S.fr.inc[0].name==="Alice","Bob got request");
  // spoof: forged request claiming Alice's code with another key
  const ev=A.X.EcNew().split("|")[1];relay["xaly-fr-"+B.c.S.user.code].push({id:"spoof",time:1,event:"message",topic:"xaly-fr-"+B.c.S.user.code,message:JSON.stringify({xaly:1,t:"req",code:"123456789",name:"Evil",pub:ev})});
  B.c.frPoll();await sleep(20);ok(B.c.S.fr.inc.length===1,"spoofed request rejected");
  B.c.acceptReq(B.c.S.fr.inc[0]);await sleep(20);ok(B.c.S.friends.length===1,"Bob friends with Alice");
  A.c.frPoll();await sleep(20);ok(A.c.S.friends.length===1&&A.c.S.friends[0].nick==="Bobby"&&!A.c.S.fr.out.length,"Alice got acceptance");
  ok(A.c.S.friends[0].key===B.c.S.friends[0].key,"shared key matches");
  A.c.frPoll();B.c.frPoll();await sleep(20);A.c.frPoll();B.c.frPoll();await sleep(20);
  ok(A.c.S.friends[0].sk&&B.c.S.friends[0].sk,"status keys exchanged");
  ok(A.c.pstate(A.c.S.friends[0])==="online"&&B.c.pstate(B.c.S.friends[0])==="online","both see each other online");
  B.c.S.user.status="away";B.c.syncNow();await sleep(20);A.c.frPoll();await sleep(20);ok(A.c.pstate(A.c.S.friends[0])==="away","status change seen");
  B.c.S.prof={rn:"Bobby",cn:"Canada",bio:"hi i like fnaf"};B.c.S.games.push({id:"g9",name:"FNAF World",seconds:7200,last:Date.now(),plays:2,sess:[[Date.now()-3600000,3600]]});B.c.pubStatus(true);await sleep(20);A.c.frPoll();await sleep(20);
  ok(A.c.S.friends[0].prof&&A.c.S.friends[0].prof.bio==="hi i like fnaf"&&A.c.S.friends[0].prof.rec[0].n==="FNAF World","friend profile shared");
  A.c.openProfile(A.c.S.friends[0].code);ok(A.E.reg.pProf.textContent.indexOf("hi i like fnaf")>-1&&A.E.reg.pProf.textContent.indexOf("Canada")>-1,"friend profile page renders");
  A.c.openProfile("");ok(A.E.reg.pProf.textContent.indexOf("LEVEL")>-1&&A.E.reg.pProf.textContent.indexOf("Badges")>-1,"own profile renders");
  const bf=B.c.S.friends[0];B.c.sendChat(bf,"hey alice");await sleep(20);ok(!bf.msgs[0].pend&&!bf.msgs[0].fail,"sent");
  A.c.frPoll();await sleep(20);const af=A.c.S.friends[0];ok(af.msgs.length===1&&af.msgs[0].x==="hey alice"&&af.unread===1,"Alice received chat, unread=1");
  ok(A.E.reg.badge.className==="al","badge alert");
  await sleep(20);B.c.frPoll();await sleep(20);ok(bf.msgs[0].st==="dlv","sender sees Delivered ("+bf.msgs[0].st+")");
  A.E.reg.fp.className="open";A.c.P.chat=af.code;A.c.sendRc(af);await sleep(20);B.c.frPoll();await sleep(20);ok(bf.msgs[0].st==="read"&&bf.msgs[0].rt,"sender sees Read");
  B.E.reg.fp.className="open";B.c.P.chat=bf.code;B.c.renderFriends(true);ok(B.E.reg.fpb.textContent.indexOf("Read ")>-1,"read receipt shown in chat");A.E.reg.fp.className="";A.c.P.chat=null;
  A.E.reg.fp.className="open";A.c.P.chat=af.code;A.c.renderFriends(true);ok(A.E.reg.fpb.textContent.indexOf("hey alice")>-1,"chat view renders");
  A.c.shareGame(A.c.S.store.items[0]);const sel=A.E.reg.mbox.find(x=>x.tag==="select")[0];sel.value=af.code;A.E.reg.mbox.find(x=>x.tag==="button"&&x._text==="Send")[0].onclick({});await sleep(20);
  B.c.frPoll();await sleep(20);ok(bf.msgs.length===2&&bf.msgs[1].g&&bf.msgs[1].g.sid===A.c.S.store.items[0].id,"game recommendation received");
  // tampered message ignored
  const t=Object.keys(relay).find(k=>k.indexOf("xaly-p-")===0);relay[t].push({id:"bad",time:1,event:"message",topic:t,message:"AAAA"+relay[t][0].message.slice(4)});
  B.c.frPoll();await sleep(20);ok(bf.msgs.length===2,"tampered message dropped");
  // dedupe
  B.c.frPoll();await sleep(20);ok(bf.msgs.length===2,"no duplicates on re-poll");
  // ---- 1.6.3 compare, challenges, alerts
  {const t=Date.now();
  A.c.S.games.push({id:"ga",name:"FNAF World",storeId:"zz1",seconds:600,plays:1,last:t,ach:{first:t}});
  const bg=B.c.S.games.find(g=>g.id==="g9");bg.storeId="zz1";bg.ach={first:t,night:t};
  B.c.sendGs(true);await sleep(1700);A.c.frPoll();await sleep(30);
  ok(af.gs&&af.gs.l.length===1&&af.gs.l[0][2]===7200,"Alice got Bob's game summary");
  const box=A.c.cmpSection(A.c.gameBy("ga")).textContent;
  ok(box.indexOf("Bobby beat Night Shift, you haven’t yet")>-1&&box.indexOf("more than you")>-1,"compare shows who's ahead: "+box.slice(0,160));
  A.c.chalForm(af,A.c.gameBy("ga"),"mara");A.E.reg.mbox.find(x=>x.tag==="button"&&x._text==="Send challenge")[0].onclick({});await sleep(30);
  ok(A.c.S.chal[0].dir==="out"&&A.c.S.chal[0].a==="mara","Alice sent a challenge");
  B.c.frPoll();await sleep(30);const bc=B.c.S.chal&&B.c.S.chal[0];
  ok(bc&&bc.dir==="in"&&bc.a==="mara"&&bc.st==="open"&&B.c.S.notes.some(n=>/challenged you/.test(n.title)),"Bob got it with a notification");
  bg.sess=[[t-4*3600000,3*3600+120]];B.c.checkAch(bg);await sleep(30);
  ok(bc.st==="done"&&B.c.S.notes.some(n=>n.title==="Challenge complete!"),"Bob's challenge completed by itself when Marathon unlocked");
  A.c.frPoll();await sleep(30);ok(A.c.S.chal[0].st==="done"&&A.c.S.notes.some(n=>/completed your challenge/.test(n.title)),"Alice was told");
  A.c.chalForm(af,A.c.gameBy("ga"),"x");const ins=A.E.reg.mbox.find(x=>x.tag==="input");ins[0].value="Beat night 5";A.E.reg.mbox.find(x=>x.tag==="button"&&x._text==="Send challenge")[0].onclick({});await sleep(30);
  B.c.frPoll();await sleep(30);const cx=B.c.S.chal.find(c=>c.x==="Beat night 5");ok(cx&&cx.st==="open","custom challenge received");
  B.c.P.ftab="ch";B.E.reg.fp.className="open";B.c.P.chat=null;B.c.renderFriends();ok(B.E.reg.fpb.textContent.indexOf("I did it")>-1,"challenges tab with I did it");
  B.c.chalDone(cx);A.c.frPoll();await sleep(30);ok(A.c.S.chal.find(c=>c.x==="Beat night 5").st==="done","custom challenge done on both sides");
  // online / playing alerts
  A.c.frReadyT=0;af.seen=Date.now()-A.c.FR_ON-5000;B.c.S.user.status="online";B.c.pubStatus(true);await sleep(30);A.c.frPoll();await sleep(30);
  ok(A.c.S.notes.some(n=>n.title==="Bobby is online"),"online alert");
  B.c.run["g9"]={t:Date.now()};B.c.pubStatus(true);await sleep(30);A.c.frPoll();await sleep(30);
  ok(A.c.S.notes.some(n=>n.title==="Bobby started playing"&&n.msg==="FNAF World"),"started playing alert");
  const nN=A.c.S.notes.length;B.c.pubStatus(true);await sleep(30);A.c.frPoll();await sleep(30);ok(A.c.S.notes.length===nN,"no repeat alerts");
  delete B.c.run["g9"];
  // 1.6.4 message alerts
  const snd=[];A.X.PlaySound=n=>{snd.push(n);return "ok";};A.c.P.chat=null;A.E.reg.fp.className="";
  A.c.msgSndT=0;B.c.sendChat(bf,"you up for fnaf?");await sleep(30);A.c.frPoll();await sleep(30);
  ok(snd.indexOf("message")>=0&&A.E.reg.ncard.textContent.indexOf("you up for fnaf?")>-1&&A.E.reg.ncard.textContent.indexOf("Reply")>-1,"message chime + card with Reply");
  snd.length=0;A.c.msgSndT=0;A.E.reg.fp.className="open";A.c.P.chat=af.code;B.c.sendChat(bf,"hello?");await sleep(30);A.c.frPoll();await sleep(30);
  ok(snd.indexOf("message")<0,"no chime while you're looking at that chat");A.E.reg.fp.className="";A.c.P.chat=null;
  }
  // ---- 1.7.0 delete messages + block
  {A.c.closeForm&&A.c.closeForm();A.c.P.chat=null;A.E.reg.fp.className="";
   B.c.sendChat(bf,"oops wrong chat");await sleep(30);A.c.frPoll();await sleep(30);
   ok(af.msgs.some(m=>m.x==="oops wrong chat"),"message arrived");
   const sent=bf.msgs.find(m=>m.x==="oops wrong chat");B.c.deleteMsg(bf,sent,true);await sleep(30);A.c.frPoll();await sleep(30);
   ok(!bf.msgs.some(m=>m.x==="oops wrong chat")&&!af.msgs.some(m=>m.x==="oops wrong chat"),"deleted for everyone: gone on both sides");
   const keep=af.msgs[0];A.c.deleteMsg(af,keep,false);ok(af.msgs.indexOf(keep)<0&&bf.msgs.some(m=>m.x===keep.x),"deleted for me: only gone on my side");
   A.c.frPoll();await sleep(30);ok(!af.msgs.some(m=>m.x==="oops wrong chat"),"deleted message doesn't come back on the next check");
   // block
   A.c.blockUser(af.code,"Bobby",af);A.E.reg.mbox.find(x=>x.tag==="button"&&x._text==="Block")[0].onclick({});await sleep(30);
   ok(!A.c.S.friends.length&&A.c.isBlocked(af.code),"Alice blocked Bob: he's gone from her friends");
   B.c.frPoll();await sleep(30);ok(!B.c.S.friends.length,"Bob's side drops the friendship too");
   B.c.addFriend(A.c.S.user.code,"");await sleep(30);A.c.frPoll();await sleep(30);
   ok(A.c.S.fr.inc.length===0,"Bob's new friend request is silently ignored");
   ok(A.c.addFriend(B.c.S.user.code,"")===false,"Alice can't add someone she blocked without unblocking");
   A.c.nav("settings","friends");A.c.P.tab="friends";A.c.renderSettings();ok(A.E.reg.sbody.textContent.indexOf("Bobby")>-1&&A.E.reg.sbody.textContent.indexOf("Unblock")>-1,"blocked list in settings");
   A.c.unblockUser(B.c.S.user.code);ok(!A.c.isBlocked(B.c.S.user.code),"unblock");
   B.c.S.fr.out=[];B.c.addFriend(A.c.S.user.code,"");await sleep(30);A.c.frPoll();await sleep(30);ok(A.c.S.fr.inc.length===1,"after unblocking, requests come through again");
   A.c.acceptReq(A.c.S.fr.inc[0]);await sleep(30);B.c.frPoll();await sleep(30);A.c.frPoll();await sleep(30);
  }
  const af2=A.c.S.friends[0],bf2=B.c.S.friends[0];
  // remove
  B.c.S.fr.out=[];A.c.removeFriend(af2||af);A.E.reg.mbox.find(x=>x.tag==="button"&&x._text==="Remove")[0].onclick({});await sleep(20);B.c.frPoll();await sleep(20);
  ok(!A.c.S.friends.length&&!B.c.S.friends.length,"unfriend removes on both sides");
  // settings render
  A.c.nav("settings","friends");A.c.P.tab="friends";A.c.renderSettings();ok(A.E.reg.sbody.textContent.indexOf("Your friend code")>-1,"settings friends tab");
  console.log(fails?"FAILED "+fails:"FRIENDS OK");process.exit(fails?1:0);
})();

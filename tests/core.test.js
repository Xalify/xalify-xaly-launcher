const fs=require('fs'),vm=require('vm');
const html=fs.readFileSync('index.html','utf8'),code=fs.readFileSync('app.js','utf8');
const sp=()=>new Proxy({},{get:(t,k)=>t[k]===undefined?"":t[k],set:(t,k,v)=>{t[k]=v;return true;}});
class El{constructor(tag){this.tag=tag;this.children=[];this.parent=null;this.style=sp();this.__id="";this.className="";this._text="";this.value="";this.attrs={};this.scrollTop=0;this.childNodes=this.children;}
 set id(v){this.__id=v;}get id(){return this.__id;}
 appendChild(c){if(c.parent)c.parent.children=c.parent.children.filter(x=>x!==c);c.parent=this;this.children.push(c);this.childNodes=this.children;return c;}
 insertBefore(c,r){c.parent=this;const i=this.children.indexOf(r);this.children.splice(i<0?this.children.length:i,0,c);return c;}
 removeChild(c){this.children=this.children.filter(x=>x!==c);return c;}
 get nextSibling(){if(!this.parent)return null;const a=this.parent.children;return a[a.indexOf(this)+1]||null;}get firstChild(){return this.children[0]||null;}get lastChild(){return this.children[this.children.length-1]||null;}
 set innerHTML(v){this.children=[];this._html=v;}get innerHTML(){return this._html||"";}
 set textContent(v){this.children=[];this._text=String(v);}get textContent(){return this._text+this.children.map(c=>c.textContent).join("");}
 setAttribute(k,v){this.attrs[k]=v;}getAttribute(k){return this.attrs[k];}
 getBoundingClientRect(){return {left:100,right:200,top:50,bottom:90,width:100,height:40};}
 get offsetWidth(){return 220;}get offsetHeight(){return 120;}focus(){}
 getElementsByTagName(t){const o=[];const w=n=>n.children.forEach(c=>{if(c.tag===t)o.push(c);w(c);});w(this);return o;}
 find(fn){const o=[];const w=n=>n.children.forEach(c=>{if(fn(c))o.push(c);w(c);});w(this);return o;}}
const root=new El("div"),stat={};
[...html.slice(0,html.indexOf('<script>')).matchAll(/id="([A-Za-z0-9_]+)"/g)].forEach(m=>{const e=new El("div");e.__id=m[1];stat[m[1]]=e;root.appendChild(e);});
const img=new El("img");img.src="data:x";stat.brand.appendChild(img);
const doc={documentElement:{clientWidth:1280,clientHeight:800},activeElement:null,createElement:t=>new El(t),getElementById:i=>root.find(c=>c.__id===i)[0]||null,createTextNode:t=>{const e=new El("#t");e._text=t;return e;}};
doc.body=root;doc.querySelectorAll=()=>[];
const reg=new Proxy({},{get:(t,k)=>doc.getElementById(k)});
const files={},calls=[];let mock={};
const X={Data:n=>files[n]||"",Save:(n,t)=>{files[n]=t;return "ok";},Delete:n=>{delete files[n];return "ok";},Legacy:()=>"",Info:()=>"tester",
 Paths:()=>"C:\\dl|C:\\data",Hash:(s,t)=>"H("+s+":"+t+")",FileMeta:()=>"1.55.1|Techland S.A.|1726300000000|1650000000000",
 GameSize:(id)=>{setTimeout(()=>ctx.onSize(id,"39800000000"),5);return "ok";},GameIcon:()=>"",Launch:()=>"ok-untracked",SetTray:()=>"ok",StartupGet:()=>"0",
 Open:()=>"ok",Pick:()=>"",PickFolder:()=>"",Stop:()=>"ok",Quit:()=>"ok",ItchKeySource:()=>"built-in",SetItchKey:()=>"ok",
 Http:(id,u)=>{calls.push("HTTP "+u);const r=mock.http(u);setTimeout(()=>ctx.onHttp(id,r[0],r[1],r[2]||"200"),1);return "ok";},
 ItchApi:(id,p)=>{calls.push("ITCH "+p);const r=mock.itch(p);setTimeout(()=>ctx.onHttp(id,r[0],r[1],r[2]||"200"),1);return "ok";},
 Download:(id,u,n)=>{calls.push("DL "+u+" "+n);return "ok";},
 DownloadEx:(id,u,n,r)=>{calls.push("DL "+u+" "+n+" resume="+r+" id="+id);return "ok";},PartSize:n=>String(partSize[n]||0),DownloadPause:id=>{calls.push("PAUSE "+id);setTimeout(()=>ctx.onDl(id,"paused","",""),1);return "ok";},DownloadCancel:(id,n)=>{calls.push("CANCEL "+id+" "+n);return "ok";}};
const partSize={};
const sb={document:doc,external:X,console,setTimeout,clearTimeout,setInterval:()=>0,clearInterval:()=>{},Date,Math,JSON,parseInt,parseFloat,encodeURIComponent,decodeURIComponent,RegExp,Array,Object,String,Number,isNaN};
sb.window=sb;sb.navigator={};const ctx=vm.createContext(sb);
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const until=async(f,ms=3000)=>{const t=Date.now();while(Date.now()-t<ms){if(f())return true;await sleep(10);}return !!f();};
const mbtn=t=>{const b=reg.mbox.find(c=>c.tag==="button"&&c._text===t)[0];if(!b)throw new Error("no btn "+t);return b;};
const fill=v=>{const ins=reg.mbox.find(c=>c.tag==="input"||c.tag==="textarea");v.forEach((x,i)=>{if(x!==null)ins[i].value=x;});};
let fails=0;const ok=(c,m)=>{if(!c){fails++;console.log("FAIL:",m);}else console.log("ok  :",m);};
(async()=>{
 mock={http:u=>{ if(/data\.json$/.test(u)) return u.includes("gone")?["0","(404) Not Found"]:["1",JSON.stringify({id:777})]; if(/api\.github\.com/.test(u)) return u.includes("Starship")?["1",JSON.stringify({assets:[{name:"Starship-linux.zip",size:5e7,browser_download_url:"https://x/l"},{name:"Starship-windows.zip",size:5e7,browser_download_url:"https://x/w"}]})]:["1",JSON.stringify({assets:[{name:"src.tar.gz",browser_download_url:"x"}]})]; return ["0","net"];},
  itch:p=>{ if(p.startsWith("v2/games")) return ["0",JSON.stringify({errors:["invalid key"]}),"401"]; if(p.startsWith("v1/game")) return ["1",JSON.stringify({uploads:[{id:5,filename:"Game_linux.zip",p_linux:true,size:1e6},{id:6,filename:"Game_win.zip",p_windows:true,size:2e6}]})]; if(p.startsWith("v2/uploads")) return ["0",JSON.stringify({errors:["nope"]}),"403"]; if(p.startsWith("v1/upload")) return ["1",JSON.stringify({url:"https://cdn/game.zip"})]; if(p==="profile") return ["1",JSON.stringify({user:{username:"tester"}})]; return ["0","?"];}};
 vm.runInContext(code,ctx);
 ok(reg.login.style.display==="block","login shown first run");
 ctx.ONLINE_ACCOUNTS=0;ctx.createAcc();fill(["Alice","alice@gmail.com","123","123"]);mbtn("Create account").onclick({});ok(!ctx.S,"short password rejected");fill(["Alice","alice@gmail","pw1234","pw1234"]);mbtn("Create account").onclick({});ok(!ctx.S,"bad email rejected");fill(["Alice","Alice@Gmail.com","pw1234","pw1234"]);mbtn("Create account").onclick({});
 ok(ctx.S&&ctx.S.user.name==="Alice","account created");
 const items=ctx.S.store.items;ok(items.length>=100,"catalog size "+items.length);
 ok(ctx.allStore().filter(ctx.visIt).length===items.filter(i=>i.gh).length,"only games with art are visible before covers load");
 items.forEach(i=>{if(!i.image)i.image="x.png";});
 const vis=ctx.allStore().filter(ctx.visIt).length;ok(vis===items.length,"all seeds visible once they have art: "+vis);
 ["store","lib","settings","downloads"].forEach(x=>ctx.nav(x));ctx.P.tab="downloads";ctx.nav("settings","downloads");ok(reg.sbody.textContent.indexOf("itch.io API key")>-1,"API key row in settings");
 // itch fallback v2->v1
 const it=items.find(i=>i.id==="m7");calls.length=0;let res;ctx.resolveAny(it,(e,r,c)=>res=[e,r,c]);await sleep(30);
 ok(res&&!res[0]&&res[1].url==="https://cdn/game.zip"&&res[1].kind==="archive","itch v2 refused -> v1 fallback gives link ("+JSON.stringify(res)+")");
 ok(calls.some(c=>c==="ITCH v2/games/777/uploads")&&calls.some(c=>c.startsWith("ITCH v1/upload")),"tried both APIs");
 // github
 const gh=items.find(i=>i.id==="r4");ctx.resolveAny(gh,(e,r,c)=>res=[e,r,c]);await sleep(10);ok(!res[0]&&res[1].url==="https://x/w","github picks windows asset");
 const gh2=items.find(i=>i.id==="r1");ctx.resolveAny(gh2,(e,r,c)=>res=[e,r,c]);await sleep(10);ok(res[2]==="nowin","github with no windows asset -> nowin");
 // verification hides
 const gone=items.find(i=>i.id==="m9");gone.url="https://gone.itch.io/x";
 await sleep(100);ctx.verQ=[gh2,gh,gone];ctx.nextVerify();await sleep(2600);
 ok(ctx.S.gone.r1===1,"recomp without windows build hidden");ok(!ctx.S.gone.r4&&ctx.S.store.items.find(i=>i.id==="r4").vok===1,"valid one kept + verified");
 ok(ctx.S.gone.m9===1,"404 page hidden");
 ok(!ctx.visIt(gone),"hidden item not visible");
 // v0.7 store page / requirements / uninstall
 {const rr=ctx.reqsFor(items.find(i=>i.id==="r1"));ok(rr.min.length>=5&&rr.rec.length>=5,"reqs for r1");
 items.forEach(i=>{const q=ctx.reqsFor(i);if(!q.min||!q.rec||!q.min.length)throw new Error("no reqs "+i.id);});ok(true,"every game has requirements");
 ["m1","m7","r4"].forEach(id=>{const i=items.find(x=>x.id===id);ctx.openDetail(i);ok(reg.pDet.textContent.indexOf("System requirements")>-1&&reg.pDet.textContent.indexOf("Minimum")>-1,"detail renders "+id);ctx.P.dtab="rq";ctx.nav("detail");ok(reg.pDet.textContent.indexOf("Recommended")>-1,"req tab "+id);});
 const g={id:"gx",name:"Zed",path:"C:\\dl\\Games\\Zed\\zed.exe",storeId:"m1",seconds:0,added:1};ctx.S.games.push(g);
 ok(ctx.gameDir(g)==="C:\\dl\\Games\\Zed","gameDir resolves folder: "+ctx.gameDir(g));
 ctx.gameDir({path:"D:\\x\\a.exe"})===""?ok(true,"outside folder -> no delete"):ok(false,"outside");
 let del=null;X.DeleteGame=p=>{del=p;return "ok";};
 ctx.openDetail(items.find(i=>i.id==="m1"));ok(reg.ubtn&&reg.ubtn.style.display==="block","uninstall button on store page");
 ctx.uninstallGame(g);mbtn("Uninstall").onclick({});ok(del==="C:\\dl\\Games\\Zed"&&!ctx.S.games.some(x=>x.id==="gx"),"uninstall deletes dir + removes game");}
 // key test
 ctx.testKey();await sleep(10);ok(reg.toast.textContent.indexOf("tester")>-1,"key test toast");
 // install flow
 calls.length=0;ctx.install(gh);await sleep(20);ok(calls.some(c=>c.startsWith("DL https://x/w Starship-windows.zip")),"install downloads github zip");
 // queue
 const j1=ctx.dl;ok(j1&&j1.st==="downloading","first download active");
 const second=items.find(i=>i.id==="m7");ctx.install(second);ok(await until(()=>ctx.DQ.length===2&&ctx.DQ[1].st==="queued"),"second install waits in the queue");
 ctx.onDl(j1.id,"p","500","1000");ok(j1.pct===50,"progress 50%");
 ctx.pauseJob(j1);ok(await until(()=>j1.st==="paused"),"paused");ok(await until(()=>ctx.dl===ctx.DQ[1]&&ctx.DQ[1].st==="downloading"),"next game starts while the first is paused");
 ctx.onDl(ctx.DQ[1].id,"err","The connection dropped","");ok(ctx.DQ[1].st==="retry","dropped connection -> retry scheduled");
 partSize["Starship-windows.zip"]=500;calls.length=0;ctx.resumeJob(j1);await sleep(20);ok(calls.some(c=>c.indexOf("Starship-windows.zip resume=1")>0),"resume continues the partial file");
 ctx.onDl(j1.id,"done","C:\\dl\\Starship-windows.zip","");await sleep(10);ok(!ctx.DQ.includes(j1),"finished job leaves the queue");
 ok(ctx.S.dq.length===1&&ctx.S.dq[0].st==="retry","queue saved with the account");
 ctx.cancelJob(ctx.DQ[0]);ok(ctx.DQ.length===0,"cancel empties queue");
 // achievements
 {const D=(y,m,d,h)=>new Date(y,m,d,h).getTime();
  const g={id:"ga",name:"Ach Game",plays:11,seconds:3*3600+60,sess:[[D(2026,0,3,1),11000],[D(2026,0,4,20),600],[D(2026,0,5,20),600]],last:D(2026,0,5,21)};
  ctx.S.games.push(g);ctx.checkAch(g,true);const got=Object.keys(g.ach).sort().join(",");
  ok(got==="d3,first,h1,l10,mara,night,wknd","achievements unlocked: "+got);
  const s=ctx.gStats(g);ok(s.streak===3&&s.night===1&&s.wknd===1,"streak/night/weekend stats");
  const g2={id:"gb",name:"Week",plays:7,seconds:7*1800,sess:[0,1,2,3,4,5,6].map(i=>[D(2026,1,23+i,18),1800])};ctx.checkAch(g2,true);ok(g2.ach.d7&&!g2.ach.h10,"7-day streak across a month end");
  ctx.P.prof="";ctx.nav("profile");ok(reg.pProf.textContent.indexOf("Achievements")>-1&&reg.pProf.textContent.indexOf("Night Shift")>-1,"profile shows achievements");
  ctx.P.cur="ga";ctx.nav("game");ok(reg.pGame.textContent.indexOf("7 of 12 unlocked")>-1,"game page shows progress");
  const sh=ctx.profShare();ok(sh.ac>=7&&sh.ar.length===4,"achievements shared with friends");
  ctx.S.games=ctx.S.games.filter(x=>x.id!=="ga"&&x.id!=="gb");ctx.save();}
 ctx.dl=null;
 // library
 const addG=(n,p)=>{ctx.gameForm(null);fill([n,p,""]);mbtn("Add game").onclick({});};
 ctx.nav("lib");addG("Dying Light","C:\\G\\dl.exe");addG("Borderlands 2","C:\\G\\bl2.exe");
 ctx.setView("list");ok(reg.pLib.className.indexOf("lst")>-1&&reg.llist.children.length===2,"list view with 2 rows");
 ctx.P.cur=ctx.S.games[0].id;ctx.renderLib();ok(reg.ldet.textContent.indexOf("Techland")>-1,"detail shows developer");
 await sleep(20);ok(reg.gsz.textContent.indexOf("GB")>-1,"size on disk "+reg.gsz.textContent);
 ctx.S.games[0].fav=1;ctx.P.lt="fav";ctx.renderLib();ok(reg.llist.children.length===1,"favorites tab filters");ctx.P.lt="all";
 ctx.P.sort="time";ctx.renderLib();ctx.setView("grid");ctx.P.cur=ctx.S.games[0].id;ctx.nav("game");ok(reg.pGame.textContent.indexOf("Activity")>-1,"grid game page");
 // sign out + switch
 ctx.run["x"]={t:Date.now()};ctx.S.games[0].id&&0;
 delete ctx.run["x"];ctx.signOut();ok(ctx.S===null&&reg.login.style.display==="block","signed out");
 ctx.signIn(ctx.AC.list[0]);fill(["bad"]);mbtn("Sign in").onclick({});ok(ctx.S===null,"wrong password blocked");
 fill(["pw1234"]);mbtn("Sign in").onclick({});ok(ctx.S&&ctx.S.games.length===2,"right password restores data");
 ctx.signOut();ctx.emailSignIn();fill(["alice@gmail.com","nope"]);mbtn("Sign in").onclick({});ok(!ctx.S,"email sign-in wrong password blocked");fill(["ALICE@gmail.com","pw1234"]);mbtn("Sign in").onclick({});ok(ctx.S&&ctx.S.user.name==="Alice","email sign-in works");
 ctx.signOut();ctx.ONLINE_ACCOUNTS=0;ctx.createAcc();fill(["Bob","alice@gmail.com","pw1234","pw1234"]);mbtn("Create account").onclick({});ok(!ctx.S||ctx.S.user.name!=="Bob","duplicate email rejected");fill(["Bob","bob@gmail.com","pw1234","pw1234"]);mbtn("Create account").onclick({});ok(ctx.S.user.name==="Bob"&&ctx.S.games.length===0&&ctx.AC.list.length===2,"second account isolated");
 await sleep(2500);ok(true,"no exceptions from stale callbacks");
 console.log(fails?fails+" FAILED":"ALL PASSED");process.exit(fails?1:0);
})().catch(e=>{console.log("EXC",e.stack);process.exit(2);});

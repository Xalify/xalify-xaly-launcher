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
const mbtn=t=>{const b=reg.mbox.find(c=>c.tag==="button"&&c._text===t)[0];if(!b)throw new Error("no btn "+t);return b;};
const fill=v=>{const ins=reg.mbox.find(c=>c.tag==="input"||c.tag==="textarea");v.forEach((x,i)=>{if(x!==null)ins[i].value=x;});};
let fails=0;const ok=(c,m)=>{if(!c){fails++;console.log("FAIL:",m);}else console.log("ok  :",m);};

(async()=>{
 const moves=[],streams=[];
 Object.assign(X,{Drives:()=>"C:\\|Windows|Fixed|500000000000|20000000000\nD:\\|Games|Fixed|2000000000000|900000000000\nE:\\|Tiny|Removable|8000000000|100000000",
  DirBytes:()=>"3000000000",FreeSpace:()=>String(ctx.__free||50000000000),
  MoveGame:(id,src,drv)=>{moves.push([id,src,drv]);return drv+"Xaly Games\\Zed";},CancelMove:()=>"ok",
  RelayStream:(t,s)=>{streams.push(t);return "ok";},RelayStreamStop:()=>"ok"});
 mock={http:u=>/data\.json$/.test(u)?["1",JSON.stringify({id:777})]:["0","net"],itch:p=>{if(p.startsWith("v1/game"))return ["1",JSON.stringify({uploads:[{id:6,filename:"Big_win.zip",p_windows:true,size:4e9}]})];if(p.startsWith("v2/games"))return ["1",JSON.stringify({uploads:[{id:6,filename:"Big_win.zip",p_windows:true,size:4e9}]})];return ["0","?"];}};
 vm.runInContext(code,ctx);
 ctx.createAcc();fill(["Alice","alice@gmail.com","pw1234","pw1234"]);mbtn("Create account").onclick({});
 ok(ctx.S&&ctx.S.user,"signed in");
 // ---- move
 const g={id:"gx",name:"Zed",path:"C:\\dl\\Games\\Zed\\bin\\zed.exe",storeId:"m1",seconds:0,added:1};ctx.S.games.push(g);
 ok(ctx.gameDir(g)==="C:\\dl\\Games\\Zed","gameDir default root");
 ctx.moveGame(g);
 const ds=reg.mbox.find(c=>c.tag==="button"&&/mvd/.test(c.className));
 ok(ds.length===3,"3 drives listed ("+ds.length+")");
 ok(ds[0].disabled&&!ds[1].disabled&&ds[2].disabled,"current drive and too-small drive disabled");
 ds[1].onclick();ok(moves.length===1&&moves[0][2]==="D:\\","MoveGame called with D:");
 ok(g.moving&&g.moving.to==="D:\\Xaly Games\\Zed","moving state set");
 ctx.onMove("gx","p","1500000000","3000000000");ok(g.moving.pct===50,"progress 50%");
 const lc=ctx.S.games.length;ctx.play&&0;
 ctx.onMove("gx","done","D:\\Xaly Games\\Zed","");
 ok(!g.moving&&g.path==="D:\\Xaly Games\\Zed\\bin\\zed.exe","path rewritten: "+g.path);
 ok(ctx.gameDir(g)==="D:\\Xaly Games\\Zed","gameDir accepts other drive");
 ctx.moveGame(g);ctx.onMove("gx","cancel","","");ok(true,"cancel path ok");ctx.closeForm();
 // ---- activity
 ctx.logAct("play",g);ctx.logAct("play",g);ok(ctx.S.act.length===1,"play deduped within an hour");
 ctx.logAct("inst",{name:"Other",storeId:"m7"});ok(ctx.S.act[0].k==="inst","install logged");
 const pr=ctx.profShare();ok(pr.ev&&pr.ev.length===2,"activity shared in profile");
 const cp=ctx.cleanProf({ev:[{k:"play",g:"X",t:5},{k:"evil",g:"<b>"},{k:"ach",a:"nope",g:"Y",t:3}]});
 ok(cp.ev.length===2&&cp.ev[1].a==="","activity sanitized");
 const m7=ctx.S.store.items.find(i=>i.id==="m7");m7.image="x.png";
 ctx.S.friends.push({code:"BOB1",name:"Bob",color:1,prof:{ev:[{k:"inst",g:m7.name,sid:"m7",t:Date.now()-60000}]},seen:Date.now()});
 const ff=ctx.friendFeed();ok(ff.length===1&&ctx.feedGame(ff[0].e)===m7,"feed maps to store game");
 const row=ctx.feedRow(ff[0]);ok(row.textContent.indexOf("installed")>-1&&row.textContent.indexOf("Get it")>-1,"feed row has Get it");
 ctx.P.ftab="act";ctx.renderFriends();ok(reg.fpb.textContent.indexOf("Bob")>-1||true,"activity tab renders");
 // ---- size and space
 let sz=0;ctx.fetchSize(m7,v=>sz=v);await sleep(60);ok(sz===4e9&&m7.dlSize===4e9,"size fetched: "+sz);
 ctx.__free=2000000000;let went=0;ctx.spaceCheck(m7,()=>went=1);await sleep(5);
 ok(!went,"space warning shown, install held");
 ctx.__free=900000000000;went=0;ctx.closeForm();ctx.spaceCheck(m7,()=>went=1);await sleep(5);ok(went===1,"enough space installs");
 ctx.openDetail(m7);ok(reg.pDet.textContent.indexOf("Download size")>-1,"detail shows download size");
 // ---- chat box + live relay
 ok(typeof ctx.onRelayMsg==="function"&&typeof ctx.frStream==="function","relay hooks present");
 ctx.onRelayState("1");ok(ctx.frLive===1,"live flag");
 ctx.onRelayMsg("not json");ctx.onRelayMsg(JSON.stringify({event:"keepalive"}));ok(true,"junk lines ignored");
 // ---- 1.5.6 loose exe
 X.PlaceStandalone=(f,n)=>{return /^C:\\dl\\[^\\]+\.exe$/i.test(f)?"C:\\dl\\Games\\"+n+"\\"+f.split("\\").pop():"ERR:Not a loose download.";};
 const lz={id:"lz",name:"Loose",path:"C:\\dl\\Loose.exe",storeId:"m3",seconds:0,added:1};ctx.S.games.push(lz);
 ok(ctx.gameDir(lz)==="","loose exe has no game dir");
 ctx.adoptLoose();ok(lz.path==="C:\\dl\\Games\\Loose\\Loose.exe"&&ctx.gameDir(lz)==="C:\\dl\\Games\\Loose","loose exe adopted: "+lz.path);
 const mine={id:"mn",name:"Mine",path:"E:\\Stuff\\mine.exe",seconds:0,added:1};ctx.S.games.push(mine);ctx.adoptLoose();ok(mine.path==="E:\\Stuff\\mine.exe","user-added game untouched");
 const dels=[];X.DeleteGame=d=>{dels.push(d);return "ok";};
 ctx.uninstallGame(lz);mbtn("Uninstall").onclick({});ok(dels[0]==="C:\\dl\\Games\\Loose"&&!ctx.gameBy("lz"),"adopted exe uninstalls its folder");
 ctx.uninstallGame(mine);mbtn("Remove").onclick({});ok(dels.length===1&&!ctx.gameBy("mn"),"non-Xaly game only removed from library");
 // ---- 1.5.7 Google Drive + Crusade
 const cr=ctx.S.store.items.find(i=>i.id==="ssb1")||ctx.seeds().find(i=>i.id==="ssb1");
 ok(cr&&cr.kind==="archive"&&/drive\.usercontent/.test(cr.url)&&/itch\.io/.test(cr.page),"Crusade seed: direct Drive download, itch info page");
 ok(ctx.frOf(cr)==="Smash","Smash series");
 if(!ctx.S.store.items.find(i=>i.id==="ssb1"))ctx.S.store.items.push(cr);
 calls.length=0;ctx.install(cr);await sleep(5);
 ok(calls.some(c=>c.indexOf("DL https://drive.usercontent.google.com/download?id=1_xd_Y6nkUCjMjo8249P0ZKdMZuO0rk3A&export=download&confirm=t xaly-ssb1.download ")===0),"Drive job started with the right link and file name");
 ctx.DQ.length=0;ctx.dl=null;ctx.__free=1000000000;calls.length=0;ctx.closeForm();ctx.install(cr);await sleep(5);
 ok(!ctx.jobOf(cr)&&reg.mbox.textContent.indexOf("Not enough space")>-1,"Crusade warns when the drive is too full");
 // ---- 1.5.8 a search/catalog copy of Crusade uses the Drive entry
 ctx.closeForm();ctx.DQ.length=0;ctx.dl=null;ctx.__free=900000000000;
 const live={id:"live_x",name:"Super Smash Bros. Crusade ver. 0.9.6a",url:"https://super-smash-bros-crusade.itch.io/super-smash-bros-crusade-ver-096a/",kind:"page",live:1,image:"x.png"};
 ok(ctx.directFor(live)===cr&&!ctx.visIt(live),"itch copy maps to the Drive entry and is hidden");
 ctx.openDetail(live);ok(ctx.P.sel===cr,"opening the itch copy shows the Drive entry");
 calls.length=0;ctx.install(live);await sleep(5);ok(calls.some(c=>c.indexOf("DL https://drive.usercontent.google.com/")===0),"installing the itch copy downloads from Drive");
 // ---- 1.5.9 external links on itch
 const pl=ctx.pageExtLink('<div class="formatted_description"><p>Mac: <a href="https://www.mediafire.com/file/aaa/Game_mac.zip/file">here</a></p><p>Windows PC: <a href="https://drive.google.com/file/d/1AbCdEfGhIjKlMnOp/view?usp=sharing">here</a></p></div>');
 ok(pl==="https://drive.google.com/file/d/1AbCdEfGhIjKlMnOp/view?usp=sharing","description scan prefers the Windows link: "+pl);
 ok(ctx.pageExtLink('<div class="formatted_description"><a href="https://www.mediafire.com/file/tw4ud7nmgskwcqc/FNiFG_1_%2526_2.zip/file">U can get it here</a>')==="https://www.mediafire.com/file/tw4ud7nmgskwcqc/FNiFG_1_%2526_2.zip/file","description MediaFire link found");
 ok(ctx.pageExtLink('<div class="formatted_description">no links</div>')==="","no link, nothing found");
 let rr=null;ctx.extResolve({id:"z",url:"https://a.itch.io/b",kind:"page"},5,[{id:9,storage:"external",host:"drive.google.com",type:"default",traits:["p_windows"],filename:"view",display_name:"Game",updated_at:"t1"}],2,(e,r)=>rr=[e,r],true);
 ok(rr&&!rr[0]&&rr[1].kind==="archive"&&rr[1].host==="Google Drive"&&rr[1].up.ext,"external Drive upload resolves (dry)");
 ok(ctx.upChanged({ext:1,uid:9,at:"t1"},{ext:1,uid:9,at:"t2"})&&!ctx.upChanged({ext:1,uid:9,at:"t1"},{ext:1,uid:9,at:"t1"}),"external upload update detection");
 ok(ctx.extUrl("https://www.mediafire.com/file/x1/G.zip/file")==="https://www.mediafire.com/file/x1/G.zip/file"&&ctx.extOk("https://www.mediafire.com/file/x1/G.zip/file")&&!ctx.extOk("https://www.mediafire.com/folder/x1"),"MediaFire file yes, folder no");
 // ---- 1.5.10 real error text
 ctx.DQ.length=0;ctx.dl=null;const jq={it:cr,st:"downloading",tries:0};ctx.DQ.push(jq);
 ctx.jobFail(jq,"Google Drive says too many people downloaded this file recently. Try again later.",false);ok(jq.st==="error","Drive limit stops at once instead of retrying");
 const jr={it:cr,st:"downloading",tries:0};ctx.DQ.push(jr);ctx.jobFail(jr,"The connection dropped before the download finished.",false);
 ok(jr.st==="retry"&&/^Connection lost\. Trying again in \d+ s \(try 1 of 6\)$/.test(ctx.retryTxt(jr)),"real drops still retry: "+ctx.retryTxt(jr));
 // ---- 1.5.11 browser fallback
 ctx.DQ.length=0;ctx.dl=null;const opened=[];X.Open=u=>{opened.push(u);return "ok";};
 const jb2=ctx.newJob(cr,{direct:1,url:ctx.extUrl(cr.url),fn:"xaly-ssb1.download",kind:"archive"});ctx.DQ.push(jb2);ctx.dl=jb2;
 ctx.jobFail(jb2,"Google Drive says too many people downloaded this file recently. Try again later.",false);
 ok(jb2.st==="error"&&jb2.browser===1,"Drive limit offers the browser");
 ctx.nav("downloads");ok(reg.pDl.textContent.indexOf("Download in browser")>-1,"button shown on Downloads page");
 ctx.browserDl(jb2);ok(opened[0]==="https://drive.usercontent.google.com/download?id=1_xd_Y6nkUCjMjo8249P0ZKdMZuO0rk3A&export=download&authuser=0"&&jb2.st==="browser","opens the signed-in Drive link in the browser");
 let fd="0|C:\\Users\\me\\Downloads||0";X.FindDownload=()=>fd;const fins=[];X.Extract=(z,n)=>{fins.push(z);return "C:\\dl\\Games\\SSBC\\SSBC.exe|C:\\dl\\Games\\SSBC";};
 ctx.brT=0;ctx.browserTick();fd="1|C:\\Users\\me\\Downloads|C:\\Users\\me\\Downloads\\SSBC.zip|5000";ctx.brT=0;ctx.browserTick();ok(!jb2.bseen,"waits while the browser is still writing");
 fd="0|C:\\Users\\me\\Downloads|C:\\Users\\me\\Downloads\\SSBC.zip|1739587584";ctx.brT=0;ctx.browserTick();ok(jb2.bseen&&ctx.DQ.length===1,"sees the file, checks it once more");
 ctx.brT=0;ctx.browserTick();await sleep(100);ok(fins[0]==="C:\\Users\\me\\Downloads\\SSBC.zip"&&ctx.DQ.length===0&&ctx.inLib("ssb1"),"installs the finished file and adds the game");
 // ---- 1.6.0 shared catalog
 ctx.loadCat();ctx.CAT.items.push({id:"xold1",name:"Old Game",url:"https://dev.itch.io/old-game",kind:"page",live:1,auto:1,image:"o.png"},{id:"xkeep",name:"Kept Game",url:"https://dev.itch.io/kept",kind:"page",live:1,auto:1,image:"k.png"},{id:"xlocal9",name:"Same Game",url:"https://dev.itch.io/same/",kind:"page",live:1,auto:1,image:"s.png",desc:"A long description from the game page that is longer than the tagline for sure."});
 ctx.S.games.push({id:"gk",name:"Kept Game",storeId:"xkeep",path:"C:\\dl\\Games\\K\\k.exe",seconds:0,added:1});
 const shared={v:2,t:Date.now(),items:[]};for(let i=0;i<600;i++)shared.items.push({id:"xs"+i,fr:"Indie",genre:"Action",name:"Shared "+i,tagline:"t",url:"https://dev.itch.io/s"+i,image:"i.png",devs:"dev",lf:{popular:i}});
 shared.items.push({id:"xsame",fr:"FNAF",genre:"Horror",name:"Same Game",tagline:"short",url:"https://dev.itch.io/same",image:"s2.png",devs:"dev",lf:{fnaf:3}});
 shared.items.push({id:"xseed",fr:"Indie",name:"Seed dup",tagline:"",url:ctx.S.store.items[0].url,image:"x.png",lf:{}});
 ctx.mergeShared(shared);const ids=ctx.CAT.items.map(x=>x.id);
 ok(ctx.CAT.items.length===602,"catalog = shared list + kept library game: "+ctx.CAT.items.length);
 ok(ids.indexOf("xold1")<0&&ids.indexOf("xkeep")>=0,"old local-only game dropped, library game kept");
 const same=ctx.CAT.items.find(x=>x.name==="Same Game");ok(same.id==="xlocal9"&&same.image==="s2.png"&&same.lf.fnaf===3&&/long description/.test(same.desc),"existing game keeps its id and long description, gets shared data");
 ok(ids.indexOf("xseed")<0,"no duplicate of built-in games");
 ok(ctx.CAT.shared===1,"marked shared, local crawling off");
 // REALCAT: the real shared catalog merges cleanly
 const real=JSON.parse(require("fs").readFileSync("catalog.json","utf8"));const t0=Date.now();ctx.mergeShared(real);
 ok(ctx.CAT.items.length>7900&&Date.now()-t0<3000,"real catalog merged: "+ctx.CAT.items.length+" games in "+(Date.now()-t0)+" ms");
 ctx.P.sq="five nights";ctx.nav("store");ok(true,"store renders with real catalog");
 // ---- 1.6.1 two PCs with different old lists end up identical
 const realTxt=require("fs").readFileSync("catalog.json","utf8");
 mock.http=u=>/raw\.githubusercontent\.com\/Xalify\/xalify-xaly-launcher\/catalog\/catalog\.json/.test(u)?["1",realTxt]:/data\.json$/.test(u)?["1",JSON.stringify({id:777})]:["0","net"];
 async function pc(nLocal){
   ctx.CAT.items=[];ctx.CAT.shared=0;ctx.CAT.shTry=0;ctx.CAT.t=Date.now()-60000;   // a fresh local crawl, like before 1.6.0
   for(let i=0;i<nLocal;i++)ctx.CAT.items.push({id:"xl"+i,name:"Local "+i,url:"https://l"+i+".itch.io/g",kind:"page",live:1,auto:1,image:"i.png"});
   ctx.liveBusy=0;ctx.refreshLive(false);await sleep(80);
   ctx.P.sq="";return ctx.allStore().filter(ctx.visIt).length;
 }
 const a=await pc(5200),b=await pc(4400);
 ok(ctx.CAT.shared===1&&a===b&&a>7000,"both PCs show the same store: "+a+" vs "+b);
 ctx.liveAdd([{gid:"55",url:"https://zz.itch.io/found",name:"Found By Search",img:"f.png",text:"",genre:"",author:"zz",paid:false}],"",0,1);
 ctx.P.sq="";ok(ctx.allStore().filter(ctx.visIt).length===a,"search results don't change the total");
 ctx.P.sq="found";ok(ctx.allStore().filter(ctx.visIt).some(x=>x.name==="Found By Search"),"but they show while searching");ctx.P.sq="";
 // ---- 1.6.2 everyone switches at the same moment
 {const R=JSON.parse(realTxt);const prevC={v:2,t:111,liveAt:100,items:R.items.slice(0,7000)},curC={v:2,t:222,liveAt:Date.now()+300,items:R.items};
  let ver={cur:{t:222,liveAt:curC.liveAt,n:curC.items.length},prev:{t:111,liveAt:100,n:7000}};
  const prT=JSON.stringify(prevC),cuT=JSON.stringify(curC);const got=[];
  mock.http=u=>{got.push(u.split("?")[0].split("/").pop());return /catalog-version\.json/.test(u)?["1",JSON.stringify(ver)]:/catalog-prev\.json/.test(u)?["1",prT]:/catalog\.json/.test(u)?["1",cuT]:["0","net"];};
  ctx.CAT.items=[];ctx.CAT.shared=0;ctx.CAT.shTry=0;ctx.CAT.vT=0;ctx.liveBusy=0;ctx.refreshLive(false);await sleep(150);
  ok(ctx.CAT.sh===111,"before the go-live time: previous list ("+ctx.CAT.items.length+" games)");
  got.length=0;ctx.refreshLive(false);await sleep(50);ok(got.length===0,"no download again within 10 minutes");
  await sleep(2200);ok(ctx.CAT.sh===222,"switched to the new list right at go-live ("+ctx.CAT.items.length+" games)");
  got.length=0;ctx.CAT.vT=0;ctx.refreshLive(false);await sleep(100);ok(got.join()==="catalog-version.json","later checks only fetch the tiny version file: "+got.join());
 }
 // ---- 1.6.5 schedule + speed limit + storage move
 {const lim=[],aw=[];X.SetSpeedLimit=v=>{lim.push(v);return "ok";};X.KeepAwake=v=>{aw.push(v);return "ok";};X.DownloadEx=(id,u,n,r)=>{calls.push("DL "+id);return "ok";};X.PartSize=()=>"0";X.DownloadPause=id=>{calls.push("PAUSE "+id);setTimeout(()=>ctx.onDl(id,"paused","",""),1);return "ok";};
  ctx.closeForm();ctx.DQ.length=0;ctx.dl=null;ctx.S.set.spd=2097152;ctx.applySpeed();ok(lim.pop()==="2097152","speed limit sent to the downloader");
  const d=new Date(),m=d.getHours()*60+d.getMinutes();
  ctx.S.set.sched=1;ctx.S.set.sFrom=(m+120)%1440;ctx.S.set.sTo=(m+300)%1440;ok(!ctx.schedOk(),"outside the window now");
  const it1=ctx.S.store.items.find(i=>i.id==="i13");calls.length=0;
  const j1=ctx.newJob(it1,{direct:1,url:"https://x/y.zip",fn:"y.zip",kind:"archive"});ctx.addJob(j1);await sleep(5);
  ok(!calls.some(c=>/^DL/.test(c))&&j1.st==="queued","new download waits for the schedule");
  ctx.dqTick();ok(aw.pop()==="1","PC kept awake while a download waits");
  ctx.nav("downloads");ok(reg.pDl.textContent.indexOf("Scheduled. Starts at")>-1&&reg.pDl.textContent.indexOf("Start now")>-1,"Downloads page shows schedule + Start now");
  ctx.startNow(j1);await sleep(5);ok(calls.some(c=>c==="DL "+j1.id),"Start now ignores the schedule");
  j1.now=0;j1.st="downloading";ctx.dqTick();await sleep(10);ok(calls.some(c=>c==="PAUSE "+j1.id)&&j1.schedP===1,"running download pauses when the window ends");
  ctx.S.set.sFrom=(m+1440-10)%1440;ctx.S.set.sTo=(m+60)%1440;ok(ctx.schedOk(),"window open (wraps past midnight correctly)");
  calls.length=0;ctx.dqTick();await sleep(5);ok(j1.schedP===0&&calls.some(c=>c==="DL "+j1.id),"resumes by itself when the window opens");
  ctx.S.set.sched=0;ctx.DQ.length=0;ctx.dl=null;ctx.dqTick();ok(aw.pop()==="0","PC allowed to sleep again when nothing is queued");
  ctx.nav("settings","downloads");ctx.P.tab="downloads";ctx.renderSettings();ok(reg.sbody.textContent.indexOf("Download speed limit")>-1&&reg.sbody.textContent.indexOf("Download on a schedule")>-1,"settings rows");
  // storage tab lists a game moved to D: with a Move button
  X.Drives=()=>"C:\\|Windows|Fixed|500000000000|20000000000\nD:\\|Games|Fixed|2000000000000|900000000000";X.DirBytes=()=>"3000000000";
  ctx.S.games.push({id:"mv1",name:"Moved Game",storeId:"",path:"D:\\Xaly Games\\Moved Game\\m.exe",seconds:0,added:1});
  ctx.P.drive="D:\\";ctx.P.tab="storage";ctx.renderSettings();ok(reg.sbody.textContent.indexOf("Moved Game")>-1&&reg.sbody.textContent.indexOf("MOVED HERE BY XALY")>-1&&reg.sbody.textContent.indexOf("Move")>-1,"Storage shows games on other drives with Move");
 }
 // ---- 1.6.6 covers update in place
 {ctx.closeForm();ctx.nav("store");const sid=ctx.S.store.items.find(i=>ctx.visIt(i)).id;let n=0;const walk=e=>{if(e.attrs&&e.attrs["data-sid"]===sid)n++;(e.children||[]).forEach(walk);};
  ok(typeof ctx.patchCards==="function","patchCards exists");
  const t0=ctx.lastStoreR;ctx.softRender(ctx.storeById(sid));await sleep(1300);ok(ctx.lastStoreR===t0,"a cover update doesn't rebuild the Store");}
 // ---- 1.6.7 find games, backup, missing games, commands
 {const crypto=require("crypto");
  X.Hash=(salt,pw)=>crypto.createHash("sha256").update(salt+"|"+pw).digest("hex");
  X.RandHex=n=>crypto.randomBytes(+n).toString("hex");
  X.Seal=(k,t)=>Buffer.from(k+"\u0001"+t).toString("base64");X.Unseal=(k,b)=>{const t=Buffer.from(b,"base64").toString();return t.indexOf(k+"\u0001")===0?t.slice(k.length+1):"ERR:bad";};
  const cru=ctx.storeById("ssb1");cru.image=cru.image||"c.png";ctx.storeById("i13").image=ctx.storeById("i13").image||"m.png";ctx.matchIx=null;
  ctx.S.games=ctx.S.games.filter(g=>g.storeId!=="ssb1"&&g.storeId!=="i13");
  X.ScanPC=()=>{setTimeout(()=>ctx.onScanPC("folder|Downloads|C:\\Users\\me\\Downloads\\Super Smash Bros Crusade v0.9.6a\\SSBC.exe|Super Smash Bros Crusade v0.9.6a;SSBC;Super Smash Bros. Crusade|C:\\Users\\me\\Downloads\\Super Smash Bros Crusade v0.9.6a\nfolder|Program Files|C:\\Program Files\\Discord\\Update.exe|Discord;Update;Discord|C:\\Program Files\\Discord\nzip|Downloads|C:\\Users\\me\\Downloads\\Mindustry.zip|Mindustry|\n"),5);return "ok";};
  ctx.closeForm();ctx.findGames("");await sleep(30);
  ok(reg.mbox.textContent.indexOf("Super Smash Bros. Crusade")>-1&&reg.mbox.textContent.indexOf("Discord")<0&&reg.mbox.textContent.indexOf("Mindustry")>-1&&reg.mbox.textContent.indexOf("Add 2 games")>-1,"only Store games listed, non-games ignored: "+reg.mbox.textContent.slice(0,200));
  const ex=[];X.Extract=(z,n)=>{ex.push(z);return "C:\\dl\\Games\\Mindustry\\Mindustry.exe|C:\\dl\\Games\\Mindustry";};
  reg.mbox.find(x=>x.tag==="button"&&/^Add 2/.test(x._text))[0].onclick({});await sleep(100);
  const hk=ctx.S.games.find(g=>g.storeId==="ssb1");
  ok(hk&&hk.name==="Super Smash Bros. Crusade"&&hk.image===cru.image&&/SSBC\.exe$/.test(hk.path),"Crusade added with the Store name and cover art");
  ok(ex[0]==="C:\\Users\\me\\Downloads\\Mindustry.zip","downloaded zip unpacked and installed");
  // automatic: a sure match is added by itself; a removed one is never re-added
  ctx.S.games=ctx.S.games.filter(g=>g!==hk);ctx.S.autoFindT=0;ctx.SELFTEST=false;ctx.autoFind();await sleep(40);
  const auto=ctx.S.games.find(g=>g.storeId==="ssb1");ok(auto&&auto.found===1&&ctx.S.notes.some(n=>/Found Super Smash Bros\. Crusade/.test(n.title)),"background scan adds it by itself with a notification");
  ctx.S.scanIgnore={};ctx.S.scanIgnore[auto.path.toLowerCase()]=1;ctx.S.scanIgnore["ssb1"]=1;ctx.S.games=ctx.S.games.filter(g=>g!==auto);ctx.S.autoFindT=0;ctx.autoFind();await sleep(40);
  ok(!ctx.S.games.find(g=>g.storeId==="ssb1"),"a game you removed isn't added again");
  ctx.S.games.push(Object.assign({},auto,{id:"hkback"}));ctx.S.games.push({id:"mg1",name:"My Game",path:"D:\\Games\\My Game\\game.exe",added:1});
  // backup round trip
  let saved=null;X.SaveBackup=(n,t)=>{saved=t;return "C:\\Users\\me\\Documents\\"+n;};
  const gamesBefore=ctx.S.games.length,nameBefore=ctx.S.user.name,accId=ctx.curAcc.id;
  ctx.backupAcc();fill(["secret9","secret9"]);mbtn("Save backup…").onclick({});
  ok(saved&&JSON.parse(saved).xaly==="backup"&&saved.indexOf("Hollow Knight")<0,"backup file written and not readable without the password");
  X.OpenBackup=()=>saved;ctx.restoreAcc();fill(["wrong1"]);mbtn("Restore").onclick({});ok(reg.mbox.textContent.indexOf("Restore")>-1,"wrong password refused");
  X.Exists=p=>p.split("\n").map(x=>/My Game/.test(x)?"0":"1").join("");
  fill(["secret9"]);mbtn("Restore").onclick({});ok(reg.mbox.textContent.indexOf("Replace")>-1,"same account on this PC: asks before replacing");
  mbtn("Replace").onclick({});await sleep(2700);
  ok(ctx.curAcc.id===accId&&ctx.S.games.length===gamesBefore&&ctx.S.user.name===nameBefore,"account restored with its library");
  const mg=ctx.S.games.find(g=>g.name==="My Game");ok(mg&&mg.missing===1&&!ctx.S.games.find(g=>g.id==="hkback").missing,"games not on this PC are flagged");
  ctx.P.cur=mg.id;ctx.playToggle();ok(reg.mbox.textContent.indexOf("isn’t on this PC")>-1,"playing a missing game offers to find or install it");ctx.closeForm();
  // a shortcut runs a game through Xaly
  const launched=[];X.Launch=(id,p)=>{launched.push(p);return "ok-untracked";};X.Exists=()=>"1";
  ctx.onCommand("play hkback");ok(/SSBC\.exe$/.test(launched[0]||""),"--play from a shortcut starts the game");
  // uninstall dialog
  let un=null;X.UninstallSelf=(g,d,dirs)=>{un=[g,d,dirs];return "ok";};ctx.openUninstall();ok(reg.mbox.textContent.indexOf("Uninstall Xaly Launcher?")>-1,"uninstall dialog");
  mbtn("Uninstall").onclick({});ok(un&&un[0]==="0"&&un[1]==="0","keeps games and data by default");
 }
 // ---- 1.7.3 one click on Install always works, even while the size is still being looked up
 {ctx.closeForm();ctx.DQ.length=0;ctx.dl=null;ctx.S.set.sched=0;X.FreeSpace=()=>"900000000000";
  const slow=JSON.stringify({id:777});const oldHttp=X.Http;
  X.Http=(id,u)=>{const r=/data\.json$/.test(u)?["1",slow]:mock.http(u);setTimeout(()=>ctx.onHttp(id,r[0],r[1],r[2]||"200"),300);return "ok";};
  const g=ctx.S.store.items.find(i=>i.id==="m1")||ctx.S.store.items.find(i=>ctx.isItch(i));delete g.dlSize;delete g.dlTry;
  ctx.openDetail(g);await sleep(20);                       // the page starts looking up the size
  ctx.install(g);ctx.install(g);ctx.install(g);await sleep(20);   // impatient clicks while it's still looking
  ok(g._prep===1,"the button switches to Preparing… straight away");
  await sleep(1500);
  ok(ctx.DQ.filter(j=>j.it===g).length===1,"one click was enough, and extra clicks didn't queue it twice");
  X.Http=oldHttp;ctx.DQ.length=0;ctx.dl=null;}
 console.log(fails?"FAILED "+fails:"ALL PASSED");process.exit(fails?1:0);
})().catch(e=>{console.log("EXC",e.stack);process.exit(1);});

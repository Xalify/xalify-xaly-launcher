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
 Download:(id,u,n)=>{calls.push("DL "+u+" "+n);return "ok";}};
const sb={document:doc,external:X,console,setTimeout,clearTimeout,setInterval:()=>0,clearInterval:()=>{},Date,Math,JSON,parseInt,parseFloat,encodeURIComponent,decodeURIComponent,RegExp,Array,Object,String,Number,isNaN};
sb.window=sb;sb.navigator={};const ctx=vm.createContext(sb);
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const mbtn=t=>{const b=reg.mbox.find(c=>c.tag==="button"&&c._text===t)[0];if(!b)throw new Error("no btn "+t);return b;};
const fill=v=>{const ins=reg.mbox.find(c=>c.tag==="input"||c.tag==="textarea");v.forEach((x,i)=>{if(x!==null)ins[i].value=x;});};
let fails=0;const ok=(c,m)=>{if(!c){fails++;console.log("FAIL:",m);}else console.log("ok  :",m);};
(async()=>{
 mock={http:()=>["0","net"],itch:()=>["0","?"]};
 files["data.json"]=JSON.stringify({v:1,user:{name:"Old",code:"123456789",color:2,status:"online",joined:1600000000000},set:{tray:1,startup:0,startMin:1,accent:1,intro:0,sync:"",showPlaying:1,feed:""},games:[{id:"g1",name:"Old Game",path:"C:\\x\\a.exe",args:"",fav:1,seconds:3600,plays:3,last:1}],friends:[{code:"987654321",nick:"Pal",name:"Pal",color:1,status:"online",playing:"",seen:0}],seedV:3,wish:{m1:1},store:{items:[{id:"m1",name:"Super Mario Escapade",price:"Free",kind:"page",url:"https://expert-eft.itch.io/ilikeyourcutg",fr:"Mario"}]}});
 vm.runInContext(code,ctx);
 ok(ctx.S&&ctx.S.user.name==="Old","auto signed in");
 ok(ctx.S.games[0].seconds===3600&&ctx.S.friends.length===1&&ctx.S.wish.m1===1&&ctx.S.set.accent===1,"data preserved");
 ok(ctx.S.store.items.length>=55&&ctx.S.seedV===6,"catalog upgraded to v6: "+ctx.S.store.items.length);
 ok(ctx.AC.list[0].file==="data.json","uses existing data.json");
 console.log(fails?"FAILED":"MIGRATION OK");process.exit(fails?1:0);})();
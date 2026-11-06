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
 vm.runInContext(code,ctx);
 // itch.io game page: cover, screenshots, rating, comments, price
 const page=`<meta property="og:image" content="https://img.itch.zone/abc/315x250%23c/cover.png"/>
<script type="application/ld+json">{"@context":"http://schema.org/","@type":"Product","aggregateRating":{"@type":"AggregateRating","ratingValue":"4.75","ratingCount":312},"name":"X"}</script>
<a data-image_lightbox="true" href="https://img.itch.zone/s1.png" target="_blank"><img/></a>
<div class="buy_row"><a class="button buy_btn">Download Now</a> <span class="buy_message"><span class="sub">Name your own price</span></span></div>
<div class="community_post has_vote_column" id="post-1"><div class="post_grid"><div class="post_header"><span class="post_author"><a href="https://itch.io/profile/bob">Bob &amp; Co</a></span><span class="post_date" title="2024-03-02 10:11:12"><a href="#">1 year ago</a></span></div><div class="post_content"><div class="post_body user_formatted"><p>Great game!<br>Scared me &quot;a lot&quot;.</p><p>10/10</p></div></div></div></div>`;
 const it={};ctx.itchExtras(it,page);
 ok(/cover\.png$/.test(it.image),"page cover image");
 ok(it.shots&&it.shots.length===1,"page screenshots");
 ok(it.rt&&it.rt.v===4.75&&it.rt.n===312,"page rating");
 ok(it.cm&&it.cm[0].a==="Bob & Co"&&/Scared me "a lot"/.test(it.cm[0].t),"page comments as reviews");
 ok(!it.paid,"free game not marked paid");
 const it2={};ctx.itchExtras(it2,'<div class="buy_row"><span class="dollars original_price">$4.99</span></div>');ok(!!it2.paid,"paid game detected");
 // itch.io browse list
 const cell=(id,u,t,img,txt,g,price)=>`<div data-game_id="${id}" class="game_cell has_cover lazy_images"><a href="${u}" class="thumb_link game_link"><div class="game_thumb"><img data-lazy_src="${img}" width="315" height="250"/></div></a><div class="game_cell_data"><div class="game_title"><a class="title game_link" href="${u}">${t}</a>${price?'<a class="price_tag meta_tag" href="#"><div class="price_value">'+price+'</div></a>':''}</div><div class="game_text" title="${txt}">${txt}</div><div class="game_author"><a href="https://dev.itch.io">Some Dev</a></div><div class="game_genre">${g}</div></div></div>`;
 const cells=ctx.parseCells(cell(11,"https://a.itch.io/fnaf-thing","FNAF &amp; Friends","https://img.itch.zone/a.png","Survive &quot;five&quot; nights","Survival")+cell(13,"https://c.itch.io/paid","Paid Game","https://img.itch.zone/c.png","costs","Action","$4.99"));
 ok(cells.length===2&&cells[0].name==="FNAF & Friends"&&cells[0].text==='Survive "five" nights'&&!cells[0].paid&&cells[1].paid,"browse list parsed");
 ok(ctx.frGuess("FNAF & Friends")==="FNAF"&&ctx.urlKey("https://A.itch.io/x/?a=1")==="a.itch.io/x","series guess and url keys");
 // RSS feed
 const rss=`<?xml version="1.0"?><rss><channel><item><plainTitle>Five Nights &amp; Stuff</plainTitle><imageurl>https://img.itch.zone/a/b.png</imageurl><price>$0.00</price><platforms><windows>yes</windows></platforms><link>https://dev.itch.io/fns</link><description><![CDATA[<p>A scary game</p>]]></description></item>
<item><plainTitle>MacOnly</plainTitle><imageurl>https://i/m.png</imageurl><platforms><osx>yes</osx></platforms><link>https://m.itch.io/mac</link></item></channel></rss>`;
 const r=ctx.parseRss(rss);ok(r.length===1&&r[0].name==="Five Nights & Stuff"&&r[0].text==="A scary game","RSS parsed, Mac-only game skipped");
 // game update detection
 const o={src:"itch",uid:5,fn:"a.zip",sz:100,bid:"",at:"x"};
 ok(ctx.upChanged(o,{src:"itch",uid:5,fn:"a.zip",sz:100,at:"y"})===false,"same file: no update");
 ok(ctx.upChanged(o,{src:"itch",uid:5,fn:"a.zip",sz:120})===true,"size changed: update");
 ok(ctx.upChanged({src:"itch",uid:5,bid:11,sz:1},{src:"itch",uid:5,bid:12,sz:1})===true,"new butler build: update");
 ok(ctx.upChanged({src:"gh",tag:"v1"},{src:"gh",tag:"v2"})===true&&ctx.upChanged({src:"gh",tag:"v1"},{src:"gh",tag:"v1"})===false,"GitHub tags");
 ok(ctx.upChanged({ext:1,uid:9,at:"t1"},{ext:1,uid:9,at:"t2"})===true,"external link games: new upload date");
 // Google Drive / MediaFire links
 ok(ctx.driveId("https://drive.google.com/file/d/1_xd_Y6nkUCjMjo8249P0ZKdMZuO0rk3A/view?usp=sharing")==="1_xd_Y6nkUCjMjo8249P0ZKdMZuO0rk3A","Drive share link");
 ok(/confirm=t$/.test(ctx.driveUrl("https://drive.usercontent.google.com/download?id=1_xd_Y6nkUCjMjo8249P0ZKdMZuO0rk3A&export=download&authuser=0")),"Drive download link");
 ok(ctx.extOk("https://www.mediafire.com/file/x1/G.zip/file")&&!ctx.extOk("https://www.mediafire.com/folder/x1"),"MediaFire file yes, folder no");
 // names of games found on the PC
 ok(ctx.normName("Super Smash Bros Crusade v0.9.6a")===ctx.normName("Super Smash Bros. Crusade"),"version numbers ignored when matching names");
 ok(ctx.normName("FNAF 2")!==ctx.normName("FNAF"),"sequel numbers kept");
 ok(ctx.verNum("1.6.10")>ctx.verNum("1.6.9")&&ctx.verNum("1.7.0")>ctx.verNum("1.6.10"),"version numbers compare as numbers");
 console.log(fails?"FAILED "+fails:"ALL PASSED");process.exit(fails?1:0);
})().catch(e=>{console.log("EXC",e.stack);process.exit(1);});

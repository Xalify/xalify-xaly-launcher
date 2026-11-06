// Online accounts (1.8.0) end to end in Chromium, against a fake Supabase: sign up with an email code,
// taken usernames, add friends by username, sign in on a new PC, offline sign-in, password reset,
// putting an old PC-only account online, change username/password, delete, and the off switch.
const { chromium } = require("playwright"), fs = require("fs");
let fails = 0; const ok = (c, m) => { if (!c) { fails++; console.log("FAIL:", m); } else console.log("ok  :", m); };
(async () => {
  const html = fs.readFileSync("index.html", "utf8");
  const extra = ';var E=window.external;["OverlayHotkey","SetSpeedLimit","KeepAwake","RegisterApp","MakeShortcut","RemoveShortcut","RelayStream","RelayStreamStop","PlaySound","Popup","Notify"].forEach(function(n){E[n]=function(){return "ok";};});' +
    'E.HasShortcut=function(){return "0";};E.StartCommand=function(){return "";};E.Exists=function(){return "1";};E.Scan=function(){return "ok";};E.FindLeftovers=function(){return "ok";};E.SelfTest=function(){return "0";};E.Engine=function(){return "webview2";};E.Info=function(){return "Bredy";};' +
    'E.EcNew=function(){return "priv|pub"+Math.random();};E.PubCode=function(){return String(100000000+Math.floor(Math.random()*899999999));};E.RandHex=function(){return "ab";};E.Topic=function(){return "t";};E.Relay=function(id){setTimeout(function(){onHttp(id,"1","{}","200");},10);return "ok";};';
  fs.writeFileSync("ui-acc.html", html.replace("<script>", "<script>" + fs.readFileSync("ui-stub.js", "utf8") + extra + fs.readFileSync("ui-fakesb.js", "utf8") + "</script><script>"));
  const b = await chromium.launch(), p = await b.newPage({ viewport: { width: 1400, height: 860 } });
  const errs = []; p.on("pageerror", e => errs.push(e.message));
  await p.goto("file://" + require("path").resolve("ui-acc.html")); await p.waitForTimeout(2000);
  const w=ms=>p.waitForTimeout(ms);
  const fill=async(vals)=>p.evaluate(v=>{var ins=[].slice.call(document.querySelectorAll('#mbox input,#mbox select'));v.forEach(function(x,i){if(x!==null)ins[i].value=x;});},vals);
  const click=async(t)=>p.evaluate(t=>{var bt=[].slice.call(document.querySelectorAll('#mbox button')).filter(x=>x.textContent===t)[0];if(!bt)throw new Error("no button "+t);bt.click();},t);
  const st=async()=>p.evaluate(()=>{var s=document.getElementById('sbst');return (s?s.textContent:'')+' | '+document.getElementById('toast').textContent;});
  // the new sign-in screen
  const af=async(m)=>p.evaluate(m=>{for(var k in m){var i=document.getElementById("au_"+k);if(!i)throw new Error("no field "+k+" on "+AUTH.v);i.value=m[k];if(i.oninput)i.oninput();}},m);
  const go=async()=>p.evaluate(()=>{document.getElementById("au_go").click();});
  const view=async()=>p.evaluate(()=>document.getElementById("login").style.display==="block"?AUTH.v:"(signed in)");
  const hint=async(k)=>p.evaluate(k=>{var h=document.getElementById("au_"+k+"_h");return h?h.textContent:"";},k);
  const banner=async()=>p.evaluate(()=>{var e=document.getElementById("au_err");return e?e.textContent:"";});
  const typeCode=async(c)=>{await p.evaluate(()=>document.getElementById("au_c0").focus());await p.keyboard.type(c);};
  // 1. first start: create an online account and confirm it with the code
  ok(await view()==="register","a fresh install opens on Create account");
  ok(await p.evaluate(()=>document.getElementById("au_name").value==="Bredy"&&document.getElementById("au_user").value==="Bredy"),"the name and a username are filled in from Windows");
  await af({user:"Bredy_G",pw:"abc"});await w(700);
  ok(/available/.test(await hint("user")),"the username is checked while typing: "+await hint("user"));
  await go();await w(100);
  ok(/valid email/.test(await hint("email"))&&/6 characters/.test(await hint("pw"))&&/match/.test(await hint("pw2")),"mistakes show under the fields they belong to");
  await af({email:"bredy@gmail.com",pw:"secret1",pw2:"secret1"});await go();await w(700);
  ok(await view()==="code","after creating, the code screen opens");
  ok(/\(\d+s\)/.test(await p.evaluate(()=>document.getElementById("au_resend").textContent)),"sending a new code waits 30 seconds");
  await typeCode("111111");await w(400);
  ok(/wrong or has expired/.test(await banner()),"a wrong code is explained: "+await banner());
  await p.evaluate(()=>{FSB.rejectSignupType=1;});
  await p.evaluate(()=>{var b=document.getElementById("au_c0");var ev;try{ev=new ClipboardEvent("paste",{clipboardData:new DataTransfer()});ev.clipboardData.setData("text","123 456");}catch(e){ev=null;}if(ev)b.dispatchEvent(ev);else{b.value="123456";b.oninput();}});await w(500);
  ok(await p.evaluate(()=>!!S&&curAcc.sb&&curAcc.sb.uid&&!curAcc.sb.pending&&curAcc.sb.at),"pasting the code confirms it and signs in");
  await w(3400);
  ok(await p.evaluate(()=>FSB.profiles[curAcc.sb.uid].friend_code===S.user.code),"the friend code is published to the profile");
  // 2. second account: a taken username is caught, then fixed
  await p.evaluate(()=>{signOut();});await w(200);
  ok(await view()==="pick","with accounts on this PC, sign-in opens on Who's playing");
  await p.evaluate(()=>authGo("register"));await w(100);
  await af({name:"Other",user:"bredy_g",email:"o@gmail.com",pw:"secret2",pw2:"secret2"});await w(700);
  ok(/taken/.test(await hint("user")),"a taken username shows before you even submit");
  await go();await w(400);ok(await view()==="register"&&/taken/.test(await hint("user")),"and blocks creating the account");
  await af({user:"Other_1"});await go();await w(700);await typeCode("123456");await w(600);
  ok(await view()==="(signed in)","the second account is created and confirmed");
  // 3. add a friend by username
  await p.evaluate(()=>{frOn=function(){return true};nav("settings","account");curAcc.sb.at="";curAcc.sb.exp=0;$("fadd").onclick();});await w(150);
  await fill(['Bredy_G','']);await click('Send request');await w(500);
  ok(await p.evaluate(()=>S.fr.out.length===1&&S.fr.out[0].code===FSB.profiles["u1-0000"].friend_code),"adding by username finds their friend code and sends the request");
  await p.evaluate(()=>$("fadd").onclick());await w(150);await fill(['nobody_here','']);await click('Send request');await w(400);
  ok(/Nobody is using/.test(await st()),"an unknown username is explained");await p.evaluate(()=>closeForm());
  // 4. a new PC: sign in with email adds the account here
  await p.evaluate(()=>{signOut();AC.list=AC.list.filter(function(a){return a.email!=="bredy@gmail.com";});saveAcc();authGo("signin");});
  await af({email:"bredy@gmail.com",pw:"wrongpw"});await go();await w(400);
  ok(/isn.t right/.test(await banner()),"a wrong password is refused");
  await af({pw:"secret1"});await p.focus("#au_pw");await p.keyboard.press("Enter");await w(600);
  ok(await p.evaluate(()=>S&&curAcc.email==="bredy@gmail.com"&&curAcc.sb.uid==="u1-0000"&&curAcc.name==="Bredy_G"),"Enter signs in, and a new PC gets the account named after the username");
  // 5. picking an account asks for its password; offline it still works
  await p.evaluate(()=>{signOut();FSB.online=false;});await w(100);
  await p.evaluate(()=>{var r=[].slice.call(document.querySelectorAll(".arow")).filter(function(x){return /Bredy_G/.test(x.textContent);})[0];r.click();});await w(100);
  ok(await view()==="pw","picking an account asks for its password");
  await af({pw:"secret1"});await go();await w(500);
  ok(await p.evaluate(()=>!!S&&curAcc.email==="bredy@gmail.com"),"offline, the password this PC knows still signs in");
  await p.evaluate(()=>{FSB.online=true;});
  // 6. forgot password: code + new password in one screen
  await p.evaluate(()=>{signOut();authGo("forgot",{email:"bredy@gmail.com"});});await go();await w(400);
  ok(await view()==="code"&&await p.evaluate(()=>AUTH.purpose==="recovery"&&!!document.getElementById("au_pw")),"forgot password sends a code and asks for a new password");
  await af({pw:"newpass1"});await typeCode("654321");await w(700);
  ok(await p.evaluate(()=>FSB.users["u1-0000"].pw==="newpass1"&&!!S&&hashPw(curAcc,"newpass1")===curAcc.pw),"the reset changes the password online and on this PC, and signs in");
  // 7. an old PC-only account goes online from Settings, then confirms
  await p.evaluate(()=>{signOut();var a={id:"aold",name:"Old Timer",email:"",color:1,pw:"",file:"data_aold.json",created:1};AC.list.push(a);saveAcc();startSession(a);nav("settings","account");});await w(300);
  ok(await p.evaluate(()=>/Not online/.test($("pSet").innerText)),"an account without email or password shows the opt-in");
  await p.evaluate(()=>sbGoOnline());await w(150);
  await fill(['old@gmail.com','Old_Timer','oldpass1']);await click('Continue');await w(500);
  ok(await p.evaluate(()=>curAcc.sb&&curAcc.sb.pending===1&&curAcc.email==="old@gmail.com"&&curAcc.pw===hashPw(curAcc,"oldpass1")),"opt-in signs it up and keeps everything on this PC");
  await fill(['123456']);await click('Confirm');await w(400);
  ok(await p.evaluate(()=>curAcc.sb.uid&&!curAcc.sb.pending),"confirming finishes it");
  // 8. username, password, delete
  await p.evaluate(()=>sbChangeUser());await w(100);await fill(['Bredy_G']);await click('Save');await w(300);ok(/taken/.test(await st()),"changing to a taken username is refused");
  await fill(['Oldie']);await click('Save');await w(300);ok(await p.evaluate(()=>curAcc.sb.user==="Oldie"),"the username changes");
  await p.evaluate(()=>sbChangePw());await w(100);await fill(['oldpass1','brandnew1','brandnew1']);await click('Save');await w(500);
  ok(await p.evaluate(()=>FSB.users[curAcc.sb.uid].pw==="brandnew1"&&curAcc.pw===hashPw(curAcc,"brandnew1")),"the password changes online and on this PC");
  var uidOld=await p.evaluate(()=>curAcc.sb.uid);
  await p.evaluate(()=>sbDelete());await w(100);await fill(['brandnew1']);await click('Delete online account');await w(500);
  ok(await p.evaluate(u=>!FSB.users[u]&&!curAcc.sb&&!!S,uidOld),"deleting the online account keeps the account on this PC");
  // 9. email setup not finished yet: the account is made on this PC
  await p.evaluate(()=>{signOut();var o=window.external.Supa;window.__realSupa=o;window.external.Supa=function(id,m,path,b,t){if(path==="/auth/v1/signup"){setTimeout(function(){onHttp(id,"0",JSON.stringify({error_code:"email_address_not_authorized",msg:"Email address not authorized"}),"400");},20);return "ok";}return o(id,m,path,b,t);};authGo("register");});await w(100);
  await af({name:"Friend",user:"Friend_9",email:"friend@gmail.com",pw:"secret9",pw2:"secret9"});await go();await w(1300);
  ok(await p.evaluate(()=>!!S&&curAcc.email==="friend@gmail.com"&&!curAcc.sb),"if email isn't set up, the account is made on this PC: "+await p.evaluate(()=>$("toast").textContent));
  // 9b. a slow confirmation email still goes to the code screen
  await p.evaluate(()=>{signOut();window.external.Supa=window.__realSupa;FSB.slowMail=1;authGo("register");});await w(100);
  await af({name:"Slow",user:"Slow_Mail",email:"slow@gmail.com",pw:"secret5",pw2:"secret5"});await go();await w(700);
  ok(await view()==="code","a slow confirmation email goes straight to the code screen");
  await typeCode("123456");await w(600);ok(await p.evaluate(()=>!!S&&curAcc.sb&&curAcc.sb.uid),"and the code still confirms it");
  await p.evaluate(()=>{FSB.slowMail=0;});
  // 9c. "Only on this PC" skips the online part
  await p.evaluate(()=>{signOut();authGo("register");});await w(100);
  await p.evaluate(()=>document.querySelector(".aswr").click());
  ok(await p.evaluate(()=>document.getElementById("au_user").parentNode.parentNode.style.display==="none"),"turning off Online account hides the username");
  var n0=await p.evaluate(()=>FSB.log.length);
  await af({name:"Local Only",email:"local@gmail.com",pw:"secret7",pw2:"secret7"});await go();await w(300);
  ok(await p.evaluate(n=>!!S&&curAcc.name==="Local Only"&&!curAcc.sb&&FSB.log.length===n,n0),"and makes the account on this PC without going online");
  // 9d. switch account: straight in without a password, Welcome back with one, and the menu lists the others
  ok(await p.evaluate(()=>{var it=switchItems();return it.length===AC.list.length+1&&it.some(function(x){return /Bredy_G/.test(x.t||"");})&&!it.some(function(x){return x.t===curAcc.name;});}),"the switch menu lists every other account on this PC");
  await p.evaluate(()=>{var a=AC.list.filter(function(x){return x.name==="Friend";})[0];a.pw="";switchTo(a);});await w(300);
  ok(await p.evaluate(()=>!!S&&curAcc.name==="Friend"),"switching to an account without a password signs straight in");
  await p.evaluate(()=>{switchTo(accByEmail("bredy@gmail.com"));});await w(300);
  ok(await view()==="pw"&&await p.evaluate(()=>AUTH.a&&AUTH.a.email==="bredy@gmail.com"),"switching to an account with a password asks for it");
  await af({pw:"newpass1"});await go();await w(500);
  ok(await p.evaluate(()=>!!S&&curAcc.email==="bredy@gmail.com"),"and then signs in");
  await p.evaluate(()=>{run.zz={t:Date.now()};S.games.push({id:"zz",name:"Busy Game",path:"C:\\z.exe",seconds:0,plays:0,last:0,added:1});switchTo(AC.list.filter(function(x){return x.name==="Friend";})[0]);});await w(200);
  ok(await p.evaluate(()=>!!S&&curAcc.email==="bredy@gmail.com"),"you can't switch while a game is running");
  await p.evaluate(()=>{delete run.zz;S.games.pop();});
  // 10. the switch
  await p.evaluate(()=>{signOut();ONLINE_ACCOUNTS=0;renderLogin();});
  ok(await p.evaluate(()=>!document.querySelector(".auc")&&!sbOn()),"with the switch off, the old PC-only screen is back");
  await p.evaluate(()=>{var a=accByEmail("bredy@gmail.com");signIn(a);});await w(150);await fill(['newpass1']);await click('Sign in');await w(300);
  ok(await p.evaluate(()=>!!S),"with the switch off, an online-linked account signs in on this PC as before");
  ok(!errs.length, "no JavaScript errors" + (errs.length ? ": " + errs.join(" | ") : ""));
  await b.close();
  console.log(fails ? "FAILED " + fails : "ALL PASSED"); process.exit(fails ? 1 : 0);
})().catch(e => { console.log("EXC", e.stack); process.exit(1); });

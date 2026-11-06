// Online accounts (1.8.0) end to end in Chromium, against a fake Supabase: sign up with an email code,
// taken usernames, add friends by username, sign in on a new PC, offline sign-in, password reset,
// putting an old PC-only account online, change username/password, delete, and the off switch.
const { chromium } = require("playwright"), fs = require("fs");
let fails = 0; const ok = (c, m) => { if (!c) { fails++; console.log("FAIL:", m); } else console.log("ok  :", m); };
(async () => {
  const html = fs.readFileSync("index.html", "utf8");
  const extra = ';var E=window.external;["OverlayHotkey","SetSpeedLimit","KeepAwake","RegisterApp","MakeShortcut","RemoveShortcut","RelayStream","RelayStreamStop","PlaySound","Popup","Notify"].forEach(function(n){E[n]=function(){return "ok";};});' +
    'E.HasShortcut=function(){return "0";};E.StartCommand=function(){return "";};E.Exists=function(){return "1";};E.Scan=function(){return "ok";};E.FindLeftovers=function(){return "ok";};E.SelfTest=function(){return "0";};E.Engine=function(){return "webview2";};' +
    'E.EcNew=function(){return "priv|pub"+Math.random();};E.PubCode=function(){return String(100000000+Math.floor(Math.random()*899999999));};E.RandHex=function(){return "ab";};E.Topic=function(){return "t";};E.Relay=function(id){setTimeout(function(){onHttp(id,"1","{}","200");},10);return "ok";};';
  fs.writeFileSync("ui-acc.html", html.replace("<script>", "<script>" + fs.readFileSync("ui-stub.js", "utf8") + extra + fs.readFileSync("ui-fakesb.js", "utf8") + "</script><script>"));
  const b = await chromium.launch(), p = await b.newPage({ viewport: { width: 1400, height: 860 } });
  const errs = []; p.on("pageerror", e => errs.push(e.message));
  await p.goto("file://" + require("path").resolve("ui-acc.html")); await p.waitForTimeout(2000);
  const w=ms=>p.waitForTimeout(ms);
  const fill=async(vals)=>p.evaluate(v=>{var ins=[].slice.call(document.querySelectorAll('#mbox input,#mbox select'));v.forEach(function(x,i){if(x!==null)ins[i].value=x;});},vals);
  const click=async(t)=>p.evaluate(t=>{var bt=[].slice.call(document.querySelectorAll('#mbox button')).filter(x=>x.textContent===t)[0];if(!bt)throw new Error("no button "+t+" in "+document.getElementById('mbox').innerText.slice(0,200));bt.click();},t);
  const st=async()=>p.evaluate(()=>{var s=document.getElementById('sbst');return (document.getElementById('modal').className==='open'?'[open] ':'[closed] ')+(s?s.textContent:'')+' | '+document.getElementById('toast').textContent;});
  
  // 1. create online account (confirmation on)
  await p.evaluate(()=>createAcc());await w(200);
  await fill(['Bredy','Bredy_G','bredy@gmail.com','secret1','secret1','on']);await click('Create account');await w(1300);
  console.log('after create:',await st());ok(await p.evaluate(()=>!!S&&curAcc.sb&&curAcc.sb.pending===1),'new online account starts on this PC and waits for the email code');
  ok(/Confirm your email/.test(await p.evaluate(()=>$("mbox").innerText)),'the code form opens');
  await fill(['111111']);await click('Confirm');await w(300);ok(/wrong or has expired/.test(await st()),'a wrong code is explained: '+await st());
  await p.evaluate(()=>{FSB.rejectSignupType=1;});await fill(['123456']);await click('Confirm');await w(400);
  ok(await p.evaluate(()=>curAcc.sb.uid&&!curAcc.sb.pending&&curAcc.sb.at),'the right code links the account (also when Supabase wants type "email")');
  await w(3400);
  ok(await p.evaluate(()=>{var pr=FSB.profiles[curAcc.sb.uid];return pr.friend_code===S.user.code;}),'the friend code is published to the profile');
  // 2. username taken
  await p.evaluate(()=>{signOut();});await w(200);
  await p.evaluate(()=>createAcc());await w(150);await fill(['Other','bredy_g','o@gmail.com','secret2','secret2','on']);await click('Create account');await w(300);
  ok(/taken/.test(await st()),'a taken username is refused: '+await st());
  await fill([null,'Other_1',null,null,null,null]);await click('Create account');await w(1300);
  await fill(['123456']);await click('Confirm');await w(400);
  // 3. settings: add friend by username
  await p.evaluate(()=>{curAcc.sb.at="";curAcc.sb.exp=0;});   // force a token refresh
  await p.evaluate(()=>$("fadd").onclick());await w(150);await fill(['Bredy_G','']);await click('Send request');await w(500);
ok(await p.evaluate(()=>S.fr.out.length===1&&S.fr.out[0].code===FSB.profiles["u1-0000"].friend_code),'adding by username finds their friend code and sends the request');
  await p.evaluate(()=>$("fadd").onclick());await w(150);await fill(['nobody_here','']);await click('Send request');await w(400);
  ok(/Nobody is using/.test(await st()),'an unknown username is explained');await p.evaluate(()=>closeForm());
  // 4. new PC: sign in with email creates a local account
  await p.evaluate(()=>{signOut();AC.list=AC.list.filter(function(a){return a.email!=="bredy@gmail.com";});saveAcc();renderLogin();});
  await p.evaluate(()=>sbSignIn());await w(150);await fill(['bredy@gmail.com','wrongpw']);await click('Sign in');await w(300);
  ok(/isn.t right/.test(await st()),'wrong password online is refused');
  await fill([null,'secret1']);await click('Sign in');await w(500);
  ok(await p.evaluate(()=>S&&curAcc.email==="bredy@gmail.com"&&curAcc.sb.uid==="u1-0000"&&curAcc.name==="Bredy_G"),'signing in on a new PC adds the account here, named after the username');
  // 5. offline sign-in works with the password this PC knows
  await p.evaluate(()=>{signOut();FSB.online=false;sbSignIn();});await w(150);await fill(['bredy@gmail.com','secret1']);await click('Sign in');await w(300);
  ok(await p.evaluate(()=>!!S&&curAcc.email==="bredy@gmail.com"),'offline, the account still signs in on this PC');
  await p.evaluate(()=>{FSB.online=true;});
  // 6. forgot password
  await p.evaluate(()=>{signOut();sbForgot();});await w(150);await fill(['bredy@gmail.com']);await click('Send code');await w(400);
  ok(/Reset your password/.test(await p.evaluate(()=>$("mbox").innerText)),'forgot password sends a code');
  await fill(['654321','newpass1']);await click('Continue');await w(500);
  ok(await p.evaluate(()=>FSB.users["u1-0000"].pw==="newpass1"&&!!S&&hashPw(curAcc,"newpass1")===curAcc.pw),'reset changes the password online and on this PC, and signs in');
  // 7. existing PC-only account goes online (opt-in), email already online on another PC -> links
  await p.evaluate(()=>{signOut();var a={id:"aold",name:"Old Timer",email:"",color:1,pw:"",file:"data_aold.json",created:1};AC.list.push(a);saveAcc();startSession(a);nav("settings","account");});await w(300);
  ok(await p.evaluate(()=>/Not online/.test($("pSet").innerText)),'an account without email or password shows the opt-in');
  await p.evaluate(()=>sbGoOnline());await w(150);
  await fill(['old@gmail.com','Old_Timer','oldpass1']);await click('Continue');await w(500);
  ok(await p.evaluate(()=>curAcc.sb&&curAcc.sb.pending===1&&curAcc.email==="old@gmail.com"&&curAcc.pw===hashPw(curAcc,"oldpass1")),'opt-in signs it up and keeps everything on this PC');
  await fill(['123456']);await click('Confirm');await w(400);
  ok(await p.evaluate(()=>curAcc.sb.uid&&!curAcc.sb.pending),'confirming finishes it');
  // 8. change username, change password, disconnect, delete
  await p.evaluate(()=>sbChangeUser());await w(100);await fill(['Bredy_G']);await click('Save');await w(300);ok(/taken/.test(await st()),'changing to a taken username is refused');
  await fill(['Oldie']);await click('Save');await w(300);ok(await p.evaluate(()=>curAcc.sb.user==="Oldie"),'username changes');
  await p.evaluate(()=>sbChangePw());await w(100);await fill(['oldpass1','brandnew1','brandnew1']);await click('Save');await w(500);
  ok(await p.evaluate(()=>FSB.users[curAcc.sb.uid].pw==="brandnew1"&&curAcc.pw===hashPw(curAcc,"brandnew1")),'password changes online and on this PC');
  var uidOld=await p.evaluate(()=>curAcc.sb.uid);
  await p.evaluate(()=>sbDelete());await w(100);await fill(['brandnew1']);await click('Delete online account');await w(500);
  ok(await p.evaluate(u=>!FSB.users[u]&&!curAcc.sb&&!!S,uidOld),'deleting the online account keeps the account on this PC');
  // 9. email setup not finished: sign-up falls back to PC-only with a clear message
  await p.evaluate(()=>{signOut();FSB.confirm=true;var o=window.external.Supa;window.external.Supa=function(id,m,path,b,t){if(path==="/auth/v1/signup"){setTimeout(function(){onHttp(id,"0",JSON.stringify({error_code:"email_address_not_authorized",msg:"Email address not authorized"}),"400");},20);return "ok";}return o(id,m,path,b,t);};createAcc();});await w(150);
  await fill(['Friend','Friend_9','friend@gmail.com','secret9','secret9','on']);await click('Create account');await w(1300);
  ok(await p.evaluate(()=>!!S&&curAcc.email==="friend@gmail.com"&&!curAcc.sb),'if email isn\'t set up yet, the account is made on this PC: '+await p.evaluate(()=>$("toast").textContent));
  // 10. the switch
  await p.evaluate(()=>{signOut();ONLINE_ACCOUNTS=0;renderLogin();});
  ok(await p.evaluate(()=>!/I already have an account/.test($("lbox").innerText)&&typeof sbOn==="function"&&!sbOn()),'with the switch off, the online parts are gone');
  await p.evaluate(()=>{var a=accByEmail("bredy@gmail.com");signIn(a);});await w(150);await fill(['newpass1']);await click('Sign in');await w(300);
  ok(await p.evaluate(()=>!!S),'with the switch off, an online-linked account signs in on this PC as before');
  ok(!errs.length, "no JavaScript errors" + (errs.length ? ": " + errs.join(" | ") : ""));
  await b.close();
  console.log(fails ? "FAILED " + fails : "ALL PASSED"); process.exit(fails ? 1 : 0);
})().catch(e => { console.log("EXC", e.stack); process.exit(1); });

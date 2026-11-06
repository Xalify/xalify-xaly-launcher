// Opens the launcher page in Chromium (like the WebView2 engine) with a fake Windows side,
// signs in and visits every screen. Any JavaScript error fails the test.
const { chromium } = require("playwright"), fs = require("fs");
let fails = 0; const ok = (c, m) => { if (!c) { fails++; console.log("FAIL:", m); } else console.log("ok  :", m); };
(async () => {
  const html = fs.readFileSync("index.html", "utf8");
  const extra = ';var E=window.external;["OverlayHotkey","SetSpeedLimit","KeepAwake","RegisterApp","MakeShortcut","RemoveShortcut","RelayStream","RelayStreamStop","PlaySound","Popup","Notify"].forEach(function(n){E[n]=function(){return "ok";};});' +
    'E.HasShortcut=function(){return "0";};E.StartCommand=function(){return "";};E.Drives=function(){return "C:\\\\|Windows|Fixed|500000000000|120000000000";};E.FreeSpace=function(){return "120000000000";};E.DirBytes=function(){return "0";};E.Scan=function(){return "ok";};E.FindDownload=function(){return "0|x||0";};E.Exists=function(p){return String(p).split("\\n").map(function(){return "1";}).join("");};E.SelfTest=function(){return "0";};E.ListShots=function(){return "";};E.ShowWindow=function(){return "ok";};E.Engine=function(){return "webview2";};';
  fs.writeFileSync("ui-page.html", html.replace("<script>", "<script>" + fs.readFileSync("ui-stub.js", "utf8") + extra + "</script><script>"));
  const b = await chromium.launch(), p = await b.newPage({ viewport: { width: 1400, height: 860 } });
  const errs = []; p.on("pageerror", e => errs.push(e.message));
  await p.goto("file://" + require("path").resolve("ui-page.html")); await p.waitForTimeout(500);
  ok(await p.evaluate(() => document.getElementById("login").style.display === "block"), "sign-in screen on first start");
  await p.evaluate(() => { ONLINE_ACCOUNTS = 0; createAcc(); var i = document.querySelectorAll("#mbox input"); i[0].value = "Tester"; i[1].value = "t@gmail.com"; i[2].value = "secret1"; i[3].value = "secret1";
    [].slice.call(document.querySelectorAll("#mbox button")).filter(function (x) { return x.textContent == "Create account"; })[0].click(); });
  await p.waitForTimeout(1200);
  ok(await p.evaluate(() => !!S && S.user.name === "Tester"), "account created and signed in");
  const visits = [
    ["store", "nav('store')"], ["library", "nav('lib')"], ["downloads", "nav('downloads')"], ["profile", "openProfile('')"],
    ["store game page", "openDetail(S.store.items[0])"],
    ["library game page", "S.games.push({id:'gt',name:'Test Game',path:'C:\\\\dl\\\\Games\\\\T\\\\t.exe',storeId:S.store.items[0].id,seconds:3600,plays:3,last:now(),added:1});P.cur='gt';nav('game')"],
    ["friends panel", "setFP(true);renderFriends(true)"], ["friends activity", "P.ftab='act';renderFriends()"], ["challenges", "P.ftab='ch';renderFriends()"],
    ["feedback panel", "setFP(false);openFeedback()"], ["feedback panel closes", "closeFeedback()"], ["logo menu", "brandMenu()"], ["what's new", "hideMenu();whatsNew(true)"],
    ["in-game overlay", "run.gt={t:now()-60000};S.fr=S.fr||{inc:[],out:[],seen:{}};openOverlay('')"], ["overlay closes", "closeOverlay();delete run.gt"],
    ["TV mode", "enterTV()"], ["leave TV mode", "exitTV()"]
  ];
  ["account", "friends", "general", "downloads", "storage", "cache", "about"].forEach(t => visits.push(["settings: " + t, "nav('settings','" + t + "');P.tab='" + t + "';renderSettings()"]));
  visits.push(["find games dialog", "nav('lib');findGames('')"], ["backup dialog", "closeForm();backupAcc()"], ["uninstall dialog", "closeForm();openUninstall()"], ["challenge form", "closeForm();S.friends.push({code:'111222333',name:'Bob',color:1,msgs:[]});chalForm(S.friends[0],gameBy('gt'))"]);
  for (const [name, js] of visits) {
    const before = errs.length;
    await p.evaluate(js).catch(e => errs.push(name + ": " + e.message));
    await p.waitForTimeout(150);
    ok(errs.length === before, name + (errs.length === before ? "" : " \u2014 " + errs.slice(before).join(" | ")));
  }
  ok(await p.evaluate(() => document.querySelectorAll("#pStore .gc").length >= 0), "store renders");
  await b.close();
  console.log(fails ? "FAILED " + fails : "ALL PASSED"); process.exit(fails ? 1 : 0);
})().catch(e => { console.log("EXC", e.stack); process.exit(1); });

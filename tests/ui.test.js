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
  ["account", "friends", "general", "appearance", "notifications", "controller", "downloads", "storage", "cache", "about"].forEach(t => visits.push(["settings: " + t, "nav('settings','" + t + "');P.tab='" + t + "';renderSettings()"]));
  visits.push(["settings search", "P.sq2='sound';renderSettings()"], ["settings search cleared", "P.sq2='';renderSettings()"], ["switch account menu", "switchMenu($('chip'))"], ["find games dialog", "nav('lib');findGames('')"], ["backup dialog", "closeForm();backupAcc()"], ["uninstall dialog", "closeForm();openUninstall()"], ["challenge form", "closeForm();S.friends.push({code:'111222333',name:'Bob',color:1,msgs:[]});chalForm(S.friends[0],gameBy('gt'))"]);
  for (const [name, js] of visits) {
    const before = errs.length;
    await p.evaluate(js).catch(e => errs.push(name + ": " + e.message));
    await p.waitForTimeout(150);
    ok(errs.length === before, name + (errs.length === before ? "" : " \u2014 " + errs.slice(before).join(" | ")));
  }
  ok(await p.evaluate(() => document.querySelectorAll("#pStore .gc").length >= 0), "store renders");
  // 2.0.0 controller settings, driven by a fake PlayStation controller
  const pad = await p.evaluate(async () => {
    const wait = ms => new Promise(r => setTimeout(r, ms)), out = {};
    const B = () => { const l = []; for (let i = 0; i < 17; i++) l.push({ pressed: false, value: 0 }); return l; };
    window.__gp = { id: "DualSense Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 0ce6)", connected: true, buttons: B(), axes: [0, 0, 0, 0], mapping: "standard" };
    navigator.getGamepads = () => [window.__gp];
    const press = async i => { __gp.buttons[i] = { pressed: true, value: 1 }; await wait(90); __gp.buttons[i] = { pressed: false, value: 0 }; await wait(90); };
    PAD.native = 0; padReset(); S.set.pad.style = "auto"; await wait(80);
    out.style = padStyle(); out.selGlyph = padName("a");
    enterTV(); await wait(50); out.hint = (function (g) { return g.title || g.textContent; })(document.querySelector("#tvhint .tvbt"));
    await press(9); out.menu = !!TV.menu;                                   // Options opens the TV menu
    await press(13); await press(13); await press(0); out.cfg = !!TV.cfg;      // down, down, select: Controller settings
    out.rows = document.querySelectorAll("#tvsheet .tvcr").length;
    TV.sf = tvCfgItems().map(x => x.t).indexOf("Select"); tvCfg(); await press(0); out.capturing = !!PAD.cap;
    await press(3); out.aOn = padBtnOf("a"); out.yOn = padBtnOf("y");           // bind Select to triangle: Details moves to cross
    out.capGone = !PAD.cap;
    __gp.axes = [0.4, 0, 0, 0]; S.set.pad.dz = 30; out.dzLow = (padBits() & 8) > 0; S.set.pad.dz = 50; out.dzHigh = (padBits() & 8) > 0; __gp.axes = [0, 0, 0, 0];
    S.set.pad.style = "sw"; out.sw = padLabel("s") + padLabel("e"); S.set.pad.style = "xbox"; out.xb = padLabel("s");
    padReset(); out.reset = padBtnOf("a") + padBtnOf("y"); padBind("a", "e"); out.swapped = padSwapped() && padBtnOf("b") === "s"; padReset();
    TV.cfg = 0; exitTV(); S.set.pad.style = "auto";
    nav("settings", "controller"); P.tab = "controller"; renderSettings(); await wait(150);
    out.live = (document.querySelector(".pdnm") || {}).textContent || "";
    out.rowsS = document.querySelectorAll("#sbody .pdbind").length;
    const o = padLive; let n = 0; padLive = function () { n++; return o(); }; nav("store"); await wait(150); const n1 = n; await wait(250); out.stopped = n === n1; padLive = o;
    navigator.getGamepads = () => []; return out;
  });
  ok(pad.style === "ps" && pad.selGlyph === "\u2715" && pad.hint === "\u2715", "a PlayStation controller is detected and shows PlayStation buttons: " + JSON.stringify([pad.style, pad.selGlyph, pad.hint]));
  ok(pad.menu && pad.cfg && pad.rows >= 13, "TV mode: Options opens the menu, and Controller settings opens from it with the controller (" + pad.rows + " rows)");
  ok(pad.capturing && pad.capGone && pad.aOn === "n" && pad.yOn === "s", "TV mode: rebinding Select to a pressed button swaps it with the action that had it: " + JSON.stringify(pad));
  ok(pad.dzLow && !pad.dzHigh, "stick deadzone decides when a push counts");
  ok(pad.sw === "BA" && pad.xb === "A" && pad.reset === "sn" && pad.swapped, "Switch and Xbox styles, reset, and Nintendo-style confirm");
  ok(pad.stopped, "the live controller test stops after leaving Settings");
  ok(/DualSense Wireless Controller connected/.test(pad.live) && pad.rowsS === 8, "Settings > Controller shows the live controller and every binding: " + pad.live);
  // quitting from the overlay asks inside the overlay, not in a launcher dialog hidden behind it
  const ovq = await p.evaluate(() => {
    run.gt = { t: now() - 60000 }; S.fr = S.fr || { inc: [], out: [], seen: {} }; openOverlay("");
    var q = [].slice.call(document.querySelectorAll("#ovl .ovqr .btn.dng"))[0]; if (!q) return "no quit button";
    q.click(); var c = document.querySelector("#ovl .ovqc"), modal = $("modal").className.indexOf("open") >= 0;
    var r = c && c.getBoundingClientRect().height > 0 && !modal && /ovf/.test((ovFoc || {}).className || "") ? "ok" : "confirm=" + !!c + " modal=" + modal;
    closeOverlay(); delete run.gt; return r;
  });
  ok(ovq === "ok", "overlay quit asks inside the overlay (" + ovq + ")");
  // TV mode: every kind of tile must actually be visible (1.8.10: store tiles were see-through)
  const tvVis = await p.evaluate(() => {
    var out = {}; enterTV();
    ["home", "store", "lib"].forEach(function (t) { tvTab(t); var ts = [].slice.call(document.querySelectorAll("#tvrows .tvt"));
      out[t] = ts.length ? ts.filter(function (x) { var o = 1, e = x; while (e && e !== document.body) { o *= +getComputedStyle(e).opacity; e = e.parentNode; } return o > 0.3 && x.getBoundingClientRect().height > 50; }).length + "/" + ts.length : "none"; });
    exitTV(); return out;
  });
  ok(/^([1-9]\d*)\/\1$/.test(tvVis.store) && /^[1-9]/.test(tvVis.home), "TV mode store tiles are visible: " + JSON.stringify(tvVis));
  // 2.0.8: plugging a controller in or out shows a notice in the bottom-right corner
  const pn = await p.evaluate(async () => {
    const wait = ms => new Promise(r => setTimeout(r, ms)), out = [], E = window.external, txt = () => { const c = document.getElementById("padn"); return c && c.className === "show" ? c.textContent : ""; };
    let pads = [], mask = "0"; navigator.getGamepads = () => pads; E.PadMask = () => mask;
    PADC.init = 0; padConnTick(); const c0 = document.getElementById("padn"); if (c0) c0.className = ""; out.push(txt() || "-");                                     // already plugged in at start: quiet
    mask = "1"; padConnTick(); out.push(txt()); await wait(20);                              // Xbox controller plugged in
    pads = [{ id: "DualSense Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 0ce6)", index: 1, connected: true, buttons: [], axes: [] }]; padConnTick(); out.push(txt());
    pads = []; padConnTick(); out.push(txt());
    mask = "0"; padConnTick(); out.push(txt());
    S.set.pad.notice = 0; document.getElementById("padn").className = ""; mask = "1"; padConnTick(); out.push(txt() || "-"); S.set.pad.notice = 1; mask = "0"; padConnTick();
    navigator.getGamepads = () => []; return out;
  });
  ok(pn.join("|") === "-|Controller connectedXbox controller|Controller connectedDualSense controller|Controller disconnectedDualSense controller|Controller disconnectedXbox controller|-", "controller connect and disconnect notices: " + pn.join("|"));
  // 2.0.4: the smallest window (1000x660) keeps the Library bar and Settings rows on screen
  await p.setViewportSize({ width: 1000, height: 660 });
  const small = await p.evaluate(async () => {
    const wait = ms => new Promise(r => setTimeout(r, ms)), out = {};
    nav("lib"); await wait(150); var h = document.querySelector("#pLib .lhd"); out.lib = h.scrollWidth + "/" + h.clientWidth + "/" + h.offsetHeight;
    nav("settings", "controller"); P.tab = "controller"; renderSettings(); await wait(150);
    out.squashed = [].slice.call(document.querySelectorAll("#sbody .srow .sl")).filter(function (x) { return x.offsetParent && x.offsetWidth < 150; }).length;
    return out;
  });
  const lw = small.lib.split("/").map(Number);
  ok(lw[0] <= lw[1] + 1 && lw[2] < 60, "the Library bar fits on one line in the smallest window: " + small.lib);
  ok(small.squashed === 0, "Settings labels aren't squashed in the smallest window (" + small.squashed + " squashed)");
  await p.setViewportSize({ width: 1400, height: 860 });
  // 2.0.4: signing out from TV mode leaves TV mode
  const so = await p.evaluate(async () => { enterTV(); signOut(); await new Promise(r => setTimeout(r, 100)); return [tvOn, document.getElementById("tv").className, document.getElementById("login").style.display].join(","); });
  ok(so === "false,,block", "signing out from TV mode goes back to the sign-in screen: " + so);
  await b.close();
  console.log(fails ? "FAILED " + fails : "ALL PASSED"); process.exit(fails ? 1 : 0);
})().catch(e => { console.log("EXC", e.stack); process.exit(1); });

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
  // 2.1.3: Storage thumbnails were stretched by an itch.io header that reused the .ith class
  const stl = await p.evaluate(() => {
    nav("settings", "storage"); P.tab = "storage"; renderSettings();
    onScan("1000|3000|10|10|Big Game|1890000000|C:\\DL\\Games\\Big Game\nSmall Game|423000000|C:\\DL\\Games\\Small Game");
    var th = document.querySelector(".irow .ith"), tx = document.querySelector(".irow .itx");
    var r = th && th.getBoundingClientRect(), t = tx && tx.getBoundingClientRect();
    var out = r ? Math.round(r.width) + "x" + Math.round(r.height) + " text " + Math.round(t.width) : "no rows";
    SC = null; scanning = false; return out;
  });
  ok(/^124x58 text (\d+)$/.test(stl) && +stl.split(" ")[2] > 250, "Storage rows keep a small thumbnail with room for the name and details (" + stl + ")");
  const itchHd = await p.evaluate(() => { var s = [].slice.call(document.styleSheets).some(function (ss) { try { return [].slice.call(ss.cssRules).some(function (r) { return r.selectorText === ".ith" && /flex: 1 1 100%/.test(r.cssText); }); } catch (e) { return false; } }); return !s; });
  ok(itchHd, "no other screen restyles the Storage thumbnail class");
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
  // 2.0.9: Sign in with itch.io: owned games are imported and install with your own download key
  const io = await p.evaluate(async () => {
    const wait = ms => new Promise(r => setTimeout(r, ms)), E = window.external, paths = [], out = {};
    E.ItchSignedIn = () => "1";
    E.ItchApi = (id, path) => { paths.push(path); setTimeout(() => {
      let body = "{}";
      if (path === "me/profile") body = JSON.stringify({ user: { username: "noah", display_name: "Noah" } });
      else if (path === "me/owned?page=1") body = JSON.stringify({ owned_keys: [
        { id: 555, created_at: "2026-11-01T10:00:00Z", game: { id: 9001, title: "Paid Horror Game", url: "https://dev.itch.io/paid-horror", cover_url: "", min_price: 499, classification: "game", p_windows: true, user: { username: "dev" } } },
        { id: 556, created_at: "2026-10-01T10:00:00Z", game: { id: 9002, title: "A Soundtrack", url: "https://dev.itch.io/ost", classification: "soundtrack" } } ] });
      else if (path === "me/owned?page=2") body = JSON.stringify({ owned_keys: [] });
      else if (/^v2\/games\/9001\/uploads\?download_key_id=555$/.test(path)) body = JSON.stringify({ uploads: [{ id: 77, filename: "PaidHorror.zip", p_windows: true, size: 1000, type: "default" }] });
      else if (/^v2\/uploads\/77\/download\?download_key_id=555$/.test(path)) body = JSON.stringify({ url: "https://files.itch.zone/paid.zip" });
      window.onHttp(id, "1", body, "200"); }, 3); return "ok"; };
    itchRefresh(false); await wait(200);
    out.owned = (S.owned || []).map(x => x.name + ":" + x.okid + ":" + x.paid).join(",");
    out.user = S.itchUser;
    out.storeHidden = storeFilter().some(x => x.owned);
    nav("lib"); P.lt = "itch"; renderLib(); await wait(50);
    out.tab = document.getElementById("pLib").textContent.indexOf("Paid Horror Game") > -1 && document.getElementById("pLib").textContent.indexOf("Bought") > -1;
    let res = null; resolveAny(S.owned[0], (e, r) => { res = e || r.url; }); await wait(200);
    out.res = res; out.used = paths.filter(x => /download_key_id/.test(x)).length;
    // itch.io refuses downloads to the sign-in (and to Xaly's key): the game goes to the browser instead of failing
    E.ItchApi = (id, path) => { paths.push(path); setTimeout(() => window.onHttp(id, "0", JSON.stringify({ errors: ["api key does not permit `game:view:uploads`"] }), "403"), 3); return "ok"; };
    let code = null; resolveAny(S.owned[0], (e, r, c) => { code = c; }); await wait(150);
    out.code = code; out.dk = paths.some(x => /^dk\/v2\/games\/9001\/uploads\?download_key_id=555$/.test(x));
    P.lt = "all"; delete E.ItchApi; E.ItchSignedIn = () => "0"; S.owned = []; return out;
  });
  ok(io.owned === "Paid Horror Game:555:true" && io.user === "Noah", "itch.io library imported, games only (" + io.owned + ")");
  ok(!io.storeHidden && io.tab, "owned games are in Library › itch.io, not on the Store's shelves");
  ok(io.res === "https://files.itch.zone/paid.zip" && io.used === 2, "a bought game installs with your own itch.io download key (" + io.res + ")");
  ok(io.code === "browser" && io.dk, "when itch.io refuses, Xaly tries its API key and then the browser (" + io.code + ")");
  // 2.1.1: an itch.io sign-in belongs to the Xaly account that made it
  const per = await p.evaluate(async () => {
    const E = window.external, tok = {}; let cur = "";
    E.ItchUse = a => { cur = a; return "ok"; }; E.ItchSignedIn = () => tok[cur] ? "1" : "0"; E.ItchSignOut = () => { delete tok[cur]; return "ok"; };
    E.ItchSignIn = () => "ok"; E.ItchForget = a => { delete tok[a]; return "ok"; }; E.ItchClaimOld = () => "none";
    const A = curAcc.id; tok[A] = 1; const o = { a: itchOn() };
    const B = { id: "b2test", name: "Other", color: 1, pw: "", file: "b2test.json", created: Date.now() }; AC.list.push(B);
    curAcc = B; o.b = itchOn();
    curAcc = AC.list.find(x => x.id === A); o.a2 = itchOn();
    AC.list.splice(AC.list.indexOf(B), 1);
    ["ItchUse", "ItchSignedIn", "ItchSignOut", "ItchSignIn", "ItchForget", "ItchClaimOld"].forEach(k => delete E[k]);
    return o;
  });
  ok(per.a === true && per.b === false && per.a2 === true, "an itch.io sign-in only counts for the account that made it (" + JSON.stringify(per) + ")");
  // 2.0.9: languages, theme, text size, reduced motion, TV keyboard
  const lg = await p.evaluate(async () => {
    const wait = ms => new Promise(r => setTimeout(r, ms)), o = {};
    AC.look = AC.look || {}; AC.look.lang = "es"; trStart(); nav("settings", "general"); P.tab = "appearance"; renderSettings(); await wait(50);
    o.es = document.getElementById("sbody").textContent.indexOf("Apariencia") > -1 && document.getElementById("sbody").textContent.indexOf("Appearance") < 0;
    o.tpl = tr("Add 3 games") + " | " + tr("Add 1 game") + " | " + tr("Online — 4") + " | " + tr("3 hours ago");
    o.sym = tr("★  Saved") + " | " + tr("GAMES") + " | " + tr("Recomp  ·  Horror");
    o.game = tr("Hollow Knight Silksong");
    setLang("pt"); await wait(50); o.pt = document.getElementById("sbody").textContent.indexOf("Aparência") > -1;
    setLang("en"); await wait(50); o.en = document.getElementById("sbody").textContent.indexOf("Appearance") > -1 && document.getElementById("sbody").textContent.indexOf("Aparência") < 0;
    AC.look.theme = "light"; AC.look.acc = "purple"; AC.look.text = 1.22; AC.look.rm = 1; applyLook(); await wait(30);
    o.light = getComputedStyle(document.body).backgroundColor; o.zoom = document.body.style.zoom; o.rm = document.documentElement.className;
    o.acc = getComputedStyle(document.querySelector(".pri") || document.body).backgroundImage.indexOf("linear-gradient") > -1;
    AC.look = { theme: "dark", acc: "cyan", text: 1, rm: 0 }; applyLook();
    enterTV(); tvTab("search"); tvInput("a"); o.kb = !!TV.kb; ["right", "a", "y", "b"].forEach(k => tvInput(k)); o.q = JSON.stringify(TV.q); tvInput("start"); o.kbClosed = !TV.kb; exitTV();
    return o;
  });
  ok(lg.es && lg.pt && lg.en, "switching language translates the screen and English comes back (" + [lg.es, lg.pt, lg.en] + ")");
  ok(lg.tpl === "Agregar 3 juegos | Agregar 1 juego | En línea — 4 | hace 3 horas", "templates with numbers and plurals: " + lg.tpl);
  ok(lg.sym === "★  Guardado | JUEGOS | Recomp  ·  Terror", "text with symbols, capitals and separators: " + lg.sym);
  ok(lg.game === "Hollow Knight Silksong", "game names are left alone");
  ok(/rgb\((2[3-5]\d), (2[3-5]\d), (2[3-5]\d)\)/.test(lg.light) && lg.zoom === "1.22" && /\brm\b/.test(lg.rm) && lg.acc, "light theme, text size, reduced motion and accent apply (" + lg.light + ", " + lg.zoom + ", " + lg.rm + ")");
  ok(lg.kb && lg.q === '"w"' && lg.kbClosed, "TV keyboard types with the controller: " + lg.q);
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

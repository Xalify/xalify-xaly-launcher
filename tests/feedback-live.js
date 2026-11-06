// One-time live check (not part of the normal test run): opens the launcher page in Chromium,
// presses Give feedback and submits a test message through the embedded Youform form.
const { chromium } = require("playwright"), fs = require("fs"), path = require("path");
(async () => {
  const html = fs.readFileSync("index.html", "utf8");
  const extra = ';var E=window.external;["OverlayHotkey","SetSpeedLimit","KeepAwake","RegisterApp","MakeShortcut","RelayStream","RelayStreamStop","PlaySound","Popup","Notify"].forEach(function(n){E[n]=function(){return "ok";};});E.HasShortcut=function(){return "0";};E.StartCommand=function(){return "";};E.SelfTest=function(){return "0";};E.Engine=function(){return "webview2";};E.ListShots=function(){return "";};';
  fs.writeFileSync("fb-page.html", html.replace("<script>", "<script>" + fs.readFileSync("ui-stub.js", "utf8") + extra + "</script><script>"));
  const b = await chromium.launch(), p = await b.newPage({ viewport: { width: 1280, height: 800 } });
  await p.goto("file://" + path.resolve("fb-page.html")); await p.waitForTimeout(500);
  await p.evaluate(() => { createAcc(); var i = document.querySelectorAll("#mbox input"); i[0].value = "Build Server"; i[1].value = "b@gmail.com"; i[2].value = "secret1"; i[3].value = "secret1";
    [].slice.call(document.querySelectorAll("#mbox button")).filter(function (x) { return x.textContent == "Create account"; })[0].click(); });
  await p.waitForTimeout(800);
  await p.evaluate(() => { ENGINE = "webview2"; openFeedback(); });
  const fe = await p.waitForSelector("#fbw iframe", { timeout: 10000 });
  const fr = await fe.contentFrame();
  await fr.waitForSelector("textarea", { timeout: 30000 });
  console.log("form loaded inside the launcher panel");
  await fr.fill("textarea", "Test from the Xaly Launcher build server: the Give feedback button works end to end. You can delete this response.");
  console.log("FORM TEXT:", (await fr.innerText("body")).replace(/\s+/g, " ").slice(0, 300));
  console.log("CLICKABLES:", JSON.stringify(await fr.evaluate(() => [].slice.call(document.querySelectorAll("button,[role=button],a,input[type=submit]")).map(e => e.tagName + ":" + (e.innerText || e.value || e.getAttribute("aria-label") || "").trim().slice(0, 30)).slice(0, 30))));
  const click = async re => {
    const bt = fr.locator("button, [role=button], a", { hasText: re }).first();
    try { await bt.waitFor({ state: "visible", timeout: 6000 }); await bt.click(); }
    catch (e) { console.log("no button matching " + re + ", pressing Ctrl+Enter"); await p.keyboard.press("Control+Enter"); }
    await p.waitForTimeout(1500); };
  await click(/Next|OK/);
  console.log("after message:", (await fr.innerText("body")).replace(/\s+/g, " ").slice(0, 160));
  await click(/Next|Skip|OK/);
  console.log("after topic:", (await fr.innerText("body")).replace(/\s+/g, " ").slice(0, 160));
  await click(/Send feedback|Submit|OK/);
  await p.waitForTimeout(3000);
  const end = (await fr.innerText("body")).replace(/\s+/g, " ");
  console.log("end screen:", end.slice(0, 200));
  console.log(/on its way/i.test(end) ? "LIVE FEEDBACK OK" : "LIVE FEEDBACK DID NOT REACH THE THANK-YOU SCREEN");
  await p.screenshot({ path: "fb-final.png" });
  await b.close();
})().catch(e => { console.log("LIVE FEEDBACK ERROR", e.message); process.exit(1); });

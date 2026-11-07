// Builds the shared store catalog that every Xaly Launcher downloads, so all players see the same games.
// Runs on GitHub Actions every few hours. It reuses the launcher's own itch.io parsing code from index.html,
// so game ids and fields match what launchers already have.
const fs = require("fs"), vm = require("vm");
const html = fs.readFileSync(__dirname + "/../index.html", "utf8");
function grab(name) {
  const i = html.indexOf("function " + name + "(");
  if (i < 0) throw new Error("missing " + name);
  let d = 0, j = html.indexOf("{", i);
  for (; j < html.length; j++) { if (html[j] === "{") d++; else if (html[j] === "}" && --d === 0) break; }
  return html.slice(i, j + 1);
}
function grabVar(re) { const m = html.match(re); if (!m) throw new Error("missing " + re); return m[0]; }
const code = ["trim", "hash", "htmlText", "decEnt", "parseRss", "parseCells", "frGuess", "urlKey"].map(grab).join("\n") + "\n" +
  grabVar(/var FAN=[^\n]*/) + "\n" + grabVar(/var IB=[^\n]*/) + "\n" + grabVar(/var LIVEF=\[[\s\S]*?\n\];/) + "\n" + grabVar(/var LGEN=[^\n]*/);
const ctx = vm.createContext({});
vm.runInContext(code, ctx);
const { parseRss, parseCells, frGuess, urlKey, hash, LIVEF, LGEN } = ctx;

const TARGET = +(process.env.TARGET || 8000), MAX_PAGES = 80, UA = "XalyLauncher-catalog (+https://github.com/Xalify/xalify-xaly-launcher)";
const sleep = ms => new Promise(r => setTimeout(r, ms));
// "" = the page doesn't exist; null = itch.io refused or didn't answer (try that page again later)
let slowUntil = 0;
async function get(url) {
  for (let t = 0; t < 4; t++) {
    if (Date.now() < slowUntil) await sleep(slowUntil - Date.now());
    try {
      const r = await fetch(url, { headers: { "User-Agent": UA }, signal: AbortSignal.timeout(20000) });
      if (r.ok) return await r.text(); if (r.status === 404) return "";
      console.log("HTTP " + r.status + " " + url);
      if (r.status === 429) {   // asked to slow down: every list waits, not just this one
        const ra = +(r.headers.get("retry-after") || 0);
        slowUntil = Math.max(slowUntil, Date.now() + (ra > 0 ? Math.min(ra, 120) * 1000 : 15000 * (t + 1)));
        continue;
      }
    }
    catch (e) { }
    await sleep(2000 * (t + 1));
  }
  return null;
}
async function page(url, n) {
  const base = url.split("?")[0];
  const x = await get(base + ".xml" + (n > 1 ? "?page=" + n : ""));
  let c = x ? parseRss(x) : [];
  if (c.length) return c;
  const t = await get(base + "?page=" + n + "&format=json");
  if (t === null) return x === null ? null : [];
  let h = t; try { const j = JSON.parse(t); if (j && j.content !== undefined) h = j.content; } catch (e) { }
  return parseCells(h);
}
const T0 = Date.now(), BUDGET = +(process.env.BUDGET_MIN || 28) * 60000;   // always publish within the time budget
(async () => {
  const items = [], idx = {}, pg = {}, done = {}, fails = {};
  let round = 0;
  while (items.length < TARGET && LIVEF.some(f => !done[f[0]]) && Date.now() - T0 < BUDGET) {
    round++;
    const lists = LIVEF.filter(f => !done[f[0]]);
    for (let b = 0; b < lists.length; b += 4) {   // four lists at a time
      if (items.length >= TARGET || Date.now() - T0 >= BUDGET) break;
      const batch = lists.slice(b, b + 4);
      const results = await Promise.all(batch.map(f => { const n = (pg[f[0]] || 0) + 1; pg[f[0]] = n; return page(f[2], n).then(cells => [f, n, cells]); }));
      for (const [f, n, got] of results) {
      if (got === null) {   // refused: ask for the same page again next round, and give up on the list after 5 refusals in a row
        pg[f[0]] = n - 1; fails[f[0]] = (fails[f[0]] || 0) + 1; if (fails[f[0]] >= 5) done[f[0]] = 1;
        console.log(`round ${round} ${f[0]} page ${n}: refused (${fails[f[0]]} in a row)`); continue;
      }
      const cells = got; fails[f[0]] = 0;
      if (!cells.length || n >= MAX_PAGES) done[f[0]] = 1;
      let added = 0;
      cells.forEach((c, i) => {
        if (c.paid || !c.img || !c.name) return;
        const k = urlKey(c.url); let it = idx[k];
        if (!it) {
          const fr = frGuess(c.name + " " + c.text + " " + f[0]);
          it = { id: "x" + (c.gid || hash(k).toString(36)), fr, genre: c.genre || LGEN[f[0]] || "", name: c.name.substr(0, 90), tagline: c.text.substr(0, 160), url: c.url, image: c.img, devs: c.author, lf: {}, seen: T0 };
          items.push(it); idx[k] = it; added++;
        }
        if (!it.genre && LGEN[f[0]]) it.genre = LGEN[f[0]];
        const rank = (n - 1) * 1000 + i;
        if (it.lf[f[0]] === undefined || rank < it.lf[f[0]]) it.lf[f[0]] = rank;
      });
      console.log(`round ${round} ${f[0]} page ${n}: ${cells.length} games, ${added} new, total ${items.length}, ${Math.round((Date.now() - T0) / 1000)}s`);
      }
      await sleep(300);
    }
  }
  if (items.length < 500) { console.error("Too few games (" + items.length + "), keeping the old catalog."); process.exit(1); }
  // If itch.io cut this run short, keep the games from last time that it didn't get to, so the store doesn't shrink.
  // A game drops out once no run has seen it for KEEP_DAYS (it went paid, was removed, or fell out of every list).
  let old = null; try { old = JSON.parse(fs.readFileSync("out/catalog.json", "utf8")); } catch (e) { }
  const fresh = items.length, KEEP = +(process.env.KEEP_DAYS || 4) * 86400000;
  if (old && old.items) old.items.forEach(o => {
    if (items.length >= TARGET) return;
    const k = urlKey(o.url || ""); if (!k || idx[k]) return;
    const seen = o.seen || old.t || 0; if (T0 - seen > KEEP) return;
    const lf = {}; for (const x in o.lf || {}) lf[x] = (o.lf[x] % 100000) + 100000;   // after this run's games in each list
    const it = Object.assign({}, o, { lf, seen }); items.push(it); idx[k] = it;
  });
  if (items.length > fresh) console.log("kept " + (items.length - fresh) + " games from the last list that this run didn't reach");
  // Every launcher switches to a new list at the same moment: it goes live 30 minutes after it's built.
  // Until then launchers keep showing the previous list, which stays published as catalog-prev.json.
  const t = Date.now(), out = { v: 2, t, liveAt: t + 30 * 60000, n: items.length, items };
  fs.mkdirSync("out", { recursive: true });
  let prev = null;
  try { prev = JSON.parse(fs.readFileSync("out/catalog.json", "utf8")); } catch (e) { }   // the list published last time
  if (prev && prev.items) {
    if (!prev.liveAt) prev.liveAt = prev.t;
    fs.writeFileSync("out/catalog-prev.json", JSON.stringify(prev));
  }
  fs.writeFileSync("out/catalog.json", JSON.stringify(out));
  const ver = { cur: { t, liveAt: out.liveAt, n: items.length }, prev: prev && prev.items ? { t: prev.t, liveAt: prev.liveAt, n: prev.items.length } : null };
  fs.writeFileSync("out/catalog-version.json", JSON.stringify(ver));
  console.log("wrote " + items.length + " games, " + Math.round(fs.statSync("out/catalog.json").size / 1024) + " KB, live at " + new Date(out.liveAt).toISOString());
})();

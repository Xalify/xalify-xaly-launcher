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
async function get(url) {
  for (let t = 0; t < 3; t++) {
    try { const r = await fetch(url, { headers: { "User-Agent": UA } }); if (r.ok) return await r.text(); if (r.status === 404) return ""; }
    catch (e) { }
    await sleep(2000 * (t + 1));
  }
  return "";
}
async function page(url, n) {
  const base = url.split("?")[0];
  let c = parseRss(await get(base + ".xml" + (n > 1 ? "?page=" + n : "")));
  if (c.length) return c;
  const t = await get(base + "?page=" + n + "&format=json");
  let h = t; try { const j = JSON.parse(t); if (j && j.content !== undefined) h = j.content; } catch (e) { }
  return parseCells(h);
}
(async () => {
  const items = [], idx = {}, pg = {}, done = {};
  let round = 0;
  while (items.length < TARGET && LIVEF.some(f => !done[f[0]])) {
    round++;
    for (const f of LIVEF) {
      if (done[f[0]] || items.length >= TARGET) continue;
      const n = (pg[f[0]] || 0) + 1; pg[f[0]] = n;
      const cells = await page(f[2], n);
      if (!cells.length || n >= MAX_PAGES) done[f[0]] = 1;
      let added = 0;
      cells.forEach((c, i) => {
        if (c.paid || !c.img || !c.name) return;
        const k = urlKey(c.url); let it = idx[k];
        if (!it) {
          const fr = frGuess(c.name + " " + c.text + " " + f[0]);
          it = { id: "x" + (c.gid || hash(k).toString(36)), fr, genre: c.genre || LGEN[f[0]] || "", name: c.name.substr(0, 90), tagline: c.text.substr(0, 160), url: c.url, image: c.img, devs: c.author, lf: {} };
          items.push(it); idx[k] = it; added++;
        }
        if (!it.genre && LGEN[f[0]]) it.genre = LGEN[f[0]];
        const rank = (n - 1) * 1000 + i;
        if (it.lf[f[0]] === undefined || rank < it.lf[f[0]]) it.lf[f[0]] = rank;
      });
      console.log(`round ${round} ${f[0]} page ${n}: ${cells.length} games, ${added} new, total ${items.length}`);
      await sleep(350);
    }
  }
  if (items.length < 500) { console.error("Too few games (" + items.length + "), keeping the old catalog."); process.exit(1); }
  const out = { v: 2, t: Date.now(), n: items.length, items };
  fs.mkdirSync("out", { recursive: true });
  fs.writeFileSync("out/catalog.json", JSON.stringify(out));
  console.log("wrote " + items.length + " games, " + Math.round(fs.statSync("out/catalog.json").size / 1024) + " KB");
})();

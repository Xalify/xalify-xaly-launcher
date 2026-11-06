// Runs every test suite against the current index.html.
//   node tests/run.js          logic tests (fast, no browser needed)
//   node tests/run.js --ui     also opens the launcher page in Chromium (needs: npm i playwright && npx playwright install chromium)
// Exits with 1 if anything fails, so GitHub stops a broken version from being released.
const fs = require("fs"), path = require("path"), cp = require("child_process"), https = require("https");
const root = path.join(__dirname, ".."), work = path.join(__dirname, ".work"), withUi = process.argv.includes("--ui");

function getCatalog(cb) {
  const out = path.join(work, "catalog.json");
  const local = path.join(__dirname, "fixtures", "catalog.json");
  if (fs.existsSync(local)) { fs.copyFileSync(local, out); return cb(true); }
  try {   // the shared store list GitHub publishes on the "catalog" branch
    const t = cp.execSync("git show origin/catalog:catalog.json", { cwd: root, maxBuffer: 64 << 20, stdio: ["ignore", "pipe", "ignore"] });
    if (t.length > 1000) { fs.writeFileSync(out, t); return cb(true); }
  } catch (e) { }
  https.get("https://raw.githubusercontent.com/Xalify/xalify-xaly-launcher/catalog/catalog.json", r => {
    if (r.statusCode !== 200) return cb(false);
    const f = fs.createWriteStream(out); r.pipe(f); f.on("finish", () => f.close(() => cb(true)));
  }).on("error", () => cb(false));
}

fs.rmSync(work, { recursive: true, force: true }); fs.mkdirSync(work, { recursive: true });
const html = fs.readFileSync(path.join(root, "index.html"), "utf8").replace(/\r\n/g, "\n");   // Windows checkouts use CRLF
fs.writeFileSync(path.join(work, "index.html"), html);
const i = html.indexOf("<script>\n") + 9, j = html.lastIndexOf("</script>");
fs.writeFileSync(path.join(work, "app.js"), html.slice(i, j));
for (const f of fs.readdirSync(__dirname)) if (/\.js$/.test(f) && f !== "run.js") fs.writeFileSync(path.join(work, f), fs.readFileSync(path.join(__dirname, f), "utf8").replace(/\r\n/g, "\n"));

getCatalog(ok => {
  if (!ok) console.log("note: couldn't get the shared store list; tests that use it may fail");
  const suites = fs.readdirSync(work).filter(f => /\.test\.js$/.test(f) && (withUi || f !== "ui.test.js")).sort();
  let failed = 0; const t0 = Date.now();
  for (const s of suites) {
    const r = cp.spawnSync(process.execPath, [s], { cwd: work, encoding: "utf8", timeout: 240000, maxBuffer: 32 << 20 });
    const out = (r.stdout || "") + (r.stderr || "");
    const checks = (out.match(/^ok  :/gm) || []).length, bad = out.split("\n").filter(l => /^(FAIL:|EXC |PAGEERR)/.test(l));
    const pass = r.status === 0 && !bad.length && !/\bFAILED\b/.test(out);
    console.log((pass ? "PASS " : "FAIL ") + s.padEnd(22) + checks + " checks");
    if (!pass) { failed++; bad.slice(0, 20).forEach(l => console.log("     " + l)); if (r.error) console.log("     " + r.error.message); if (!bad.length) console.log(out.split("\n").slice(-15).map(l => "     " + l).join("\n")); }
  }
  console.log((failed ? failed + " suite(s) FAILED" : "ALL TESTS PASSED") + " in " + Math.round((Date.now() - t0) / 1000) + " s");
  process.exit(failed ? 1 : 0);
});

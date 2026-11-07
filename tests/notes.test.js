// Every version shows "What's new" after updating, so the newest patch notes must be for the version being released.
const fs = require("fs"), html = fs.readFileSync("index.html", "utf8");
let fails = 0; const ok = (c, m) => { if (!c) { fails++; console.log("FAIL:", m); } else console.log("ok  :", m); };
const ver = (html.match(/var VER="([^"]+)"/) || [])[1], first = (html.match(/var NOTES=\[\s*\{v:"([^"]+)"/) || [])[1];
ok(ver && first === ver, "the newest patch notes are for this version (VER " + ver + ", notes " + first + ")");
const all = [...html.matchAll(/^\s*\{v:"([^"]+)",d:"[^"]*",t:\[(.*)\]\},?\s*$/gm)];
ok(all.length > 5 && all.every(m => m[2].length > 10), "every version has written notes (" + all.length + " versions)");
console.log(fails ? "FAILED " + fails : "ALL PASSED"); process.exit(fails ? 1 : 0);

# Tests

Automatic checks for Xaly Launcher. They load `index.html` with a fake Windows side, so they run on any
computer with Node.js, and on GitHub for every push. A release is only published when they pass.

```
node tests/run.js        # logic tests, about 20 seconds
node tests/run.js --ui   # also opens the page in Chromium (npm i playwright && npx playwright install chromium)
```

| Suite | What it checks |
|---|---|
| `core.test.js` | accounts and sign-in, the store, itch.io and GitHub installs, game pages, uninstalling, verification |
| `migration.test.js` | old saves and catalogs upgrade correctly |
| `friends.test.js` | two simulated players: requests, encrypted chat, read receipts, profiles, compare, challenges, alerts, message pop-ups, blocking, deleting messages |
| `features.test.js` | moving games, live chat, activity feed, size checks, Google Drive / MediaFire, browser hand-off, shared store list, speed limit and schedule, find games on PC, account backup, shortcuts, uninstaller, and more |
| `parsers.test.js` | reading itch.io pages, lists and feeds, update detection, Drive and MediaFire links, name matching |
| `ui.test.js` | the real page in Chromium: signs in and opens every screen, failing on any JavaScript error |
| `ui-accounts.test.js` | the sign-in screen and online accounts end to end against a fake Supabase (`ui-fakesb.js`): email codes, usernames, friends by username, new-PC and offline sign-in, password reset, opt-in, delete, and the off switch |

The native (C#) side is tested on Windows by `--selftest` in `.github/workflows/check.yml`.

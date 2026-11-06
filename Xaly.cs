// Xaly Launcher - native host. C# 5 compatible (builds with the csc.exe that ships with Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
#if WEBVIEW2
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
#endif

namespace Xaly
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    public class Bridge
    {
        MainForm f;
        Dictionary<string, Process> procs = new Dictionary<string, Process>();
        Dictionary<string, WebClient> dls = new Dictionary<string, WebClient>();
        public Bridge(MainForm form) { f = form; }

        static string SafeName(string n)
        {
            if (n == null) n = "x";
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return n.Length == 0 ? "x" : n;
        }

        // ---- storage
        public string Data(string name)
        {
            try { string p = Path.Combine(f.DataDir, SafeName(name)); return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : ""; }
            catch (Exception) { return ""; }
        }
        public string Save(string name, string text)
        {
            try
            {
                string p = Path.Combine(f.DataDir, SafeName(name)); string tmp = p + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string Legacy()
        {
            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string[] names = new string[] { "XalyLauncher", "Launchpad" };
                foreach (string n in names)
                {
                    string p = Path.Combine(Path.Combine(root, n), "library.json");
                    if (File.Exists(p)) return File.ReadAllText(p);
                }
                return "";
            }
            catch (Exception) { return ""; }
        }
        public string Info() { return Environment.UserName; }
        public string Paths() { return f.DownloadsDir + "|" + f.DataDir; }
        // ---- cache: game icons + unfinished (.part) downloads
        List<string> CacheFiles()
        {
            List<string> l = new List<string>();
            try { string d = Path.Combine(f.DataDir, "icons"); if (Directory.Exists(d)) l.AddRange(Directory.GetFiles(d, "*", SearchOption.AllDirectories)); } catch (Exception) { }
            try { if (Directory.Exists(f.DownloadsDir)) l.AddRange(Directory.GetFiles(f.DownloadsDir, "*.part", SearchOption.TopDirectoryOnly)); } catch (Exception) { }
            return l;
        }
        public string CacheSize()
        {
            long n = 0;
            foreach (string p in CacheFiles()) { try { n += new FileInfo(p).Length; } catch (Exception) { } }
            return n.ToString();
        }
        public string ClearCache()
        {
            long n = 0;
            foreach (string p in CacheFiles())
            {
                if (dls.Count > 0 && p.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue; // a download is running
                try { long s = new FileInfo(p).Length; File.Delete(p); n += s; } catch (Exception) { }
            }
            return n.ToString();
        }
        public string Restart()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & start \"\" \"" + Application.ExecutablePath + "\"");
                psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
                f.RealExit();
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        // deletes an unpacked game folder, only if it lives inside <downloads>\Games
        // a game folder Xaly may touch: <Downloads>\Xaly Launcher\Games\<game> or <drive>:\Xaly Games\<game>
        static bool UnderGameRoot(MainForm mf, string path)
        {
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\') + "\\";
                string r1 = Path.GetFullPath(Path.Combine(mf.DownloadsDir, "Games")).TrimEnd('\\') + "\\";
                if (full.StartsWith(r1, StringComparison.OrdinalIgnoreCase) && full.Length > r1.Length) return true;
                return System.Text.RegularExpressions.Regex.IsMatch(full, @"^[A-Za-z]:\\Xaly Games\\[^\\]+\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch (Exception) { return false; }
        }
        public string DeleteGame(string path)
        {
            try
            {
                if (!UnderGameRoot(f, path)) return "ERR:Refusing to delete outside the Xaly games folders.";
                if (Directory.Exists(path)) Directory.Delete(path, true);
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        static long DirSize(string p)
        {
            long n = 0;
            try
            {
                DirectoryInfo di = new DirectoryInfo(p);
                foreach (FileInfo fi in di.GetFiles()) { try { n += fi.Length; } catch (Exception) { } }
                foreach (DirectoryInfo sd in di.GetDirectories())
                {
                    if ((sd.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    n += DirSize(sd.FullName);
                }
            }
            catch (Exception) { }
            return n;
        }
        // measures Xaly's own folders on a background thread, then pushes the result to the page (onScan)
        public string Scan()
        {
            string dl = f.DownloadsDir, dd = f.DataDir;
            Thread t = new Thread(delegate()
            {
                long games = 0, all = 0, data = 0, icons = 0, parts = 0;
                StringBuilder sb = new StringBuilder();
                try
                {
                    string gdir = Path.Combine(dl, "Games");
                    if (Directory.Exists(gdir))
                        foreach (string d in Directory.GetDirectories(gdir))
                        {
                            long s = DirSize(d); games += s;
                            sb.Append(Path.GetFileName(d)).Append("|").Append(s.ToString()).Append("|").Append(d).Append("\n");
                        }
                    if (Directory.Exists(dl))
                    {
                        all = DirSize(dl);
                        foreach (string p in Directory.GetFiles(dl, "*.part", SearchOption.TopDirectoryOnly)) { try { parts += new FileInfo(p).Length; } catch (Exception) { } }
                    }
                    data = DirSize(dd);
                    icons = DirSize(Path.Combine(dd, "icons"));
                }
                catch (Exception) { }
                long dlOnly = Math.Max(0, all - games - parts), cache = icons + parts, dataOnly = Math.Max(0, data - icons);
                string payload = dlOnly.ToString() + "|" + games.ToString() + "|" + cache.ToString() + "|" + dataOnly.ToString() + "\n" + sb.ToString();
                try { f.BeginInvoke(new MethodInvoker(delegate { f.Js("onScan", payload); })); } catch (Exception) { }
            });
            t.IsBackground = true; t.Start();
            return "ok";
        }
        public string SetDownloads(string dir)
        {
            try { if (dir != null && dir.Length > 0) { Directory.CreateDirectory(dir); f.DownloadsDir = dir; } return "ok"; }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        // every drive on this PC: one line each, fields split by |  (root|label|type|total|free)
        public string Drives()
        {
            StringBuilder sb = new StringBuilder();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    string label = d.VolumeLabel;
                    sb.Append(d.Name).Append("|").Append(label == null ? "" : label.Replace("|", " ")).Append("|")
                      .Append(d.DriveType.ToString()).Append("|").Append(d.TotalSize.ToString()).Append("|")
                      .Append(d.AvailableFreeSpace.ToString()).Append("\n");
                }
                catch (Exception) { }
            }
            return sb.ToString();
        }

        // ---- launching games
        public string Launch(string id, string path, string args)
        {
            try
            {
                bool uri = path.IndexOf("://") > 0;
                ProcessStartInfo psi = new ProcessStartInfo(path);
                psi.UseShellExecute = true;
                if (!uri)
                {
                    if (args != null && args.Length > 0) psi.Arguments = args;
                    if (File.Exists(path)) psi.WorkingDirectory = Path.GetDirectoryName(path);
                }
                Process p = Process.Start(psi);
                bool track = !uri && p != null && id.Length > 0 && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                if (!track) return "ok-untracked";
                procs[id] = p;
                p.EnableRaisingEvents = true;
                p.Exited += delegate(object s, EventArgs e)
                {
                    f.BeginInvoke(new MethodInvoker(delegate { procs.Remove(id); f.Js("onExit", id); }));
                };
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string Stop(string id)
        {
            Process p;
            if (procs.TryGetValue(id, out p)) { try { p.Kill(); } catch (Exception) { } }
            return "ok";
        }
        public string Open(string target)
        {
            try { Process.Start(target); return "ok"; } catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ---- dialogs
        public string Pick(string filter)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = filter; d.CheckFileExists = true;
                return d.ShowDialog(f) == DialogResult.OK ? d.FileName : "";
            }
        }
        // square-crops and shrinks a picture, returned as a data: URL for profile pictures
        public string ImageData(string path, string size, string quality)
        {
            try
            {
                int sz = 128, q = 85; int.TryParse(size, out sz); int.TryParse(quality, out q);
                if (sz < 24 || sz > 512) sz = 128; if (q < 30 || q > 100) q = 85;
                using (Image src = Image.FromFile(path))
                {
                    int side = Math.Min(src.Width, src.Height);
                    Rectangle crop = new Rectangle((src.Width - side) / 2, (src.Height - side) / 2, side, side);
                    using (Bitmap bmp = new Bitmap(sz, sz))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.Clear(Color.FromArgb(20, 25, 37));
                            g.DrawImage(src, new Rectangle(0, 0, sz, sz), crop, GraphicsUnit.Pixel);
                        }
                        ImageCodecInfo jpg = null;
                        foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") jpg = c;
                        using (MemoryStream ms = new MemoryStream())
                        {
                            if (jpg != null)
                            {
                                using (EncoderParameters ep = new EncoderParameters(1))
                                {
                                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)q);
                                    bmp.Save(ms, jpg, ep);
                                }
                            }
                            else bmp.Save(ms, ImageFormat.Jpeg);
                            return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
                        }
                    }
                }
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        // ---- downloads handed to the browser (for Google Drive limits and MediaFire blocks): spot the finished file
        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);
        static string UserDownloads()
        {
            try
            {
                IntPtr p;
                if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out p) == 0)
                { string r = Marshal.PtrToStringUni(p); Marshal.FreeCoTaskMem(p); if (!string.IsNullOrEmpty(r)) return r; }
            }
            catch (Exception) { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
        // returns "busy|folder|newest finished file since then|its size"
        public string FindDownload(string sinceMs)
        {
            try
            {
                DateTime t0 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(double.Parse(sinceMs, System.Globalization.CultureInfo.InvariantCulture));
                string dir = UserDownloads(), best = ""; DateTime bt = DateTime.MinValue; long bsz = 0; bool busy = false;
                if (Directory.Exists(dir))
                    foreach (string p in Directory.GetFiles(dir))
                    {
                        FileInfo fi = new FileInfo(p);
                        DateTime w = fi.LastWriteTimeUtc > fi.CreationTimeUtc ? fi.LastWriteTimeUtc : fi.CreationTimeUtc;
                        if (w < t0) continue;
                        string e = fi.Extension.ToLowerInvariant();
                        if (e == ".crdownload" || e == ".part" || e == ".partial" || e == ".tmp" || e == ".download" || e == ".opdownload") { busy = true; continue; }
                        if (e != ".zip" && e != ".7z" && e != ".rar" && e != ".exe") continue;
                        if (w > bt) { bt = w; best = p; bsz = fi.Length; }
                    }
                return (busy ? "1" : "0") + "|" + dir + "|" + best + "|" + bsz;
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string PickFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                return d.ShowDialog(f) == DialogResult.OK ? d.SelectedPath : "";
            }
        }

        // ---- downloads (progress is pushed to the page)
        public string Download(string id, string url, string name)
        {
            try
            {
                Directory.CreateDirectory(f.DownloadsDir);
                string dest = Path.Combine(f.DownloadsDir, SafeName(name));
                string part = dest + ".part";
                if (File.Exists(part)) File.Delete(part);
                WebClient wc = new WebClient();
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 XalyLauncher";
                long last = 0;
                wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                {
                    long t = Environment.TickCount;
                    if (t - last < 250) return;
                    last = t;
                    f.Js("onDl", id, "p", e.BytesReceived.ToString(), e.TotalBytesToReceive.ToString());
                };
                wc.DownloadFileCompleted += delegate(object s, System.ComponentModel.AsyncCompletedEventArgs e)
                {
                    dls.Remove(id);
                    try
                    {
                        if (e.Cancelled) { if (File.Exists(part)) File.Delete(part); f.Js("onDl", id, "cancel", "", ""); return; }
                        if (e.Error != null) { if (File.Exists(part)) File.Delete(part); f.Js("onDl", id, "err", e.Error.Message, ""); return; }
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(part, dest);
                        f.Js("onDl", id, "done", dest, "");
                    }
                    catch (Exception ex2) { f.Js("onDl", id, "err", ex2.Message, ""); }
                };
                dls[id] = wc;
                wc.DownloadFileAsync(new Uri(url), part);
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        // ---- resumable downloads: keep a .part file, continue it with an HTTP Range request
        class DlJob { public volatile int stop; public HttpWebRequest rq; }
        static long speedLimit = 0;   // bytes per second, 0 = no limit
        public string SetSpeedLimit(string bps) { long v; speedLimit = long.TryParse(bps, out v) && v > 0 ? Math.Max(16384, v) : 0; return "ok"; }
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        // keeps the PC from going to sleep while downloads run (for overnight downloads); the screen may still turn off
        public string KeepAwake(string on)
        {
            try { SetThreadExecutionState(on == "1" ? 0x80000001u : 0x80000000u); return "ok"; }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        Dictionary<string, DlJob> jobs2 = new Dictionary<string, DlJob>();
        public string DownloadEx(string id, string url, string name, string resume)
        {
            try
            {
                Directory.CreateDirectory(f.DownloadsDir);
                string dest = Path.Combine(f.DownloadsDir, SafeName(name));
                string part = dest + ".part";
                if (resume != "1" && File.Exists(part)) File.Delete(part);
                DlJob job = new DlJob();
                lock (jobs2) { DlJob old; if (jobs2.TryGetValue(id, out old)) return "ERR:This download is already running."; jobs2[id] = job; }
                ThreadPool.QueueUserWorkItem(delegate(object o) { RunDl(id, url, dest, part, job); });
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        void RunDl(string id, string url, string dest, string part, DlJob job)
        {
            string result = "err", detail = "";
            try
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; } catch (Exception) { }
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                using (HttpWebResponse rs = OpenDl(url, have, job))
                {
                    long total;
                    if (have > 0 && rs.StatusCode == HttpStatusCode.PartialContent) total = rs.ContentLength >= 0 ? have + rs.ContentLength : -1;
                    else { have = 0; total = rs.ContentLength; }   // the server ignored the range: start over
                    using (Stream inp = rs.GetResponseStream())
                    using (FileStream fs = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        byte[] buf = new byte[131072]; int n; long got = have; int last = 0;
                        f.Js("onDl", id, "p", got.ToString(), total.ToString());
                        int winT = Environment.TickCount; long winB = 0; bool limited = id != "upd";   // the launcher's own update is never slowed down
                        while ((n = inp.Read(buf, 0, limited && speedLimit > 0 ? (int)Math.Max(4096, Math.Min(buf.Length, speedLimit / 8)) : buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n); got += n;
                            if (job.stop != 0) break;
                            int t = Environment.TickCount;
                            if (t - last > 250) { last = t; f.Js("onDl", id, "p", got.ToString(), total.ToString()); }
                            long lim = limited ? speedLimit : 0;
                            if (lim > 0)
                            {
                                // speed limit: sleep until this window's bytes fit the cap; a new window every 2 s follows changes quickly
                                winB += n;
                                if (t - winT > 2000) { winT = t; winB = n; }
                                long due = winB * 1000 / lim - (Environment.TickCount - winT);
                                while (due > 0 && job.stop == 0) { int z = (int)Math.Min(due, 200); Thread.Sleep(z); due -= z; }
                            }
                        }
                        f.Js("onDl", id, "p", got.ToString(), total.ToString());
                        if (job.stop == 0 && total > 0 && got < total) throw new IOException("The connection dropped before the download finished.");
                    }
                }
                if (job.stop == 0)
                {
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(part, dest);
                    result = "done"; detail = dest;
                }
            }
            catch (WebException we)
            {
                HttpWebResponse r = we.Response as HttpWebResponse;
                if (r != null && (int)r.StatusCode == 416) { try { File.Delete(part); } catch (Exception) { } }
                detail = r != null ? "HTTP " + (int)r.StatusCode + " " + r.StatusDescription : we.Message;
            }
            catch (Exception ex) { detail = ex.Message; }
            if (job.stop == 1) { result = "paused"; detail = ""; }
            else if (job.stop == 2) { try { File.Delete(part); } catch (Exception) { } result = "cancel"; detail = ""; }
            lock (jobs2) { jobs2.Remove(id); }
            f.Js("onDl", id, result, detail, "");
        }
        // opens a download; a web page instead of a file is an error, except Google Drive's
        // "can't scan this file for viruses" page, whose form is followed to the real file
        static HttpWebResponse OpenDl(string url, long have, DlJob job)
        {
            CookieContainer jar = new CookieContainer();
            for (int attempt = 0; ; attempt++)
            {
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
                rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
                rq.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"; rq.Headers["Accept-Language"] = "en-US,en;q=0.9";
                rq.Timeout = 30000; rq.ReadWriteTimeout = 30000; rq.AllowAutoRedirect = true; rq.CookieContainer = jar;
                if (have > 0) rq.AddRange(have);
                job.rq = rq;
                if (job.stop != 0) throw new OperationCanceledException();
                HttpWebResponse rs = (HttpWebResponse)rq.GetResponse();
                string ct = rs.ContentType ?? "";
                if (ct.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) < 0) return rs;
                string host = rs.ResponseUri.Host.ToLowerInvariant(), page;
                using (rs)
                using (StreamReader sr = new StreamReader(rs.GetResponseStream()))
                { char[] b = new char[400000]; int n = sr.ReadBlock(b, 0, b.Length); page = new string(b, 0, n); }
                bool google = host.EndsWith("google.com") || host.EndsWith("googleusercontent.com");
                if (host.EndsWith("mediafire.com"))
                {
                    // MediaFire: the file page holds the real link in its download button, sometimes base64-scrambled
                    System.Text.RegularExpressions.Match dm = System.Text.RegularExpressions.Regex.Match(page, "href=\"(https?://download\\d+\\.mediafire\\.com/[^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    string nx = dm.Success ? WebUtility.HtmlDecode(dm.Groups[1].Value) : null;
                    if (nx == null)
                    {
                        System.Text.RegularExpressions.Match sm = System.Text.RegularExpressions.Regex.Match(page, "data-scrambled-url=\"([^\"]+)\"");
                        if (sm.Success) { try { nx = Encoding.UTF8.GetString(Convert.FromBase64String(sm.Groups[1].Value)); } catch (Exception) { nx = null; } }
                    }
                    if (nx != null && attempt < 2 && nx.StartsWith("http")) { url = nx; continue; }
                    if (page.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0 && url.IndexOf("/folder/", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new IOException("This MediaFire link is a folder. Xaly can only download single files.");
                    throw new IOException("MediaFire didn't give a download link. The file may have been removed.");
                }
                if (!google) throw new IOException("The link opened a web page instead of a game file.");
                System.Text.RegularExpressions.Match fm = System.Text.RegularExpressions.Regex.Match(page, "<form[^>]*download-form[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fm.Success && attempt < 2)
                {
                    System.Text.RegularExpressions.Match am = System.Text.RegularExpressions.Regex.Match(fm.Value, "action=\"([^\"]+)\"");
                    string act = am.Success ? WebUtility.HtmlDecode(am.Groups[1].Value) : "https://drive.usercontent.google.com/download";
                    StringBuilder q = new StringBuilder();
                    foreach (System.Text.RegularExpressions.Match im in System.Text.RegularExpressions.Regex.Matches(page, "<input[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        if (im.Value.IndexOf("hidden", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        System.Text.RegularExpressions.Match nm = System.Text.RegularExpressions.Regex.Match(im.Value, "name=\"([^\"]*)\""), vm = System.Text.RegularExpressions.Regex.Match(im.Value, "value=\"([^\"]*)\"");
                        if (!nm.Success) continue;
                        q.Append(q.Length == 0 ? "?" : "&").Append(Uri.EscapeDataString(WebUtility.HtmlDecode(nm.Groups[1].Value))).Append("=").Append(Uri.EscapeDataString(vm.Success ? WebUtility.HtmlDecode(vm.Groups[1].Value) : ""));
                    }
                    url = act + q.ToString();
                    continue;
                }
                // newer pages: a plain link to the confirmed download instead of a form
                System.Text.RegularExpressions.Match lm = System.Text.RegularExpressions.Regex.Match(page, "(https://drive\\.usercontent\\.google\\.com/download\\?[^\"'<>\\s]*confirm[^\"'<>\\s]*|/uc\\?export=download[^\"'<>\\s]*confirm=[^\"'<>\\s]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (lm.Success && attempt < 2)
                {
                    string nx = WebUtility.HtmlDecode(lm.Groups[1].Value).Replace("\\u003d", "=").Replace("\\u0026", "&");
                    if (nx.StartsWith("/")) nx = "https://drive.google.com" + nx;
                    if (!string.Equals(nx, url, StringComparison.OrdinalIgnoreCase)) { url = nx; continue; }
                }
                System.Text.RegularExpressions.Match tm = System.Text.RegularExpressions.Regex.Match(page, "<title>([^<]*)</title>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                string title = tm.Success ? WebUtility.HtmlDecode(tm.Groups[1].Value).Trim() : "";
                string text = System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(page, "<(script|style)[\\s\\S]*?</\\1>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase), "<[^>]+>", " "), "\\s+", " ").Trim();
                text = WebUtility.HtmlDecode(text);
                if (text.Length > 160) text = text.Substring(0, 160);
                if (title.IndexOf("Quota exceeded", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Too many users have viewed or downloaded", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new IOException("Google Drive says too many people downloaded this file recently. Try again later.");
                if (text.IndexOf("unusual traffic", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new IOException("Google Drive is blocking automatic downloads from this connection for now. Try again later.");
                throw new IOException("Google Drive didn't send the file. Google said: " + (title.Length > 0 ? title + ". " : "") + text);
            }
        }
        public string DownloadPause(string id)
        {
            DlJob j; lock (jobs2) { if (!jobs2.TryGetValue(id, out j)) return "ok"; j.stop = 1; }
            try { if (j.rq != null) j.rq.Abort(); } catch (Exception) { }
            return "ok";
        }
        public string DownloadCancel(string id, string name)
        {
            DlJob j; bool active;
            lock (jobs2) { active = jobs2.TryGetValue(id, out j); if (active) j.stop = 2; }
            if (active) { try { if (j.rq != null) j.rq.Abort(); } catch (Exception) { } }
            else if (!string.IsNullOrEmpty(name)) { try { File.Delete(Path.Combine(f.DownloadsDir, SafeName(name)) + ".part"); } catch (Exception) { } }
            return "ok";
        }
        public string FileSizeOf(string name)
        {
            try { string pth = Path.Combine(f.DownloadsDir, SafeName(name)); return File.Exists(pth) ? new FileInfo(pth).Length.ToString() : "0"; }
            catch (Exception) { return "0"; }
        }
        public string PartSize(string name)
        {
            try { string pth = Path.Combine(f.DownloadsDir, SafeName(name)) + ".part"; return File.Exists(pth) ? new FileInfo(pth).Length.ToString() : "0"; }
            catch (Exception) { return "0"; }
        }

        public string Cancel(string id)
        {
            WebClient wc;
            if (dls.TryGetValue(id, out wc)) wc.CancelAsync();
            return "ok";
        }
        public string Extract(string zip, string name)
        {
            try
            {
                string dir = Path.Combine(Path.Combine(f.DownloadsDir, "Games"), SafeName(name));
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                Unpack(zip, dir);
                string best = null; long size = -1;
                foreach (string p in Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories))
                {
                    string n = Path.GetFileName(p).ToLower();
                    if (n.IndexOf("unins") >= 0 || n.IndexOf("crash") >= 0 || n.IndexOf("redist") >= 0 || n.IndexOf("dxsetup") >= 0 || n.IndexOf("helper") >= 0) continue;
                    long s = new FileInfo(p).Length;
                    if (s > size) { size = s; best = p; }
                }
                try { File.Delete(zip); } catch (Exception) { }   // the archive is not kept once unpacked
                return (best == null ? "" : best) + "|" + dir;
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        // ---- game updates: unpack over the installed copy, keeping files the update doesn't contain (like saves)
        public string ExtractUpdate(string zip, string dir, string oldExe)
        {
            try
            {
                if (!UnderGameRoot(f, dir)) return "ERR:Only games installed by Xaly can be updated.";
                string tmp = Path.Combine(Path.GetTempPath(), "XalyGameUpdate_" + DateTime.Now.Ticks);
                Directory.CreateDirectory(tmp);
                Unpack(zip, tmp);
                CopyTree(tmp, dir);
                try { Directory.Delete(tmp, true); } catch (Exception) { }
                try { File.Delete(zip); } catch (Exception) { }
                string exe = (!string.IsNullOrEmpty(oldExe) && File.Exists(oldExe)) ? oldExe : BestExe(dir);
                return (exe ?? "") + "|" + dir;
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string ReplaceFile(string src, string dest)
        {
            try
            {
                string root = Path.GetFullPath(f.DownloadsDir).TrimEnd('\\') + "\\";
                if (!Path.GetFullPath(dest).StartsWith(root, StringComparison.OrdinalIgnoreCase) && !UnderGameRoot(f, dest)) return "ERR:Only games downloaded by Xaly can be updated.";
                File.Copy(src, dest, true);
                try { File.Delete(src); } catch (Exception) { }
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        // a single-.exe game gets its own folder under Games, so it can be moved and uninstalled like any other game
        public string PlaceStandalone(string file, string game)
        {
            try
            {
                string full = Path.GetFullPath(file);
                string root = Path.GetFullPath(f.DownloadsDir).TrimEnd('\\');
                if (!string.Equals((Path.GetDirectoryName(full) ?? "").TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase)) return "ERR:Not a loose download.";
                if (!File.Exists(full) || !full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "ERR:File not found.";
                string games = Path.Combine(root, "Games"), nm = SafeName((game ?? "").Trim().TrimEnd('.'));
                if (nm == "x") nm = Path.GetFileNameWithoutExtension(full);
                string dir = Path.Combine(games, nm); int n = 2;
                while (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length > 0) { dir = Path.Combine(games, nm + " (" + n + ")"); n++; }
                Directory.CreateDirectory(dir);
                string to = Path.Combine(dir, Path.GetFileName(full));
                File.Move(full, to);
                return to;
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        static string Sniff(string p)
        {
            byte[] b = new byte[8]; int n;
            using (FileStream fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) n = fs.Read(b, 0, 8);
            if (n >= 2 && b[0] == 0x50 && b[1] == 0x4B) return "zip";
            if (n >= 4 && b[0] == 0x37 && b[1] == 0x7A && b[2] == 0xBC && b[3] == 0xAF) return "7z";
            if (n >= 4 && b[0] == 0x52 && b[1] == 0x61 && b[2] == 0x72 && b[3] == 0x21) return "rar";
            if (n >= 2 && b[0] == 0x4D && b[1] == 0x5A) return "exe";
            if (n >= 1 && (b[0] == 0x3C || (n >= 4 && b[0] == 0xEF && b[3] == 0x3C))) return "html";
            return "unknown";
        }
        public string SniffFile(string name)
        {
            try
            {
                string p = Path.Combine(f.DownloadsDir, SafeName(name));
                if (!File.Exists(p)) p += ".part";
                return File.Exists(p) ? Sniff(p) : "missing";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        static int RunTool(string exe, string args)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit(45 * 60 * 1000)) { try { p.Kill(); } catch (Exception) { } return -1; }
                return p.ExitCode;
            }
        }
        // zip with .NET; 7z and rar with 7-Zip if it is installed, otherwise Windows' own tar; a lone .exe is copied in
        static void Unpack(string arc, string dir)
        {
            string k = Sniff(arc);
            if (k == "zip") { ZipFile.ExtractToDirectory(arc, dir); return; }
            if (k == "html") throw new IOException("The download was a web page, not a game. The link may be broken or private.");
            if (k == "exe") { File.Copy(arc, Path.Combine(dir, Path.GetFileName(dir.TrimEnd('\\')) + ".exe"), true); return; }
            List<string> sevens = new List<string>();
            foreach (string pf in new string[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
                if (!string.IsNullOrEmpty(pf)) sevens.Add(Path.Combine(Path.Combine(pf, "7-Zip"), "7z.exe"));
            foreach (string z in sevens)
                if (File.Exists(z) && RunTool(z, "x -y \"-o" + dir + "\" \"" + arc + "\"") == 0) return;
            string tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
            if (File.Exists(tar) && RunTool(tar, "-xf \"" + arc + "\" -C \"" + dir + "\"") == 0 && Directory.GetFileSystemEntries(dir).Length > 0) return;
            throw new IOException("This game is packed as ." + k + ", which Windows can't open by itself. Install 7-Zip (free, from 7-zip.org), then install the game again.");
        }
        static void CopyTree(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string file in Directory.GetFiles(src))
            {
                string to = Path.Combine(dst, Path.GetFileName(file));
                if (File.Exists(to)) File.SetAttributes(to, FileAttributes.Normal);
                File.Copy(file, to, true);
            }
            foreach (string d in Directory.GetDirectories(src)) CopyTree(d, Path.Combine(dst, Path.GetFileName(d)));
        }
        static string BestExe(string dir)
        {
            string best = null; long size = -1;
            foreach (string p in Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories))
            {
                string n = Path.GetFileName(p).ToLower();
                if (n.IndexOf("unins") >= 0 || n.IndexOf("crash") >= 0 || n.IndexOf("redist") >= 0 || n.IndexOf("dxsetup") >= 0 || n.IndexOf("helper") >= 0) continue;
                long sz = new FileInfo(p).Length;
                if (sz > size) { size = sz; best = p; }
            }
            return best;
        }
        // ---- TV mode
        public string SetFullscreen(string on) { f.SetFullscreen(on == "1"); return "ok"; }
        public string PadNative(string on) { f.PadNative(on == "1"); return "ok"; }

        // ---- move a game folder to another drive (<drive>:\Xaly Games\<game>), with progress and cancel
        public string FreeSpace(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace.ToString(); }
            catch (Exception) { return "-1"; }
        }
        public string DirBytes(string path) { try { return Directory.Exists(path) ? DirSize(path).ToString() : "0"; } catch (Exception) { return "0"; } }
        public string MoveGame(string id, string src, string drive)
        {
            try
            {
                src = Path.GetFullPath(src).TrimEnd('\\');
                if (!UnderGameRoot(f, src) || !Directory.Exists(src)) return "ERR:Only games installed by Xaly can be moved.";
                string root = Path.GetPathRoot(drive);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return "ERR:That drive isn't available.";
                string dst = Path.Combine(Path.Combine(root, "Xaly Games"), Path.GetFileName(src));
                bool same = string.Equals(Path.GetPathRoot(src), root, StringComparison.OrdinalIgnoreCase);
                if (string.Equals(Path.GetFullPath(dst).TrimEnd('\\'), src, StringComparison.OrdinalIgnoreCase)) return "ERR:The game is already there.";
                if (Directory.Exists(dst)) return "ERR:" + dst + " already exists. Move or rename that folder first.";
                long size = DirSize(src);
                if (!same && new DriveInfo(root).AvailableFreeSpace < size + 64L * 1024 * 1024) return "ERR:There isn't enough free space on " + root.TrimEnd('\\') + ".";
                DlJob job = new DlJob();
                lock (jobs2) { if (jobs2.ContainsKey("mv" + id)) return "ERR:This game is already being moved."; jobs2["mv" + id] = job; }
                ThreadPool.QueueUserWorkItem(delegate(object o) { RunMove(id, src, dst, size, same, job); });
                return dst;
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string CancelMove(string id) { DlJob j; lock (jobs2) { if (jobs2.TryGetValue("mv" + id, out j)) j.stop = 2; } return "ok"; }
        void RunMove(string id, string src, string dst, long size, bool same, DlJob job)
        {
            string result = "err", detail = "";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (same) { Directory.Move(src, dst); result = "done"; detail = dst; }
                else
                {
                    long[] done = new long[] { 0 }; int[] last = new int[] { 0 };
                    CopyWithProgress(src, dst, done, size, job, id, last);
                    if (job.stop != 0) { try { Directory.Delete(dst, true); } catch (Exception) { } result = "cancel"; }
                    else
                    {
                        if (DirSize(dst) < size) throw new IOException("The copy is incomplete, so the original was kept.");
                        result = "done"; detail = dst;
                        try { Directory.Delete(src, true); } catch (Exception ex2) { detail = dst + "|" + ex2.Message; }
                    }
                }
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                if (!same) { try { if (Directory.Exists(dst)) Directory.Delete(dst, true); } catch (Exception) { } }
            }
            lock (jobs2) { jobs2.Remove("mv" + id); }
            f.Js("onMove", id, result, detail, "");
        }
        void CopyWithProgress(string s, string d, long[] done, long total, DlJob job, string id, int[] last)
        {
            Directory.CreateDirectory(d);
            byte[] buf = new byte[1048576];
            foreach (string file in Directory.GetFiles(s))
            {
                if (job.stop != 0) return;
                string to = Path.Combine(d, Path.GetFileName(file));
                using (FileStream fi = File.OpenRead(file))
                using (FileStream fo = new FileStream(to, FileMode.Create, FileAccess.Write))
                {
                    int n;
                    while ((n = fi.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (job.stop != 0) return;
                        fo.Write(buf, 0, n); done[0] += n;
                        int t = Environment.TickCount;
                        if (t - last[0] > 250) { last[0] = t; f.Js("onMove", id, "p", done[0].ToString(), total.ToString()); }
                    }
                }
                try { File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(file)); } catch (Exception) { }
            }
            foreach (string dir in Directory.GetDirectories(s)) { if (job.stop != 0) return; CopyWithProgress(dir, Path.Combine(d, Path.GetFileName(dir)), done, total, job, id, last); }
        }

        // ---- live friend relay: one long-lived ntfy.sh stream, so messages arrive within a second
        static int streamGen = 0;
        HttpWebRequest streamRq;
        public string RelayStream(string topics, string since)
        {
            if (!OkTopics(topics)) return "ERR:Bad topic.";
            int gen = Interlocked.Increment(ref streamGen);
            try { if (streamRq != null) streamRq.Abort(); } catch (Exception) { }
            Thread t = new Thread(delegate() { StreamLoop(gen, topics, since); });
            t.IsBackground = true; t.Start();
            return "ok";
        }
        public string RelayStreamStop() { Interlocked.Increment(ref streamGen); try { if (streamRq != null) streamRq.Abort(); } catch (Exception) { } return "ok"; }
        void StreamLoop(int gen, string topics, string since)
        {
            string sinceArg = since ?? ""; int fails = 0;
            while (gen == streamGen)
            {
                try
                {
                    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; } catch (Exception) { }
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://ntfy.sh/" + topics + "/json" + (sinceArg.Length > 0 ? "?since=" + Uri.EscapeDataString(sinceArg) : ""));
                    rq.UserAgent = "XalyLauncher"; rq.Timeout = 30000; rq.ReadWriteTimeout = 120000; rq.KeepAlive = true;
                    streamRq = rq;
                    using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                    {
                        fails = 0; f.Js("onRelayState", "1");
                        string line;
                        while (gen == streamGen && (line = sr.ReadLine()) != null)
                        {
                            if (line.IndexOf("\"event\":\"message\"") < 0) continue;
                            f.Js("onRelayMsg", line);
                            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(line, "\"time\":(\\d+)");
                            if (m.Success) { long tm; if (long.TryParse(m.Groups[1].Value, out tm)) sinceArg = (tm - 5).ToString(); }
                        }
                    }
                }
                catch (Exception) { }
                if (gen != streamGen) break;
                f.Js("onRelayState", "0");
                fails++;
                Thread.Sleep(Math.Min(30000, 1500 * fails));
            }
        }

        public string Http(string id, string url)
        {
            try
            {
                WebClient wc = new WebClient();
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 XalyLauncher";
                wc.Headers[HttpRequestHeader.Accept] = "text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.8,*/*;q=0.7";
                wc.Headers[HttpRequestHeader.AcceptLanguage] = "en-US,en;q=0.9";
                wc.DownloadStringCompleted += delegate(object s, DownloadStringCompletedEventArgs e)
                {
                    if (e.Cancelled || e.Error != null)
                    {
                        string code = "0";
                        WebException we = e.Error as WebException;
                        if (we != null && we.Response is HttpWebResponse) code = ((int)((HttpWebResponse)we.Response).StatusCode).ToString();
                        f.Js("onHttp", id, "0", e.Error != null ? e.Error.Message : "cancelled", code);
                    }
                    else f.Js("onHttp", id, "1", e.Result, "200");
                };
                wc.DownloadStringAsync(new Uri(url));
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ---- itch.io API (the key stays on the C# side; the page only sees the API's answers)
        // Public key from a throwaway itch.io account, so installs work out of the box.
        // Order: the player's own key (Settings > Downloads), then itchkey.txt next to the .exe, then this one.
        const string DefaultItchKey = "9cF1xD1Ssn1EMXAZFYWonRHPIE4ZNhfZcRWTDiPX";
        string ItchKey()
        {
            try
            {
                string p = Path.Combine(f.DataDir, "itch.key");
                if (File.Exists(p)) { string k = File.ReadAllText(p).Trim(); if (k.Length > 0) return k; }
                string b = Path.Combine(f.AppDir, "itchkey.txt");
                if (File.Exists(b)) { string k2 = File.ReadAllText(b).Trim(); if (k2.Length > 0) return k2; }
            }
            catch (Exception) { }
            return DefaultItchKey;
        }
        public string SetItchKey(string key)
        {
            try
            {
                string p = Path.Combine(f.DataDir, "itch.key");
                key = key == null ? "" : key.Trim();
                if (key.Length == 0) { if (File.Exists(p)) File.Delete(p); return "ok"; }
                File.WriteAllText(p, key, new UTF8Encoding(false));
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string ItchKeySource()
        {
            try
            {
                string p = Path.Combine(f.DataDir, "itch.key");
                if (File.Exists(p) && File.ReadAllText(p).Trim().Length > 0) return "custom";
                string b = Path.Combine(f.AppDir, "itchkey.txt");
            }
            catch (Exception) { }
            return "built-in";
        }
        internal static string JsonStr(string s)
        {
            if (s == null) s = "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '\u2028' || c == '\u2029' || c == '<') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
        // maps the page's short request names onto itch.io's real addresses (v2 = api.itch.io, v1 = itch.io/api/1)
        string ItchUrl(string path, string key)
        {
            if (path == null) return null;
            System.Text.RegularExpressions.Match m;
            if (path == "profile") return "https://api.itch.io/profile";
            m = System.Text.RegularExpressions.Regex.Match(path, @"^v2/games/(\d+)/uploads$");
            if (m.Success) return "https://api.itch.io/games/" + m.Groups[1].Value + "/uploads";
            m = System.Text.RegularExpressions.Regex.Match(path, @"^v2/uploads/(\d+)/download$");
            if (m.Success) return "https://api.itch.io/uploads/" + m.Groups[1].Value + "/download";
            m = System.Text.RegularExpressions.Regex.Match(path, @"^v1/game/(\d+)/uploads$");
            if (m.Success) return "https://itch.io/api/1/" + Uri.EscapeDataString(key) + "/game/" + m.Groups[1].Value + "/uploads";
            m = System.Text.RegularExpressions.Regex.Match(path, @"^v1/upload/(\d+)/download$");
            if (m.Success) return "https://itch.io/api/1/" + Uri.EscapeDataString(key) + "/upload/" + m.Groups[1].Value + "/download";
            return null;
        }
        // Answer arrives through onHttp(id, "1" or "0", body, status). A redirect is turned into {"url": "..."}.
        // Sends feedback text to the developer's inbox through the FormSubmit email relay (no password is stored in the app).
        // ---- crash reports: saved to %AppData%\XalyLauncher\crashes, emailed in the background, deleted once sent
        public string CrashLog(string kind, string text) { MainForm.LogCrash(kind, text); return "ok"; }
        static int crashSending = 0;
        public string SendCrashes(string ver)
        {
            if (Interlocked.Exchange(ref crashSending, 1) == 1) return "busy";
            string dir = Path.Combine(f.DataDir, "crashes");
            ThreadPool.QueueUserWorkItem(delegate(object st)
            {
                try
                {
                    if (!Directory.Exists(dir)) return;
                    string[] files = Directory.GetFiles(dir, "crash-*.txt");
                    Array.Sort(files);
                    int sent = 0;
                    foreach (string file in files)
                    {
                        if (sent >= 5) break;
                        string text; try { text = File.ReadAllText(file); } catch (Exception) { continue; }
                        string ce;
                        if (MailDev("Xaly crash report (" + ver + ")", text, out ce) || PostForm("Xaly crash report (" + ver + ")", text, ver, "")) { try { File.Delete(file); } catch (Exception) { } sent++; }
                        else break;   // offline or the inbox isn't activated yet: try again next start
                    }
                    // never let old reports pile up
                    files = Directory.GetFiles(dir, "crash-*.txt"); Array.Sort(files);
                    for (int i = 0; i < files.Length - 20; i++) { try { File.Delete(files[i]); } catch (Exception) { } }
                }
                catch (Exception) { }
                finally { Interlocked.Exchange(ref crashSending, 0); }
            });
            return "ok";
        }
        static bool PostForm(string subject, string text, string ver, string email)
        {
            try
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; } catch (Exception) { }
                string body = "{\"_subject\":" + JsonStr(subject) + ",\"_captcha\":\"false\",\"_template\":\"box\",\"name\":" + JsonStr("Xaly Launcher " + ver)
                    + (string.IsNullOrEmpty(email) ? "" : ",\"email\":" + JsonStr(email))
                    + ",\"message\":" + JsonStr(text) + ",\"windows\":" + JsonStr(Environment.OSVersion.VersionString) + "}";
                byte[] data = Encoding.UTF8.GetBytes(body);
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://formsubmit.co/ajax/xalify2004@gmail.com");
                rq.Method = "POST"; rq.ContentType = "application/json"; rq.Accept = "application/json";
                rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) XalyLauncher";
                rq.Referer = "https://xaly-launcher.app/feedback"; rq.Headers["Origin"] = "https://xaly-launcher.app";
                rq.Timeout = 25000; rq.ReadWriteTimeout = 25000; rq.ContentLength = data.Length;
                using (Stream os = rq.GetRequestStream()) os.Write(data, 0, data.Length);
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                {
                    string resp = sr.ReadToEnd().Replace(" ", "");
                    return resp.IndexOf("\"success\":\"true\"") >= 0 || resp.IndexOf("\"success\":true") >= 0;
                }
            }
            catch (Exception) { return false; }
        }

        // Emails a message to the developer through ntfy.sh's email forwarding: no account, no activation.
        // "Cache: no" means ntfy doesn't keep the message after delivering it.
        const string DevEmail = "xalify2004@gmail.com", FbTopic = "xaly-fb-7c3e9a51d2b84f60";
        static bool MailDev(string title, string text, out string err)
        {
            err = "";
            try
            {
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; } catch (Exception) { }
                byte[] data = Encoding.UTF8.GetBytes(text.Length > 3800 ? text.Substring(0, 3800) + "\n[cut short]" : text);
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://ntfy.sh/" + FbTopic);
                rq.Method = "POST"; rq.ContentType = "text/plain; charset=utf-8"; rq.UserAgent = "XalyLauncher";
                rq.Headers["Title"] = System.Text.RegularExpressions.Regex.Replace(title, "[^\\x20-\\x7E]", "");
                rq.Headers["Email"] = DevEmail; rq.Headers["Tags"] = "speech_balloon"; rq.Headers["Cache"] = "no";
                rq.Timeout = 20000; rq.ReadWriteTimeout = 20000; rq.ContentLength = data.Length;
                using (Stream os = rq.GetRequestStream()) os.Write(data, 0, data.Length);
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse()) return (int)rs.StatusCode == 200;
            }
            catch (WebException we)
            {
                HttpWebResponse r = we.Response as HttpWebResponse;
                string why = "";
                if (r != null) { try { using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) why = sr.ReadToEnd(); } catch (Exception) { } }
                err = r != null ? ((int)r.StatusCode == 429 ? "Too many messages were sent from this connection today. Try again tomorrow." : "HTTP " + (int)r.StatusCode + " " + (why.Length > 160 ? why.Substring(0, 160) : why)) : we.Message;
                return false;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }
        public string Feedback(string id, string text, string ver, string email)
        {
            // everything happens on a background thread so the window never freezes
            ThreadPool.QueueUserWorkItem(delegate(object st)
            {
                string okFlag = "0", msg = "";
                // 1st choice: straight to the developer's inbox through ntfy.sh
                string err1;
                string mail = text + "\n\n----\nFrom: Xaly Launcher " + ver + (string.IsNullOrEmpty(email) ? "" : "\nReply to: " + email) + "\nWindows: " + Environment.OSVersion.VersionString;
                if (MailDev("Xaly feedback (" + ver + ")", mail, out err1))
                {
                    try { f.BeginInvoke(new MethodInvoker(delegate { f.Js("onHttp", id, "1", "sent", "200"); })); } catch (Exception) { }
                    return;
                }
                // 2nd choice: the old FormSubmit route
                try
                {
                    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; } catch (Exception) { }
                    string body = "{\"_subject\":" + JsonStr("Xaly Launcher feedback (" + ver + ")") + ",\"_captcha\":\"false\",\"_template\":\"box\",\"name\":" + JsonStr("Xaly Launcher " + ver)
                        + (string.IsNullOrEmpty(email) ? "" : ",\"email\":" + JsonStr(email) + ",\"_replyto\":" + JsonStr(email))
                        + ",\"message\":" + JsonStr(text) + ",\"windows\":" + JsonStr(Environment.OSVersion.VersionString) + "}";
                    byte[] data = Encoding.UTF8.GetBytes(body);
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create("https://formsubmit.co/ajax/xalify2004@gmail.com");
                    rq.Method = "POST";
                    rq.ContentType = "application/json";
                    rq.Accept = "application/json";
                    rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36 XalyLauncher";
                    rq.Referer = "https://xaly-launcher.app/feedback";
                    rq.Headers["Origin"] = "https://xaly-launcher.app";
                    rq.Timeout = 25000; rq.ReadWriteTimeout = 25000;
                    rq.ContentLength = data.Length;
                    using (Stream os = rq.GetRequestStream()) os.Write(data, 0, data.Length);
                    HttpWebResponse rs = null;
                    try { rs = (HttpWebResponse)rq.GetResponse(); }
                    catch (WebException we) { rs = we.Response as HttpWebResponse; if (rs == null) throw; }
                    string resp; int code = (int)rs.StatusCode;
                    using (rs) using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) resp = sr.ReadToEnd();
                    bool good = resp.Replace(" ", "").IndexOf("\"success\":\"true\"") >= 0 || resp.Replace(" ", "").IndexOf("\"success\":true") >= 0;
                    okFlag = good ? "1" : "0";
                    int m = resp.IndexOf("\"message\"");
                    if (m >= 0) { int a = resp.IndexOf('"', resp.IndexOf(':', m) + 1); int b = a >= 0 ? resp.IndexOf('"', a + 1) : -1; if (a >= 0 && b > a) msg = resp.Substring(a + 1, b - a - 1); }
                    if (msg.Length == 0) msg = good ? "sent" : "HTTP " + code;
                }
                catch (Exception ex) { msg = ex.Message; }
                if (okFlag != "1") msg = "ntfy: " + err1 + " / formsubmit: " + msg;
                string o2 = okFlag, m2 = msg;
                try { f.BeginInvoke(new MethodInvoker(delegate { f.Js("onHttp", id, o2, m2, "200"); })); } catch (Exception) { }
            });
            return "ok";
        }
        public string ItchApi(string id, string path)
        {
            try
            {
                string key = ItchKey();
                string url = ItchUrl(path, key);
                if (url == null) return "ERR:Unsupported itch.io request.";
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
                rq.UserAgent = "Mozilla/5.0 XalyLauncher";
                rq.AllowAutoRedirect = false;
                rq.Timeout = 30000;
                if (url.StartsWith("https://api.itch.io/")) rq.Headers["Authorization"] = key;
                rq.BeginGetResponse(delegate(IAsyncResult ar)
                {
                    string okFlag = "0", body = "", status = "0";
                    try
                    {
                        HttpWebResponse rs = null;
                        try { rs = (HttpWebResponse)rq.EndGetResponse(ar); }
                        catch (WebException we)
                        {
                            rs = we.Response as HttpWebResponse;
                            if (rs == null) throw;
                        }
                        using (rs)
                        {
                            int code = (int)rs.StatusCode; status = code.ToString();
                            if (code >= 300 && code < 400)
                            {
                                body = "{\"url\":" + JsonStr(rs.Headers["Location"]) + "}";
                                okFlag = "1";
                            }
                            else
                            {
                                using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) { body = sr.ReadToEnd(); }
                                if (body.Length > 400000) body = body.Substring(0, 400000);
                                okFlag = (code >= 200 && code < 300) ? "1" : "0";
                            }
                        }
                    }
                    catch (Exception ex) { body = ex.Message; }
                    string b2 = body, o2 = okFlag, s2 = status;
                    try { f.BeginInvoke(new MethodInvoker(delegate { f.Js("onHttp", id, o2, b2, s2); })); } catch (Exception) { }
                }, null);
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ---- accounts: password hashing + removing an account's data file
        public string Hash(string salt, string text)
        {
            try
            {
                byte[] sb = Encoding.UTF8.GetBytes("xaly:" + salt);
                using (Rfc2898DeriveBytes kdf = new Rfc2898DeriveBytes(text == null ? "" : text, sb, 20000))
                {
                    byte[] h = kdf.GetBytes(32);
                    StringBuilder o = new StringBuilder();
                    foreach (byte b in h) o.Append(b.ToString("x2"));
                    return o.ToString();
                }
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string Delete(string name)
        {
            try
            {
                string p = Path.Combine(f.DataDir, SafeName(name));
                if (File.Exists(p)) File.Delete(p);
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ---- game details for the library: version, developer, dates, size on disk
        static long UnixMs(DateTime utc)
        {
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }
        public string FileMeta(string path)
        {
            try
            {
                if (path == null || !File.Exists(path)) return "";
                FileInfo fi = new FileInfo(path);
                string ver = "", co = "";
                try
                {
                    FileVersionInfo vi = FileVersionInfo.GetVersionInfo(path);
                    if (vi.FileVersion != null) ver = vi.FileVersion;
                    if (vi.CompanyName != null) co = vi.CompanyName;
                }
                catch (Exception) { }
                ver = ver.Replace("|", " ").Trim(); co = co.Replace("|", " ").Trim();
                return ver + "|" + co + "|" + UnixMs(fi.LastWriteTimeUtc) + "|" + UnixMs(fi.CreationTimeUtc);
            }
            catch (Exception) { return ""; }
        }
        static bool BroadFolder(string dir)
        {
            try
            {
                string d = Path.GetFullPath(dir).TrimEnd('\\');
                if (d.Length <= 3) return true;
                string[] bad = new string[] {
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
                foreach (string b in bad)
                    if (b != null && b.Length > 0 && string.Equals(d, b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch (Exception) { return true; }
        }
        static long TimedDirBytes(string d, Stopwatch sw)
        {
            long t = 0;
            if (sw.ElapsedMilliseconds > 8000) return t;
            try
            {
                foreach (string x in Directory.GetFiles(d)) { try { t += new FileInfo(x).Length; } catch (Exception) { } }
                foreach (string s2 in Directory.GetDirectories(d)) { t += TimedDirBytes(s2, sw); if (sw.ElapsedMilliseconds > 8000) break; }
            }
            catch (Exception) { }
            return t;
        }
        // measures a game's folder on a background thread; the answer arrives as onSize(id, bytes), -1 = unknown
        public string GameSize(string id, string path)
        {
            try
            {
                if (path == null || !File.Exists(path)) return "";
                string dir = Path.GetDirectoryName(path);
                bool single = dir == null || BroadFolder(dir);
                long one = new FileInfo(path).Length;
                ThreadPool.QueueUserWorkItem(delegate(object o)
                {
                    long total = one;
                    if (!single)
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        total = TimedDirBytes(dir, sw);
                        if (sw.ElapsedMilliseconds > 8000) total = -1;
                    }
                    try { f.BeginInvoke(new MethodInvoker(delegate { f.Js("onSize", id, total.ToString()); })); } catch (Exception) { }
                });
                return "ok";
            }
            catch (Exception) { return ""; }
        }

        // ---- game icons
        public string GameIcon(string id, string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                string dir = Path.Combine(f.DataDir, "icons");
                Directory.CreateDirectory(dir);
                string png = Path.Combine(dir, SafeName(id) + ".png");
                if (!File.Exists(png))
                {
                    using (System.Drawing.Icon ic = System.Drawing.Icon.ExtractAssociatedIcon(path))
                    {
                        if (ic == null) return "";
                        using (Bitmap b = ic.ToBitmap()) { b.Save(png, ImageFormat.Png); }
                    }
                }
                return f.FileUrl(png);
            }
            catch (Exception) { return ""; }
        }

        // ---- settings: tray + start with Windows
        public string SetTray(string on) { f.CloseToTray = (on == "1"); return "ok"; }
        public string StartupGet()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                { return (k != null && k.GetValue("XalyLauncher") != null) ? "1" : "0"; }
            }
            catch (Exception) { return "0"; }
        }
        // ================= 1.6.7: find games on this PC =================
        static string Line(params string[] f) { for (int i = 0; i < f.Length; i++) f[i] = (f[i] ?? "").Replace("|", " ").Replace("\n", " ").Replace("\r", ""); return string.Join("|", f) + "\n"; }
        static string RegStr(RegistryKey root, string sub, string name)
        {
            try { using (RegistryKey k = root.OpenSubKey(sub)) { object v = k == null ? null : k.GetValue(name); return v == null ? null : v.ToString(); } }
            catch (Exception) { return null; }
        }
        // the biggest .exe in a folder, looking at most 3 levels deep and 600 files, skipping helpers and installers
        static string GameExe(string dir)
        {
            string best = null; long size = -1; int seen = 0;
            Queue<KeyValuePair<string, int>> q = new Queue<KeyValuePair<string, int>>(); q.Enqueue(new KeyValuePair<string, int>(dir, 0));
            while (q.Count > 0 && seen < 600)
            {
                KeyValuePair<string, int> cur = q.Dequeue();
                try
                {
                    foreach (string p in Directory.GetFiles(cur.Key, "*.exe"))
                    {
                        seen++;
                        string n = Path.GetFileName(p).ToLowerInvariant();
                        if (n.IndexOf("unins") >= 0 || n.IndexOf("crash") >= 0 || n.IndexOf("redist") >= 0 || n.IndexOf("setup") >= 0 || n.IndexOf("install") >= 0 || n.IndexOf("helper") >= 0 || n.IndexOf("dxwebsetup") >= 0 || n.IndexOf("vc_redist") >= 0 || n.IndexOf("ue4prereq") >= 0 || n.IndexOf("dotnet") >= 0 || n.IndexOf("launcherpatcher") >= 0 || n == "notification_helper.exe" || n == "unitycrashhandler64.exe" || n == "unitycrashhandler32.exe") continue;
                        long sz = new FileInfo(p).Length;
                        if (cur.Value == 0) sz *= 4;   // prefer the exe at the top of the game folder
                        if (sz > size) { size = sz; best = p; }
                    }
                    if (cur.Value < 3)
                        foreach (string d in Directory.GetDirectories(cur.Key))
                        {
                            string dn = Path.GetFileName(d).ToLowerInvariant();
                            if (dn == "_commonredist" || dn == "redist" || dn == "directx" || dn == "__installer" || dn == "support" || dn == "engine" && cur.Value == 0 && best != null) continue;
                            q.Enqueue(new KeyValuePair<string, int>(d, cur.Value + 1));
                        }
                }
                catch (Exception) { }
            }
            return best;
        }
        // names a game folder or .exe goes by: folder names, the .exe's file name, and the product name built into the .exe
        static string ExeNames(string exe, string top)
        {
            List<string> n = new List<string>();
            if (!string.IsNullOrEmpty(top)) n.Add(top);
            try { string pd = Path.GetFileName(Path.GetDirectoryName(exe)); if (pd != top) n.Add(pd); } catch (Exception) { }
            n.Add(Path.GetFileNameWithoutExtension(exe));
            try
            {
                FileVersionInfo v = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrEmpty(v.ProductName)) n.Add(v.ProductName);
                if (!string.IsNullOrEmpty(v.FileDescription)) n.Add(v.FileDescription);
            }
            catch (Exception) { }
            return string.Join(";", n.ToArray()).Replace("|", " ");
        }
        // finds games people downloaded themselves (Downloads, Desktop, Documents, Program Files, Games folders,
        // the itch.io app, folders they picked); the page matches them against the Store's itch.io games
        public string ScanPC(string extra)
        {
            string ours = Path.GetFullPath(f.DownloadsDir).TrimEnd('\\').ToLowerInvariant();
            Thread t = new Thread(delegate()
            {
                StringBuilder sb = new StringBuilder();
                Dictionary<string, bool> done = new Dictionary<string, bool>();
                List<KeyValuePair<string, string>> roots = new List<KeyValuePair<string, string>>();
                roots.Add(new KeyValuePair<string, string>("Downloads", UserDownloads()));
                roots.Add(new KeyValuePair<string, string>("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
                roots.Add(new KeyValuePair<string, string>("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
                roots.Add(new KeyValuePair<string, string>("itch.io app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"itch\apps")));
                roots.Add(new KeyValuePair<string, string>("Program Files", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
                string pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)"); if (!string.IsNullOrEmpty(pf86)) roots.Add(new KeyValuePair<string, string>("Program Files", pf86));
                try { foreach (DriveInfo d in DriveInfo.GetDrives()) if (d.DriveType == DriveType.Fixed && d.IsReady) roots.Add(new KeyValuePair<string, string>("Games folder", Path.Combine(d.RootDirectory.FullName, "Games"))); } catch (Exception) { }
                foreach (string x in (extra ?? "").Split('\n')) if (x.Trim() != "") roots.Add(new KeyValuePair<string, string>("Your folder", x.Trim()));
                foreach (KeyValuePair<string, string> r in roots)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(r.Value) || !Directory.Exists(r.Value)) continue;
                        foreach (string d in Directory.GetDirectories(r.Value))
                        {
                            string full = Path.GetFullPath(d).TrimEnd('\\').ToLowerInvariant();
                            if (full == ours || full.StartsWith(ours + "\\") || Path.GetFileName(full) == "xaly games" || done.ContainsKey(full)) continue;
                            done[full] = true;
                            string dn = Path.GetFileName(d), low = dn.ToLowerInvariant();
                            if (r.Key == "Program Files" && (low == "windows nt" || low.StartsWith("microsoft") || low.StartsWith("windows") || low == "common files" || low == "internet explorer" || low == "dotnet" || low == "nvidia corporation" || low == "steam" || low == "epic games")) continue;
                            string exe = GameExe(d);
                            if (exe != null) sb.Append(Line("folder", r.Key, exe, ExeNames(exe, dn), d));
                        }
                        foreach (string e in Directory.GetFiles(r.Value, "*.exe"))
                        {
                            string n = Path.GetFileName(e).ToLowerInvariant();
                            if (n.IndexOf("setup") >= 0 || n.IndexOf("install") >= 0 || n.IndexOf("unins") >= 0) continue;
                            sb.Append(Line("exe", r.Key, e, ExeNames(e, ""), ""));
                        }
                        if (r.Key == "Downloads" || r.Key == "Desktop" || r.Key == "Your folder")
                            foreach (string z in Directory.GetFiles(r.Value, "*.zip"))
                                sb.Append(Line("zip", r.Key, z, Path.GetFileNameWithoutExtension(z), ""));
                    }
                    catch (Exception) { }
                }
                string payload = sb.ToString();
                try { f.BeginInvoke(new MethodInvoker(delegate { f.Js("onScanPC", payload); })); } catch (Exception) { }
            });
            t.IsBackground = true; t.Start();
            return "ok";
        }
        public string Exists(string paths)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string p in (paths ?? "").Split('\n')) { bool ok = false; try { ok = p.IndexOf("://") > 0 || File.Exists(p) || Directory.Exists(p); } catch (Exception) { } sb.Append(ok ? "1" : "0"); }
            return sb.ToString();
        }

        // ================= account backup files =================
        public string SaveBackup(string fileName, string text)
        {
            try
            {
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Filter = "Xaly account backup|*.xalybackup"; d.FileName = SafeName(fileName); d.DefaultExt = "xalybackup";
                    d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (d.ShowDialog(f) != DialogResult.OK) return "";
                    File.WriteAllText(d.FileName, text, new UTF8Encoding(false));
                    return d.FileName;
                }
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string OpenBackup()
        {
            try
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = "Xaly account backup|*.xalybackup|All files|*.*"; d.CheckFileExists = true;
                    d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (d.ShowDialog(f) != DialogResult.OK) return "";
                    if (new FileInfo(d.FileName).Length > 60L * 1024 * 1024) return "ERR:That file is too big to be a Xaly backup.";
                    return File.ReadAllText(d.FileName, Encoding.UTF8);
                }
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ================= shortcuts =================
        static string StartMenuDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"); }
        static void WriteLnk(string lnkPath, string target, string args, string icon, string workDir, string desc)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            try
            {
                object l = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
                Type lt = l.GetType();
                lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, l, new object[] { target });
                lt.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, l, new object[] { args ?? "" });
                lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, l, new object[] { workDir ?? "" });
                lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, l, new object[] { desc ?? "" });
                if (!string.IsNullOrEmpty(icon)) lt.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, l, new object[] { icon + ",0" });
                lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, l, null);
                Marshal.FinalReleaseComObject(l);
            }
            finally { Marshal.FinalReleaseComObject(sh); }
        }
        static string LnkTarget(string lnkPath)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell"); object sh = Activator.CreateInstance(t);
            try { object l = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath }); string r = (string)l.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, l, null); Marshal.FinalReleaseComObject(l); return r ?? ""; }
            catch (Exception) { return ""; }
            finally { Marshal.FinalReleaseComObject(sh); }
        }
        // where: "desktop" or "start"; a game shortcut starts the game through Xaly so play time is counted
        public string MakeShortcut(string where, string name, string gameId, string icon)
        {
            try
            {
                string dir = where == "desktop" ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : (string.IsNullOrEmpty(gameId) ? StartMenuDir() : Path.Combine(StartMenuDir(), "Xaly Games"));
                Directory.CreateDirectory(dir);
                string lnk = Path.Combine(dir, SafeName(string.IsNullOrEmpty(name) ? "Xaly Launcher" : name) + ".lnk");
                string ic = (!string.IsNullOrEmpty(icon) && File.Exists(icon) && icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ? icon : Application.ExecutablePath;
                WriteLnk(lnk, Application.ExecutablePath, string.IsNullOrEmpty(gameId) ? "" : "--play " + gameId, ic, f.AppDir, string.IsNullOrEmpty(gameId) ? "Xaly Launcher" : "Play " + name + " with Xaly Launcher");
                return lnk;
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string HasShortcut(string where, string name, string gameId)
        {
            string dir = where == "desktop" ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : (string.IsNullOrEmpty(gameId) ? StartMenuDir() : Path.Combine(StartMenuDir(), "Xaly Games"));
            return File.Exists(Path.Combine(dir, SafeName(string.IsNullOrEmpty(name) ? "Xaly Launcher" : name) + ".lnk")) ? "1" : "0";
        }
        public string RemoveShortcut(string where, string name, string gameId)
        {
            try
            {
                string dir = where == "desktop" ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : (string.IsNullOrEmpty(gameId) ? StartMenuDir() : Path.Combine(StartMenuDir(), "Xaly Games"));
                string lnk = Path.Combine(dir, SafeName(string.IsNullOrEmpty(name) ? "Xaly Launcher" : name) + ".lnk");
                if (File.Exists(lnk) && string.Equals(LnkTarget(lnk), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) File.Delete(lnk);
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        // every shortcut on the desktop and in the Start menu that points at this launcher
        static List<string> OurShortcuts()
        {
            List<string> l = new List<string>();
            foreach (string root in new string[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StartMenuDir() })
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string p in Directory.GetFiles(root, "*.lnk", root == StartMenuDir() ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                        if (string.Equals(LnkTarget(p), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) l.Add(p);
                }
                catch (Exception) { }
            }
            return l;
        }

        // ================= Apps & features entry + uninstalling =================
        const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\XalyLauncher";
        public string RegisterApp(string version)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstKey))
                {
                    string exe = Application.ExecutablePath;
                    k.SetValue("DisplayName", "Xaly Launcher"); k.SetValue("DisplayVersion", version ?? ""); k.SetValue("Publisher", "Xalify");
                    k.SetValue("DisplayIcon", exe); k.SetValue("InstallLocation", f.AppDir.TrimEnd('\\'));
                    k.SetValue("UninstallString", "\"" + exe + "\" --uninstall"); k.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall");
                    k.SetValue("URLInfoAbout", "https://github.com/Xalify/xalify-xaly-launcher"); k.SetValue("HelpLink", "https://github.com/Xalify/xalify-xaly-launcher");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    long kb = 0; try { kb = DirSize(f.AppDir) / 1024; } catch (Exception) { }
                    k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, kb), RegistryValueKind.DWord);
                    if (k.GetValue("InstallDate") == null) k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                }
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string ShowWindow() { f.ShowMain(); return "ok"; }
        public string StartCommand() { string c = f.StartCmd ?? ""; f.StartCmd = ""; return c; }
        // gameDirs: folders of games installed by Xaly (one per line), deleted only when delGames is "1"
        public string UninstallSelf(string delGames, string delData, string gameDirs)
        {
            try
            {
                foreach (string l in OurShortcuts()) { try { File.Delete(l); } catch (Exception) { } }
                try { string xg = Path.Combine(StartMenuDir(), "Xaly Games"); if (Directory.Exists(xg) && Directory.GetFileSystemEntries(xg).Length == 0) Directory.Delete(xg); } catch (Exception) { }
                try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) { if (k != null) k.DeleteValue("XalyLauncher", false); } } catch (Exception) { }
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey); } catch (Exception) { }
                try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION", true)) { if (k != null) k.DeleteValue(Path.GetFileName(Application.ExecutablePath), false); } } catch (Exception) { }
                try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true)) { if (k != null && k.OpenSubKey("xaly") != null) k.DeleteSubKeyTree("xaly"); } } catch (Exception) { }

                List<string> rm = new List<string>();     // folders removed after Xaly has closed
                if (delGames == "1")
                {
                    foreach (string d in (gameDirs ?? "").Split('\n')) { string x = d.Trim(); if (x != "" && UnderGameRoot(f, x + "\\")) rm.Add(x); }
                    rm.Add(Path.Combine(f.DownloadsDir, "Games"));
                }
                if (delData == "1") rm.Add(f.DataDir);
                string app = f.AppDir.TrimEnd('\\'), exe = Application.ExecutablePath;
                int pid = Process.GetCurrentProcess().Id;
                string cmd = Path.Combine(Path.GetTempPath(), "xaly_uninstall_" + pid + ".cmd");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine(":wait");
                sb.AppendLine("tasklist /fi \"PID eq " + pid + "\" | find \"" + pid + "\" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)");
                sb.AppendLine("ping -n 4 127.0.0.1 >nul");   // give WebView2's helper processes a moment to let go of their files
                foreach (string d in rm) sb.AppendLine("rmdir /s /q \"" + d + "\" 2>nul");
                if (delGames == "1")
                {
                    sb.AppendLine("del /q \"" + Path.Combine(f.DownloadsDir, "*.part") + "\" 2>nul");
                    sb.AppendLine("rmdir \"" + f.DownloadsDir.TrimEnd('\\') + "\" 2>nul");
                    foreach (DriveInfo dv in DriveInfo.GetDrives()) { try { if (dv.DriveType == DriveType.Fixed) sb.AppendLine("rmdir \"" + Path.Combine(dv.RootDirectory.FullName, "Xaly Games") + "\" 2>nul"); } catch (Exception) { } }
                }
                // only the launcher's own files are removed, so a shared folder like Downloads is never wiped
                sb.AppendLine("del /f /q \"" + exe + "\" 2>nul");
                foreach (string fn in new string[] { "index.html", "xaly.ico", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "Microsoft.Web.WebView2.Wpf.dll", "WebView2Loader.dll", "use-ie.txt", "webview2-error.txt", "selftest.txt", "release-notes.md", "README.md" })
                    sb.AppendLine("del /f /q \"" + Path.Combine(app, fn) + "\" 2>nul");
                foreach (string dn in new string[] { "x64", "x86", "arm64", "sounds", "runtimes" }) sb.AppendLine("rmdir /s /q \"" + Path.Combine(app, dn) + "\" 2>nul");
                sb.AppendLine("cd /d \"%TEMP%\"");
                sb.AppendLine("rmdir \"" + app + "\" 2>nul");
                sb.AppendLine("(goto) 2>nul & del \"%~f0\"");
                File.WriteAllText(cmd, sb.ToString(), Encoding.Default);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"");
                psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
                f.BeginInvoke(new MethodInvoker(delegate { f.RealExit(); }));
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string StartupSet(string on, string minimized)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (on == "1") k.SetValue("XalyLauncher", "\"" + Application.ExecutablePath + "\"" + (minimized == "1" ? " --tray" : ""));
                    else k.DeleteValue("XalyLauncher", false);
                }
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string HideNow() { f.HideToTray(); return "ok"; }
        public string Quit() { f.RealExit(); return "ok"; }
        public string Notify(string title, string text)
        {
            try { f.tray.ShowBalloonTip(3000, title, text, ToolTipIcon.Info); } catch (Exception) { }
            return "ok";
        }

        // ---- self-update: unpack the downloaded release, then a small script swaps the files once we have exited
        public string UpdateApply(string zip, string sha256)
        {
            try
            {
                if (!File.Exists(zip)) return "ERR:The update download is missing.";
                if (!string.IsNullOrEmpty(sha256))
                {
                    string got;
                    using (SHA256 h = SHA256.Create()) using (FileStream fs = File.OpenRead(zip)) got = Hex(h.ComputeHash(fs));
                    if (!string.Equals(got, sha256.Trim(), StringComparison.OrdinalIgnoreCase)) { try { File.Delete(zip); } catch (Exception) { } return "ERR:The update download was damaged (checksum mismatch). Try again."; }
                }
                string tmp = Path.Combine(Path.GetTempPath(), "XalyUpdate_" + DateTime.Now.Ticks);
                ZipFile.ExtractToDirectory(zip, tmp);
                string src = null;
                if (File.Exists(Path.Combine(tmp, "index.html"))) src = tmp;
                else foreach (string d in Directory.GetDirectories(tmp)) if (File.Exists(Path.Combine(d, "index.html"))) { src = d; break; }
                if (src == null) return "ERR:The update package doesn't contain the launcher files.";
                string app = f.AppDir.TrimEnd('\\');
                string exe = Path.Combine(app, Path.GetFileName(Application.ExecutablePath));
                int pid = Process.GetCurrentProcess().Id;
                string cmd = Path.Combine(tmp, "apply_update.cmd");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine(":wait");
                sb.AppendLine("tasklist /fi \"PID eq " + pid + "\" | find \"" + pid + "\" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)");
                sb.AppendLine("xcopy \"" + src + "\\*\" \"" + app + "\\\" /e /y /q /r /h >nul");
                sb.AppendLine("start \"\" \"" + exe + "\"");
                sb.AppendLine("cd /d \"%TEMP%\"");
                sb.AppendLine("rmdir /s /q \"" + tmp + "\"");
                File.WriteAllText(cmd, sb.ToString(), Encoding.Default);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"");
                psi.CreateNoWindow = true; psi.UseShellExecute = false; psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
                try { File.Delete(zip); } catch (Exception) { }
                f.BeginInvoke(new MethodInvoker(delegate { f.RealExit(); }));
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ---- notifications (shown by Windows when the launcher isn't the active window)
        public string Notify(string title, string msg, string tag) { return f.ShowNote(title, msg, tag); }
        // a small card in the bottom-right corner that stays above games and doesn't take focus from them
        public string Popup(string title, string msg, string tag, string accent)
        {
            try
            {
                Color c = Color.FromArgb(46, 230, 240);
                if (!string.IsNullOrEmpty(accent) && System.Text.RegularExpressions.Regex.IsMatch(accent, "^#[0-9a-fA-F]{6}$")) c = ColorTranslator.FromHtml(accent);
                new PopupCard(f, title ?? "", msg ?? "", tag ?? "", c).ShowCard();
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ---- sounds: short .wav files in the sounds folder next to the app, played by Windows (heard even while a game is focused)
        static System.Media.SoundPlayer player;
        public string PlaySound(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9_-]+$")) return "ERR:bad name";
                string p = Path.Combine(Path.Combine(f.AppDir, "sounds"), name + ".wav");
                if (!File.Exists(p)) return "ERR:missing";
                if (player != null) { try { player.Stop(); player.Dispose(); } catch (Exception) { } }
                player = new System.Media.SoundPlayer(p);
                player.Play();
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ---- display engine
        public string Engine() { return f.Modern ? "webview2" : "ie"; }
        public string UseEngine(string name)
        {
            try
            {
                string flag = Path.Combine(f.DataDir, "use-ie.txt");
                if (name == "ie") File.WriteAllText(flag, "1"); else if (File.Exists(flag)) File.Delete(flag);
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string ModernAvailable() { return f.ModernBuilt ? "1" : "0"; }
        // self-test used by the build server: the page reports that both directions of the bridge work
        public string SelfTest() { return f.SelfTesting ? "1" : "0"; }
        public string SelfTestDone(string result) { f.FinishSelfTest(result); return "ok"; }

        // ---- in-game overlay (Shift+Tab)
        public string OverlayHotkey(string on) { return f.SetHotkey(on == "1") ? "ok" : "ERR:Shift+Tab is already used by another app."; }
        public string OverlayClose() { f.CloseOverlay(); return "ok"; }
        public string OverlayShots()
        {
            try { string d = ShotDir(); Directory.CreateDirectory(d); Process.Start(d); return "ok"; }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        static string ShotDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Xaly Screenshots"); }
        // the newest screenshots of a game (one path per line), for the overlay's gallery strip
        public string ListShots(string game)
        {
            try
            {
                string d = ShotDir(); if (!Directory.Exists(d)) return "";
                string pre = SafeName(string.IsNullOrEmpty(game) ? "Game" : game) + " ";
                List<FileInfo> l = new List<FileInfo>();
                foreach (string p in Directory.GetFiles(d, "*.jpg")) if (Path.GetFileName(p).StartsWith(pre, StringComparison.OrdinalIgnoreCase)) l.Add(new FileInfo(p));
                l.Sort(delegate(FileInfo x, FileInfo y) { return y.LastWriteTimeUtc.CompareTo(x.LastWriteTimeUtc); });
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < l.Count && i < 6; i++) sb.Append(l[i].FullName).Append("\n");
                return sb.ToString();
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string OverlayShot(string game)
        {
            try
            {
                if (!File.Exists(f.OverlayShotPath)) return "ERR:No screenshot was captured.";
                string d = ShotDir(); Directory.CreateDirectory(d);
                string nm = SafeName(string.IsNullOrEmpty(game) ? "Game" : game) + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".jpg";
                string dest = Path.Combine(d, nm);
                File.Copy(f.OverlayShotPath, dest, true);
                return dest;
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ---- online friends: ECDH key exchange + AES/HMAC encryption, messages carried by the public ntfy.sh relay
        static string Hex(byte[] b) { StringBuilder sb = new StringBuilder(b.Length * 2); foreach (byte x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
        static byte[] FromHex(string h)
        {
            if (h == null || h.Length % 2 != 0) throw new ArgumentException("bad key");
            byte[] b = new byte[h.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
            return b;
        }
        static byte[] Hm(byte[] key, string label) { using (HMACSHA256 h = new HMACSHA256(key)) return h.ComputeHash(Encoding.UTF8.GetBytes(label)); }
        static bool OkPub(byte[] p) { return p != null && p.Length == 72 && p[0] == 0x45 && p[1] == 0x43 && p[2] == 0x4B && p[3] == 0x31; }
        public string EcNew()
        {
            try
            {
                CngKeyCreationParameters cp = new CngKeyCreationParameters();
                cp.ExportPolicy = CngExportPolicies.AllowPlaintextExport;
                using (CngKey k = CngKey.Create(CngAlgorithm.ECDiffieHellmanP256, null, cp))
                    return Convert.ToBase64String(k.Export(CngKeyBlobFormat.EccPrivateBlob)) + "|" + Convert.ToBase64String(k.Export(CngKeyBlobFormat.EccPublicBlob));
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string PubCode(string pub)
        {
            try
            {
                byte[] p = Convert.FromBase64String(pub);
                if (!OkPub(p)) return "";
                byte[] h; using (SHA256 sh = SHA256.Create()) h = sh.ComputeHash(p);
                ulong v = 0; for (int i = 0; i < 8; i++) v = (v << 8) | h[i];
                return (v % 1000000000UL).ToString("D9");
            }
            catch (Exception) { return ""; }
        }
        public string EcShared(string priv, string pub)
        {
            try
            {
                byte[] pb = Convert.FromBase64String(pub);
                if (!OkPub(pb)) return "ERR:Bad public key.";
                using (CngKey mine = CngKey.Import(Convert.FromBase64String(priv), CngKeyBlobFormat.EccPrivateBlob))
                using (CngKey theirs = CngKey.Import(pb, CngKeyBlobFormat.EccPublicBlob))
                using (ECDiffieHellmanCng dh = new ECDiffieHellmanCng(mine))
                {
                    dh.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash;
                    dh.HashAlgorithm = CngAlgorithm.Sha256;
                    return Hex(dh.DeriveKeyMaterial(theirs));
                }
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string RandHex(string n)
        {
            int c = 32; int.TryParse(n, out c); if (c < 1 || c > 64) c = 32;
            byte[] b = new byte[c]; using (RNGCryptoServiceProvider r = new RNGCryptoServiceProvider()) r.GetBytes(b);
            return Hex(b);
        }
        public string Topic(string keyHex, string label)
        {
            try { return Hex(Hm(FromHex(keyHex), "xaly-topic-" + label)).Substring(0, 32); }
            catch (Exception) { return "invalid"; }
        }
        public string Seal(string keyHex, string text)
        {
            try
            {
                byte[] k = FromHex(keyHex), ek = Hm(k, "xaly-enc"), mk = Hm(k, "xaly-mac");
                byte[] pt = Encoding.UTF8.GetBytes(text == null ? "" : text), iv, ct;
                using (RijndaelManaged a = new RijndaelManaged())
                {
                    a.BlockSize = 128; a.KeySize = 256; a.Mode = CipherMode.CBC; a.Padding = PaddingMode.PKCS7; a.Key = ek; a.GenerateIV(); iv = a.IV;
                    using (ICryptoTransform e = a.CreateEncryptor()) ct = e.TransformFinalBlock(pt, 0, pt.Length);
                }
                byte[] body = new byte[iv.Length + ct.Length];
                Buffer.BlockCopy(iv, 0, body, 0, iv.Length); Buffer.BlockCopy(ct, 0, body, iv.Length, ct.Length);
                byte[] mac; using (HMACSHA256 h = new HMACSHA256(mk)) mac = h.ComputeHash(body);
                byte[] outp = new byte[body.Length + 16];
                Buffer.BlockCopy(body, 0, outp, 0, body.Length); Buffer.BlockCopy(mac, 0, outp, body.Length, 16);
                return Convert.ToBase64String(outp);
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string Unseal(string keyHex, string b64)
        {
            try
            {
                byte[] k = FromHex(keyHex), ek = Hm(k, "xaly-enc"), mk = Hm(k, "xaly-mac");
                byte[] all = Convert.FromBase64String(b64 == null ? "" : b64.Trim());
                if (all.Length < 16 + 16 + 16) return "ERR:Too short.";
                int bl = all.Length - 16;
                byte[] mac; using (HMACSHA256 h = new HMACSHA256(mk)) mac = h.ComputeHash(all, 0, bl);
                int diff = 0; for (int i = 0; i < 16; i++) diff |= mac[i] ^ all[bl + i];
                if (diff != 0) return "ERR:Bad signature.";
                byte[] iv = new byte[16]; Buffer.BlockCopy(all, 0, iv, 0, 16);
                using (RijndaelManaged a = new RijndaelManaged())
                {
                    a.BlockSize = 128; a.KeySize = 256; a.Mode = CipherMode.CBC; a.Padding = PaddingMode.PKCS7; a.Key = ek; a.IV = iv;
                    using (ICryptoTransform d = a.CreateDecryptor())
                        return Encoding.UTF8.GetString(d.TransformFinalBlock(all, 16, bl - 16));
                }
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        static bool OkTopics(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length > 9000) return false;
            foreach (char c in t) if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == ',')) return false;
            return true;
        }
        // body empty = poll the listed topics; body set = publish to one topic. Result arrives through onHttp(id, ok, body, status).
        public string Relay(string id, string topics, string since, string body)
        {
            if (!OkTopics(topics)) return "ERR:Bad topic.";
            bool post = !string.IsNullOrEmpty(body);
            if (post && (topics.IndexOf(',') >= 0 || body.Length > 3800)) return "ERR:Message too long.";
            string url = "https://ntfy.sh/" + topics + (post ? "" : "/json?poll=1&since=" + Uri.EscapeDataString(string.IsNullOrEmpty(since) ? "12h" : since));
            ThreadPool.QueueUserWorkItem(delegate(object st)
            {
                string okFlag = "0", resp = "", status = "0";
                try
                {
                    try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; } catch (Exception) { }
                    HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
                    rq.UserAgent = "XalyLauncher";
                    rq.Timeout = 30000; rq.ReadWriteTimeout = 30000;
                    if (post)
                    {
                        byte[] d = Encoding.UTF8.GetBytes(body);
                        rq.Method = "POST"; rq.ContentType = "text/plain; charset=utf-8"; rq.ContentLength = d.Length;
                        using (Stream os = rq.GetRequestStream()) os.Write(d, 0, d.Length);
                    }
                    HttpWebResponse rs = null;
                    try { rs = (HttpWebResponse)rq.GetResponse(); }
                    catch (WebException we) { rs = we.Response as HttpWebResponse; if (rs == null) throw; }
                    using (rs)
                    {
                        int code = (int)rs.StatusCode; status = code.ToString();
                        using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) resp = sr.ReadToEnd();
                        if (resp.Length > 2000000) resp = resp.Substring(resp.Length - 2000000);
                        okFlag = (code >= 200 && code < 300) ? "1" : "0";
                    }
                }
                catch (Exception ex) { resp = ex.Message; }
                string o2 = okFlag, r2 = resp, s2 = status;
                try { f.BeginInvoke(new MethodInvoker(delegate { f.Js("onHttp", id, o2, r2, s2); })); } catch (Exception) { }
            });
            return "ok";
        }

        // ---- friends: presence files in a shared folder
        public string PutPeer(string dir, string code, string json)
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, SafeName(code) + ".xaly"), json, new UTF8Encoding(false));
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        public string Peers(string dir)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string p in Directory.GetFiles(dir, "*.xaly"))
                {
                    try { sb.Append(File.ReadAllText(p).Replace("\r", " ").Replace("\n", " ")).Append("\n"); }
                    catch (Exception) { }
                }
                return sb.ToString();
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
    }

    public class PopupCard : Form
    {
        static readonly List<PopupCard> open = new List<PopupCard>();
        readonly MainForm mf; readonly string tag; readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        int age = 0; bool closing = false;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; return cp; }   // no activate, tool window, topmost
        }
        public PopupCard(MainForm m, string title, string msg, string tg, Color accent)
        {
            mf = m; tag = tg;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; ShowInTaskbar = false; TopMost = true;
            BackColor = Color.FromArgb(22, 26, 36); Size = new Size(360, 86); Opacity = 0; Cursor = Cursors.Hand;
            Panel bar = new Panel(); bar.BackColor = accent; bar.SetBounds(0, 0, 5, 86); Controls.Add(bar);
            Label brand = new Label(); brand.Text = "XALY"; brand.ForeColor = accent; brand.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold); brand.AutoSize = true; brand.Location = new Point(20, 10); Controls.Add(brand);
            Label t = new Label(); t.Text = title.Length > 70 ? title.Substring(0, 70) : title; t.ForeColor = Color.White; t.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold); t.AutoEllipsis = true; t.SetBounds(20, 26, 326, 22); Controls.Add(t);
            Label b = new Label(); b.Text = msg.Length > 140 ? msg.Substring(0, 140) : msg; b.ForeColor = Color.FromArgb(170, 178, 200); b.Font = new Font("Segoe UI", 9f); b.AutoEllipsis = true; b.SetBounds(20, 49, 326, 32); Controls.Add(b);
            EventHandler click = delegate(object s, EventArgs e) { mf.ShowMain(); mf.Js("onNotifyClick", tag); StartClose(); };
            Click += click; foreach (Control k in Controls) k.Click += click;
            using (System.Drawing.Drawing2D.GraphicsPath gp = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int r = 14; gp.AddArc(0, 0, r, r, 180, 90); gp.AddArc(Width - r, 0, r, r, 270, 90); gp.AddArc(Width - r, Height - r, r, r, 0, 90); gp.AddArc(0, Height - r, r, r, 90, 90); gp.CloseFigure();
                Region = new Region(gp);
            }
            anim.Interval = 30;
            anim.Tick += delegate(object s, EventArgs e)
            {
                age += anim.Interval;
                if (!closing) { if (Opacity < 0.97) Opacity = Math.Min(0.97, Opacity + 0.12); if (age > 6000) StartClose(); }
                else { Opacity -= 0.1; if (Opacity <= 0.05) { anim.Stop(); open.Remove(this); Close(); Restack(); } }
            };
        }
        void StartClose() { closing = true; }
        static void Restack()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea; int y = wa.Bottom - 16;
            for (int i = open.Count - 1; i >= 0; i--) { PopupCard p = open[i]; y -= p.Height; p.Location = new Point(wa.Right - p.Width - 16, y); y -= 10; }
        }
        public void ShowCard()
        {
            while (open.Count >= 3) { PopupCard o = open[0]; open.RemoveAt(0); try { o.anim.Stop(); o.Close(); } catch (Exception) { } }
            open.Add(this); Restack(); Show(); anim.Start();
        }
    }
    public class MainForm : Form
    {
        public WebBrowser browser;           // legacy engine (Internet Explorer 11)
#if WEBVIEW2
        public WebView2 wv;                  // modern engine (Microsoft Edge WebView2)
        public bool ModernBuilt = true;
#else
        public bool ModernBuilt = false;
#endif
        public bool Modern = false, SelfTesting = false;
        Bridge bridge;
        public NotifyIcon tray;
        public bool CloseToTray = true;
        public string StartCmd = "";
        public string DataDir, DownloadsDir, AppDir;
        bool allowVisible = true, exiting = false, tipShown = false;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        const int OVERLAY_HOTKEY = 0x58A1;
        bool hotkeyOn = false, overlayOn = false, ovWasVisible = false;
        IntPtr ovGame = IntPtr.Zero;
        Rectangle ovBounds;
        FormWindowState ovState = FormWindowState.Normal;
        FormBorderStyle ovBorder = FormBorderStyle.Sizable;
        public string OverlayShotPath { get { return Path.Combine(DataDir, "overlay.jpg"); } }

        string noteTag = "";
        public string ShowNote(string title, string msg, string tag)
        {
            try
            {
                if (Visible && WindowState != FormWindowState.Minimized && Form.ActiveForm == this) return "skipped";
                noteTag = tag == null ? "" : tag;
                tray.BalloonTipTitle = string.IsNullOrEmpty(title) ? "Xaly Launcher" : (title.Length > 60 ? title.Substring(0, 60) : title);
                tray.BalloonTipText = string.IsNullOrEmpty(msg) ? " " : (msg.Length > 240 ? msg.Substring(0, 240) : msg);
                tray.BalloonTipIcon = ToolTipIcon.Info;
                tray.ShowBalloonTip(8000);
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        public bool SetHotkey(bool on)
        {
            if (on && !hotkeyOn) hotkeyOn = RegisterHotKey(Handle, OVERLAY_HOTKEY, 0x0004 | 0x4000, 0x09);   // Shift + Tab, no auto-repeat
            else if (!on && hotkeyOn) { UnregisterHotKey(Handle, OVERLAY_HOTKEY); hotkeyOn = false; }
            return on ? hotkeyOn : true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == OVERLAY_HOTKEY)
            {
                if (overlayOn) CloseOverlay(); else OpenOverlay();
                return;
            }
            base.WndProc(ref m);
        }

        // ---- TV mode: borderless full screen and Xbox controller input via XInput
        bool fsOn = false; Rectangle fsBounds; FormWindowState fsState; FormBorderStyle fsBorder;
        public void SetFullscreen(bool on)
        {
            if (overlayOn || on == fsOn) return;
            if (on)
            {
                fsState = WindowState; fsBorder = FormBorderStyle; fsBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                fsOn = true;
                if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                fsOn = false;
                FormBorderStyle = fsBorder; Bounds = fsBounds; WindowState = fsState;
            }
            FocusView();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XGamepad { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }
        [StructLayout(LayoutKind.Sequential)]
        struct XState { public uint dwPacketNumber; public XGamepad Gamepad; }
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern int XInputGetState14(int user, ref XState state);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern int XInputGetState910(int user, ref XState state);
        System.Windows.Forms.Timer padTimer; int padLast = -1, padApi = 14;
        public void PadNative(bool on)
        {
            if (!on) { if (padTimer != null) padTimer.Stop(); return; }
            if (padTimer == null)
            {
                padTimer = new System.Windows.Forms.Timer();
                padTimer.Interval = 16;
                padTimer.Tick += delegate(object s, EventArgs e) { PadPoll(); };
            }
            padTimer.Start();
        }
        void PadPoll()
        {
            if (Form.ActiveForm != this || padApi == 0) return;
            int bits = 0;
            for (int i = 0; i < 4; i++)
            {
                XState st = new XState(); int r;
                try { r = padApi == 14 ? XInputGetState14(i, ref st) : XInputGetState910(i, ref st); }
                catch (Exception) { padApi = padApi == 14 ? 910 : 0; return; }
                if (r != 0) continue;
                XGamepad g = st.Gamepad;
                bits |= g.wButtons & 0xF33F;
                if (g.sThumbLY > 16000) bits |= 1; if (g.sThumbLY < -16000) bits |= 2;
                if (g.sThumbLX < -16000) bits |= 4; if (g.sThumbLX > 16000) bits |= 8;
            }
            if (bits != padLast) { padLast = bits; Js("onPad", bits.ToString()); }
        }

        public void OpenOverlay()
        {
            if (overlayOn) return;
            IntPtr fg = GetForegroundWindow();
            ovGame = fg == Handle ? IntPtr.Zero : fg;
            Screen sc = ovGame != IntPtr.Zero ? Screen.FromHandle(ovGame) : Screen.FromPoint(Cursor.Position);
            Rectangle b = sc.Bounds;
            string url = "";
            try
            {
                using (Bitmap bmp = new Bitmap(b.Width, b.Height))
                {
                    using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                    bmp.Save(OverlayShotPath, ImageFormat.Jpeg);
                }
                url = FileUrl(OverlayShotPath) + "?" + DateTime.Now.Ticks;
            }
            catch (Exception) { }
            ovWasVisible = Visible;
            ovState = WindowState;
            ovBorder = FormBorderStyle;
            ovBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            overlayOn = true;
            allowVisible = true;
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = b;
            TopMost = true;
            if (!Visible) Show();
            Activate();
            FocusView();
            Js("onOverlay", "1", url);
        }

        public void CloseOverlay()
        {
            if (!overlayOn) return;
            overlayOn = false;
            Js("onOverlay", "0", "");
            TopMost = false;
            FormBorderStyle = ovBorder;
            Bounds = ovBounds;
            WindowState = ovState;
            if (!ovWasVisible) Hide();
            if (ovGame != IntPtr.Zero) { try { SetForegroundWindow(ovGame); } catch (Exception) { } }
        }

        public MainForm(bool startHidden)
        {
            AppDir = AppDomain.CurrentDomain.BaseDirectory;
            DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XalyLauncher");
            DownloadsDir = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "Xaly Launcher");
            Directory.CreateDirectory(DataDir);

            Text = "Xaly Launcher";
            ClientSize = new Size(1280, 800);
            MinimumSize = new Size(1000, 660);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(11, 11, 16);
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            bridge = new Bridge(this);
            SelfTesting = Array.IndexOf(Environment.GetCommandLineArgs(), "--selftest") >= 0;
            // the embedded browser must own keyboard/mouse focus, otherwise the first click only "wakes" it up
            Activated += delegate(object s, EventArgs e) { FocusView(); };

            tray = new NotifyIcon();
            tray.Icon = Icon != null ? Icon : SystemIcons.Application;
            tray.Text = "Xaly Launcher";
            tray.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open Xaly Launcher", null, delegate(object s, EventArgs e) { ShowMain(); });
            menu.Items.Add("Exit", null, delegate(object s, EventArgs e) { RealExit(); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowMain(); };
            tray.BalloonTipClicked += delegate(object s, EventArgs e) { ShowMain(); Js("onNotifyClick", noteTag); };

            allowVisible = !startHidden;
            IntPtr dummy = Handle;            // create the window (and browser) even when starting hidden
            GC.KeepAlive(dummy);
            string idx = Path.Combine(AppDir, "index.html");
            if (SelfTesting) StartSelfTestTimer();
#if WEBVIEW2
            if (!File.Exists(Path.Combine(DataDir, "use-ie.txt"))) { StartModern(idx); return; }
#endif
            StartLegacy(idx);
        }

        const string Missing = "<body style='background:#111;color:#eee;font:16px Segoe UI'><p>index.html is missing next to Xaly Launcher.exe.</p></body>";

        void StartLegacy(string idx)
        {
            Modern = false;
            browser = new WebBrowser();
            browser.Dock = DockStyle.Fill;
            browser.ScriptErrorsSuppressed = true;
            browser.IsWebBrowserContextMenuEnabled = false;
            browser.AllowWebBrowserDrop = false;
            browser.WebBrowserShortcutsEnabled = false;
            browser.ObjectForScripting = bridge;
            Controls.Add(browser);
            browser.DocumentCompleted += delegate(object s, WebBrowserDocumentCompletedEventArgs e) { FocusView(); };
            if (File.Exists(idx)) browser.Navigate(new Uri(idx));
            else browser.DocumentText = Missing;
        }

#if WEBVIEW2
        async void StartModern(string idx)
        {
            try
            {
                wv = new WebView2();
                wv.Dock = DockStyle.Fill;
                wv.DefaultBackgroundColor = Color.FromArgb(8, 10, 16);
                Controls.Add(wv);
                string loader = Path.Combine(AppDir, Environment.Is64BitProcess ? "x64" : "x86");
                if (Directory.Exists(loader)) CoreWebView2Environment.SetLoaderDllFolderPath(loader);
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "WebView2"));
                await wv.EnsureCoreWebView2Async(env);
                CoreWebView2 c = wv.CoreWebView2;
                c.Settings.AreDefaultContextMenusEnabled = false;
                c.Settings.IsStatusBarEnabled = false;
                c.Settings.AreBrowserAcceleratorKeysEnabled = false;
                c.Settings.IsZoomControlEnabled = false;
                c.Settings.AreDevToolsEnabled = File.Exists(Path.Combine(DataDir, "devtools.txt"));
                c.AddHostObjectToScript("external", bridge);
                c.SetVirtualHostNameToFolderMapping("app.xaly", AppDir, CoreWebView2HostResourceAccessKind.Allow);
                c.SetVirtualHostNameToFolderMapping("data.xaly", DataDir, CoreWebView2HostResourceAccessKind.Allow);
                c.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; try { Process.Start(e.Uri); } catch (Exception) { } };
                c.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs e) { FocusView(); };
                c.ProcessFailed += delegate(object s, CoreWebView2ProcessFailedEventArgs e)
                {
                    LogCrash("webview2", "The WebView2 " + e.ProcessFailedKind + " stopped (" + e.Reason + ", exit code " + e.ExitCode + ").");
                    if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
                    { try { wv.Reload(); } catch (Exception) { } }
                };
                Modern = true;
                if (File.Exists(idx)) c.Navigate("https://app.xaly/index.html");
                else c.NavigateToString(Missing);
            }
            catch (Exception ex)
            {
                // no WebView2 runtime (or its files are missing): fall back to the Internet Explorer engine
                try { File.WriteAllText(Path.Combine(DataDir, "webview2-error.txt"), DateTime.Now + "\r\n" + ex); } catch (Exception) { }
                try { if (wv != null) { Controls.Remove(wv); wv.Dispose(); } } catch (Exception) { }
                wv = null;
                StartLegacy(idx);
            }
        }
#endif

        public static void LogCrash(string kind, string text)
        {
            try
            {
                string dir = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XalyLauncher"), "crashes");
                Directory.CreateDirectory(dir);
                string user = Environment.UserName;
                string body = (text ?? "");
                if (!string.IsNullOrEmpty(user) && user.Length > 1) body = body.Replace(user, "<user>");
                if (kind != "page") body = "Xaly app error (" + kind + ")\r\nWindows: " + Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " 64-bit" : " 32-bit") + "\r\nWhen: " + DateTime.UtcNow.ToString("u") + "\r\n\r\n" + body;
                File.WriteAllText(Path.Combine(dir, "crash-" + DateTime.UtcNow.Ticks + ".txt"), body.Length > 8000 ? body.Substring(0, 8000) : body, Encoding.UTF8);
            }
            catch (Exception) { }
        }

        public void FocusView()
        {
            try
            {
#if WEBVIEW2
                if (Modern && wv != null) { wv.Focus(); return; }
#endif
                if (browser != null) browser.Focus();
            }
            catch (Exception) { }
        }

        // turns a file inside the data folder into an address the current engine can load
        public string FileUrl(string path)
        {
            if (Modern)
            {
                string d = DataDir.TrimEnd('\\') + "\\";
                if (path.StartsWith(d, StringComparison.OrdinalIgnoreCase)) return "https://data.xaly/" + Uri.EscapeUriString(path.Substring(d.Length).Replace('\\', '/'));
            }
            return new Uri(path).AbsoluteUri;
        }

        System.Windows.Forms.Timer selfTimer;
        void StartSelfTestTimer()
        {
            selfTimer = new System.Windows.Forms.Timer();
            selfTimer.Interval = 90000;
            selfTimer.Tick += delegate(object s, EventArgs e) { FinishSelfTest("{\"ok\":false,\"error\":\"timeout: the page never reported back\",\"engine\":\"" + (Modern ? "webview2" : "ie") + "\"}"); };
            selfTimer.Start();
        }
        public void FinishSelfTest(string result)
        {
            try { if (selfTimer != null) selfTimer.Stop(); } catch (Exception) { }
            try { File.WriteAllText(Path.Combine(AppDir, "selftest.txt"), result ?? ""); } catch (Exception) { }
            // CI only: run the real uninstaller on a throw-away copy of the launcher
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--selftest-uninstall") >= 0 && File.Exists(Path.Combine(AppDir, "uninstall-test.txt")))
            { try { new Bridge(this).UninstallSelf("0", "0", ""); return; } catch (Exception) { } }
            RealExit();
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(allowVisible && value); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int one = 1; int cap = 0x100B0B; int txt = 0xF7F3F3;
                DwmSetWindowAttribute(Handle, 20, ref one, 4);
                DwmSetWindowAttribute(Handle, 19, ref one, 4);
                DwmSetWindowAttribute(Handle, 35, ref cap, 4);
                DwmSetWindowAttribute(Handle, 36, ref txt, 4);
            }
            catch (Exception) { }
        }

        public void Js(string fn, params object[] args)
        {
            if (InvokeRequired) { try { BeginInvoke(new MethodInvoker(delegate { Js(fn, args); })); } catch (Exception) { } return; }
#if WEBVIEW2
            if (Modern)
            {
                try
                {
                    if (wv == null || wv.CoreWebView2 == null) return;
                    StringBuilder sb = new StringBuilder();
                    sb.Append("window.").Append(fn).Append("&&window.").Append(fn).Append('(');
                    for (int i = 0; i < args.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Bridge.JsonStr(args[i] == null ? "" : args[i].ToString())); }
                    sb.Append(')');
                    wv.CoreWebView2.ExecuteScriptAsync(sb.ToString());
                }
                catch (Exception) { }
                return;
            }
#endif
            try { if (browser != null && browser.Document != null) browser.Document.InvokeScript(fn, args); } catch (Exception) { }
        }

        public void ShowMain()
        {
            allowVisible = true;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            TopMost = true; TopMost = false;
            Activate();
            Js("onShown");
        }

        public void HideToTray()
        {
            Hide();
            if (!tipShown)
            {
                tipShown = true;
                try { tray.ShowBalloonTip(2500, "Xaly Launcher", "Still running in the tray. Click the icon to open it, or right-click to exit.", ToolTipIcon.Info); } catch (Exception) { }
            }
        }

        public void RealExit()
        {
            exiting = true;
            Js("onQuit");
            tray.Visible = false;
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (overlayOn && !exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; CloseOverlay(); return; }
            if (!exiting && CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            tray.Visible = false;
            base.OnFormClosing(e);
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            string cmd = "";
            int pi = Array.IndexOf(args, "--play");
            if (pi >= 0 && pi + 1 < args.Length && System.Text.RegularExpressions.Regex.IsMatch(args[pi + 1], "^[A-Za-z0-9_-]{1,40}$")) cmd = "play " + args[pi + 1];
            if (Array.IndexOf(args, "--uninstall") >= 0) cmd = "uninstall";
            string cmdFile = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XalyLauncher"), "command.txt");
            bool created;
            EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\XalyLauncherShow", out created);
            if (!created)
            {
                // already running: hand over the command (play a game / uninstall) and bring it forward
                try { if (cmd != "") File.WriteAllText(cmdFile, cmd); } catch (Exception) { }
                ev.Set(); return;
            }
            try { if (File.Exists(cmdFile)) File.Delete(cmdFile); } catch (Exception) { }

            try { SetProcessDPIAware(); } catch (Exception) { }
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 768 | 192); } catch (Exception) { }   // TLS 1.2 / 1.1 / 1.0
            try
            {
                // make the embedded browser behave like IE11 instead of IE7
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                { k.SetValue(Path.GetFileName(Application.ExecutablePath), 11001, RegistryValueKind.DWord); }
            }
            catch (Exception) { }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { MainForm.LogCrash("ui", e.Exception.ToString()); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { MainForm.LogCrash("fatal", e.ExceptionObject == null ? "unknown" : e.ExceptionObject.ToString()); };
            Application.EnableVisualStyles();
            MainForm form = new MainForm(Array.IndexOf(args, "--tray") >= 0 || cmd.StartsWith("play "));
            form.StartCmd = cmd;
            Thread t = new Thread(delegate()
            {
                while (ev.WaitOne())
                {
                    string c = "";
                    try { if (File.Exists(cmdFile)) { c = File.ReadAllText(cmdFile).Trim(); File.Delete(cmdFile); } } catch (Exception) { }
                    try
                    {
                        form.BeginInvoke(new MethodInvoker(delegate
                        {
                            if (!c.StartsWith("play ")) form.ShowMain();
                            if (c != "") form.Js("onCommand", c);
                        }));
                    }
                    catch (Exception) { }
                }
            });
            t.IsBackground = true;
            t.Start();
            Application.Run(form);
        }
    }
}

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
                HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
                rq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) XalyLauncher";
                rq.Timeout = 30000; rq.ReadWriteTimeout = 30000; rq.AllowAutoRedirect = true;
                if (have > 0) rq.AddRange(have);
                job.rq = rq;
                if (job.stop != 0) throw new OperationCanceledException();
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                {
                    long total;
                    if (have > 0 && rs.StatusCode == HttpStatusCode.PartialContent) total = rs.ContentLength >= 0 ? have + rs.ContentLength : -1;
                    else { have = 0; total = rs.ContentLength; }   // the server ignored the range: start over
                    using (Stream inp = rs.GetResponseStream())
                    using (FileStream fs = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        byte[] buf = new byte[131072]; int n; long got = have; int last = 0;
                        f.Js("onDl", id, "p", got.ToString(), total.ToString());
                        while ((n = inp.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n); got += n;
                            if (job.stop != 0) break;
                            int t = Environment.TickCount;
                            if (t - last > 250) { last = t; f.Js("onDl", id, "p", got.ToString(), total.ToString()); }
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
                ZipFile.ExtractToDirectory(zip, dir);
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
                ZipFile.ExtractToDirectory(zip, tmp);
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
                if (!Path.GetFullPath(dest).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "ERR:Only games downloaded by Xaly can be updated.";
                File.Copy(src, dest, true);
                try { File.Delete(src); } catch (Exception) { }
                return "ok";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
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
                        if (PostForm("Xaly crash report (" + ver + ")", text, ver, "")) { try { File.Delete(file); } catch (Exception) { } sent++; }
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

        public string Feedback(string id, string text, string ver, string email)
        {
            // everything happens on a background thread so the window never freezes
            ThreadPool.QueueUserWorkItem(delegate(object st)
            {
                string okFlag = "0", msg = "";
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
                    string resp;
                    using (rs) using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8)) resp = sr.ReadToEnd();
                    bool good = resp.Replace(" ", "").IndexOf("\"success\":\"true\"") >= 0 || resp.Replace(" ", "").IndexOf("\"success\":true") >= 0;
                    okFlag = good ? "1" : "0";
                    int m = resp.IndexOf("\"message\"");
                    if (m >= 0) { int a = resp.IndexOf('"', resp.IndexOf(':', m) + 1); int b = a >= 0 ? resp.IndexOf('"', a + 1) : -1; if (a >= 0 && b > a) msg = resp.Substring(a + 1, b - a - 1); }
                    if (msg.Length == 0) msg = good ? "sent" : "HTTP " + (int)rs.StatusCode;
                }
                catch (Exception ex) { msg = ex.Message; }
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
            bool created;
            EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\XalyLauncherShow", out created);
            if (!created) { ev.Set(); return; }   // already running: just bring it forward

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
            MainForm form = new MainForm(Array.IndexOf(args, "--tray") >= 0);
            Thread t = new Thread(delegate()
            {
                while (ev.WaitOne())
                {
                    try { form.BeginInvoke(new MethodInvoker(form.ShowMain)); } catch (Exception) { }
                }
            });
            t.IsBackground = true;
            t.Start();
            Application.Run(form);
        }
    }
}

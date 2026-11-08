using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

// MultiRoblox: holds Roblox's single-instance lock (multi-client) and launches each saved account.
// shortcut: only affects clients opened after this program; stops working if Roblox changes its check.
// Security: no password is stored. Only the cookie, encrypted with DPAPI (readable only by this Windows user),
// and it is only ever sent to *.roblox.com.
class Account
{
    public long Id; public string Name; public string Cookie; public string Nick = ""; public string Note = ""; public int Pid; public int PresType = -1; public string Presence = "";
    public string Shown { get { return Nick.Length > 0 ? Nick : Name; } }
}

static class Store
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MultiRoblox");
    static readonly string FilePath = Path.Combine(Dir, "accounts.dat");
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MultiRoblox.v1");

    public static string LoadPlace() { try { return File.ReadAllText(Path.Combine(Dir, "place.txt")).Trim(); } catch (Exception) { return ""; } }
    public static void SavePlace(string p) { if (ReadOnly) return; try { Directory.CreateDirectory(Dir); File.WriteAllText(Path.Combine(Dir, "place.txt"), p); } catch (Exception) { } }

    public static List<KeyValuePair<long, string>> LoadFavs()
    {
        var l = new List<KeyValuePair<long, string>>();
        try
        {
            foreach (string line in File.ReadAllLines(Path.Combine(Dir, "favs.txt")))
            {
                string[] p = line.Split('\t');
                long id;
                if (p.Length == 2 && long.TryParse(p[0], out id)) l.Add(new KeyValuePair<long, string>(id, p[1]));
            }
        }
        catch (Exception) { }
        return l;
    }

    public static void SaveFavs(List<KeyValuePair<long, string>> f)
    {
        if (ReadOnly) return;
        try { Directory.CreateDirectory(Dir); File.WriteAllLines(Path.Combine(Dir, "favs.txt"), f.Select(x => x.Key + "\t" + x.Value.Replace('\t', ' ')).ToArray()); }
        catch (Exception) { }
    }

    public static bool ReadOnly;   // --demo: fake data, nothing is written

    public static void Log(string line)
    {
        try { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "log.txt"), DateTime.Now.ToString("HH:mm:ss ") + line + Environment.NewLine); } catch (Exception) { }
    }

    public static List<Account> Load()
    {
        var list = new List<Account>();
        try
        {
            if (!File.Exists(FilePath)) return list;
            string text = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
            foreach (string line in text.Split('\n'))
            {
                string[] p = line.Trim().Split('\t');
                long id;
                if (p.Length >= 3 && long.TryParse(p[0], out id)) list.Add(new Account { Id = id, Name = p[1], Cookie = p[2], Nick = p.Length > 3 ? p[3] : "" });
            }
        }
        catch (Exception) { }   // file from another user/PC or corrupted: start empty (the old file is not overwritten until a save)
        return list;
    }

    public static void Save(List<Account> list)
    {
        if (ReadOnly) return;
        Directory.CreateDirectory(Dir);
        string text = string.Join("\n", list.Select(a => a.Id + "\t" + a.Name + "\t" + a.Cookie + "\t" + a.Nick).ToArray());
        string tmp = FilePath + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(Encoding.UTF8.GetBytes(text), Entropy, DataProtectionScope.CurrentUser));
        File.Copy(tmp, FilePath, true);
        File.Delete(tmp);
    }
}

static class Roblox
{
    static readonly HttpClient Http = new HttpClient(new HttpClientHandler { UseCookies = false });
    static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

    static HttpRequestMessage Req(HttpMethod m, string url, string cookie)
    {
        var r = new HttpRequestMessage(m, url);
        if (cookie != null) r.Headers.TryAddWithoutValidation("Cookie", ".ROBLOSECURITY=" + cookie);
        r.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        r.Headers.TryAddWithoutValidation("Referer", "https://www.roblox.com/games/4924922222/Brookhaven-RP");
        return r;
    }

    public static Dictionary<string, object> Obj(string json) { return Js.DeserializeObject(json) as Dictionary<string, object>; }

    // Returns (id, name) if the cookie is valid, otherwise null.
    public static async Task<Account> Whoami(string cookie)
    {
        try
        {
            using (var resp = await Http.SendAsync(Req(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated", cookie)))
            {
                if (!resp.IsSuccessStatusCode) { Store.Log("Whoami: HTTP " + (int)resp.StatusCode); return null; }
                var o = Obj(await resp.Content.ReadAsStringAsync());
                return new Account { Id = Convert.ToInt64(o["id"]), Name = (string)o["name"], Cookie = cookie };
            }
        }
        catch (Exception ex) { Store.Log("Whoami error: " + ex.Message + " | " + (ex.InnerException != null ? ex.InnerException.Message : "")); return null; }
    }

    public class GameInfo { public string Name; public long Playing; public Image Icon; }

    static async Task<string> GetPublic(string url)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, url);
        r.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        using (var resp = await Http.SendAsync(r)) return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync() : null;
    }

    // Game name, player count and icon (public APIs, no cookie).
    public static async Task<GameInfo> Game(long placeId)
    {
        try
        {
            var u = Obj(await GetPublic("https://apis.roblox.com/universes/v1/places/" + placeId + "/universe"));
            if (u == null || u["universeId"] == null) return null;
            object uid = u["universeId"];
            var d = Obj(await GetPublic("https://games.roblox.com/v1/games?universeIds=" + uid))["data"] as object[];
            if (d == null || d.Length == 0) return null;
            var row = (Dictionary<string, object>)d[0];
            var info = new GameInfo { Name = (string)row["name"], Playing = Convert.ToInt64(row["playing"]) };
            try
            {
                var t = Obj(await GetPublic("https://thumbnails.roblox.com/v1/games/icons?universeIds=" + uid + "&size=150x150&format=Png&isCircular=false"))["data"] as object[];
                string img = (string)((Dictionary<string, object>)t[0])["imageUrl"];
                if (!string.IsNullOrEmpty(img))
                    using (var ms = new MemoryStream(await Http.GetByteArrayAsync(img))) info.Icon = new Bitmap(Image.FromStream(ms));
            }
            catch (Exception) { }   // no icon: show the name only
            return info;
        }
        catch (Exception ex) { Store.Log("Game error: " + ex.Message); return null; }
    }

    // 1 = valid, 0 = expired (401), -1 = unknown (network / rate limit)
    public static async Task<int> CookieState(string cookie)
    {
        try
        {
            using (var resp = await Http.SendAsync(Req(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated", cookie)))
                return resp.IsSuccessStatusCode ? 1 : (int)resp.StatusCode == 401 ? 0 : -1;
        }
        catch (Exception) { return -1; }
    }

    public static bool Installed() { using (var k = Registry.ClassesRoot.OpenSubKey("roblox-player")) return k != null; }

    public static HashSet<int> ClientPids()
    {
        var procs = Process.GetProcessesByName("RobloxPlayerBeta");
        var set = new HashSet<int>(procs.Select(p => p.Id));
        foreach (var p in procs) p.Dispose();
        return set;
    }

    // POST with the CSRF handshake (403 + token on the first try). cookie may be null. Returns the body or null.
    static async Task<string> PostJson(string url, string cookie, string body)
    {
        string csrf = null;
        for (int i = 0; i < 2; i++)
        {
            var r = Req(HttpMethod.Post, url, cookie);
            if (csrf != null) r.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
            r.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using (var resp = await Http.SendAsync(r))
            {
                if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync();
                IEnumerable<string> v;
                if ((int)resp.StatusCode == 403 && csrf == null && resp.Headers.TryGetValues("x-csrf-token", out v)) csrf = v.First(); else return null;
            }
        }
        return null;
    }

    // An account's own presence, read with its own cookie (an account can always see itself). On error the last value is kept.
    public static async Task Presence(Account a)
    {
        try
        {
            string json = await PostJson("https://presence.roblox.com/v1/presence/users", a.Cookie, "{\"userIds\":[" + a.Id + "]}");
            var p = (Dictionary<string, object>)(Obj(json)["userPresences"] as object[])[0];
            int t = Convert.ToInt32(p["userPresenceType"]);
            string where = p.ContainsKey("lastLocation") && p["lastLocation"] != null ? MainForm.Clean((string)p["lastLocation"]) : "";
            a.PresType = t;
            a.Presence = t == 2 ? "in game" + (where.Length > 0 ? ": " + where : "") : t == 1 ? "online (website)" : t == 3 ? "in Studio" : "offline";
        }
        catch (Exception ex) { Store.Log("Presence error: " + ex.Message); }
    }

    // Accepts a numeric ID or a username. 0 = not found.
    public static async Task<long> UserId(string nameOrId)
    {
        long id;
        if (long.TryParse(nameOrId, out id)) return id > 0 ? id : 0;
        if (!System.Text.RegularExpressions.Regex.IsMatch(nameOrId, "^[A-Za-z0-9_]{3,20}$")) return 0;   // valid username format: also prevents injecting into the JSON
        try
        {
            var d = Obj(await PostJson("https://users.roblox.com/v1/usernames/users", null, "{\"usernames\":[\"" + nameOrId + "\"],\"excludeBannedUsers\":true}"))["data"] as object[];
            return d != null && d.Length > 0 ? Convert.ToInt64(((Dictionary<string, object>)d[0])["id"]) : 0;
        }
        catch (Exception ex) { Store.Log("UserId error: " + ex.Message); return 0; }
    }

    // One-time login ticket. Roblox answers 403 + CSRF token on the first try.
    public static async Task<string> Ticket(string cookie)
    {
        string csrf = null;
        for (int i = 0; i < 2; i++)
        {
            var r = Req(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket/", cookie);
            if (csrf != null) r.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf);
            r.Content = new StringContent("", Encoding.UTF8, "application/json");
            using (var resp = await Http.SendAsync(r))
            {
                IEnumerable<string> v;
                if (resp.Headers.TryGetValues("rbx-authentication-ticket", out v)) return v.First();
                if (resp.Headers.TryGetValues("x-csrf-token", out v)) csrf = v.First(); else { Store.Log("Ticket: HTTP " + (int)resp.StatusCode); return null; }
            }
        }
        return null;
    }

    static string UrlEncode(string s)
    {
        var sb = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            char c = (char)b;
            if (char.IsLetterOrDigit(c) && b < 128 || "-_.!*()".IndexOf(c) >= 0) sb.Append(c);
            else sb.Append('%').Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    public static void Launch(string ticket, long placeId, long followUserId = 0)
    {
        long tracker = 100000000000L + (long)(new Random().NextDouble() * 899999999999L);
        long now = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        string url = followUserId > 0
            ? "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestFollowUser&userId=" + followUserId
            : "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId=" + tracker + "&placeId=" + placeId + "&isPlayTogetherGame=false";
        string proto = "roblox-player:1+launchmode:play+gameinfo:" + ticket + "+launchtime:" + now
            + "+placelauncherurl:" + UrlEncode(url) + "+browsertrackerid:" + tracker
            + "+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";
        Process.Start(new ProcessStartInfo(proto) { UseShellExecute = true });
    }

    // ---- browser login: temporary profile of the Windows DEFAULT browser; reads the cookie through the debug port ----
    // Chromium (Edge, Chrome, Brave, Vivaldi) speaks CDP; Firefox speaks WebDriver BiDi. Others (Opera...) fall back to Edge/Chrome.
    static string FindBrowser()
    {
        string pf = Environment.GetEnvironmentVariable("ProgramFiles"), pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
               lad = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var c = new List<string>();
        if (pf86 != null) c.Add(Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe"));
        if (pf != null) { c.Add(Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe")); c.Add(Path.Combine(pf, @"Google\Chrome\Application\chrome.exe")); }
        if (pf86 != null) c.Add(Path.Combine(pf86, @"Google\Chrome\Application\chrome.exe"));
        if (lad != null) c.Add(Path.Combine(lad, @"Google\Chrome\Application\chrome.exe"));
        return c.FirstOrDefault(File.Exists);
    }

    // Default browser for https links, read from the registry. null if unknown.
    static string DefaultBrowser()
    {
        try
        {
            string prog = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId", null) as string;
            string cmd = prog == null ? null : Registry.GetValue(@"HKEY_CLASSES_ROOT\" + prog + @"\shell\open\command", "", null) as string;
            if (cmd == null) return null;
            var m = System.Text.RegularExpressions.Regex.Match(cmd, "^\\s*\"([^\"]+)\"|^\\s*(\\S+)");
            string exe = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return File.Exists(exe) ? exe : null;
        }
        catch (Exception) { return null; }
    }

    static string Kind(string exe)
    {
        string n = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
        if (n == "firefox") return "firefox";
        return n == "msedge" || n == "chrome" || n == "brave" || n == "vivaldi" || n == "chromium" ? "chromium" : null;
    }

    static string PickBrowser(out string kind)
    {
        string exe = DefaultBrowser();
        kind = exe != null ? Kind(exe) : null;
        if (kind != null) return exe;
        exe = FindBrowser();   // unknown/incompatible default: Edge or Chrome
        kind = exe != null ? "chromium" : null;
        return exe;
    }

    static async Task<string> Cdp(ClientWebSocket ws, int id, string method, CancellationToken ct, string extra = "")
    {
        byte[] data = Encoding.UTF8.GetBytes("{\"id\":" + id + ",\"method\":\"" + method + "\"" + extra + "}");
        await ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, ct);
        var buf = new byte[65536];
        while (true)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult res;
            do
            {
                res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                if (res.MessageType == WebSocketMessageType.Close) return null;
                sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
            } while (!res.EndOfMessage);
            string t = sb.ToString();
            if (t.Contains("\"id\":" + id + ",") || t.Contains("\"id\":" + id + "}")) return t;
        }
    }

    static async Task<string> DebugUrl(string kind, int port)
    {
        try
        {
            if (kind == "firefox")   // BiDi only: no /json/version, the WebSocket lives at /session
                using (var tc = new System.Net.Sockets.TcpClient()) { await tc.ConnectAsync("127.0.0.1", port); return "ws://127.0.0.1:" + port + "/session"; }
            using (var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                return (string)Obj(await hc.GetStringAsync("http://127.0.0.1:" + port + "/json/version"))["webSocketDebuggerUrl"];
        }
        catch (Exception) { return null; }
    }

    public static bool ProbeOk;   // only --logintest uses this: the browser protocol answered

    public static async Task<Account> BrowserLogin(CancellationToken ct, Action<string> say, bool probe = false)
    {
        string kind;
        string exe = PickBrowser(out kind);
        if (exe == null) { say("No compatible browser found (Firefox, Edge, Chrome, Brave or Vivaldi)."); return null; }
        bool ff = kind == "firefox";
        string profile = Path.Combine(Path.GetTempPath(), "mrb_login_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        Account found = null;
        try
        {
            string url = probe ? "about:blank" : "https://www.roblox.com/login";
            string args = ff
                ? "-no-remote -new-instance -profile \"" + profile + "\" --remote-debugging-port=" + port + (probe ? " -headless" : "") + " \"" + url + "\""
                : "--user-data-dir=\"" + profile + "\" --remote-debugging-port=" + port + " --remote-allow-origins=* --no-first-run --no-default-browser-check"
                    + (probe ? " --headless=new" : " --new-window") + " \"" + url + "\"";
            Store.Log("browser (" + kind + "): " + exe + " port " + port);
            Process.Start(exe, args);
            say("Browser opened (" + Path.GetFileNameWithoutExtension(exe) + "): sign in to the account you want to add.");
            string wsUrl = null;
            for (int i = 0; i < 60 && wsUrl == null && !ct.IsCancellationRequested; i++)
            {
                wsUrl = await DebugUrl(kind, port);
                if (wsUrl == null) await Task.Delay(500, ct);
            }
            Store.Log("debug port: " + (wsUrl ?? "NO REPLY"));
            if (wsUrl == null) { say("Could not talk to the browser (the debug port did not open). See log.txt."); return null; }
            using (var ws = new ClientWebSocket())
            {
                await ws.ConnectAsync(new Uri(wsUrl), ct);
                int id = 0, fails = 0; bool loggedCookie = false, loggedReply = false;
                Store.Log("websocket connected");
                if (ff)
                {
                    string sn = await Cdp(ws, ++id, "session.new", ct, ",\"params\":{\"capabilities\":{}}");
                    Store.Log("session.new: " + (sn == null ? "no reply" : sn.Substring(0, Math.Min(120, sn.Length))));
                }
                var start = DateTime.Now;
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open && (DateTime.Now - start).TotalMinutes < 10)
                {
                    string reply = ff ? await Cdp(ws, ++id, "storage.getCookies", ct, ",\"params\":{\"filter\":{\"name\":\".ROBLOSECURITY\"}}")
                                      : await Cdp(ws, ++id, "Storage.getCookies", ct);
                    if (reply == null) break;
                    if (probe) { ProbeOk = reply.Contains("\"cookies\""); break; }
                    if (!loggedReply && !reply.Contains("\"cookies\"")) { Store.Log("unexpected reply: " + reply.Substring(0, Math.Min(200, reply.Length))); loggedReply = true; }
                    string cookie = ff ? CookieFromBidi(reply) : CookieFrom(reply);
                    if (cookie != null && !loggedCookie) { Store.Log("found .ROBLOSECURITY cookie, validating"); loggedCookie = true; }
                    if (cookie != null)
                    {
                        if ((found = await Whoami(cookie)) != null) break;
                        if (++fails >= 5) { say("Could not validate the login with Roblox (see log.txt)."); break; }
                    }
                    await Task.Delay(1500, ct);
                }
                if (ws.State == WebSocketState.Open && !ct.IsCancellationRequested) { try { await Cdp(ws, ++id, ff ? "browser.close" : "Browser.close", ct); } catch (Exception) { } }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Store.Log("BrowserLogin error: " + ex); say("Login ended: " + ex.Message); }
        Store.Log("login finished: " + (found != null ? "OK " + found.Name : "no account"));
        var _ = Task.Run(async delegate   // the browser is slow to release the profile files; whatever is left of it gets closed
        {
            await Task.Delay(4000);
            KillByProfile(profile);
            for (int i = 0; i < 6; i++) { try { if (Directory.Exists(profile)) Directory.Delete(profile, true); return; } catch (Exception) { } await Task.Delay(4000); }
        });
        return found;
    }

    // Closes only processes whose command line contains the UNIQUE temporary profile name (mrb_login_<guid>): never the user's own.
    static void KillByProfile(string profile)
    {
        try
        {
            string tag = Path.GetFileName(profile);
            using (var q = new System.Management.ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE CommandLine LIKE '%" + tag + "%'"))
                foreach (System.Management.ManagementObject o in q.Get())
                    try { Process.GetProcessById(Convert.ToInt32(o["ProcessId"])).Kill(); } catch (Exception) { }
        }
        catch (Exception ex) { Store.Log("KillByProfile error: " + ex.Message); }
    }

    static string CookieFrom(string reply)
    {
        try
        {
            var cookies = (Obj(reply)["result"] as Dictionary<string, object>)["cookies"] as object[];
            foreach (Dictionary<string, object> c in cookies)
                if ((string)c["name"] == ".ROBLOSECURITY" && ((string)c["domain"]).IndexOf("roblox.com", StringComparison.OrdinalIgnoreCase) >= 0 && ((string)c["value"]).Length > 40)
                    return (string)c["value"];
        }
        catch (Exception) { }
        return null;
    }

    // BiDi returns the value as an object: {"type":"string","value":"..."}
    static string CookieFromBidi(string reply)
    {
        try
        {
            var cookies = (Obj(reply)["result"] as Dictionary<string, object>)["cookies"] as object[];
            foreach (Dictionary<string, object> c in cookies)
            {
                string v = (string)((Dictionary<string, object>)c["value"])["value"];
                if ((string)c["name"] == ".ROBLOSECURITY" && ((string)c["domain"]).IndexOf("roblox.com", StringComparison.OrdinalIgnoreCase) >= 0 && v.Length > 40) return v;
            }
        }
        catch (Exception) { }
        return null;
    }
}

static class Tiler
{
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint action, uint p, out RECT r, uint win);
    [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
    struct RECT { public int Left, Top, Right, Bottom; }

    // n grid cells (columns = ceiling of the square root of n) that cover the area without overlapping.
    public static Rectangle[] Grid(Rectangle area, int n)
    {
        var r = new Rectangle[n];
        if (n == 0) return r;
        int cols = (int)Math.Ceiling(Math.Sqrt(n)), rows = (n + cols - 1) / cols;
        for (int i = 0; i < n; i++)
        {
            int c = i % cols, row = i / cols;
            int x0 = area.X + area.Width * c / cols, x1 = area.X + area.Width * (c + 1) / cols;
            int y0 = area.Y + area.Height * row / rows, y1 = area.Y + area.Height * (row + 1) / rows;
            r[i] = new Rectangle(x0, y0, x1 - x0, y1 - y0);
        }
        return r;
    }

    static int Rank(IList<int> order, int pid) { int i = order.IndexOf(pid); return i < 0 ? int.MaxValue : i; }

    // shortcut: primary monitor only; for multi-monitor setups, use the monitor MultiRoblox is on.
    public static int TileRoblox(IList<int> order)
    {
        IntPtr old = IntPtr.Zero; bool aware = false;
        try { old = SetThreadDpiAwarenessContext(new IntPtr(-4)); aware = true; } catch (Exception) { }   // real pixels: Roblox is DPI-aware, this program is not
        try
        {
            var pids = Roblox.ClientPids();
            var wins = new List<KeyValuePair<int, IntPtr>>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (pids.Contains((int)pid) && IsWindowVisible(h))
                {
                    var sb = new StringBuilder(64); GetWindowText(h, sb, 64);
                    if (sb.ToString() == "Roblox") wins.Add(new KeyValuePair<int, IntPtr>((int)pid, h));
                }
                return true;
            }, IntPtr.Zero);
            wins.Sort((a, b) => Rank(order, a.Key).CompareTo(Rank(order, b.Key)));   // same order as the account list
            RECT wa; SystemParametersInfo(0x30, 0, out wa, 0);   // SPI_GETWORKAREA: screen minus the taskbar
            var cells = Grid(new Rectangle(wa.Left, wa.Top, wa.Right - wa.Left, wa.Bottom - wa.Top), wins.Count);
            for (int i = 0; i < wins.Count; i++)
            {
                ShowWindow(wins[i].Value, 9);   // SW_RESTORE: leaves maximized/minimized
                SetWindowPos(wins[i].Value, IntPtr.Zero, cells[i].X, cells[i].Y, cells[i].Width, cells[i].Height, 0x0004 | 0x0040);   // NOZORDER | SHOWWINDOW
            }
            return wins.Count;
        }
        finally { if (aware && old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int L, R, T, B; }
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern bool AdjustWindowRectEx(ref RECT r, int style, bool menu, int exStyle);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

    // screen x/y coordinates from lParam (each signed: a monitor to the left has negative x)
    public static Point LParamPoint(IntPtr lp) { long v = lp.ToInt64(); return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF)); }
}

// Panel that leaves the window edge to the form (resizing): returns HTTRANSPARENT near the edge.
class EdgePanel : Panel
{
    public const int Edge = 7;
    public EdgePanel() { DoubleBuffered = true; }

    public static bool NearEdge(Control c, Point screen)
    {
        var f = c.FindForm();
        if (f == null || f.WindowState != FormWindowState.Normal) return false;
        var r = f.Bounds;
        return screen.X < r.Left + Edge || screen.X >= r.Right - Edge || screen.Y < r.Top + Edge || screen.Y >= r.Bottom - Edge;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84 && NearEdge(this, Native.LParamPoint(m.LParam))) { m.Result = (IntPtr)(-1); return; }
        base.WndProc(ref m);
    }
}

// Title bar button: minimize (0), maximize/restore (1), close (2). Glyphs are drawn, no font dependency.
class CaptionButton : Control
{
    public readonly int Kind;
    public bool Maxed, Hot;

    public CaptionButton(int kind)
    {
        Kind = kind; Size = new Size(46, 36);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hot = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84 && EdgePanel.NearEdge(this, Native.LParamPoint(m.LParam))) { m.Result = (IntPtr)(-1); return; }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Hot ? (Kind == 2 ? Color.FromArgb(232, 17, 35) : Color.FromArgb(54, 54, 66)) : Parent.BackColor);
        int cx = Width / 2, cy = Height / 2;
        using (var p = new Pen(Color.White, 1f))
        {
            if (Kind == 0) g.DrawLine(p, cx - 5, cy, cx + 5, cy);
            else if (Kind == 1 && !Maxed) g.DrawRectangle(p, cx - 5, cy - 5, 10, 10);
            else if (Kind == 1)   // restore: two overlapping squares
            {
                g.DrawRectangle(p, cx - 5, cy - 2, 7, 7);
                g.DrawLines(p, new[] { new Point(cx - 2, cy - 2), new Point(cx - 2, cy - 5), new Point(cx + 5, cy - 5), new Point(cx + 5, cy + 2), new Point(cx + 2, cy + 2) });
            }
            else { g.SmoothingMode = SmoothingMode.AntiAlias; g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5); g.DrawLine(p, cx - 5, cy + 5, cx + 5, cy - 5); }
        }
    }
}

// Custom title bar: icon + name. Returns HTTRANSPARENT everywhere, so the form answers HTCAPTION
// (dragging, double-click to maximize and edge snapping work natively).
class TitleBar : EdgePanel
{
    static readonly Font F = new Font("Segoe UI Semibold", 9f);
    public Image Logo;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Logo != null) e.Graphics.DrawImage(Logo, 12, (Height - 16) / 2, 16, 16);
        var f = FindForm();
        TextRenderer.DrawText(e.Graphics, f == null ? "" : f.Text, F, new Point(36, (Height - F.Height) / 2), Color.FromArgb(205, 205, 215));
    }
}

class MainForm : Form
{
    static readonly Color Bg = Color.FromArgb(18, 18, 22), Card = Color.FromArgb(28, 28, 35), CardSel = Color.FromArgb(44, 44, 68),
        Accent = Color.FromArgb(99, 102, 241), Green = Color.FromArgb(80, 220, 120), Orange = Color.FromArgb(255, 170, 60),
        Muted = Color.FromArgb(150, 150, 165), Red = Color.FromArgb(240, 90, 90);
    static readonly Font FAvatar = new Font("Segoe UI Semibold", 12f), FName = new Font("Segoe UI Semibold", 11f),
        FSub = new Font("Segoe UI", 8.5f), FNote = new Font("Segoe UI", 9f);

    readonly Mutex mutex = new Mutex(false, "ROBLOX_singletonMutex");
    readonly EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.ManualReset, "ROBLOX_singletonEvent");
    public static bool Demo;
    readonly List<Account> accounts = Demo ? DemoAccounts() : Store.Load();
    readonly ListBox list = new ListBox();
    readonly TextBox place = new TextBox();
    readonly Label pill = new Label(), clients = new Label(), msg = new Label();
    readonly Button btnAdd, btnNick, btnRemove, btnPlay, btnStar, btnTile, btnFollow, btnPres;
    readonly TitleBar bar = new TitleBar();
    readonly EdgePanel body = new EdgePanel();
    CaptionButton btnMin, btnMax, btnClose;
    readonly ComboBox favBox = new ComboBox();
    readonly List<KeyValuePair<long, string>> favs = Store.LoadFavs();
    string lastGameName = ""; long lastGameId; bool syncing;
    readonly PictureBox icon = new PictureBox();
    readonly Label gameName = new Label(), gameSub = new Label();
    readonly System.Windows.Forms.Timer lookup = new System.Windows.Forms.Timer();
    int lookupVer;
    readonly NotifyIcon tray = new NotifyIcon();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    CancellationTokenSource cts;
    bool owned, busy;

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--selftest") return SelfTest();
        if (args.Length > 1 && args[0] == "--makeico") return MakeIco(args[1]);   // generates the exe icon (build tool)
        if (args.Length > 0 && args[0] == "--demo") { Demo = true; Store.ReadOnly = true; }   // fake accounts, nothing written: for screenshots and the README
        if (args.Length > 0 && args[0] == "--logintest")   // opens the default browser with NO window and NO login, only checks the protocol
        {
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;
            var tcs = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            Roblox.BrowserLogin(tcs.Token, t => Store.Log("probe: " + t), true).GetAwaiter().GetResult();
            Thread.Sleep(15000);   // lets the background cleanup (closing the temporary browser) finish before exiting
            return Roblox.ProbeOk ? 0 : 10;
        }
        bool first;
        using (new Mutex(true, "MultiRoblox_SingleInstance", out first))
        {
            if (!first) { MessageBox.Show("MultiRoblox is already running (check the system tray).", "MultiRoblox"); return 0; }
            System.Net.ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)3072;   // TLS 1.2: Roblox rejects the .NET 4 default
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }
        return 0;
    }

    // Checks the window grid: right size, everything inside the area, nothing overlapping. Usage: MultiRoblox.exe --selftest
    static int SelfTest()
    {
        var area = new Rectangle(0, 40, 1920, 1000);
        for (int n = 1; n <= 9; n++)
        {
            var g = Tiler.Grid(area, n);
            if (g.Length != n) return 1;
            for (int i = 0; i < n; i++)
            {
                if (!area.Contains(g[i])) return 2;
                for (int j = i + 1; j < n; j++) if (g[i].IntersectsWith(g[j])) return 3;
            }
        }
        return 0;
    }

    static Button MakeButton(string text, Color back)
    {
        var b = new Button { Text = text, BackColor = back, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = new Font("Segoe UI Semibold", 10f) };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    static List<Account> DemoAccounts()
    {
        return new List<Account>
        {
            new Account { Id = 1001, Name = "main_account", Nick = "Main", Cookie = "", PresType = 2, Presence = "in game: Rogue Lineage", Note = "open" },
            new Account { Id = 1002, Name = "alt_farm_01", Nick = "Farm 1", Cookie = "", PresType = 2, Presence = "in game: Rogue Lineage", Note = "open" },
            new Account { Id = 1003, Name = "alt_farm_02", Nick = "Farm 2", Cookie = "", PresType = 1, Presence = "online (website)" },
            new Account { Id = 1004, Name = "alt_backup", Cookie = "", PresType = 0, Presence = "offline" },
        };
    }

    // The program logo, drawn at any size: used for the window, tray, title bar and the exe icon.
    static Bitmap Logo(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var r = new Rectangle(0, 0, s - 1, s - 1);
            using (var p = RoundRect(r, (int)(s * 0.44))) using (var br = new LinearGradientBrush(r, Color.FromArgb(129, 140, 248), Color.FromArgb(79, 70, 229), 45f)) g.FillPath(br, p);
            using (var f = new Font("Segoe UI", s * 0.52f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("M", f, Brushes.White, new RectangleF(0, s * 0.03f, s, s), sf);
        }
        return bmp;
    }

    static int MakeIco(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var png = new List<byte[]>();
        foreach (int sz in sizes)
            using (var b = Logo(sz)) using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); png.Add(ms.ToArray()); }
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int off = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte d = (byte)(sizes[i] == 256 ? 0 : sizes[i]);   // 0 = 256 in the ICO format
                w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(png[i].Length); w.Write(off); off += png[i].Length;
            }
            foreach (var p in png) w.Write(p);
        }
        return 0;
    }

    // Window without the Windows frame: keeps resizing, animated minimize and edge snapping.
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.Style |= 0x00040000 | 0x00020000 | 0x00010000; return cp; }   // THICKFRAME | MINIMIZEBOX | MAXIMIZEBOX
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var mg = new Native.MARGINS { L = 1, R = 1, T = 1, B = 1 };
        Native.DwmExtendFrameIntoClientArea(Handle, ref mg);   // shadow and rounded corners on Windows 11
    }

    // Without the Windows frame, WinForms still adds the frame (7 px per side) to the "normal" size when the window is
    // restored, and it would grow 14 px on every maximize/restore. frameExtra is exactly that extra: we undo only it.
    Size normalSize = new Size(562, 578), frameExtra;

    void FixRestoredSize()
    {
        if (WindowState != FormWindowState.Normal) return;
        if (Size == normalSize + frameExtra) Size = normalSize;   // fallback: the extra slipped past WM_WINDOWPOSCHANGING
        else normalSize = Size;   // size chosen by the user (or the initial one): remembered for the next restore
    }

    int HitTest(Point screen)
    {
        var p = PointToClient(screen);
        if (WindowState == FormWindowState.Normal)
        {
            int e = EdgePanel.Edge;
            bool l = p.X < e, r = p.X >= ClientSize.Width - e, t = p.Y < e, b = p.Y >= ClientSize.Height - e;
            if (t && l) return 13; if (t && r) return 14; if (b && l) return 16; if (b && r) return 17;
            if (l) return 10; if (r) return 11; if (t) return 12; if (b) return 15;
        }
        return p.Y < bar.Bottom ? 2 : 1;   // HTCAPTION : HTCLIENT
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x46)   // WM_WINDOWPOSCHANGING: the "normal + frame" size WinForms would ask for becomes the normal size
        {
            var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
            if ((wp.flags & 0x1) == 0 && wp.cx == normalSize.Width + frameExtra.Width && wp.cy == normalSize.Height + frameExtra.Height)   // 0x1 = SWP_NOSIZE
            {
                wp.cx = normalSize.Width; wp.cy = normalSize.Height;
                Marshal.StructureToPtr(wp, m.LParam, false);
            }
        }
        if (m.Msg == 0x83 && m.WParam != IntPtr.Zero) { m.Result = IntPtr.Zero; return; }   // WM_NCCALCSIZE: client area = the whole window
        if (m.Msg == 0x84) { m.Result = (IntPtr)HitTest(Native.LParamPoint(m.LParam)); return; }   // WM_NCHITTEST
        if (m.Msg == 0x24)   // WM_GETMINMAXINFO: maximize only up to the work area (otherwise it covers the taskbar)
        {
            base.WndProc(ref m);
            var mmi = (Native.MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(Native.MINMAXINFO));
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            if (Native.GetMonitorInfo(Native.MonitorFromWindow(Handle, 2), ref mi))
            {
                mmi.ptMaxPosition = new Native.POINT { X = mi.rcWork.Left - mi.rcMonitor.Left, Y = mi.rcWork.Top - mi.rcMonitor.Top };
                mmi.ptMaxSize = new Native.POINT { X = mi.rcWork.Right - mi.rcWork.Left, Y = mi.rcWork.Bottom - mi.rcWork.Top };
                Marshal.StructureToPtr(mmi, m.LParam, true);
            }
            return;
        }
        base.WndProc(ref m);
    }

    // Dark title bar for the small dialog windows too (Windows 10 2004+ and 11).
    static void DarkTitle(Form f)
    {
        f.HandleCreated += delegate
        {
            int on = 1, color = 18 | (18 << 8) | (22 << 16);   // COLORREF 0x00BBGGRR = program background
            Native.DwmSetWindowAttribute(f.Handle, 20, ref on, 4);      // DWMWA_USE_IMMERSIVE_DARK_MODE
            Native.DwmSetWindowAttribute(f.Handle, 35, ref color, 4);   // DWMWA_CAPTION_COLOR (Windows 11 only)
        };
    }

    MainForm()
    {
        Text = "MultiRoblox  v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(562, 578);   // 560 of content + 1 px border on each side; 36 for the bar + 540 for the body
        MinimumSize = new Size(562, 578);
        Icon = Icon.FromHandle(Logo(32).GetHicon());
        BackColor = Color.FromArgb(52, 52, 64);   // the form background color becomes the 1 px border
        StartPosition = FormStartPosition.CenterScreen;

        pill.SetBounds(318, 14, 114, 28); pill.TextAlign = ContentAlignment.MiddleCenter; pill.Font = new Font("Segoe UI Semibold", 10f);
        pill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        clients.SetBounds(20, 18, 290, 20); clients.ForeColor = Muted;

        list.SetBounds(20, 56, 520, 246);
        list.BackColor = Bg; list.BorderStyle = BorderStyle.None; list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = 54;
        list.SelectionMode = SelectionMode.MultiExtended;
        list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        list.DrawItem += DrawAccount;

        const AnchorStyles BL = AnchorStyles.Bottom | AnchorStyles.Left;
        icon.SetBounds(20, 312, 48, 48); icon.SizeMode = PictureBoxSizeMode.StretchImage; icon.BackColor = Card; icon.Anchor = BL;
        gameName.SetBounds(78, 314, 462, 24); gameName.Font = new Font("Segoe UI Semibold", 11f); gameName.AutoEllipsis = true; gameName.Anchor = BL | AnchorStyles.Right;
        gameSub.SetBounds(78, 340, 462, 20); gameSub.ForeColor = Muted; gameSub.Anchor = BL | AnchorStyles.Right;

        var lbl = new Label { Text = "Place ID", ForeColor = Muted, AutoSize = true, Location = new Point(20, 375), Anchor = BL };
        place.SetBounds(82, 370, 170, 26); place.Text = Store.LoadPlace().Length > 0 ? Store.LoadPlace() : "3016661674";
        place.BackColor = Card; place.ForeColor = Color.White; place.BorderStyle = BorderStyle.FixedSingle; place.Anchor = BL;
        btnStar = MakeButton("\u2606", Card); btnStar.SetBounds(258, 370, 32, 26); btnStar.Font = new Font("Segoe UI Symbol", 11f); btnStar.Anchor = BL;
        favBox.SetBounds(296, 370, 244, 26); favBox.DropDownStyle = ComboBoxStyle.DropDownList; favBox.FlatStyle = FlatStyle.Flat;
        favBox.BackColor = Card; favBox.ForeColor = Color.White; favBox.Anchor = BL | AnchorStyles.Right;
        favBox.DrawMode = DrawMode.OwnerDrawFixed; favBox.ItemHeight = 20;
        favBox.DrawItem += delegate(object o, DrawItemEventArgs e)   // the stock ComboBox ignores the dark theme
        {
            bool hot = e.Index >= 0 && (e.State & DrawItemState.ComboBoxEdit) == 0 && (e.State & DrawItemState.Selected) != 0;
            using (var br = new SolidBrush(hot ? CardSel : Card)) e.Graphics.FillRectangle(br, e.Bounds);
            string t = e.Index >= 0 ? (string)favBox.Items[e.Index] : "Favorites";
            TextRenderer.DrawText(e.Graphics, t, FNote, new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), e.Index >= 0 ? Color.White : Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        RefreshFavBox();

        btnAdd = MakeButton("+ Add account", Card); btnAdd.SetBounds(20, 408, 150, 38);
        btnNick = MakeButton("Nickname", Card); btnNick.SetBounds(178, 408, 96, 38);
        btnRemove = MakeButton("Remove", Card); btnRemove.SetBounds(282, 408, 96, 38);
        btnTile = MakeButton("Tile windows", Card); btnTile.SetBounds(386, 408, 154, 38);
        btnFollow = MakeButton("Follow user...", Card); btnFollow.SetBounds(20, 454, 232, 42);
        btnPlay = MakeButton("\u25B6  Play", Accent); btnPlay.SetBounds(260, 454, 280, 42);
        btnPres = MakeButton("\u21BB Status", Card); btnPres.SetBounds(440, 14, 100, 28); btnPres.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        foreach (var b in new[] { btnAdd, btnNick, btnRemove, btnTile, btnFollow }) b.Anchor = BL;
        btnPlay.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        msg.SetBounds(20, 506, 520, 20); msg.ForeColor = Muted; msg.Anchor = BL;

        bar.SetBounds(1, 1, 560, 36); bar.BackColor = Color.FromArgb(24, 24, 30); bar.Logo = Logo(16);
        bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        btnMin = new CaptionButton(0); btnMax = new CaptionButton(1); btnClose = new CaptionButton(2);
        btnMin.Location = new Point(560 - 138, 0); btnMax.Location = new Point(560 - 92, 0); btnClose.Location = new Point(560 - 46, 0);
        foreach (var cb in new[] { btnMin, btnMax, btnClose }) cb.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        bar.Controls.AddRange(new Control[] { btnMin, btnMax, btnClose });
        btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
        btnMax.Click += delegate { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; };
        btnClose.Click += delegate { Close(); };
        body.SetBounds(1, 37, 560, 540); body.BackColor = Bg; body.ForeColor = Color.White; body.Font = Font;
        body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        Controls.Add(body); Controls.Add(bar);
        body.Controls.AddRange(new Control[] { pill, clients, btnPres, list, icon, gameName, gameSub, lbl, place, btnStar, favBox, btnAdd, btnNick, btnRemove, btnTile, btnFollow, btnPlay, msg });
        btnAdd.Click += delegate { AddAccount(); };
        btnRemove.Click += delegate { RemoveSelected(); };
        btnNick.Click += delegate { RenameSelected(); };
        list.DoubleClick += delegate { RenameSelected(); };
        lookup.Interval = 600; lookup.Tick += delegate { lookup.Stop(); LookupGame(); };   // wait until typing stops
        place.TextChanged += delegate { lookup.Stop(); lookup.Start(); SyncFavUi(); };
        btnPlay.Click += delegate { PlaySelected(); };
        btnFollow.Click += delegate { FollowSelected(); };
        btnTile.Click += delegate { TileClick(); };
        btnPres.Click += delegate { RefreshPresence(); };
        btnStar.Click += delegate { ToggleFav(); };
        favBox.SelectedIndexChanged += delegate { if (!syncing && favBox.SelectedIndex >= 0) place.Text = favs[favBox.SelectedIndex].Key.ToString(); };

        tray.Icon = Icon.FromHandle(Logo(16).GetHicon()); tray.Visible = true;
        tray.DoubleClick += delegate { Restore(); };
        var menu = new ContextMenu();
        menu.MenuItems.Add("Show", delegate { Restore(); });
        menu.MenuItems.Add("Exit", delegate { Close(); });
        tray.ContextMenu = menu;

        var fr = new Native.RECT();
        Native.AdjustWindowRectEx(ref fr, CreateParams.Style, false, CreateParams.ExStyle);
        frameExtra = new Size(fr.Right - fr.Left, fr.Bottom - fr.Top);
        Resize += delegate { FixRestoredSize(); btnMax.Maxed = WindowState == FormWindowState.Maximized; btnMax.Invalidate(); if (WindowState == FormWindowState.Minimized) Hide(); };
        FormClosed += delegate
        {
            tray.Visible = false; timer.Stop(); if (cts != null) cts.Cancel();
            if (owned) mutex.ReleaseMutex();
            mutex.Dispose(); ev.Dispose();
        };

        RefreshList();
        timer.Interval = 1000; timer.Tick += delegate { Tick(); }; timer.Start();
        Tick();
        Say(accounts.Count == 0 ? "Click \"+ Add account\" to get started." : "Double-click an account to set a nickname.");
        LookupGame();
        SyncFavUi();
        if (!Demo) ValidateAll();   // demo: no network to validate fake accounts
    }

    async void ValidateAll()
    {
        foreach (var a in accounts.ToList())
        {
            int st = await Roblox.CookieState(a.Cookie);
            if (IsDisposed) return;
            if (st == 0) { a.Note = "! login expired - add it again"; list.Invalidate(); }   // -1 (network) marks nothing
            await Task.Delay(400);   // gap between accounts: avoids the rate limit
        }
        RefreshPresence();
    }

    void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    void Say(string s, bool bad = false) { msg.Text = s; msg.ForeColor = bad ? Red : Muted; }

    void RefreshList()
    {
        list.BeginUpdate(); list.Items.Clear();
        foreach (var a in accounts) list.Items.Add(a);
        list.EndUpdate();
    }

    void DrawAccount(object s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var a = (Account)list.Items[e.Index];
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Bg)) g.FillRectangle(bg, e.Bounds);
        var row = new Rectangle(e.Bounds.X, e.Bounds.Y + 3, e.Bounds.Width - 2, e.Bounds.Height - 6);
        using (var p = RoundRect(row, 10)) using (var br = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? CardSel : Card)) g.FillPath(br, p);
        var dot = new Rectangle(row.X + 10, row.Y + 8, 32, 32);
        using (var br = new SolidBrush(Accent)) g.FillEllipse(br, dot);
        TextRenderer.DrawText(g, a.Shown.Substring(0, 1).ToUpper(), FAvatar, dot, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, a.Shown, FName, new Point(row.X + 52, row.Y + 6), Color.White);
        TextRenderer.DrawText(g, (a.Nick.Length > 0 ? a.Name + "  \u00B7  " : "") + "ID " + a.Id, FSub, new Point(row.X + 52, row.Y + 27), Muted);
        var right = new Rectangle(row.X + 200, row.Y + 6, row.Width - 210, 20);
        if (a.Presence.Length > 0)
            TextRenderer.DrawText(g, "\u25CF " + a.Presence, FNote, right, a.PresType == 2 ? Green : a.PresType == 1 ? Accent : Muted, TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
        if (a.Note.Length > 0)
            TextRenderer.DrawText(g, a.Note, FNote, new Rectangle(right.X, row.Y + 26, right.Width, 20), a.Note.StartsWith("!") ? Red : Green, TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
    }

    static GraphicsPath RoundRect(Rectangle r, int d)
    {
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    void Tick()
    {
        // Always try to grab the lock (same thread as the UI): if a Roblox already holds it, wait for it to close.
        if (!owned)
        {
            try { owned = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
        }
        var pids = Roblox.ClientPids();
        int n = pids.Count;
        bool changed = false;
        foreach (var a in accounts)
            if (a.Pid != 0 && !pids.Contains(a.Pid)) { a.Pid = 0; if (a.Note == "open") a.Note = ""; changed = true; }
        if (changed) list.Invalidate();
        bool shown = owned || Demo;
        if (Demo) n = 2;
        clients.Text = "Open clients: " + n + (shown ? "" : "  -  close them all to enable");
        pill.Text = shown ? "MULTI ON" : "WAITING";
        pill.BackColor = shown ? Color.FromArgb(24, 70, 44) : Color.FromArgb(80, 56, 20);
        pill.ForeColor = shown ? Green : Orange;
        tray.Text = "MultiRoblox - " + (shown ? "ON" : "WAITING") + " (" + n + ")";
    }

    void SetBusy(bool b) { busy = b; btnAdd.Enabled = btnNick.Enabled = btnRemove.Enabled = btnPlay.Enabled = btnFollow.Enabled = !b; }

    async void AddAccount()
    {
        if (busy) return;
        SetBusy(true);
        cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            Account a = await Roblox.BrowserLogin(cts.Token, s => Say(s));
            if (a == null) { Say("Login cancelled."); return; }
            accounts.RemoveAll(x => x.Id == a.Id);   // same account again = just refresh the cookie
            accounts.Add(a);
            Store.Save(accounts);
            RefreshList();
            Say("Account " + a.Name + " added.");
        }
        catch (Exception ex) { Store.Log("AddAccount error: " + ex); Say("Error: " + ex.Message, true); }
        finally { SetBusy(false); }
    }

    async void LookupGame()
    {
        int ver = ++lookupVer;
        long id;
        if (!long.TryParse(place.Text.Trim(), out id) || id <= 0) { SetGame(null, "Enter the Place ID (numbers only)"); return; }
        SetGame(null, "Looking up game...");
        Roblox.GameInfo g = await Roblox.Game(id);
        if (ver != lookupVer) return;   // a stale reply for an old ID is discarded
        SetGame(g, "Game not found - check the ID");
        lastGameId = g != null ? id : 0; lastGameName = g != null ? Clean(g.Name) : "";
    }

    void SetGame(Roblox.GameInfo g, string fallback)
    {
        var old = icon.Image;
        icon.Image = g != null ? g.Icon : null;
        if (old != null) old.Dispose();
        gameName.Text = g != null ? Clean(g.Name) : fallback;
        gameSub.Text = g != null ? g.Playing.ToString("N0") + " playing now" : "";
    }

    // The Windows font cannot draw emoji (they become boxes): strips emoji and the brackets left empty.
    public static string Clean(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
            if (!char.IsSurrogate(c) && c != '\uFE0F' && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.OtherSymbol) sb.Append(c);
        string r = System.Text.RegularExpressions.Regex.Replace(sb.ToString().Replace("[]", "").Replace("()", ""), @"\s+", " ").Trim();
        return r.Length > 0 ? r : s;
    }

    static string Ask(string title, string label, string current)
    {
        using (var f = new Form { Text = title, ClientSize = new Size(340, 120), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, BackColor = Bg, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f) })
        {
            DarkTitle(f);
            var l = new Label { Text = label, AutoSize = true, Location = new Point(16, 14), ForeColor = Muted };
            var t = new TextBox { Text = current, MaxLength = 24, Location = new Point(16, 40), Width = 308, BackColor = Card, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            var ok = MakeButton("Save", Accent); ok.SetBounds(224, 74, 100, 32); ok.DialogResult = DialogResult.OK;
            f.Controls.AddRange(new Control[] { l, t, ok });
            f.AcceptButton = ok;
            return f.ShowDialog() == DialogResult.OK ? t.Text.Trim() : null;
        }
    }

    void RenameSelected()
    {
        var a = list.SelectedItems.Cast<Account>().FirstOrDefault();
        if (a == null) { Say("Select an account to set a nickname.", true); return; }
        string n = Ask("Nickname", "Nickname for " + a.Name + " (empty = use the Roblox name)", a.Nick);
        if (n == null) return;
        a.Nick = n.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');   // the file uses tab/newline as separators
        Store.Save(accounts);
        list.Invalidate();
        Say(a.Nick.Length > 0 ? "Nickname saved." : "Nickname removed.");
    }

    void RemoveSelected()
    {
        var sel = list.SelectedItems.Cast<Account>().ToList();
        if (sel.Count == 0) return;
        if (MessageBox.Show("Remove " + sel.Count + " account(s) from this program? (the Roblox account is not affected)", "MultiRoblox", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        accounts.RemoveAll(sel.Contains);
        Store.Save(accounts);
        RefreshList();
    }

    void PlaySelected()
    {
        if (busy) return;
        long placeId;
        if (!long.TryParse(place.Text.Trim(), out placeId) || placeId <= 0) { Say("Invalid Place ID (numbers only).", true); return; }
        Store.SavePlace(placeId.ToString());
        LaunchSelected(placeId, 0, null);
    }

    // Joins the game/server where the user is (a friend or one of your other accounts). Roblox requires friendship or permission to join.
    async void FollowSelected()
    {
        if (busy) return;
        string who = AskUser(accounts.Select(a => a.Name).ToList());
        if (string.IsNullOrEmpty(who)) return;
        SetBusy(true);
        long uid = 0;
        try
        {
            var own = accounts.FirstOrDefault(a => a.Name.Equals(who, StringComparison.OrdinalIgnoreCase));
            uid = own != null ? own.Id : await Roblox.UserId(who);
        }
        finally { SetBusy(false); }
        if (uid <= 0) { Say("User \"" + who + "\" not found.", true); return; }
        LaunchSelected(0, uid, who);
    }

    static string AskUser(List<string> names)
    {
        using (var f = new Form { Text = "Follow user", ClientSize = new Size(340, 140), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false, MinimizeBox = false, BackColor = Bg, ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f) })
        {
            DarkTitle(f);
            var l = new Label { Text = "User name or ID to follow (pick one of your accounts or type one). Must be a friend or allow joining.", Size = new Size(308, 36), Location = new Point(16, 12), ForeColor = Muted };
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Location = new Point(16, 54), Width = 308, FlatStyle = FlatStyle.Flat, BackColor = Card, ForeColor = Color.White };
            c.Items.AddRange(names.Cast<object>().ToArray());
            var ok = MakeButton("Join", Accent); ok.SetBounds(224, 94, 100, 32); ok.DialogResult = DialogResult.OK;
            f.Controls.AddRange(new Control[] { l, c, ok });
            f.AcceptButton = ok;
            return f.ShowDialog() == DialogResult.OK ? c.Text.Trim() : null;
        }
    }

    async void LaunchSelected(long placeId, long followId, string followName)
    {
        var sel = list.SelectedItems.Cast<Account>().ToList();
        if (followId > 0) sel.RemoveAll(a => a.Id == followId);   // do not follow yourself
        if (sel.Count == 0) { Say(followId > 0 ? "Select accounts other than " + followName + "." : "Select one or more accounts (Ctrl/Shift for several).", true); return; }
        if (!Roblox.Installed()) { Say("Roblox is not installed (the roblox-player protocol is missing). Install it from roblox.com.", true); return; }
        if (!owned && Process.GetProcessesByName("RobloxPlayerBeta").Length > 0 &&
            MessageBox.Show("Multi-client is NOT active: opening another account will close the Roblox that is already open. Continue?", "MultiRoblox", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        SetBusy(true);
        int opened = 0;
        try
        {
            for (int i = 0; i < sel.Count; i++)
            {
                var a = sel[i];
                a.Note = "opening..."; list.Invalidate();
                string ticket = await Roblox.Ticket(a.Cookie);
                if (ticket == null) { a.Note = "! login expired - add it again"; list.Invalidate(); continue; }
                var before = Roblox.ClientPids();
                Roblox.Launch(ticket, placeId, followId);
                Store.Log("launch sent to " + a.Name + (followId > 0 ? " following " + followId : " place " + placeId));
                int pid = 0;
                for (int w = 0; w < 90 && pid == 0; w++)   // wait up to ~45 s for the client to appear
                {
                    await Task.Delay(500);
                    pid = Roblox.ClientPids().Except(before).FirstOrDefault();
                }
                if (pid == 0) { a.Note = "! Roblox did not open"; Store.Log("no new RobloxPlayerBeta for " + a.Name); list.Invalidate(); continue; }
                a.Pid = pid; a.Note = "open"; opened++; list.Invalidate();
                if (i < sel.Count - 1) await Task.Delay(4000);   // gap between clients: avoids Roblox's rate limit
            }
            Say(opened == sel.Count ? "Done." : "Opened " + opened + " of " + sel.Count + " (see the accounts in red).", opened != sel.Count);
        }
        catch (Exception ex) { Store.Log("Launch error: " + ex); Say("Error: " + ex.Message, true); }
        finally { SetBusy(false); }
    }

    // ---- favorites ----
    void RefreshFavBox()
    {
        favBox.Items.Clear();
        foreach (var f in favs) favBox.Items.Add(f.Value);
    }

    long PlaceNow() { long id; return long.TryParse(place.Text.Trim(), out id) ? id : 0; }

    void SyncFavUi()
    {
        int idx = favs.FindIndex(f => f.Key == PlaceNow());
        syncing = true;
        favBox.SelectedIndex = idx;   // -1 = none
        syncing = false;
        btnStar.Text = idx >= 0 ? "\u2605" : "\u2606";
        btnStar.ForeColor = idx >= 0 ? Orange : Color.White;
    }

    void ToggleFav()
    {
        long id = PlaceNow();
        if (id <= 0) { Say("Enter a valid Place ID to favorite it.", true); return; }
        int idx = favs.FindIndex(f => f.Key == id);
        if (idx >= 0) favs.RemoveAt(idx);
        else favs.Add(new KeyValuePair<long, string>(id, lastGameId == id && lastGameName.Length > 0 ? lastGameName : "Place " + id));
        Store.SaveFavs(favs);
        RefreshFavBox();
        SyncFavUi();
        Say(idx >= 0 ? "Removed from favorites." : "Added to favorites.");
    }

    // ---- online status (only when the program opens or Status is clicked; uses each account's cookie) ----
    async void RefreshPresence()
    {
        btnPres.Enabled = false;
        try
        {
            foreach (var a in accounts.ToList())
            {
                if (a.Note.Contains("expired")) continue;   // dead cookie: do not even try
                await Roblox.Presence(a);
                if (IsDisposed) return;
                list.Invalidate();
                await Task.Delay(300);
            }
        }
        finally { if (!IsDisposed) btnPres.Enabled = true; }
    }

    void TileClick()
    {
        try
        {
            int n = Tiler.TileRoblox(accounts.Where(a => a.Pid != 0).Select(a => a.Pid).ToList());
            Say(n == 0 ? "No Roblox windows found." : "Tiled " + n + " window(s).", n == 0);
        }
        catch (Exception ex) { Store.Log("Tile error: " + ex); Say("Error tiling windows: " + ex.Message, true); }
    }
}

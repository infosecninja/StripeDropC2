# Security Policy

## Purpose & Scope

StripeDropC2 is a security research tool designed for **authorized penetration testing and red team operations only**. This document outlines security considerations, responsible use, and how to report vulnerabilities in the tool itself.

## Responsible Use

### Legal Requirements

Before using StripeDropC2, you MUST have:

1. **Written authorization** from the system owner
2. **Defined scope** of what systems can be tested
3. **Clear rules of engagement** with the client/organization
4. **Proper insurance** for professional penetration testing
5. **Legal counsel review** if operating across jurisdictions

### Prohibited Uses

Deploying against systems without explicit written permission 
Using in production Stripe accounts (financial fraud) 
Accessing, modifying, or exfiltrating data without authorization 
Causing damage, disruption, or unauthorized changes 
Violating laws including CFAA, ECPA, or equivalent in your jurisdiction 

### Recommended Uses

Internal security testing in isolated lab environments 
Authorized red team engagements with signed contracts 
Security research and defensive technique development 
Educational demonstrations in controlled settings 
Proof-of-concept development for novel C2 techniques 

## Security Considerations for Operators

### Protecting Your Infrastructure

1. **API Key Security**
- Never commit `c2_config.py` with real API keys
- Use Stripe **test mode** keys only (`sk_test_...`)
- Rotate keys regularly
- Never use live mode (creates financial audit trail)

2. **Operator Isolation**
- Run operator console on dedicated/ephemeral infrastructure
- Use VPN or proxy for additional operational security
- Avoid running from personal networks or machines
- Clear logs and artifacts after operations

3. **Implant Handling**
- Regenerate XOR key before each build: `python3 regenerate_key.py`
- Store compiled implants securely, encrypt at rest
- Use code signing if deploying in realistic environments
- Delete local builds after deployment

4. **Data Protection**
- Exfiltrated data may contain sensitive information
- Screenshots saved to `./screenshots/` - protect this directory
- Downloaded files in `./downloads/` - encrypt if needed
- Clean up Stripe objects regularly with `clear` command

### OpSec Failures to Avoid

**Using personal Stripe accounts** - creates direct attribution 
**Leaving test API keys in public repositories** - instant compromise 
**Excessive polling rates** - triggers detection and rate limits 
**Ignoring cleanup** - leaves forensic artifacts on Stripe 
**Predictable timing patterns** - use jittered intervals 

## Detection & Defense

### How to Detect This Tool

Organizations defending against StripeDropC2-style attacks should:

1. **Network Monitoring**
- Alert on Stripe API calls from non-payment systems
- Monitor for high-frequency Customer object creation
- Track metadata-heavy operations

2. **Endpoint Detection**
- Flag executables named `stripe.exe` or `MsMpEng.exe`
- Detect persistence mechanisms (Run keys, scheduled tasks)
- Monitor unusual paths: `%APPDATA%\Microsoft\Windows\WinSxS\`

3. **Cloud Logging**
- Enable Stripe webhook logging
- Monitor unusual Customer object patterns
- Alert on metadata operations with encrypted-looking content

4. **Behavioral Analysis**
- Identify regular heartbeat patterns
- Detect anomalous API usage from unexpected processes
- Correlate with other endpoint indicators

### Defensive Mitigations

- Application whitelisting (only approved apps can make Stripe calls)
- Network segmentation (financial systems isolated)
- API access controls (restrict which keys can create customers)
- Regular Stripe dashboard audits for unusual objects
- EDR rules to detect C2-like communication patterns

## Reporting Security Vulnerabilities

### In This Tool

If you discover a security vulnerability in StripeDropC2 itself (not in a target system):

**DO:**
- Report privately via GitHub Security Advisories
- Include detailed reproduction steps
- Suggest potential fixes if possible
- Allow reasonable time for patching before public disclosure

**DON'T:**
- Open public issues for security vulnerabilities
- Exploit vulnerabilities against others' infrastructure
- Demand payment or ransom for disclosure

### Scope

We consider these in scope for vulnerability reporting:

Authentication bypasses in operator console 
Code injection in implant build process 
Unintended data leakage from operator to targets 
Privilege escalation in implant 
Cryptographic weaknesses in XOR implementation 

Out of scope:

Vulnerabilities in Stripe's infrastructure (report to Stripe) 
Social engineering attack vectors 
Physical security issues 
Denial of service against test infrastructure 

### Response Timeline

- **Initial Response**: Within 7 days
- **Validation**: Within 14 days 
- **Patch Development**: Within 30-60 days (complexity dependent)
- **Public Disclosure**: After patch release + 14 days

### Recognition

Security researchers who responsibly disclose vulnerabilities will be:
- Credited in release notes (with permission)
- Listed in a Hall of Fame (if desired)
- Provided with technical details of the fix

## Security Checklist for Users

Before deploying StripeDropC2:

- [ ] Written authorization obtained and reviewed by legal counsel
- [ ] Test mode Stripe API key configured (not live mode)
- [ ] XOR encryption key regenerated with `regenerate_key.py`
- [ ] Appropriate stealth profile selected for environment
- [ ] Operator infrastructure isolated and secured
- [ ] Egress filtering bypassed or approved by client
- [ ] Data handling procedures defined for exfiltrated information
- [ ] Cleanup procedures planned (Stripe objects, local artifacts)
- [ ] Defensive team notified (if blue team engagement)
- [ ] Post-engagement report template prepared

## Ethical Guidelines

As security professionals, we commit to:

1. **Authorization First**: Never deploy without explicit permission
2. **Minimize Impact**: Avoid disrupting production systems
3. **Responsible Disclosure**: Report vulnerabilities to vendors privately
4. **Continuous Learning**: Share defensive knowledge with community
5. **Legal Compliance**: Respect all applicable laws and regulations

## Additional Resources

- [MITRE ATT&CK - Command and Control](https://attack.mitre.org/tactics/TA0011/)
- [PTES - Penetration Testing Execution Standard](http://www.pentest-standard.org/)
- [OWASP Testing Guide](https://owasp.org/www-project-web-security-testing-guide/)
- [Red Team Field Manual](https://www.amazon.com/Rtfm-Red-Team-Field-Manual/dp/1494295504)

## Contact

For security concerns: [Create a GitHub Security Advisory](https://github.com/infosecninja/StripeDropC2/security/advisories/new)

For general questions: [Open a GitHub Issue](https://github.com/infosecninja/StripeDropC2/issues/new)

---

**Remember**: With great power comes great responsibility. Use this tool ethically, legally, and professionally.

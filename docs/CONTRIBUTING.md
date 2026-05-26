# Contributing to StripeDropC2

First off, thank you for considering contributing to StripeDropC2! This project exists to advance security research and improve defensive capabilities through offensive tool development.

## Project Vision

StripeDropC2 demonstrates creative abuse of legitimate cloud APIs for command and control. Contributions should align with these goals:

1. **Educational Value**: Teach offensive and defensive security concepts
2. **Research Quality**: Advance the state of C2 covert channels
3. **Responsible Disclosure**: Balance offensive capability with defensive awareness
4. **Legal Compliance**: All contributions must be for lawful security research

## Code of Conduct

### Core Principles

**DO:**
- Share knowledge that helps defenders and attackers alike
- Write clear, well-documented code
- Test thoroughly in isolated environments
- Respect intellectual property and licensing
- Report vulnerabilities responsibly
- Help others learn and grow

**DON'T:**
- Share exploits for actively vulnerable systems without vendor notification
- Encourage or facilitate illegal activity
- Include malicious code beyond the stated C2 functionality
- Dox, harass, or discriminate against contributors
- Plagiarize code without proper attribution

### Enforcement

Violations will result in:
1. Warning and request for correction
2. Temporary ban from project (if repeated)
3. Permanent ban (for severe violations)

Report violations to: [maintainer email/contact]

## Getting Started

### Prerequisites

**For Code Contributions:**
- Python 3.8+ for operator development
- .NET 10.0 SDK for implant development
- Git for version control
- Stripe test account for testing

**For Documentation:**
- Markdown knowledge
- Understanding of C2 concepts
- Clear, technical writing skills

### Setting Up Development Environment

1. **Fork the repository**
```bash
# Via GitHub web interface
# Click "Fork" at https://github.com/infosecninja/StripeDropC2
```

2. **Clone your fork**
```bash
git clone https://github.com/YourUsername/StripeDropC2.git
cd StripeDropC2
```

3. **Add upstream remote**
```bash
git remote add upstream https://github.com/original/StripeDropC2.git
```

4. **Create development branch**
```bash
git checkout -b feature/your-feature-name
```

5. **Set up environment**
```bash
# Python
python3 -m venv venv
source venv/bin/activate
pip install stripe

# .NET
dotnet restore Implant.csproj

# Create test config
cp c2_config.py.example c2_config.py
# Add your Stripe test key
```

## Development Workflow

### Branch Naming

Use descriptive branch names:
- `feature/add-persistence-method` - New features
- `bugfix/fix-upload-timeout` - Bug fixes
- `docs/update-usage-guide` - Documentation updates
- `refactor/optimize-polling` - Code refactoring
- `security/patch-xss` - Security fixes

### Commit Messages

Follow conventional commits:

```
type(scope): description

[optional body]

[optional footer]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation change
- `style`: Code style/formatting (no logic change)
- `refactor`: Code restructuring (no behavior change)
- `test`: Adding/updating tests
- `chore`: Maintenance tasks

**Examples:**
```bash
feat(operator): add multi-implant command broadcasting

fix(implant): correct screenshot JPEG encoding on multi-monitor setups

docs(readme): clarify stealth profile selection guidance

security(implant): patch command injection in path handling
```

### Code Style

**Python:**
```python
# Follow PEP 8
# Use descriptive variable names
# Add docstrings for functions
# Maximum line length: 100 characters

def send_command(implant_id: str, cmd: str) -> str:
"""
Send a command to the specified implant.

Args:
implant_id: Unique identifier for target implant
cmd: Command string to execute

Returns:
Stripe Customer ID of created task object

Raises:
stripe.RateLimitError: If API rate limit exceeded
"""
# Implementation
```

**C#:**
```csharp
// Follow Microsoft C# conventions
// Use PascalCase for methods, camelCase for variables
// Add XML documentation comments
// Maximum line length: 120 characters

/// <summary>
/// Executes a shell command and returns the output.
/// </summary>
/// <param name="cmd">Command to execute</param>
/// <param name="cwd">Current working directory (updated on return)</param>
/// <returns>Command output as byte array</returns>
static byte[] ExecuteCommand(string cmd, ref string cwd)
{
// Implementation
}
```

**Markdown:**
```markdown
# Headers use ATX style (#)
- Lists use hyphens
- Code blocks use triple backticks with language
- Links use reference style when repeated
- Keep lines under 100 characters when possible
```

### Testing

**Before submitting:**

1. **Test operator functionality**
```bash
python3 c2_operator.py
# Verify all commands work
# Test error handling
# Check edge cases
```

2. **Test implant builds**
```bash
python3 regenerate_key.py
dotnet build Implant.csproj -c Release
# Verify no compilation errors
# Test on Windows VM
```

3. **Test integration**
```bash
# Deploy implant to test VM
# Verify E2E workflow:
# 1. Heartbeat appears
# 2. Commands execute
# 3. Results return
# 4. File transfer works
# 5. Screenshots capture
```

4. **Manual testing checklist**
- [ ] Code compiles without errors/warnings
- [ ] Existing functionality still works
- [ ] New features work as intended
- [ ] Error handling is robust
- [ ] No secrets committed (API keys, etc.)
- [ ] Documentation updated

## Contribution Types

### 1. Feature Additions

**Examples:**
- New persistence mechanisms
- Additional command implementations
- Enhanced stealth features
- Better file transfer protocols
- Improved error recovery

**Process:**
1. Open an issue first to discuss
2. Wait for maintainer approval
3. Implement on feature branch
4. Submit PR with tests and docs

**Requirements:**
- Maintain backward compatibility
- Add configuration options if needed
- Update relevant documentation
- Include usage examples

### 2. Bug Fixes

**Process:**
1. Identify bug (or pick from issues)
2. Create bugfix branch
3. Fix and test thoroughly
4. Submit PR with reproduction steps

**Requirements:**
- Describe the bug clearly
- Explain the fix
- Show before/after behavior
- Add regression test if possible

### 3. Documentation

**Needed:**
- Improved installation instructions
- More usage examples
- Troubleshooting guides
- Architecture diagrams
- Video tutorials/demos

**Process:**
1. Identify documentation gap
2. Write clear, accurate content
3. Include examples and screenshots
4. Submit PR with docs changes

### 4. Security Improvements

**Examples:**
- Better string obfuscation
- Enhanced encryption
- Anti-debugging techniques
- Evasion improvements

**Process:**
1. Responsibly disclose vulnerability first (if applicable)
2. Discuss approach with maintainers
3. Implement security improvement
4. Update SECURITY.md if needed

**Critical**: Never introduce vulnerabilities that could harm users

### 5. Performance Optimizations

**Examples:**
- Faster file transfers
- Reduced memory usage
- Optimized polling algorithms
- Better Stripe API utilization

**Requirements:**
- Benchmark before and after
- Document performance gains
- Ensure no functionality regression
- Consider trade-offs (speed vs stealth)

## Pull Request Process

### 1. Preparation

Before opening a PR:

- [ ] Code is tested and working
- [ ] Commits are clean and atomic
- [ ] Branch is up-to-date with main
- [ ] No merge conflicts
- [ ] Documentation updated
- [ ] CHANGELOG.md entry added (if applicable)

### 2. PR Template

Use this template for your PR description:

```markdown
## Description
Brief summary of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Documentation update
- [ ] Refactoring
- [ ] Security improvement

## Related Issues
Closes #123
Related to #456

## Testing Performed
- [ ] Operator console testing
- [ ] Implant build testing
- [ ] Integration testing
- [ ] Manual testing on Windows VM

## Checklist
- [ ] Code compiles without errors
- [ ] Tests pass
- [ ] Documentation updated
- [ ] No secrets committed
- [ ] Conventional commits used

## Screenshots (if UI change)
[Add screenshots here]

## Additional Notes
Any extra context about the PR
```

### 3. Review Process

**What to expect:**
1. Automated checks run (GitHub Actions)
2. Maintainer reviews code within 7 days
3. Feedback provided via PR comments
4. Iteration as needed
5. Approval and merge

**Review criteria:**
- Code quality and style
- Test coverage
- Documentation completeness
- Security considerations
- Performance impact
- Backward compatibility

### 4. After Merge

- Your branch will be deleted
- You'll be credited in release notes
- Feature will be in next release
- You can help with bug reports

## Recognition

Contributors are recognized through:

1. **AUTHORS.md** - List of all contributors
2. **Release Notes** - Credit in CHANGELOG.md
3. **Hall of Fame** - Outstanding contributions highlighted
4. **Social Media** - Shoutouts on project Twitter/blog (with permission)

## Resources

### Learning Resources
- [C2 Infrastructure Design](https://www.cobaltstrike.com/blog)
- [MITRE ATT&CK - Command and Control](https://attack.mitre.org/tactics/TA0011/)
- [Red Team Infrastructure Wiki](https://github.com/bluscreenofjeff/Red-Team-Infrastructure-Wiki)

### Code References
- [Stripe API Documentation](https://stripe.com/docs/api)
- [.NET Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [Python Best Practices](https://docs.python-guide.org/)

### Security Research
- [APT C2 Analysis](https://www.fireeye.com/current-threats/apt-groups.html)
- [Covert Channels Research](https://scholar.google.com/scholar?q=covert+channel+command+control)
- [Malware Traffic Analysis](https://malware-traffic-analysis.net/)

## Bug Reports

### Where to Report

**Security vulnerabilities**: Use GitHub Security Advisories (private) 
**Regular bugs**: Open a GitHub Issue (public)

### Bug Report Template

```markdown
**Describe the bug**
Clear description of what's broken

**To Reproduce**
Steps to reproduce:
1. Run operator with '...'
2. Execute command '...'
3. See error

**Expected behavior**
What should have happened

**Actual behavior**
What actually happened

**Environment:**
- OS: [e.g. Windows 11, Ubuntu 22.04]
- Python version: [e.g. 3.11.2]
- .NET version: [e.g. 10.0.1]
- Stripe SDK version: [e.g. 11.2.0]

**Logs/Screenshots**
```
[error messages here]
```

**Additional context**
Any other relevant information
```

## Feature Requests

### How to Request

1. Check existing issues first
2. Open a new issue with [FEATURE] prefix
3. Describe use case and benefits
4. Propose implementation if possible

### Feature Request Template

```markdown
**Feature Description**
What feature would you like to see?

**Use Case**
Why is this needed? What problem does it solve?

**Proposed Implementation**
How might this work? (optional)

**Alternatives Considered**
Other approaches you've thought about

**Additional Context**
Screenshots, mockups, examples, etc.
```

## First-Time Contributors

Welcome! Here's how to get started:

1. **Find a good first issue**
- Look for `good-first-issue` label
- These are designed for newcomers
- Usually well-defined and scoped

2. **Ask questions**
- Comment on the issue to claim it
- Ask for clarification if needed
- Request mentorship from maintainers

3. **Start small**
- Documentation fixes are great first PRs
- Simple bug fixes help you learn the codebase
- Don't be afraid to make mistakes

4. **Learn from feedback**
- Code reviews are learning opportunities
- Ask questions about feedback
- Iterate on your PR

## Contact

- **GitHub Issues**: For bugs and features
- **GitHub Discussions**: For questions and ideas
- **Security**: Use GitHub Security Advisories
- **Email**: [maintainer email] (for private matters)

## Legal

By contributing, you agree that:

1. Your contributions are your original work
2. You have the right to submit this work
3. Your contributions will be licensed under the project's MIT License
4. You understand this tool is for authorized security research only
5. You won't use contributions to facilitate illegal activity

---

**Thank you for contributing to StripeDropC2!**

*Together, we advance security research and improve defenses.*

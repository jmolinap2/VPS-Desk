# Security Policy

VPS Desk is a local desktop tool that can connect to real VPS hosts and execute
remote commands. Treat configuration and logs as sensitive.

## Supported Versions

Security fixes are accepted for the current `main` branch.

## Reporting a Vulnerability

Please report vulnerabilities privately to the repository owner. Do not open a
public issue with working exploits, private server details, tokens, passwords or
keys.

Include:

- A short description of the issue.
- Steps to reproduce with safe sample data.
- The affected feature or file.
- Any suggested fix, if you already have one.

## Secret Handling

- Do not commit `.env`.
- Do not commit private SSH keys.
- Do not paste real tokens or passwords into issues or pull requests.
- Rotate any credential that may have been exposed.

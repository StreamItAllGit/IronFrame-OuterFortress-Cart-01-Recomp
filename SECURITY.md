# Security Policy

## Supported Versions

| Version | Supported | Notes |
| :--- | :--- | :--- |
| `0.1.x` | Yes | Active pre-1.0 development baseline |

## Security Boundaries

- **Path & Data Isolation:** Runtime data, local AOT cache builds, settings, and diagnostic logs are isolated to `--data-dir`, `--config`, `--log-dir`, and `--save-dir` overrides. Automated test runs enforce strict path containment and reject host-system escape paths.
- **No Secrets in Repo:** API tokens, webhook URLs, signing certificates, and local `.env` configuration files are never committed or included in release bundles.
- **No Copyrighted Assets:** Zero retail ROMs, BIOS dumps, extracted tiles/audio, pre-lifted retail code, or copyrighted scripts are stored or distributed here.
- **Zero Background Daemons:** Installed launchers and setup packages enforce `AllowAutoStart = false` and leave zero resident background services running when closed.

## Reporting a Vulnerability

Please **do not** open a public issue for suspected security vulnerabilities or accidental asset leakage.

1. Report privately via GitHub **Security Advisories** (`Report a vulnerability` on the repository Security tab).
2. Include the affected component, version, reproduction steps, and expected vs. actual behavior.
3. Maintainers acknowledge reports within **72 hours** and coordinate a fix before public disclosure.

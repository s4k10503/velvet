# Security Policy

## Reporting a vulnerability

Please report security vulnerabilities **privately** through GitHub's
[private vulnerability reporting](https://github.com/s4k10503/velvet/security/advisories/new)
(repository **Security → Advisories → Report a vulnerability**).

Do **not** open a public issue for security reports.

This is a single-maintainer project, so responses are best-effort: reports will be
acknowledged and investigated as soon as possible, and a fix and disclosure timeline
coordinated with the reporter.

## Supported versions

| Version | Supported |
| ------- | --------- |
| 2.1.x   | ✅        |
| 2.0.x   | ❌        |
| 1.x     | ❌        |

Security fixes go to the newest release and to `main`. While `main` is building a new major, the
series before it stays supported on a maintenance branch and receives patch releases; when the new
major ships, support moves to it and this table is updated. A series marked ❌ receives no further
releases, and the fix for one is to upgrade.

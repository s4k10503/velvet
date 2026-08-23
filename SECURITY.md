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

Security fixes go to `main` and to the newest release of each series marked ✅. This table is updated
with every release, so a version it does not list is not supported: the fix for one is to upgrade, and
the [CHANGELOG](Packages/com.velvet.core/CHANGELOG.md) lists what each major changed.

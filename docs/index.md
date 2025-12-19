---
title: Home
nav_order: 1
---

Swift Migrator is a language-agnostic SQL database migration tool written in C#.  
It supports MSSQL, PostgreSQL, and MySQL and can be used by **any stack** (C#, Go, Node.js, Java, Python, etc.).

It manages versioned SQL migrations using a strict **UP / DOWN** model to ensure predictable,
repeatable schema changes across teams and environments.

Swift Migrator is intentionally simple, explicit, and safe by default.

It gives you:
- Transaction-safe SQL migrations
- Strict versioning (`<timestamp>_<id>_<description>.sql`)
- Explicit UP and DOWN scripts
- Author and branch metadata enforcement
- Table-touch conflict detection
- Migration status inspection
- Redo support for iterative development
- `.env`, `migrator.json`, environment variable, and CLI configuration
- Standalone CLI binaries (Windows, macOS, Linux)


---

## Documentation

- [Features](features.md)
- [Install](installation.md)
- [Usage](usage.md)
- [Writing Migrations](writing-migrations.md)
- [Providers & Configuration](providers.md)
- [Cross Platform](cross-platform.md)
- [Commands](commands.md)
- [Troubleshooting](troubleshooting.md)

---

## Project Repository
Source code and releases available here:

**[View project on GitHub](https://github.com/onas1/swift-migrator)**
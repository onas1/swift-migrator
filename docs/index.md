---
title: Home
nav_order: 1
---

# Swift Migrator

swift migrator database migration tool is a lightweight, language‑agnostic SQL migration runner written in c#. It works for MSSQL, PostgreSQL, and MySQL, and can be used by any project regardless of language (C#, Go, TS/Node, Java, Python, etc.).

The tool manages versioned SQL migrations following an **UP/DOWN** pattern. It ensures consistent database schema evolution across teams, projects, and environments.

It gives you:
- Transaction-safe SQL migrations  
- Multi-environment configuration (`.env`)  
- Table-touch conflict detection  
- Reversible UP/DOWN scripts  
- CLI tooling for Windows, macOS, and Linux  
- Predictable versioning (`<timestamp>_<id>_<description>.sql`)  
- Human-first workflow 


---

## Documentation

- [Features](features.md)
- [Install](installation.md)
- [Usage](usage.md)
- [Writing Migrations](writing-migrations.md)
- [Configuration](configuration.md)
- [Cross Platform](cross-platform.md)
- [Commands](commands.md)
- [Troubleshooting](troubleshooting.md)
- [Architecture](architecture.md)

---

## 🗂️ Project Repository
Source code and releases available here:

**GitHub**: https://github.com/onas1/swift-migrator


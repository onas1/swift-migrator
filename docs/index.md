## 1. Introduction (`docs/introduction.md`)

### Overview
Your database migration tool is a lightweight, language‑agnostic SQL migration runner written in .NET 8. It works for MSSQL, PostgreSQL, and MySQL, and can be used by any project regardless of language (C#, Go, TS/Node, Java, Python, etc.).

The tool manages versioned SQL migrations following an **UP/DOWN** pattern. It ensures consistent database schema evolution across teams, projects, and environments.

### Key Principles
- No ORM dependency
- No project‑specific bindings
- Pure SQL migrations
- Deterministic ordering
- Safe, transactional execution where supported
- Cross‑platform support (Windows, Linux, macOS)

---

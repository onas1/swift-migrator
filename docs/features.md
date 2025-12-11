---
title: Features
nav_order: 2
---

## Features

###  Cross‑database support
- SQL Server
- PostgreSQL
- MySQL


### Auto‑generated migration files
`create <description>` generates:
- Timestamped filename
- ID
- Clean template containing UP and DOWN sections

### Migration status inspection
`status` displays:
- Applied migrations
- Pending migrations
- Checksums

### Apply migration
`apply` Executes:
- The SQL migration script


### Rollback migration
`rollback` Executes:
- The rollback SQL migration script
- Updates current migration version

### Reapply migration after more changes
`redo <version>` Executes:
- The rollback SQL migration script
- Reapply the new SQL migration script
- Updates current migration version



### Version tracking table
Automatically manages a version table to track applied migrations.

### `.env`, `migrator.json` or Environmental variable configuration support
Reads database connection values recursively upward through directories.

### Standalone binary releases for:
- Windows (x64)
- Linux (x64, arm64)
- macOS (Intel + Apple Silicon)

---
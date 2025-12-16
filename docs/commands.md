---
title: Commands
nav_order: 8
---

# CLI Commands

## `create <description> --author <fullname>  --branch <branch>`
Creates a new timestamp-based migration file.
```
migrator create "Add orders table" --author "test user" --branch "feat/test-user/ticket-123"
```
---

## `status`
Displays applied and pending migrations.

```
migrator status
```

---

## `apply`
Applies all unapplied migrations.

```
migrator apply
```
---

## `redo <version>`
Rolls back + re-applies a specific migration.

```
migrator redo 20251210_a3f81c
```
---

## `apply --conn=... --provider=...`
Overrides `.env`.

```
migrator apply --conn="..." --provider=Npgsql
```
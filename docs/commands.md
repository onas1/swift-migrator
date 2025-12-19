---
title: Commands
nav_order: 8
---

# CLI Commands

## `create <description> --author <fullname>  --branch <branch>`
Creates a new timestamp-based migration file.

```
migrator create "Add orders table" \
  --author "Full Name" \
  --branch "feat/orders" \
  --transaction on|off
  ```
Options:
--author (required) – migrations without an author will not be applied

--branch – optional metadata

--transaction on|off – defaults to on

off is required for operations that cannot run inside a transaction
---

## `status`
Displays applied and pending migrations and also conflicting pending migrations (if any).

```
migrator status
```

```
migrator status --conn="..." --provider=SqlClient
```

---

## `apply`
Applies all unapplied migrations.

```
migrator apply
```

Additional forms:

```
migrator apply to <version>
```

```
migrator apply -v <version>
```

```
migrator apply --
```

```
migrator apply --conn="..." --provider=Npgsql
```

Notes:

Confirmation may be required

--force bypasses detected conflicts and apply changes
---


## `rollback`
rollback the most recently applied migration.

```
migrator rollback
```

```
migrator rollback -v <version>
```

```
migrator rollback to <version>
```

```
migrator rollback all
```

Notes:

Confirmation is required when executing `rollback all`

Non-transactional migrations may not be fully reversible
---

## `redo <version>`
Rolls back + re-applies a specific migration.

```
migrator redo 20251210_a3f81c
```
---

## `help`
prints out the available commands and how to use them.

```
migrator help
```
---

## `apply --conn=... --provider=...`
Overrides `.env`.

```
migrator apply --conn="..." --provider=Npgsql
```
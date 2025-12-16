---
title: Writing Migrations
nav_order: 5
---


## Writing Migrations

### Template
```
-- Migration: Add StudentNo
-- Version: <timestamp>_<id>
-- Author: <fullname>
-- Branch: <branch>

-- UP
-- Write your SQL for applying the migration here.


-- DOWN
-- Write your SQL for reverting the migration here.

```

### Example: Adding `StudentNo` to `TeacherClass`

#### UP
```
ALTER TABLE TeacherClass
ADD StudentNo INT NULL;
```

#### DOWN
```
ALTER TABLE TeacherClass
DROP COLUMN StudentNo;
```

Place both inside their respective blocks.

---

If two pending migrations modify the same table:

- Swift migrator **detects the collision**
- Refuses to apply blindly

This prevents accidental destructive changes in remote teams.

---

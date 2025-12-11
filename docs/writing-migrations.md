## 6. Writing Migrations (`docs/writing-migrations.md`)

### Template
```
-- Migration: Add StudentNo
-- Version: <timestamp>_<id>

-- UP
BEGIN;

-- SQL here

COMMIT;

-- DOWN
BEGIN;

-- Revert logic here

COMMIT;
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

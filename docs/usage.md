## 5. Usage Guide

### Initialize a new project
```
migrator init
```
Creates:
```
./migrations
.env (if missing)
```

### Create a migration
```
migrator create AddStudentNoToTeacherClass
```
Generates something like:
```
20251211_ab12cd_addstudentnototeacherclass.sql
```

### Apply migrations
```
migrator apply
```

### Check status
```
migrator status
```

### Rollback last migration
```
migrator rollback
```

---

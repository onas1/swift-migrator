---
title: Configuration
nav_order: 6
---



## Configuration

SwiftScale reads configuration in this order:
1. CLI arguments  
2. Environment variables  
3. `.env` file 
4. migration.json 

---

## `.env` example

```
MIGRATOR_PROVIDER=SqlClient|Npgsql|mysql
MIGRATOR_CONN="..."
```

---

## CLI Override

migrator apply --provider=Npgsql --conn="Host=localhost;..."

---

## Supported Providers

- `SqlClient` (SQL Server)
- `Npgsql` (PostgreSQL)
- More coming soon (MySQL, Mongo, Dynamo, SQLite)



The tool searches upward from the current directory until it finds `.env`.

### Example Project Structure
```
MyApp/
  .env
  migrations/
  src/
    ...
```

---

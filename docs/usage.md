---
title: Usage
nav_order: 4
---


## Usage Guide

## 1. Create a `.env` file

Create a `.env` file in your project root and setup configuration properties:
 
- For SQL SERVER
```
MIGRATOR_PROVIDER=SqlClient
MIGRATOR_CONN="Server=localhost;Database=MyApp;User ID=sa;Password=Pass123;TrustServerCertificate=True"
```
- Or PostgreSQL:
```
MIGRATOR_PROVIDER=Npgsql
MIGRATOR_CONN="Host=localhost;Database=mydb;Username=postgres;Password=postgres"
```

## 2.  Create your first migration
- Execute the command

```
migrator create YourMigrationFileName --Author "full name" --branch "branchName"
```
Generates something like:
```
migrations/20251211_ab12cd_YourMigrationFileName.sql
```
> ⚠️ Migrations **without an author will not be applied**.

- Open and edit the file:

```sql
-- Migration: Your Migration FIle Name
-- Version: 20251211_ab12cd
-- Author: full name
-- Branch: branch

-- UP
BEGIN;

CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Username VARCHAR(50) NOT NULL UNIQUE,
    Email VARCHAR(255) NOT NULL UNIQUE,
    CreatedAt DATETIME2 DEFAULT GETDATE()
);

COMMIT;

-- DOWN
BEGIN;
DROP TABLE Users;
COMMIT;


## 3 Apply migrations

- Execute the command

```
migrator apply
```
That's it — your database schema is now versioned.


## 4 Check status
```
migrator status
```

## 5 Rollback last migration
```
migrator rollback
```

## 6 Reapply specific migration verison
```
migrator redo verison
```

---

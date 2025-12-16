---

title: Providers and Configurations
nav_order: 6
-------------

# Database Providers

Swift Migrator supports multiple database providers out of the box. Each provider is enabled by setting the `MIGRATOR_PROVIDER` environment variable and supplying the appropriate connection configuration.

This page lists all supported providers, their expected configuration values, and practical examples.

---

## Configuration Methods

Swift Migrator supports several configuration methods, with the following priority:
CLI options > `.env` file > `migrator.json` > Environment Variables

### 1. Environment Variables

```env
MIGRATOR_PROVIDER=Postgres
MIGRATOR_CONNECTION=Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=postgres;
```

### 2. .env File

```env
MIGRATOR_PROVIDER=SqlClient
MIGRATOR_CONNECTION=Server=localhost,1433;Database=MyDatabase;User Id=sa;Password=YourStrongPassword;TrustServerCertificate=True;
```

### 3. migrator.json

```json
{
  "provider": "Npgsql",
  "ConnectionString": "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=postgres;"
}
```

### 4. CLI Options

```bash
migrator apply --provider Npgsql --connection "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=postgres;"
```

---

## Supported Database Providers

| Database   | MIGRATOR_PROVIDER |
| ---------- | ----------------- |
| SQL Server | `SqlClient`       |
| PostgreSQL | `Npgsql`          |
| MySQL      | `MySql`           |
| MariaDB    | `MySql`           |
| Oracle     | `MySql.Data`      |

### SQL Server

```env
MIGRATOR_PROVIDER=SqlClient
MIGRATOR_CONNECTION=Server=localhost,1433;Database=MyDatabase;User Id=sa;Password=YourStrongPassword;TrustServerCertificate=True;
```

* Supports SQL and Windows authentication.
* Azure SQL is supported.

### PostgreSQL

```env
MIGRATOR_PROVIDER=Npgsql
MIGRATOR_CONNECTION=Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=postgres;
```

* SSL configuration supported.
* Schema-based migrations supported.

### MySQL / MariaDB

```env
MIGRATOR_PROVIDER=MySql
MIGRATOR_CONNECTION=Server=localhost;Port=3306;Database=mydb;User=root;Password=secret;
```

* MariaDB supported transparently.
* Ensure transactional DDL is used if possible.

### Oracle

```env
MIGRATOR_PROVIDER=MySql.Data
MIGRATOR_CONNECTION=User Id=system;Password=oracle;Data Source=localhost:1521/XEPDB1;
```

* Oracle driver uses `MySql.Data` provider in Swift Migrator.
* Ensure connection string matches Oracle driver expectations.

---

## Validation & Errors

* Missing or unsupported provider will fail fast.
* Empty or invalid connection strings will cause errors.
* Always use the correct provider value for your database.

---

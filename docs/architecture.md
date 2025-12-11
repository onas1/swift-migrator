---
title: Architecture
nav_order: 10
---

# Architecture Overview

Swift Migrator is built on simple, predictable parts:

---

## Core Components

### 1. **Migration Engine**
- Finds migrations
- Parses UP/DOWN blocks
- Detects table-touch conflicts
- Runs SQL inside transactions

### 2. **SqlRunner**
- Provider-agnostic execution
- Parameter bindings
- Scalar and non-query helpers

### 3. **Providers**
- SQL Server: `SqlClient`
- PostgreSQL: `Npgsql`
- Expandable adapter interface

### 4. **Project Discovery**
- Finds project root upward
- Loads `.env`
- Ensures `/migrations` exists

---

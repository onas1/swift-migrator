---
title: Troubleshooting
nav_order: 9
---

# Troubleshooting

---

## SSL Certificate errors (SQL Server)

Add to connection string:

TrustServerCertificate=True

---

## Database not found

Create DB manually:
```
CREATE DATABASE MyDb;
```
---

## Cannot find `.env`

migrator searches upward from current directory.

Ensure you run the CLI from your project, not from inside the tool.
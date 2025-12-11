---
title: Troubleshooting
nav_order: 9
---

# Troubleshooting

---

## SSL Certificate errors (SQL Server)

Add to connection string:

TrustServerCertificate=True

yaml
Copy code

---

## Database not found

Create DB manually or add a bootstrap migration:

CREATE DATABASE MyDb;
---

## Cannot find `.env`

Swift migrator searches upward from current directory.

Ensure you run the CLI from your project, not from inside the tool.
## 4. Configuration

Your project should contain a `.env` file with:
```
DB_PROVIDER=mssql|postgres|mysql
DB_CONNECTION_STRING="..."
```

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

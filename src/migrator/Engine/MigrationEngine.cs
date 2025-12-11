
using System.Data.Common;
namespace migrator.Engine;




public class MigrationEngine
{
    private readonly string _migrationsPath;
    private readonly SqlRunner _runner;
    private readonly string _providerInvariant;
    private const string VersionTable = "migrator_versions";
    private readonly byte[] _publicKeyBytes;
    private readonly string _connectionString;

    public MigrationEngine(string migrationsPath, string providerInvariant, string connectionString)
    {
        _migrationsPath = migrationsPath;
        _providerInvariant = providerInvariant;
        _runner = new SqlRunner(providerInvariant, connectionString);
        _connectionString = connectionString;
    }

    // Provider-specific version table DDL
    public async Task EnsureVersionTableAsync()
    {
        if (_providerInvariant.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            var sql = $@"
CREATE TABLE IF NOT EXISTS {VersionTable} (
    id BIGSERIAL PRIMARY KEY,
    version VARCHAR(100) NOT NULL UNIQUE,
    filename TEXT,
    checksum CHAR(64),
    author TEXT,
    branch TEXT,
    commit_id TEXT,
    applied_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);
";
            await _runner.ExecuteNonQueryAsync(sql);
            return;
        }

        if (_providerInvariant.Contains("SqlClient", StringComparison.OrdinalIgnoreCase) || _providerInvariant.Contains("System.Data.SqlClient", StringComparison.OrdinalIgnoreCase))
        {
            var sql = $@"
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'{VersionTable}') AND type in (N'U'))
BEGIN
    CREATE TABLE {VersionTable} (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        version VARCHAR(100) NOT NULL UNIQUE,
        filename NVARCHAR(MAX),
        checksum CHAR(64),
        author NVARCHAR(200),
        branch NVARCHAR(200),
        commit_id NVARCHAR(200),
        applied_at DATETIMEOFFSET DEFAULT SYSDATETIMEOFFSET()
    );
END
";
            await _runner.ExecuteNonQueryAsync(sql);
            return;
        }

        if (_providerInvariant.Contains("MySql", StringComparison.OrdinalIgnoreCase) || _providerInvariant.Contains("MySqlConnector", StringComparison.OrdinalIgnoreCase))
        {
            var sql = $@"
CREATE TABLE IF NOT EXISTS {VersionTable} (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    version VARCHAR(100) NOT NULL UNIQUE,
    filename TEXT,
    checksum CHAR(64),
    author VARCHAR(200),
    branch VARCHAR(200),
    commit_id VARCHAR(200),
    applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB;
";
            await _runner.ExecuteNonQueryAsync(sql);
            return;
        }

        // Generic fallback: attempt a generic create; if fails, warn.
        var fallback = $@"
CREATE TABLE IF NOT EXISTS {VersionTable} (
    id INTEGER PRIMARY KEY,
    version VARCHAR(100) NOT NULL UNIQUE,
    filename TEXT,
    checksum TEXT,
    author TEXT,
    branch TEXT,
    commit_id TEXT,
    applied_at DATETIME
);
";
        try
        {
            await _runner.ExecuteNonQueryAsync(fallback);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: automatic version-table creation failed for provider {_providerInvariant}. You may need to create {VersionTable} manually. {ex.Message}");
        }
    }

    public IEnumerable<Migration> LoadAllMigrations()
    {
        var files = Directory.GetFiles(_migrationsPath, "*.sql")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var list = new List<Migration>();
        foreach (var f in files)
        {
            try
            {
                list.Add(Migration.LoadFromFile(f));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Skipping invalid migration file {f}: {ex.Message}");
            }
        }
        return list;
    }









   



    public async Task<string> GetStatusAsync()
    {
        await EnsureVersionTableAsync();
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Read applied versions as rows — safer across providers
            var allVersions = await _runner.QueryScalarAsync<string>($"SELECT STRING_AGG(version, ',') FROM {VersionTable}");
            var s = allVersions ?? "";
            if (!string.IsNullOrEmpty(s))
            {
                foreach (var v in s.Split(',', StringSplitOptions.RemoveEmptyEntries)) applied.Add(v.Trim());
            }
        }
        catch
        {
            // If STRING_AGG isn't supported, try a simple scalar fallback for SQL Server / MySQL
            try
            {
                var rows = await _runner.QueryScalarAsync<object>($"SELECT GROUP_CONCAT(version) FROM {VersionTable}"); // MySQL style
                var s = rows?.ToString() ?? "";
                foreach (var v in s.Split(',', StringSplitOptions.RemoveEmptyEntries)) applied.Add(v.Trim());
            }
            catch
            {
                // Give up gracefully
            }
        }

        var all = LoadAllMigrations().ToList();

        var buf = new System.Text.StringBuilder();
        buf.AppendLine("MIGRATIONS STATUS:");
        foreach (var m in all)
        {
            buf.AppendLine($"{(applied.Contains(m.Version) ? "[X]" : "[ ]")} {m.Version}  {m.Name}  ({Path.GetFileName(m.Filename)})");
        }

        var unapplied = all.Where(m => !applied.Contains(m.Version)).ToList();
        var touched = new Dictionary<string, List<Migration>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in unapplied)
        {
            foreach (var t in Utils.ExtractTableNames(m.Sql))
            {
                if (!touched.TryGetValue(t, out var list))
                        touched[t] = list = new List<Migration>();
                list.Add(m);
            }
        }
        var conflicts = touched.Where(kv => kv.Value.Count > 1).ToList();
        if (conflicts.Any())
        {
            buf.AppendLine();
            buf.AppendLine("POTENTIAL CONFLICTS (multiple unapplied migrations touch the same table):");
            foreach (var kv in conflicts)
            {
                buf.AppendLine($"Table: {kv.Key}");
                foreach (var m in kv.Value)
                    buf.AppendLine($"  - {m.Version} {m.Name}");
            }
        }

        return buf.ToString();
    }





    public string CreateMigrationFile(string description)
    {
        var ts = Utils.TimestampNowUtc();
        var id = Utils.ShortId();
        var name = Utils.SanitizeName(description).Replace("--", "");
        var filename = $"{ts}_{id}_{name}.sql";
        var full = Path.Combine(_migrationsPath, filename);

        var template =
            $"-- Migration: {description}\n" +
            $"-- Version: {ts}_{id}\n\n" +
            $"-- UP\n" +
            $"BEGIN;\n\n" +
            $"-- Write your SQL for applying the migration here.\n\n" +
            $"COMMIT;\n\n" +
            $"-- DOWN\n" +
            $"BEGIN;\n\n" +
            $"-- Write your SQL for reverting the migration here.\n\n" +
            $"COMMIT;\n";

        File.WriteAllText(full, template);
        return full;
    }









    public async Task ApplyMigrationsAsync()
    {
        await EnsureVersionTableAsync();

        // Already applied versions
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var rows = await _runner.QueryScalarAsync<string>($"SELECT STRING_AGG(version, ',') FROM {VersionTable}");
            var s = rows ?? "";
            if (!string.IsNullOrEmpty(s))
            {
                foreach (var v in s.Split(',', StringSplitOptions.RemoveEmptyEntries)) applied.Add(v.Trim());
            }
        }
        catch
        {
            // ignore
        }

        var all = LoadAllMigrations().ToList();
        var unapplied = all.Where(m => !applied.Contains(m.Version)).ToList();

        // detect simple conflicts
        // Simple semantic detection: if two unapplied migrations touch the same table, warn and require manual resolution
        var touched = new Dictionary<string, List<Migration>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in unapplied)
        {
            foreach (var t in Utils.ExtractTableNames(m.Sql))
            {
                if (!touched.TryGetValue(t, out var list)) touched[t] = list = new List<Migration>();
                list.Add(m);
            }
        }
        var conflicts = touched.Where(kv => kv.Value.Count > 1).ToList();
        if (conflicts.Any())
        {
            Console.WriteLine("ERROR: semantic conflicts detected — multiple pending migrations touch the same table.");
            foreach (var kv in conflicts)
            {
                Console.WriteLine($"Table: {kv.Key}");
                foreach (var m in kv.Value)
                    Console.WriteLine($"  - {m.Version} {m.Name} ({Path.GetFileName(m.Filename)})");
            }
            Console.WriteLine("Resolve conflicts (rename / reorder / combine migrations) and run apply again.");
            return;
        }

        // Acquire a global migrator lock to avoid races
        var lockName = "migrator_lock";
        var gotLock = await _runner.AcquireLockAsync(lockName, 30);
        if (!gotLock)
        {
            Console.WriteLine("Could not acquire migrator lock; aborting to avoid concurrency issues.");
            return;
        }

        try
        {
            //foreach (var m in unapplied)
            //{
            //    Console.WriteLine($"Applying {m.Version} {m.Name}...");
            //    // Execute in a transaction so either everything including version row is committed or not
            //    // We'll open a connection and transaction, run statements individually using SplitSqlStatements for safety.
            //    var factory = DbProviderFactories.GetFactory(_providerInvariant);
            //    using var con = factory.CreateConnection();
            //    con!.ConnectionString = GetConnectionStringFromRunner(); // helper to fetch conn string from runner (we'll read via reflection below)
            //    await con.OpenAsync();
            //    using var tx = con.BeginTransaction();
            //    try
            //    {
            //        // Split statements to avoid provider limitations with big single ExecuteNonQuery calls.
            //        var stmts = Utils.SplitSqlStatements(m.Sql);
            //        foreach (var stmt in stmts)
            //        {
            //            using var cmd = con.CreateCommand();
            //            cmd.Transaction = tx;
            //            cmd.CommandText = stmt;
            //            await cmd.ExecuteNonQueryAsync();
            //        }

            //        // Record migration via parameterized insert
            //        var insertSql = $"INSERT INTO {VersionTable} (version, filename, checksum, author, branch, commit_id) VALUES (@version, @filename, @checksum, @author, @branch, @commit)";
            //        using (var cmd = con.CreateCommand())
            //        {
            //            cmd.Transaction = tx;
            //            cmd.CommandText = insertSql;
            //            var p1 = cmd.CreateParameter(); p1.ParameterName = "@version"; p1.Value = m.Version; cmd.Parameters.Add(p1);
            //            var p2 = cmd.CreateParameter(); p2.ParameterName = "@filename"; p2.Value = Path.GetFileName(m.Filename); cmd.Parameters.Add(p2);
            //            var p3 = cmd.CreateParameter(); p3.ParameterName = "@checksum"; p3.Value = m.Checksum; cmd.Parameters.Add(p3);
            //            var p4 = cmd.CreateParameter(); p4.ParameterName = "@author"; p4.Value = (object?)m.Header?.Author ?? DBNull.Value; cmd.Parameters.Add(p4);
            //            var p5 = cmd.CreateParameter(); p5.ParameterName = "@branch"; p5.Value = (object?)m.Header?.Branch ?? DBNull.Value; cmd.Parameters.Add(p5);
            //            var p6 = cmd.CreateParameter(); p6.ParameterName = "@commit"; p6.Value = (object?)m.Header?.CommitId ?? DBNull.Value; cmd.Parameters.Add(p6);

            //            await cmd.ExecuteNonQueryAsync();
            //        }

            //        tx.Commit();
            //        Console.WriteLine($"Applied {m.Version}");
            //    }
            //    catch (Exception ex)
            //    {
            //        try { tx.Rollback(); } catch { /* ignore */ }
            //        Console.WriteLine($"Failed to apply migration {m.Version}: {ex.Message}");
            //        return;
            //    }
            //    finally
            //    {
            //        await con.CloseAsync();
            //    }
            //}














            foreach (var m in unapplied)
            {
                // Signature verification


                //if (!SignatureVerifier.Verify(m, _publicKeyBytes))
                //    throw new Exception($"Signature verification failed for {m.Version}");





                // Unsafe SQL detection
                //UnsafePatternDetector.AssertSafe(m.UpSql);

                Console.WriteLine($"Applying {m.Version} {m.Name}...");

                var upStatements = Utils.SplitSqlStatements(m.UpSql);
                var factory = DbProviderFactories.GetFactory(_providerInvariant);

                using var con = factory.CreateConnection();
                //con.ConnectionString = GetConnectionStringFromRunner();
                con.ConnectionString = _connectionString;

                await con.OpenAsync();
                using var tx = con.BeginTransaction();

                try
                {
                    foreach (var stmt in upStatements)
                    {
                        using var cmd = con.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = stmt;
                        await cmd.ExecuteNonQueryAsync();
                    }

                  // Record migration via parameterized insert

                   var insertSql = $"INSERT INTO {VersionTable} (version, filename, checksum, author, branch, commit_id) VALUES (@version, @filename, @checksum, @author, @branch, @commit)";
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = insertSql;
                        var p1 = cmd.CreateParameter(); p1.ParameterName = "@version"; p1.Value = m.Version; cmd.Parameters.Add(p1);
                        var p2 = cmd.CreateParameter(); p2.ParameterName = "@filename"; p2.Value = Path.GetFileName(m.Filename); cmd.Parameters.Add(p2);
                        var p3 = cmd.CreateParameter(); p3.ParameterName = "@checksum"; p3.Value = m.Checksum; cmd.Parameters.Add(p3);
                        var p4 = cmd.CreateParameter(); p4.ParameterName = "@author"; p4.Value = (object?)m.Header?.Author ?? DBNull.Value; cmd.Parameters.Add(p4);
                        var p5 = cmd.CreateParameter(); p5.ParameterName = "@branch"; p5.Value = (object?)m.Header?.Branch ?? DBNull.Value; cmd.Parameters.Add(p5);
                        var p6 = cmd.CreateParameter(); p6.ParameterName = "@commit"; p6.Value = (object?)m.Header?.CommitId ?? DBNull.Value; cmd.Parameters.Add(p6);

                        await cmd.ExecuteNonQueryAsync();
                    }

                    tx.Commit();
                    Console.WriteLine($"Applied {m.Version}");

                }
                catch (Exception ex)
                {
                    try { tx.Rollback(); } catch { }
                    throw new Exception($"Failed to apply {m.Version}: {ex.Message}", ex);
                }
            }


            Console.WriteLine("All pending migrations applied.");
        }
        finally
        {
            await _runner.ReleaseLockAsync(lockName);
        }
    }






    public async Task RollbackLastAsync()
    {
        // var last = await _runner.QueryScalarAsync<string>($"SELECT TOP 1 version FROM {VersionTable} ORDER BY id DESC LIMIT 1"); --Postgres style
        var last = await _runner.QueryScalarAsync<string>($"SELECT TOP 1 version FROM {VersionTable} ORDER BY id DESC");

        if (last == null) { Console.WriteLine("No migrations to rollback."); return; }

        await RollbackVersionAsync(last);
    }

    public async Task RollbackToAsync(string target)
    {
        var rows = await _runner.QueryScalarAsync<string>($"SELECT STRING_AGG(version, ',') FROM {VersionTable}");
        var applied = rows?.Split(',').Select(s => s.Trim()).ToList() ?? new();

        var toRollback = applied
            .Where(v => string.Compare(v, target, StringComparison.OrdinalIgnoreCase) > 0)
            .OrderByDescending(v => v)
            .ToList();

        foreach (var v in toRollback)
            await RollbackVersionAsync(v);
    }

    public async Task RedoAsync(string version)
    {
        await RollbackVersionAsync(version);
        await ApplySpecificAsync(version);
    }




    private async Task RollbackVersionAsync(string version)
    {
        var m = LoadAllMigrations().FirstOrDefault(x => x.Version == version);
        if (m == null) throw new Exception("Migration not found");

        if (!m.IsReversible)
            throw new Exception($"Migration {version} is irreversible");

        //UnsafePatternDetector.AssertSafe(m.DownSql);

        Console.WriteLine($"Rolling back {version}...");

        var stmts = Utils.SplitSqlStatements(m.DownSql);
        var factory = DbProviderFactories.GetFactory(_providerInvariant);

        using var con = factory.CreateConnection();
        //con.ConnectionString = GetConnectionStringFromRunner();
        con.ConnectionString = _connectionString;

        await con.OpenAsync();
        using var tx = con.BeginTransaction();

        try
        {
            foreach (var stmt in stmts)
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = stmt;
                await cmd.ExecuteNonQueryAsync();
            }

            await _runner.ExecuteNonQueryAsync(
                $"DELETE FROM {VersionTable} WHERE version=@v",
                new Dictionary<string, object?> { { "@v", version } },
                con, tx);

            tx.Commit();

            Console.WriteLine($"Rolled back {version}");
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }



    private async Task ApplySpecificAsync(string version)
    {
        await EnsureVersionTableAsync();

        var all = LoadAllMigrations().ToList();
        var m = all.FirstOrDefault(x => x.Version == version);

        if (m == null)
            throw new Exception($"Migration not found: {version}");

        // Check if already applied
        var exists = await _runner.QueryScalarAsync<int?>(
            $"SELECT COUNT(*) FROM {VersionTable} WHERE version=@v",
            new Dictionary<string, object?> { { "@v", version } });

        if (exists.GetValueOrDefault() > 0)
        {
            Console.WriteLine($"Migration {version} is already applied.");
            return;
        }

        // Signature verification
        //if (!SignatureVerifier.Verify(m, _publicKeyBytes))
        //    throw new Exception($"Signature verification failed for {m.Version}");

        // Unsafe SQL check
        //UnsafePatternDetector.AssertSafe(m.UpSql);

        var factory = DbProviderFactories.GetFactory(_providerInvariant);

        Console.WriteLine($"Applying (specific) {m.Version} {m.Name}...");

        using var con = factory.CreateConnection();
        con.ConnectionString = _connectionString;
        await con.OpenAsync();
        using var tx = con.BeginTransaction();

        try
        {
            var stmts = Utils.SplitSqlStatements(m.UpSql);

            foreach (var stmt in stmts)
            {
                using var cmd = con.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = stmt;
                await cmd.ExecuteNonQueryAsync();
            }

            // Insert version row
            var insertSql =
                $"INSERT INTO {VersionTable} (version, filename, checksum, author, branch, commit_id) " +
                $"VALUES (@version, @filename, @checksum, @author, @branch, @commit)";

            using var cmdIns = con.CreateCommand();
            cmdIns.Transaction = tx;
            cmdIns.CommandText = insertSql;

            cmdIns.Parameters.Add(CreateParam(cmdIns, "@version", m.Version));
            cmdIns.Parameters.Add(CreateParam(cmdIns, "@filename", Path.GetFileName(m.Filename)));
            cmdIns.Parameters.Add(CreateParam(cmdIns, "@checksum", m.Checksum));
            cmdIns.Parameters.Add(CreateParam(cmdIns, "@author", (object?)m.Header?.Author ?? DBNull.Value));
            cmdIns.Parameters.Add(CreateParam(cmdIns, "@branch", (object?)m.Header?.Branch ?? DBNull.Value));
            cmdIns.Parameters.Add(CreateParam(cmdIns, "@commit", (object?)m.Header?.CommitId ?? DBNull.Value));

            await cmdIns.ExecuteNonQueryAsync();

            tx.Commit();
            Console.WriteLine($"Applied {m.Version}");
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }






    private static DbParameter CreateParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }


    // Ugly-but-practical helper to get connection string from private field of runner (no API yet)
    //private string GetConnectionStringFromRunner()
    //{
    //    // reflection to read private field _connectionString from SqlRunner
    //    var fi = typeof(SqlRunner).GetField("_connectionString", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    //    if (fi == null) throw new InvalidOperationException("Unable to access runner connection string");
    //    return fi.GetValue(_runner) as string ?? throw new InvalidOperationException("Runner connection string is null");
    //}




}

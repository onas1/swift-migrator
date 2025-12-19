using Microsoft.Data.SqlClient;
using migrator.Providers;
using MySql.Data.MySqlClient;
using MySqlConnector;
using Npgsql;
using System.Data;
using System.Data.Common;



namespace migrator.Engine;

public class MigrationEngine
{
    private readonly string _migrationsPath;
    private readonly SqlRunner _runner;
    private readonly string _providerInvariant;
    private const string VersionTable = "migrator_versions";

    public MigrationEngine(string migrationsPath, string providerInvariant, string connectionString)
    {
        _migrationsPath = migrationsPath;
        _providerInvariant = providerInvariant;
        _runner = new SqlRunner(providerInvariant, connectionString);
    }

    // Provider-specific version table DDL
    public async Task EnsureVersionTableAsync()
    {
        try{  await _runner.ExecuteNonQueryAsync(ScriptingEngine.GetEnsureVersionTableQuery(_providerInvariant, VersionTable)); }
        catch (Exception ex){ Utils.SendWarningMessage($"Warning: automatic version-table creation failed for provider {_providerInvariant}. You may need to create {VersionTable} manually. {ex.Message}"); }
    }




    internal async Task<string> GetStatusAsync()
    {
        await EnsureVersionTableAsync();
        var applied = await GetAppliedVersionsAsync();
        var all = LoadAllMigrations().ToList();
       return ConstructStatusResult(all, applied);
    }


    internal async Task ApplyMigrationsAsync(string targetVersion = "", bool force = false)
    {
        await EnsureVersionTableAsync();

        // Already applied versions
        var applied = await GetAppliedVersionsAsync();

        var all = LoadAllMigrations()
    .Where(m => string.IsNullOrEmpty(targetVersion) ||
                string.Compare(m.Version, targetVersion, StringComparison.Ordinal) <= 0)
    .ToList();
        

        var unapplied = all.Where(m => !applied.Contains(m.Version)).ToList();

        foreach (var m in unapplied)
        {
            ValidateMigrationMetadata(m);

            if (m.Header.UseTransaction)
                UnsafePatternDetector.AssertSafe(m.UpSql, _providerInvariant);
            else
                UnsafePatternDetector.AssertSafeOutsideTransaction(m.UpSql, _providerInvariant);
        }
        // Get conflicting migrations if any
        var conflicts = GetConflictingMigrations(unapplied);
        if (conflicts.Any())
        {
            Utils.SendErrorMessage("ERROR: semantic conflicts detected — multiple pending migrations touch the same table.");
            foreach (var kv in conflicts)
            {
                Utils.SendInfoMessage($"Table: {kv.Key}");
                foreach (var m in kv.Value)
                    Console.WriteLine($"  - {m.Version} ({Path.GetFileName(m.Filename)})");
            }

            if (!force)
            {
                Utils.SendHelpMessage(
                    "Resolve conflicts (rename / reorder / combine migrations) and run apply again.\n" +
                    "Use --force to bypass this check if you know what you're doing."
                );
                return;
            }
            var confirmed = Utils.ConfirmDangerousOperation(
        "WARNING: You are about to apply migrations with detected conflicts.\n" +
        "This may cause irreversible or unwanted schema changes.");

            if (!confirmed)
            {
                Utils.SendInfoMessage("Operation aborted by user.");
                return;
            }

            Utils.SendWarningMessage("Force apply confirmed. Proceeding...");
        }
        // Acquire a global migrator lock to avoid races
        var lockName = "migrator_lock";
        var gotLock = await _runner.AcquireLockAsync(lockName, 30);
        if (!gotLock)
        {
            Utils.SendErrorMessage("Could not acquire migrator lock; aborting to avoid concurrency issues.");
            return;
        }


        try
        {
            var appliedAny = false;

            foreach (var m in unapplied.OrderBy(m => m.Version, StringComparer.Ordinal))
            {
               Utils.SendInfoMessage($"Applying {m.Version} {m.Name}...");
                await ApplyMigrationAsync(m);
                appliedAny = true;
            }

            if (appliedAny)
                Utils.SendInfoMessage("All pending migrations applied.");
            else
                Utils.SendWarningMessage("No migrations were applied.");
        }
        finally
        {
            await _runner.ReleaseLockAsync(lockName);
        }
    }


    internal async Task ApplySpecificAsync(string version)
    {
        await EnsureVersionTableAsync();

        var all = LoadAllMigrations().ToList();
        var m = all.FirstOrDefault(x => x.Version == version);

        if (m == null)
        {
            Utils.SendErrorMessage($"Migration {version} not found.");
            return;
        }

        ValidateMigrationMetadata(m);
        if (m.Header.UseTransaction)
            UnsafePatternDetector.AssertSafe(m.UpSql, _providerInvariant);
        else
            UnsafePatternDetector.AssertSafeOutsideTransaction(m.UpSql, _providerInvariant);

        // Acquire same global lock as normal apply
        var lockName = "migrator_lock";
        if (!await _runner.AcquireLockAsync(lockName, 30))
        {
            Utils.SendErrorMessage("Could not acquire migrator lock.");
            return;
        }

        try
        {
            // Check if already applied
            string sql = ScriptingEngine.GetVersionExistsQuery(_providerInvariant, VersionTable);
            var exists = await _runner.QueryScalarAsync<int?>(sql, new Dictionary<string, object?> { { "v", version } });

            if (exists.GetValueOrDefault() > 0)
            {
                Utils.SendInfoMessage($"Migration {version} is already applied.");
                return;
            }

            await ApplyMigrationAsync(m);
        }

        finally
        {
            await _runner.ReleaseLockAsync(lockName);
        }

    }
    
    internal async Task RollbackLastAsync()
    {
        string getLastVersionQuery = ScriptingEngine.GetLatestVersionQuery(_providerInvariant, VersionTable);
        var last = await _runner.QueryScalarAsync<string>(getLastVersionQuery);
        if (last == null) { Utils.SendInfoMessage("No migrations to rollback."); return; }
        await RollbackVersionAsync(last);
    }
    internal async Task RollbackToAsync(string target)
    {
        var toRollback = (await GetAppliedVersionsAsync())
            .Where(v => string.Compare(v, target, StringComparison.Ordinal) > 0)
            .OrderByDescending(v => v)
            .ToList();

        foreach (var v in toRollback) await RollbackVersionAsync(v);
    }

    internal async Task RollbackAllAppliedAsync()
    {
        var toRollback = (await GetAppliedVersionsAsync()).ToList();
        foreach (var v in toRollback) await RollbackVersionAsync(v);
    }

    internal async Task RollbackVersionAsync(string version, bool skipChecksum = false)
    {
        var m = LoadAllMigrations().FirstOrDefault(x => x.Version == version);
        if (m == null)
        {
            Utils.SendErrorMessage($"Migration {version} not found.");
            return;
        }

        if (!m.IsReversible)
        {
            Utils.SendErrorMessage($"Migration {version} is irreversible");
            return;
        }

        if (!skipChecksum && !(await ValidateChecksumAsync(m, version)))
        {
            Utils.SendErrorMessage($"Checksum validation failed for migration {version}; aborting rollback.");
            return;
        }

        Utils.SendInfoMessage($"Rolling back {version}...");

        var downSql = await GetVersionDownScriptAsync(version);
        var statements = Utils.SplitSqlStatements(downSql);

        foreach (var stmt in statements)
        {
            if (m.Header.UseTransaction)
                UnsafePatternDetector.AssertSafe(stmt, _providerInvariant);
            else
                UnsafePatternDetector.AssertSafeOutsideTransaction(stmt, _providerInvariant);
        }


        var (connection, transaction) =
            await _runner.ExecuteScriptAsync(statements, m.Header.UseTransaction);

        string deleteSql = ScriptingEngine.GetDeleteVersionQuery(_providerInvariant, VersionTable);
        await _runner.ExecuteNonQueryAsync(deleteSql, new Dictionary<string, object?> { { "v", version } }, connection, transaction);

        try
        {
            transaction?.Commit();
            Utils.SendInfoMessage($"Rolled back {version}");
        }
        catch
        {
            if (transaction != null)
            {
                try { transaction.Rollback(); } catch { }
            }
            throw;
        }
        finally
        {
            transaction?.Dispose();
            connection.Dispose();
        }
    }


    internal async Task RedoAsync(string version)
    {
        var migration = LoadAllMigrations().FirstOrDefault(m => m.Version == version);
        if (migration == null) throw new Exception($"Migration {version} not found.");
        await RollbackVersionAsync(version, skipChecksum: true);
        await ApplySpecificAsync(version);
        var newChecksum = Utils.ComputeSha256Hex(migration.UpSql + "\n" + migration.DownSql);
        await UpdateMigrationChecksumAsync(version, newChecksum);
    }


    internal void SetupSupportedProviders()
    {
        DbProviderFactories.RegisterFactory(SupportedProviders.mssql, SqlClientFactory.Instance);
        DbProviderFactories.RegisterFactory(SupportedProviders.postgresql, NpgsqlFactory.Instance);
        DbProviderFactories.RegisterFactory(SupportedProviders.mysql, MySqlConnectorFactory.Instance);
        DbProviderFactories.RegisterFactory(SupportedProviders.oracle, MySqlClientFactory.Instance);
    }

    private static List<KeyValuePair<string, List<Migration>>> GetConflictingMigrations(IEnumerable<Migration> unappliedMigrations)
    {
        var touched = new Dictionary<string, List<Migration>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in unappliedMigrations)
        {
            foreach (var t in SqlTableExtractor.ExtractTableNames(m.Sql))
            {
                if (!touched.TryGetValue(t, out var list)) touched[t] = list = new List<Migration>();
                list.Add(m);
            }
        }
        var conflicts = touched.Where(kv => kv.Value.Count > 1).ToList();
        return conflicts;
    }

    private async Task LogMigrationAsync( DbConnection con,DbTransaction tx,Migration m)
    {
        var sql = ScriptingEngine.GetInsertMigrationQuery(_providerInvariant, VersionTable);

        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;

        var values = new Dictionary<string, object?>
        {
            ["version"] = m.Version,
            ["filename"] = Path.GetFileName(m.Filename),
            ["checksum"] = m.Checksum,
            ["author"] = m.Header?.Author,
            ["branch"] = m.Header?.Branch,
            ["down"] = string.IsNullOrWhiteSpace(m.DownSql)? null: Utils.CompressString(m.DownSql),
            ["tnx"] = m.Header?.UseTransaction,

        };

        var types = new Dictionary<string, DbType>{ ["down"] = DbType.Binary};
        ScriptingEngine.BuildParameters(cmd, _providerInvariant, values, types);

        await cmd.ExecuteNonQueryAsync();
    }


  private async Task UpdateMigrationChecksumAsync(string version, string newChecksum)
    {
        var sql = ScriptingEngine.GetUpdateChecksumQuery(_providerInvariant, VersionTable);
    await _runner.ExecuteNonQueryAsync(sql, ScriptingEngine.BuildParameters(_providerInvariant, new Dictionary<string, object?>
    {
        { "version", version },
        { "checksum", newChecksum }
    }));
    }


    private async Task<HashSet<string>> GetAppliedVersionsAsync()
    {
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var query = ScriptingEngine.GetAppliedVersionsQuery(_providerInvariant, VersionTable);
        var result = await _runner.QueryScalarAsync<object>(query);

        if (result is IEnumerable<object> rows)
        {
            foreach (var row in rows) applied.Add(row.ToString());
        }
        else
        {
            string s = result?.ToString() ?? "";
            foreach (var v in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
                applied.Add(v.Trim());
        }
        return applied;

    }

    private static string ConstructStatusResult(IEnumerable<Migration> all, HashSet<string> applied)
    {
        var buf = new System.Text.StringBuilder();
        buf.AppendLine("MIGRATIONS STATUS:");
        if (all.Count() == applied.Count())
        {
            buf.AppendLine("No pending migration.\n");
        }
        else
        {
            int pM = all.Count() - applied.Count();
            buf.AppendLine($"{pM} pending migration(s).\n");
        }

        foreach (var m in all)
            {
                buf.AppendLine($"{(applied.Contains(m.Version) ? "[X]" : "[ ]")} {m.Version}  {m.Name}  ({Path.GetFileName(m.Filename)})");
            }

            var conflicts = GetConflictingMigrations(all.Where(m => !applied.Contains(m.Version)).ToList());
            if (conflicts.Any())
            {
                buf.AppendLine();
                buf.AppendLine("POTENTIAL CONFLICTS (multiple unapplied migrations touch the same table):");
                foreach (var kv in conflicts)
                {
                    buf.AppendLine($"Table: {kv.Key}");
                    foreach (var m in kv.Value)
                        buf.AppendLine($"  - {m.Version} {m.Name} --Author: {m.Header.Author}");
                    buf.AppendLine("\n");
                 }
             }
        
        return buf.ToString();
    }


    private async Task ApplyMigrationAsync(Migration m)
    {
        // Signature verification (optional)
        // if (!SignatureVerifier.Verify(m, _publicKeyBytes))
        //     throw new Exception($"Signature verification failed for {m.Version}");

        ValidateMigrationMetadata(m);

        var statements = Utils.SplitSqlStatements(m.UpSql);

        // Run safety checks before execution
        foreach (var stmt in statements)
        {
            if (m.Header.UseTransaction)
                UnsafePatternDetector.AssertSafe(stmt, _providerInvariant);
            else
                UnsafePatternDetector.AssertSafeOutsideTransaction(stmt, _providerInvariant);
        }

        // Execute statements (transactional or not)
        var (connection, transaction) = await _runner.ExecuteScriptAsync(statements,useTransaction: m.Header.UseTransaction );

        try
        {
            // Record migration
            await LogMigrationAsync(connection, transaction, m);

            // Commit only if we started the transaction
            if (m.Header.UseTransaction && transaction != null)
                transaction.Commit();

            Utils.SendInfoMessage($"Applied {m.Version}");
        }
        catch
        {
            // Rollback only if transactional
            if (m.Header.UseTransaction && transaction != null)
            {
                try { transaction.Rollback(); } catch { /* ignore */ }
            }
            throw;
        }
        finally
        {
            // Dispose connection if ExecuteScriptAsync created it internally
            if (transaction != null)
                await transaction.DisposeAsync();

            await connection.DisposeAsync();
        }
    }

    private async Task<bool> ValidateChecksumAsync(Migration m, string version )
    {
        string query = ScriptingEngine.GetChecksumByMigrationVersionQuery(_providerInvariant, VersionTable);
        string existingChecksum = await _runner.QueryScalarAsync<string>(query, new Dictionary<string, object?> { { "version", version } });
        return string.Equals(m.Checksum, existingChecksum, StringComparison.OrdinalIgnoreCase);
    }
    private async Task<string> GetVersionDownScriptAsync(string version)
    {
        string sql = ScriptingEngine.GetDownSqlByMigrationVersionQuery(_providerInvariant, VersionTable);
        byte[]? compressed = await _runner.QueryScalarBytesAsync(sql, new Dictionary<string, object?> { { "version", version } });

        return compressed == null
            ? throw new InvalidOperationException($"Migration {version} has no stored down script. " +
            "It may not have been applied or is irreversible." ) : Utils.DecompressString(compressed);
    }

    private IEnumerable<Migration> LoadAllMigrations()
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
                Utils.SendWarningMessage($"Skipping invalid migration file {f}: {ex.Message}");
            }
        }
        return list;
    }

    private static void ValidateMigrationMetadata(Migration m)
    {
        if (m.Header == null)
        {
            Utils.SendErrorMessage($"Migration {m.Version} is missing a header.");
            throw new Exception("Invalid migration header.");
        }
        if (string.IsNullOrWhiteSpace(m.Header.Author))
        {
            Utils.SendErrorMessage($"Migration {m.Version} must declare an author.");
            throw new Exception("Missing migration author.");
        }

        if (string.IsNullOrWhiteSpace(m.Header.Branch))
            Utils.SendWarningMessage($"Migration {m.Version} does not declare a branch.");
    }
  
}

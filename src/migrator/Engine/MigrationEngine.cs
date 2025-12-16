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
        try{  await _runner.ExecuteNonQueryAsync(ScriptingEngine.GetEnsureVersionTableQuery(_providerInvariant, VersionTable)); }
        catch (Exception ex){ Utils.SendWarningMessage($"Warning: automatic version-table creation failed for provider {_providerInvariant}. You may need to create {VersionTable} manually. {ex.Message}"); }
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
                Utils.SendWarningMessage($"Skipping invalid migration file {f}: {ex.Message}");
            }
        }
        return list;
    }



    public async Task<string> GetStatusAsync()
    {
        await EnsureVersionTableAsync();
        var applied = await GetAppliedVersionsAsync();
        var all = LoadAllMigrations().ToList();
       return ConstructStatusResult(all, applied);
    }


    public async Task ApplyMigrationsAsync()
    {
        await EnsureVersionTableAsync();

        // Already applied versions
        var applied = await GetAppliedVersionsAsync();

        var all = LoadAllMigrations().ToList();

        var unapplied = all.Where(m => !applied.Contains(m.Version)).ToList();

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
            Utils.SendHelpMessage("Resolve conflicts (rename / reorder / combine migrations) and run apply again.");
            return;
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

            foreach (var m in unapplied)
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


    public async Task ApplySpecificAsync(string version)
    {
        await EnsureVersionTableAsync();

        var all = LoadAllMigrations().ToList();
        var m = all.FirstOrDefault(x => x.Version == version);

        if (m == null)
            throw new Exception($"Migration not found: {version}");

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


    public async Task RollbackLastAsync()
    {
        string getLastVersionQuery = ScriptingEngine.GetLatestVersionQuery(_providerInvariant, VersionTable);
        var last = await _runner.QueryScalarAsync<string>(getLastVersionQuery);
        if (last == null) { Utils.SendInfoMessage("No migrations to rollback."); return; }
        await RollbackVersionAsync(last);
    }
    public async Task RollbackToAsync(string target)
    {
        var toRollback = (await GetAppliedVersionsAsync())
            .Where(v => string.Compare(v, target, StringComparison.OrdinalIgnoreCase) > 0)
            .OrderByDescending(v => v)
            .ToList();

        foreach (var v in toRollback) await RollbackVersionAsync(v);
    }

    public async Task RedoAsync(string version)
    {
        var migration = LoadAllMigrations().FirstOrDefault(m => m.Version == version);
        if (migration == null) throw new Exception($"Migration {version} not found.");
        await RollbackVersionAsync(version, skipChecksum: true);
        await ApplySpecificAsync(version);
        var newChecksum = Utils.ComputeSha256Hex(migration.UpSql + "\n" + migration.DownSql);
        await UpdateMigrationChecksumAsync(version, newChecksum);
    }

    private async Task RollbackVersionAsync(string version, bool skipChecksum = false)
    {
        var m = LoadAllMigrations().FirstOrDefault(x => x.Version == version);
        if (m == null) throw new Exception("Migration not found");

        if (!m.IsReversible)
        {
            Utils.SendErrorMessage($"Migration {version} is irreversible");
            return;
        }

        if(!skipChecksum && !(await ValidateChecksumAsync(m, version)))
        {
            Utils.SendErrorMessage($"Checksum validation failed for migration {version}; aborting rollback.");
            return;
        }

        Utils.SendInfoMessage($"Rolling back {version}...");

        var stmts = Utils.SplitSqlStatements((await GetVersionDownScriptAsync(version)));
        var factory = DbProviderFactories.GetFactory(_providerInvariant);

        using var con = factory.CreateConnection();
        con.ConnectionString = _connectionString;

        await con.OpenAsync();
        using var tx = con.BeginTransaction();

        try
        {
            foreach (var sqlQuery in stmts)
                await _runner.ExecuteNonQueryAsync(sqlQuery, externalConnection: con, externalTransaction: tx);

            string deleteSql = ScriptingEngine.GetDeleteVersionQuery(_providerInvariant, VersionTable);
            await _runner.ExecuteNonQueryAsync(deleteSql,new Dictionary<string, object?> { { "v", version }},con, tx);

            tx.Commit();

            Utils.SendInfoMessage($"Rolled back {version}");
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }


    public void SetupSupportedProviders()
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
            ["down"] = string.IsNullOrWhiteSpace(m.DownSql)? null: Utils.CompressString(m.DownSql)
        };

        var types = new Dictionary<string, DbType>{ ["down"] = DbType.Binary};
        ScriptingEngine.BuildParameters(cmd, _providerInvariant, values, types);

        await cmd.ExecuteNonQueryAsync();
    }


    public async Task UpdateMigrationChecksumAsync(string version, string newChecksum)
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


    public async Task ApplyMigrationAsync(Migration m)
    {

        // Signature verification
        //if (!SignatureVerifier.Verify(m, _publicKeyBytes))
        //    throw new Exception($"Signature verification failed for {m.Version}");

        // Unsafe SQL check
        //UnsafePatternDetector.AssertSafe(m.UpSql);
        ValidateMigrationMetadata(m);
       
        var upStatements = Utils.SplitSqlStatements(m.UpSql);
        var factory = DbProviderFactories.GetFactory(_providerInvariant);

        using var con = factory.CreateConnection();
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

            // Record migration
            await LogMigrationAsync(con, tx, m);
            tx.Commit();
            Utils.SendInfoMessage($"Applied {m.Version}");

        }
        catch (Exception ex)
        {
            try { tx.Rollback(); } catch { }
            throw new Exception($"Failed to apply {m.Version}: {ex.Message}", ex);
        }

    }

    public async Task<bool> ValidateChecksumAsync(Migration m, string version )
    {
        string query = ScriptingEngine.GetChecksumByMigrationVersionQuery(_providerInvariant, VersionTable);
        string existingChecksum = await _runner.QueryScalarAsync<string>(query, new Dictionary<string, object?> { { "version", version } });
        return string.Equals(m.Checksum, existingChecksum, StringComparison.OrdinalIgnoreCase);
    }
    public async Task<string> GetVersionDownScriptAsync(string version)
    {
        string sql = ScriptingEngine.GetDownSqlByMigrationVersionQuery(_providerInvariant, VersionTable);
        byte[]? compressed = await _runner.QueryScalarBytesAsync(sql, new Dictionary<string, object?> { { "version", version } });

        return compressed == null
            ? throw new InvalidOperationException($"Migration {version} has no stored down script. " +
            "It may not have been applied or is irreversible." ) : Utils.DecompressString(compressed);
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

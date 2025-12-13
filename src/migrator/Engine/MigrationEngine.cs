
//using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient;
using migrator.Providers;
using MySql.Data.MySqlClient;
using MySqlConnector;
using Npgsql;
using System.Data.Common;
//using System.Data.SqlClient;
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
        try
        {  
            await _runner.ExecuteNonQueryAsync(GetEnsureVersionTableQuery(_providerInvariant, VersionTable));
        }
        catch (Exception ex)
        {
            Utils.SendWarningMessage($"Warning: automatic version-table creation failed for provider {_providerInvariant}. You may need to create {VersionTable} manually. {ex.Message}");
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
                    Console.WriteLine($"  - {m.Version} {m.Name} ({Path.GetFileName(m.Filename)})");
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
            foreach (var m in unapplied)
            {
                Utils.SendInfoMessage($"Applying {m.Version} {m.Name}...");
                await ApplyMigrationAsync(m);
            }

            Utils.SendInfoMessage("All pending migrations applied.");
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
            string sql = GetVersionExistsQuery(_providerInvariant, VersionTable);
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
        string getLastVersionQuery = GetLatestVersionQuery(_providerInvariant, VersionTable);
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
   

        if(!(await ValidateChecksum(m, version)))
            throw new Exception($"Checksum validation failed for migration {version}; aborting rollback.");


        Utils.SendInfoMessage($"Rolling back {version}...");

        var stmts = Utils.SplitSqlStatements(m.DownSql);
        var factory = DbProviderFactories.GetFactory(_providerInvariant);

        using var con = factory.CreateConnection();
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

            string deleteSql = GetDeleteVersionQuery(_providerInvariant, VersionTable);
            await _runner.ExecuteNonQueryAsync(deleteSql,new Dictionary<string, object?> { { "v", version } },
                con, tx);

            tx.Commit();

            Utils.SendInfoMessage($"Rolled back {version}");
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


    private static async Task LogMigrationAsync(DbConnection con, DbTransaction tx, Migration m) 
    {
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

    }





    public static string GetLatestVersionQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.mssql:
                return $"SELECT TOP 1 version FROM {versionTable} ORDER BY id DESC";
            case SupportedProviders.mysql:
            case SupportedProviders.postgresql:
                return $"SELECT version FROM {versionTable} ORDER BY id DESC LIMIT 1";
            case SupportedProviders.oracle:
                return $"SELECT version FROM {versionTable} ORDER BY id DESC FETCH FIRST 1 ROWS ONLY";
            default:
                throw new NotSupportedException($"Unsupported provider: {provider}");
        }
    }



    public static string GetEnsureVersionTableQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.postgresql:
                return $@"
CREATE TABLE IF NOT EXISTS {versionTable} (
    id BIGSERIAL PRIMARY KEY,
    version VARCHAR(100) NOT NULL UNIQUE,
    filename TEXT,
    checksum CHAR(64),
    author TEXT,
    branch TEXT,
    commit_id TEXT,
    applied_at TIMESTAMP WITH TIME ZONE DEFAULT now()
);";

            case SupportedProviders.mssql:
                // SQL Server
                return $@"
IF NOT EXISTS (
    SELECT * FROM sys.objects 
    WHERE object_id = OBJECT_ID(N'{versionTable}') AND type = N'U'
)
BEGIN
    CREATE TABLE {versionTable} (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        version VARCHAR(100) NOT NULL UNIQUE,
        filename NVARCHAR(MAX),
        checksum CHAR(64),
        author NVARCHAR(200),
        branch NVARCHAR(200),
        commit_id NVARCHAR(200),
        applied_at DATETIMEOFFSET DEFAULT SYSDATETIMEOFFSET()
    );
END";

            case SupportedProviders.mysql:
                return $@"
CREATE TABLE IF NOT EXISTS {versionTable} (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    version VARCHAR(100) NOT NULL UNIQUE,
    filename TEXT,
    checksum CHAR(64),
    author VARCHAR(200),
    branch VARCHAR(200),
    commit_id VARCHAR(200),
    applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB;";

            case SupportedProviders.oracle:
                // Oracle requires a workaround because:
                // - No IF NOT EXISTS
                // - AUTO_INCREMENT requires IDENTITY since Oracle 12c
                return $@"
BEGIN
    EXECUTE IMMEDIATE '
        CREATE TABLE {versionTable} (
            id NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            version VARCHAR2(100) NOT NULL UNIQUE,
            filename CLOB,
            checksum CHAR(64),
            author VARCHAR2(200),
            branch VARCHAR2(200),
            commit_id VARCHAR2(200),
            applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
        )
    ';
EXCEPTION
    WHEN OTHERS THEN
        IF SQLCODE = -955 THEN NULL; ELSE RAISE; END IF;
END;";

            default:
                // Generic fallback
                return $@"
CREATE TABLE IF NOT EXISTS {versionTable} (
    id INTEGER PRIMARY KEY,
    version VARCHAR(100) NOT NULL UNIQUE,
    filename TEXT,
    checksum TEXT,
    author TEXT,
    branch TEXT,
    commit_id TEXT,
    applied_at DATETIME
);";
        }
    }




    private static string GetAppliedVersionsQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.postgresql:
            case SupportedProviders.mssql:
                return $"SELECT STRING_AGG(version, ',') FROM {versionTable};";

            case SupportedProviders.mysql:
                return $"SELECT GROUP_CONCAT(version) FROM {versionTable};";

            case SupportedProviders.oracle:
                return $"SELECT LISTAGG(version, ',') WITHIN GROUP (ORDER BY id) FROM {versionTable}";

            default:
                return $"SELECT version FROM {versionTable};";
        }
    }
    private static string GetChecksumByMigrationVersionQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.postgresql:
            case SupportedProviders.mssql:
            case SupportedProviders.mysql:
                // All these use '@parameter'
                return $"SELECT checksum FROM {versionTable} WHERE version = @version;";

            case SupportedProviders.oracle:
                // Oracle must use ':parameter'
                return $"SELECT checksum FROM {versionTable} WHERE version = :version FETCH FIRST 1 ROWS ONLY";

            default:
                return $"SELECT checksum FROM {versionTable} WHERE version = @version;";
        }
    }


    private async Task<HashSet<string>> GetAppliedVersionsAsync()
    {
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var query = GetAppliedVersionsQuery(_providerInvariant, VersionTable);
        var result = await _runner.QueryScalarAsync<object>(query);

        if (result is IEnumerable<object> rows)
        {
            foreach (var row in rows)
                applied.Add(row.ToString());
        }
        else
        {
            string s = result?.ToString() ?? "";
            foreach (var v in s.Split(',', StringSplitOptions.RemoveEmptyEntries))
                applied.Add(v.Trim());
        }
        return applied;

    }
    private static string GetDeleteVersionQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.postgresql:
            case SupportedProviders.mssql:
            case SupportedProviders.mysql:
                // Uses @v
                return $"DELETE FROM {versionTable} WHERE version = @v;";

            case SupportedProviders.oracle:
                // Uses :v
                return $"DELETE FROM {versionTable} WHERE version = :v";

            default:
                return $"DELETE FROM {versionTable} WHERE version = @v;";
        }
    }

    private static string GetVersionExistsQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.postgresql:
            case SupportedProviders.mssql:
            case SupportedProviders.mysql:
                return $"SELECT COUNT(*) FROM {versionTable} WHERE version = @v;";

            case SupportedProviders.oracle:
                return $"SELECT COUNT(*) FROM {versionTable} WHERE version = :v";

            default:
                return $"SELECT COUNT(*) FROM {versionTable} WHERE version = @v;";
        }
    }

    private string ConstructStatusResult(IEnumerable<Migration> all, HashSet<string> applied)
    {
        var buf = new System.Text.StringBuilder();
        buf.AppendLine("MIGRATIONS STATUS:");
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
                    buf.AppendLine($"  - {m.Version} {m.Name}");
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

    public async Task<bool> ValidateChecksum(Migration m, string version )
    {
        string query = GetChecksumByMigrationVersionQuery(_providerInvariant, VersionTable);
        string existingChecksum = await _runner.QueryScalarAsync<string>(query, new Dictionary<string, object?> { { "version", version } });
        return string.Equals(m.Checksum, existingChecksum, StringComparison.OrdinalIgnoreCase);
    }


}

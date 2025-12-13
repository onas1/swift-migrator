

using migrator.Providers;
using System.Data.Common;

namespace migrator.Engine;


public class SqlRunner
{
    private readonly string _providerInvariant;
    private readonly string _connectionString;

    public SqlRunner(string providerInvariant, string connectionString)
    {
        _providerInvariant = providerInvariant;
        _connectionString = connectionString;
    }

    private DbProviderFactory GetFactory()
    {
        return DbProviderFactories.GetFactory(_providerInvariant);
    }

    public async Task ExecuteScriptAsync(string sql, DbConnection? externalConnection = null, DbTransaction? externalTransaction = null)
    {
        // If an external connection/transaction is supplied, use that; otherwise create and execute without transaction.
        if (externalConnection != null)
        {
            using var command = externalConnection.CreateCommand();
            command.CommandText = sql;
            if (externalTransaction != null) command.Transaction = externalTransaction;
            await command.ExecuteNonQueryAsync();
            return;
        }

        using var con = GetFactory().CreateConnection();
        con!.ConnectionString = _connectionString;
        await con.OpenAsync();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }






    // Execute multiple statements within a transaction (if provider supports transactions across statements).
    public async Task ExecuteScriptWithTransactionAsync(string sql)
    {
        using var con = GetFactory().CreateConnection();
        con!.ConnectionString = _connectionString;
        await con.OpenAsync();
        using var tx = con.BeginTransaction();
        try
        {
            using var cmd = con.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
        }
        catch
        {
            try { tx.Rollback(); } catch { /* ignore */ }
            throw;
        }
    }

    // Parameterized non-query helper (for inserting into version table)
    public async Task ExecuteNonQueryAsync(string sql, IDictionary<string, object?>? parameters = null, DbConnection? externalConnection = null, DbTransaction? externalTransaction = null)
    {
        if (externalConnection != null)
        {
            using var command = externalConnection.CreateCommand();
            command.CommandText = sql;
            if (externalTransaction != null) command.Transaction = externalTransaction;
            AddParameters(command, parameters);
            await command.ExecuteNonQueryAsync();
            return;
        }

        using var con = GetFactory().CreateConnection();
        con!.ConnectionString = _connectionString;
        await con.OpenAsync();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T?> QueryScalarAsync<T>(string sql, IDictionary<string, object?>? parameters = null)
    {
        using var con = GetFactory().CreateConnection();
        con!.ConnectionString = _connectionString;
        await con.OpenAsync();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);
        var res = await cmd.ExecuteScalarAsync();



        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(res, targetType);
    }

    private void AddParameters(DbCommand cmd, IDictionary<string, object?>? parameters)
    {
        if (parameters == null) return;
        foreach (var kv in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = kv.Key.StartsWith("@") ? kv.Key : "@" + kv.Key;
            p.Value = kv.Value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }

    // Provider-specific advisory locks

    // Acquire a named lock (returns true if lock acquired)
    public async Task<bool> AcquireLockAsync(string lockName, int timeoutSeconds = 30)
    {
        // Simple provider detection based on invariant name
        if (_providerInvariant == SupportedProviders.postgresql)
        {
            // Use a 64-bit hash of the lockName and call pg_advisory_lock
            var hash = ComputeInt64Hash(lockName);
            var sql = $"SELECT pg_try_advisory_lock({hash});";
            var ok = await QueryScalarAsync<bool>(sql);
            return ok;
        }
        else if (_providerInvariant == SupportedProviders.mssql)
        {
            // Use sp_getapplock
            var sql = $"EXEC sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockTimeout = @to, @DbPrincipal = 'public'";
            var parameters = new Dictionary<string, object?> { { "@res", lockName }, { "@to", timeoutSeconds * 1000 } };
            try
            {
                await ExecuteNonQueryAsync(sql, parameters);
                return true;
            }
            catch
            {
                return false;
            }
        }
        else if (_providerInvariant == SupportedProviders.mysql)
        {
            var sql = $"SELECT GET_LOCK(@name, @to)";
            var parameters = new Dictionary<string, object?> { { "@name", lockName }, { "@to", timeoutSeconds } };
            var ok = await QueryScalarAsync<long>(sql);
            return ok == 1;
        }
        // Fallback: no lock support, return true but caller should be aware
        Utils.SendInfoMessage($"Warning: Locking not supported for provider '{_providerInvariant}'. Proceeding without acquiring lock.");
        return true;
    }

    public async Task ReleaseLockAsync(string lockName)
    {
        if (_providerInvariant == SupportedProviders.postgresql)
        {
            var hash = ComputeInt64Hash(lockName);
            await ExecuteNonQueryAsync($"SELECT pg_advisory_unlock({hash});");
        }
        else if (_providerInvariant == SupportedProviders.mssql)
        {
            var sql = $"EXEC sp_releaseapplock @Resource = @res, @LockOwner = 'Public'";
            await ExecuteNonQueryAsync(sql, new Dictionary<string, object?> { { "@res", lockName } });
        }
        else if (_providerInvariant == SupportedProviders.mysql)
        {
            await ExecuteNonQueryAsync($"SELECT RELEASE_LOCK(@name)", new Dictionary<string, object?> { { "@name", lockName } });
        }
    }

    private long ComputeInt64Hash(string input)
    {
        using var sha = System.Security.Cryptography.SHA1.Create();
        var b = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        // Use first 8 bytes as signed 64-bit
        long val = 0;
        for (int i = 0; i < 8; i++) val = (val << 8) | b[i];
        return Math.Abs(val);
    }
}

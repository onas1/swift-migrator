

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

    /// <summary>
    /// Executes statements and optionally starts a transaction.
    /// Returns the connection and transaction if one was created (transaction may be null if not used).
    /// </summary>
    public async Task<(DbConnection connection, DbTransaction? transaction)> ExecuteScriptAsync( IEnumerable<string> statements,bool useTransaction = true, DbConnection? externalConnection = null, DbTransaction? externalTransaction = null)
    {
        // Use external connection if provided; otherwise create a new one
        var ownConnection = externalConnection == null;
        var con = externalConnection ?? GetFactory().CreateConnection()
            ?? throw new InvalidOperationException("Failed to create database connection.");

        if (externalConnection == null)
            con.ConnectionString = _connectionString;
        if (con.State != System.Data.ConnectionState.Open)
            await con.OpenAsync();

        // Determine whether to use a transaction
        DbTransaction? tx = externalTransaction;
        var ownTransaction = useTransaction && tx == null;
        if (ownTransaction)
            tx = con.BeginTransaction();

        try
        {
            foreach (var stmt in statements)
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = stmt;
                if (tx != null) cmd.Transaction = tx;

                await cmd.ExecuteNonQueryAsync();
            }

            // Return connection and transaction (may be null if non-transactional)
            return (con, tx);
        }
        catch
        {
            // Only rollback if we started the transaction ourselves
            if (ownTransaction && tx != null)
            {
                try { tx.Rollback(); } catch { /* ignore */ }
            }
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




    public async Task<byte[]?> QueryScalarBytesAsync(string sql, IDictionary<string, object?>? parameters = null)
    {
        using var con = GetFactory().CreateConnection();
        con!.ConnectionString = _connectionString;
        await con.OpenAsync();

        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);

        var res = await cmd.ExecuteScalarAsync();

        if (res == null || res is DBNull)
            return null;

        return res switch
        {
            byte[] b => b,
            ReadOnlyMemory<byte> rom => rom.ToArray(),
            _ => throw new InvalidCastException($"Expected binary data but got {res.GetType().FullName}")
        };
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
        if (_providerInvariant == SupportedProviders.postgresql)
        {
            // Use a 64-bit hash of the lockName and call pg_advisory_lock
            var hash = Utils.ComputeInt64Hash(lockName);
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
            var hash = Utils.ComputeInt64Hash(lockName);
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

   
}

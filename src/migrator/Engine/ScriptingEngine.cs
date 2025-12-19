

using migrator.Providers;
using System.Data;
using System.Data.Common;

namespace migrator.Engine;

public static class ScriptingEngine
{
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
    down_script BYTEA,
    use_transaction BOOLEAN NOT NULL DEFAULT TRUE,
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
        down_script VARBINARY(MAX),
        use_transaction BIT NOT NULL DEFAULT 1,
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
    down_script LONGBLOB,
    use_transaction BOOLEAN NOT NULL DEFAULT TRUE,
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
            down_script BLOB,
            use_transaction NUMBER(1) DEFAULT 1 NOT NULL,
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
    down_script TEXT,
    use_transaction BOOLEAN NOT NULL DEFAULT TRUE,
    applied_at DATETIME
);";
        }
    }

    public static string GetAppliedVersionsQuery(string provider, string versionTable)
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
    public static string GetChecksumByMigrationVersionQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.oracle:
                // Oracle must use ':parameter'
                return $"SELECT checksum FROM {versionTable} WHERE version = :version FETCH FIRST 1 ROWS ONLY";

            default:
                return $"SELECT checksum FROM {versionTable} WHERE version = @version;";
        }
    }
    public static string GetDownSqlByMigrationVersionQuery(string provider, string versionTable)
    {
        switch (provider)
        {
           
            case SupportedProviders.oracle:
                // Oracle must use ':parameter'
                return $"SELECT down_script FROM {versionTable} WHERE version = :version FETCH FIRST 1 ROWS ONLY";

            default:
                return $"SELECT down_script FROM {versionTable} WHERE version = @version;";
        }
    }

    public static string GetDeleteVersionQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.oracle:
                return $"DELETE FROM {versionTable} WHERE version = :v";

            default:
                return $"DELETE FROM {versionTable} WHERE version = @v;";
        }
    }

    public static string GetVersionExistsQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.oracle:
                return $"SELECT COUNT(*) FROM {versionTable} WHERE version = :v";

            default:
                return $"SELECT COUNT(*) FROM {versionTable} WHERE version = @v;";
        }
    }

    public static string GetUpdateChecksumQuery(string provider, string versionTable)
    {
        switch (provider)
        {
            case SupportedProviders.oracle:
                return $"UPDATE {versionTable} SET checksum = :checksum WHERE version = :version";

            default:
                return $"UPDATE {versionTable} SET checksum = @checksum WHERE version = @version;";
        }
    }

    public static string GetInsertMigrationQuery(string provider, string versionTable)
    {
        return provider switch
        {
            SupportedProviders.oracle =>
                $@"INSERT INTO {versionTable}
               (version, filename, checksum, author, branch, down_script, use_transaction)
               VALUES (:version, :filename, :checksum, :author, :branch, :down, tnx)",

            _ =>
                $@"INSERT INTO {versionTable}
               (version, filename, checksum, author, branch, down_script, use_transaction)
               VALUES (@version, @filename, @checksum, @author, @branch, @down, @tnx)"
        };
    }

    public static Dictionary<string, object?> BuildParameters(string providerInvariant, Dictionary<string, object?> parameters)
    {
        string prefix = providerInvariant switch
        {
            SupportedProviders.oracle => ":",
            _ => "@"
        };

        var result = new Dictionary<string, object?>();
        foreach (var kv in parameters)
        {
            result[prefix + kv.Key] = kv.Value;
        }

        return result;
    }


    public static void BuildParameters( DbCommand cmd, string providerInvariant,Dictionary<string, object?> parameters,Dictionary<string, DbType>? types = null)
    {
        string prefix = providerInvariant switch
        {
            SupportedProviders.oracle => ":",
            _ => "@"
        };

        foreach (var kv in parameters)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = prefix + kv.Key;
            p.Value = kv.Value ?? DBNull.Value;

            if (types != null && types.TryGetValue(kv.Key, out var dbType))
                p.DbType = dbType;

            cmd.Parameters.Add(p);
        }
    }
}

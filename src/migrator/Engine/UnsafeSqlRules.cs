

using migrator.Providers;
using System.Text.RegularExpressions;

namespace migrator.Engine;

public static class UnsafeSqlRules
{
    public static readonly Dictionary<string, List<UnsafeSqlRule>> Rules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [SupportedProviders.postgresql] = new()
            {
                new(@"create\s+index\s+concurrently",
                    "PostgreSQL: CREATE INDEX CONCURRENTLY cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"drop\s+index\s+concurrently",
                    "PostgreSQL: DROP INDEX CONCURRENTLY cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"\bvacuum\b",
                    "PostgreSQL: VACUUM cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"reindex\s+concurrently",
                    "PostgreSQL: REINDEX CONCURRENTLY cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"refresh\s+materialized\s+view\s+concurrently",
                    "PostgreSQL: REFRESH MATERIALIZED VIEW CONCURRENTLY cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                // Operational risks (warn only)
                new(@"\bupdate\s+\w+",
                    "PostgreSQL: Large UPDATE may hold row locks for a long time",
                    SqlRiskLevel.OperationalRisk),

                new(@"\binsert\s+into\b",
                    "PostgreSQL: Large INSERT may be long-running",
                    SqlRiskLevel.OperationalRisk),
            },

            [SupportedProviders.mssql] = new()
            {
                new(@"create\s+database",
                    "SQL Server: CREATE DATABASE cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"alter\s+database",
                    "SQL Server: ALTER DATABASE cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"drop\s+database",
                    "SQL Server: DROP DATABASE cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"\bbackup\b",
                    "SQL Server: BACKUP cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"\brestore\b",
                    "SQL Server: RESTORE cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"\breconfigure\b",
                    "SQL Server: RECONFIGURE cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),
            },

            [SupportedProviders.mysql] = new()
            {
                new(@"create\s+database",
                    "MySQL: CREATE DATABASE causes implicit commit",
                    SqlRiskLevel.ImplicitCommit),

                new(@"alter\s+database",
                    "MySQL: ALTER DATABASE causes implicit commit",
                    SqlRiskLevel.ImplicitCommit),

                new(@"drop\s+database",
                    "MySQL: DROP DATABASE causes implicit commit",
                    SqlRiskLevel.ImplicitCommit),

                new(@"lock\s+tables",
                    "MySQL: LOCK TABLES cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"unlock\s+tables",
                    "MySQL: UNLOCK TABLES cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),
            },

            [SupportedProviders.oracle] = new()
            {
                new(@"alter\s+system",
                    "Oracle: ALTER SYSTEM cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"alter\s+database",
                    "Oracle: ALTER DATABASE cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"create\s+database",
                    "Oracle: CREATE DATABASE cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"\bshutdown\b",
                    "Oracle: SHUTDOWN cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),

                new(@"\bstartup\b",
                    "Oracle: STARTUP cannot run inside a transaction",
                    SqlRiskLevel.ForbiddenInTransaction),
            }
        };
}

public sealed class UnsafeSqlRule
{
    public Regex Pattern { get; }
    public string Reason { get; }
    public SqlRiskLevel Risk { get; }

    public UnsafeSqlRule(string pattern, string reason, SqlRiskLevel risk)
    {
        Pattern = new Regex(pattern,
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

        Reason = reason;
        Risk = risk;
    }
}

public enum SqlRiskLevel
{
    ForbiddenInTransaction,   // DB errors if wrapped in BEGIN
    ImplicitCommit,           // DB silently commits
    OperationalRisk           // Locks, long runtime, but valid
}

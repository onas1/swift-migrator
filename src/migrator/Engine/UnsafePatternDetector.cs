

using System.Text.RegularExpressions;

namespace migrator.Engine;




public static class UnsafePatternDetector
{
    private static readonly Regex[] Dangerous =
    {
        new Regex(@"DROP\s+TABLE", RegexOptions.IgnoreCase),
        new Regex(@"DROP\s+COLUMN", RegexOptions.IgnoreCase),
        new Regex(@"TRUNCATE\s+TABLE", RegexOptions.IgnoreCase),
        new Regex(@"ALTER\s+TABLE\s+\S+\s+DROP", RegexOptions.IgnoreCase),
        new Regex(@"UPDATE\s+\S+\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline),
        new Regex(@"DELETE\s+FROM\s+\S+\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline),
    };

    public static void AssertSafe(string sql)
    {
        foreach (var r in Dangerous)
        {
            if (r.IsMatch(sql))
                throw new Exception($"Unsafe SQL detected: {r}");
        }
    }
}


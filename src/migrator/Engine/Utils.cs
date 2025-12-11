
//using System.Security.Cryptography;
//using System.Text;
//using System.Text.RegularExpressions;

//namespace migrator.Engine;

//public static class Utils
//{
//    public static string ShortId()
//    {
//        // 6 hex chars random - enough to avoid accidental collisions
//        var b = RandomNumberGenerator.GetBytes(3);
//        return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
//    }

//    public static string TimestampNowUtc()
//    {
//        return DateTime.UtcNow.ToString("yyyyMMddHHmmss");
//    }

//    // Very cheap table name extractor - scans SQL for FROM/INTO/ALTER TABLE/CREATE TABLE/DROP TABLE/UPDATE patterns
//    public static string[] ExtractTableNames(string sql)
//    {
//        if (string.IsNullOrWhiteSpace(sql)) return Array.Empty<string>();
//        var pattern = @"\b(?:into|from|alter\s+table|create\s+table|drop\s+table|update|index\s+on)\s+([`""]?([A-Za-z0-9_\.]+)[`""]?)";
//        var matches = Regex.Matches(sql, pattern, RegexOptions.IgnoreCase);
//        return matches.Select(m => m.Groups[2].Value).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
//    }

//    public static string SanitizeName(string name)
//    {
//        // remove spaces and unsafe chars
//        return Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_\-]", "-").Trim('-');
//    }
//}














































using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;



namespace migrator.Engine;

public static class Utils
{
    public static string ShortId()
    {
        var b = RandomNumberGenerator.GetBytes(3);
        return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
    }

    public static string TimestampNowUtc()
    {
        return DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    }

    // Remove SQL comments and quoted strings to avoid false positives in table extraction/splitting.
    public static string StripCommentsAndQuotedStrings(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return sql;

        // Remove block comments /* ... */
        sql = Regex.Replace(sql, @"/\*[\s\S]*?\*/", " ", RegexOptions.Multiline);

        // Remove single-line comments starting with --
        sql = Regex.Replace(sql, @"--.*?$", " ", RegexOptions.Multiline);

        // Remove single-line comments starting with #
        sql = Regex.Replace(sql, @"#.*?$", " ", RegexOptions.Multiline);

        // Replace quoted string literals with a placeholder (handles single quote and double quote).
        sql = Regex.Replace(sql, @"'([^']|'')*'", "''", RegexOptions.Multiline);
        sql = Regex.Replace(sql, @"""([^""]|"""")*""", "\"\"", RegexOptions.Multiline);

        return sql;
    }

    // Very cheap table name extractor — but first strip comments/strings so regex is less likely to be fooled.
    public static string[] ExtractTableNames(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Array.Empty<string>();
        var clean = StripCommentsAndQuotedStrings(sql);
        var pattern = @"\b(?:into|from|alter\s+table|create\s+table|drop\s+table|update|index\s+on)\s+([`""]?([A-Za-z0-9_\.]+)[`""]?)";
        var matches = Regex.Matches(clean, pattern, RegexOptions.IgnoreCase);
        return matches.Select(m => m.Groups[2].Value).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // Splits SQL by semicolon safely (ignores semicolons in strings/comments because we strip them first).
    public static string[] SplitSqlStatements(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Array.Empty<string>();

        // Keep original statements but remove comments/strings for splitting decisions.
        var clean = StripCommentsAndQuotedStrings(sql);

        var statements = new List<string>();
        var sb = new StringBuilder();
        int idx = 0;
        for (int i = 0; i < sql.Length; i++)
        {
            // if clean[idx] is semicolon then we split — we need to map indices between original and clean.
            // Simpler approach: iterate characters and split on semicolons that are not within quotes or comments using a small state machine.
            // We'll use a safer state machine below instead of mapping to clean string.
            break;
        }

        // Simpler robust implementation: state machine that walks original SQL.
        statements.Clear();
        var cur = new StringBuilder();
        bool inSingle = false, inDouble = false, inLineComment = false, inBlockComment = false;
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                cur.Append(c);
                continue;
            }
            if (inBlockComment)
            {
                if (c == '*' && next == '/')
                {
                    inBlockComment = false;
                    cur.Append(c); // append '*' and '/'
                    cur.Append(next);
                    i++;
                    continue;
                }
                cur.Append(c);
                continue;
            }

            if (!inSingle && !inDouble)
            {
                if (c == '-' && next == '-')
                {
                    inLineComment = true;
                    cur.Append(c);
                    cur.Append(next);
                    i++;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    inBlockComment = true;
                    cur.Append(c);
                    cur.Append(next);
                    i++;
                    continue;
                }
            }

            if (!inDouble && c == '\'')
            {
                inSingle = !inSingle;
                cur.Append(c);
                continue;
            }
            if (!inSingle && c == '"')
            {
                inDouble = !inDouble;
                cur.Append(c);
                continue;
            }

            if (!inSingle && !inDouble && !inBlockComment && !inLineComment && c == ';')
            {
                var stmt = cur.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(stmt))
                    statements.Add(stmt);
                cur.Clear();
                continue;
            }

            cur.Append(c);
        }

        var last = cur.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(last)) statements.Add(last);

        return statements.ToArray();
    }

    public static string SanitizeName(string name)
    {
        return Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9_\-]", "-").Trim('-');
    }

    public static string ComputeSha256Hex(string content)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? ""));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }





   public static  string FindUpwards(string fileName)
    {
        string dir = Environment.CurrentDirectory;

        while (dir != null)
        {
            string candidate = Path.Combine(dir, fileName);
            //Console.WriteLine($"Searching for {fileName} in {candidate}...");
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName!;
        }

        Console.WriteLine($"Warning: {fileName} not found in any parent directory.");
        return fileName;
        //throw new FileNotFoundException($"{fileName} not found in directory tree.");
    }
}


using System.IO.Compression;
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


    // Splits SQL by semicolon safely (ignores semicolons in strings/comments because we strip them first).
    public static string[] SplitSqlStatements(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Array.Empty<string>();

        // Keep original statements but remove comments/strings for splitting decisions.
        var clean = StripCommentsAndQuotedStrings(sql);

        var statements = new List<string>();

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



    public static long ComputeInt64Hash(string input)
    {
        using var sha = SHA1.Create();
        var b = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        // Use first 8 bytes as signed 64-bit
        long val = 0;
        for (int i = 0; i < 8; i++) val = (val << 8) | b[i];
        return Math.Abs(val);
    }


    public static byte[] CompressString(string downSql)
    {
        var input = Encoding.UTF8.GetBytes(downSql);

        using var ms = new MemoryStream();
        using (var brotli = new BrotliStream(ms, CompressionLevel.SmallestSize))
        {
            brotli.Write(input, 0, input.Length);
        }

        return ms.ToArray();
    }


    public static string DecompressString(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        brotli.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }






    public static  string FindUpwards(string fileName)
    {
        string dir = Environment.CurrentDirectory;

        while (dir != null)
        {
            string candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;

            dir = Directory.GetParent(dir)?.FullName!;
        }
        return fileName;
    }



    public static void SendWarningMessage(string message)
    {
        Console.ForegroundColor = ConsoleColor.Black;
        Console.BackgroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
        Console.WriteLine("\n");
    }


    public static void SendErrorMessage(string message)
    {
        Console.ForegroundColor = ConsoleColor.Black;
        Console.BackgroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
        Console.WriteLine("\n");
    }


    public static void SendInfoMessage(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.BackgroundColor = ConsoleColor.Black;
        Console.WriteLine(message);
        Console.ResetColor();
        Console.WriteLine("\n");

    }

    public static void SendTitleMessage(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.BackgroundColor = ConsoleColor.Black;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public static void SendHelpMessage(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.BackgroundColor = ConsoleColor.Black;
        Console.WriteLine(message);
        Console.ResetColor();
    }


    public static void PrintHelp()
    {
        SendTitleMessage("\nMigration Tool");
        SendTitleMessage("--------------");

        SendHelpMessage("Commands:\n");

        SendHelpMessage("  migrator help");
        SendHelpMessage("      Shows this help screen.\n");

        SendHelpMessage("  migrator create \"Description\" [--author \"Full Name\"] [--branch \"branch-name\"]");
        SendHelpMessage("      Creates a new migration template file.");
        SendHelpMessage("      If --author is not provided, the migration will NOT be applied");
        SendHelpMessage("      until you manually add an Author name in the generated file.\n");

        SendHelpMessage("  migrator status");
        SendHelpMessage("      Shows the current migration status (applied/pending).\n");

        SendHelpMessage("  migrator apply");
        SendHelpMessage("      Applies all pending migrations.\n");

        SendHelpMessage("  migrator rollback");
        SendHelpMessage("      Rolls back the last applied migration.\n");

        SendHelpMessage("  migrator redo \"<version>\"");
        SendHelpMessage("      Rolls back and re-applies the migration with the given version.\n");
    }

}


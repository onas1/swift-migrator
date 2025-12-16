using System.Text.RegularExpressions;

namespace migrator.Engine;

public static class MigrationParser
{
    public static (string Up, string Down) ExtractUpDown(string sql)
    {
        var upStart = sql.IndexOf("-- UP", StringComparison.OrdinalIgnoreCase);
        if (upStart < 0)
            throw new Exception("Migration missing '-- UP' section");

        var downStart = sql.IndexOf("-- DOWN", StringComparison.OrdinalIgnoreCase);

        string up, down;

        if (downStart > upStart)
        {
            up = sql.Substring(upStart + 5, downStart - (upStart + 5));
            down = sql[(downStart + 7)..];
        }
        else
        {
            up = sql[(upStart + 5)..];
            down = "";
        }

        up = StripBeginCommit(up);
        down = StripBeginCommit(down);

        return (up.Trim(), down.Trim());
    }

    private static string StripBeginCommit(string sql)
    {
        sql = Regex.Replace(sql, @"\bBEGIN\b", "", RegexOptions.IgnoreCase);
        sql = Regex.Replace(sql, @"\bCOMMIT\b", "", RegexOptions.IgnoreCase);
        return sql.Trim();
    }
}


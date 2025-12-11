
//using System;
//using System.IO;
//using System.Linq;

namespace migrator.Engine;


//public record Migration(string Id, string Timestamp, string Name, string Filename, string Sql)
//{
//    public string Version => $"{Timestamp}_{Id}";
//    public static Migration LoadFromFile(string path)
//    {
//        var filename = Path.GetFileName(path);
//        var parts = filename.Split('_', 3);
//        if (parts.Length < 3) throw new FormatException($"Invalid migration filename: {filename}");
//        var timestamp = parts[0];
//        var id = parts[1];
//        var nameWithExt = parts[2];
//        var name = Path.GetFileNameWithoutExtension(nameWithExt);
//        var sql = File.ReadAllText(path);
//        return new Migration(id, timestamp, name, path, sql);
//    }
//}






//public record Migration(string Id, string Timestamp, string Name, string Filename, string Sql, string Checksum, MigrationHeader Header)
//{
//    public string Version => $"{Timestamp}_{Id}";

//    public static Migration LoadFromFile(string path)
//    {
//        var filename = Path.GetFileName(path);
//        var parts = filename.Split('_', 3);
//        if (parts.Length < 3) throw new FormatException($"Invalid migration filename: {filename}");
//        var timestamp = parts[0];
//        var id = parts[1];
//        var nameWithExt = parts[2];
//        var name = Path.GetFileNameWithoutExtension(nameWithExt);
//        var sql = File.ReadAllText(path);
//        var checksum = Utils.ComputeSha256Hex(sql);

//        // Parse header metadata from comments at top of file
//        var header = MigrationHeader.ParseFromSql(sql);

//        return new Migration(id, timestamp, name, path, sql, checksum, header);
//    }
//}

//public record MigrationHeader(string Author, string Branch, string CommitId, string Signature)
//{
//    public static MigrationHeader ParseFromSql(string sql)
//    {
//        // Accept header lines like:
//        // -- Author: Bob
//        // -- Branch: feature/x
//        // -- Commit: abc123
//        // -- Signature: BASE64_SIG
//        string author = null, branch = null, commit = null, signature = null;
//        using var reader = new StringReader(sql);
//        string line;
//        while ((line = reader.ReadLine()) != null)
//        {
//            var trimmed = line.Trim();
//            if (!trimmed.StartsWith("--")) break; // stop at first non-comment line
//            var content = trimmed.Substring(2).Trim();
//            var idx = content.IndexOf(':');
//            if (idx <= 0) continue;
//            var key = content.Substring(0, idx).Trim();
//            var val = content.Substring(idx + 1).Trim();
//            if (string.Equals(key, "Author", StringComparison.OrdinalIgnoreCase)) author = val;
//            if (string.Equals(key, "Branch", StringComparison.OrdinalIgnoreCase)) branch = val;
//            if (string.Equals(key, "Commit", StringComparison.OrdinalIgnoreCase)) commit = val;
//            if (string.Equals(key, "Signature", StringComparison.OrdinalIgnoreCase)) signature = val;
//        }

//        return new MigrationHeader(author, branch, commit, signature);
//    }
//}












public record Migration(string Id,string Timestamp, string Name, string Filename, string Sql, string Checksum,  MigrationHeader Header, string UpSql,  string DownSql, bool IsReversible)
{
    public string Version => $"{Timestamp}_{Id}";

    public static Migration LoadFromFile(string path)
    {
        var filename = Path.GetFileName(path);
        var parts = filename.Split('_', 3);
        if (parts.Length < 3)
            throw new FormatException($"Invalid migration filename: {filename}");

        var timestamp = parts[0];
        var id = parts[1];
        var nameWithExt = parts[2];
        var name = Path.GetFileNameWithoutExtension(nameWithExt);

        var sql = File.ReadAllText(path);

        // Extract UP/DOWN
        var (up, down) = MigrationParser.ExtractUpDown(sql);
        var reversible = !string.IsNullOrWhiteSpace(down);

        // Compute checksum of Up and Down only
        var checksum = Utils.ComputeSha256Hex(up + "\n" + down);

        var header = MigrationHeader.ParseFromSql(sql);

        // Signature verification later (MigrationEngine step)

        return new Migration(id, timestamp, name, path, sql, checksum, header, up, down, reversible);
    }
}



public record MigrationHeader(string Author, string Branch, string CommitId, string Signature)
{
    public static MigrationHeader ParseFromSql(string sql)
    {
        string author = null, branch = null, commit = null, signature = null;

        using var reader = new StringReader(sql);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("--")) break;

            var content = trimmed.Substring(2).Trim();
            var idx = content.IndexOf(':');
            if (idx <= 0) continue;

            var key = content.Substring(0, idx).Trim();
            var val = content.Substring(idx + 1).Trim();

            if (key.Equals("Author", StringComparison.OrdinalIgnoreCase)) author = val;
            else if (key.Equals("Branch", StringComparison.OrdinalIgnoreCase)) branch = val;
            else if (key.Equals("Commit", StringComparison.OrdinalIgnoreCase)) commit = val;
            else if (key.Equals("Signature", StringComparison.OrdinalIgnoreCase)) signature = val;
        }

        return new MigrationHeader(author, branch, commit, signature);
    }

    public string BuildSigningPayload(string checksum)
    {
        return
            $"Author:{Author}\n" +
            $"Branch:{Branch}\n" +
            $"Commit:{CommitId}\n" +
            $"Checksum:{checksum}";
    }
}

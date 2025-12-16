

namespace migrator.Engine;

public class MigrationTemplateEngine
{
    private readonly string _author;
    private readonly string _branch;
    private readonly string _migrationsPath;

    public MigrationTemplateEngine( string author, string branch, string migrationsPath)
    {
        _author = author;
        _branch = branch;
        _migrationsPath = migrationsPath;
    }

    public string CreateMigrationFile(string description)
    {
        var ts = Utils.TimestampNowUtc();
        var id = Utils.ShortId();
        var name = Utils.SanitizeName(description).Replace("--", "");
        var filename = $"{ts}_{id}_{name}.sql";
        var full = Path.Combine(_migrationsPath, filename);

        var template = BuildTemplate(ts, id, description);

        File.WriteAllText(full, template);
        return filename;
    }


    private string BuildTemplate(string ts, string id, string description)
    {
        return
            $"-- Migration: {description}\n" +
            $"-- Version: {ts}_{id}\n" +
            $"-- Author: {_author}\n" +
            $"-- Branch: {_branch}\n\n" +

            "-- UP\n" +
            "-- Write your SQL for applying the migration here.\n\n" +

            "-- DOWN\n" +
            "-- Write your SQL for reverting the migration here.\n";
    }

}


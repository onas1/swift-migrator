using Microsoft.Data.SqlClient;
using migrator.Config;
using migrator.Engine;
using Npgsql;
using System.Data.Common;

namespace migrator
{
   internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: migrator <command> [args]\nCommands: create <description> | status | apply");
                return 1;
            }

            var command = args[0].ToLowerInvariant();

            //var migrationsPath = Path.Combine(Directory.GetCurrentDirectory(), "migrations");
            var migrationsPath = Path.Combine(Environment.CurrentDirectory, "migrations");

            Directory.CreateDirectory(migrationsPath);

            // === CONFIG ===
            // Provide your ADO.NET provider invariant (e.g. "Npgsql" for Postgres or "System.Data.SqlClient")
            // and the connection string. For local testing, you can point to an existing DB.
            //var providerInvariant = Environment.GetEnvironmentVariable("MIGRATOR_PROVIDER") ?? "Npgsql";
            //var connectionString = Environment.GetEnvironmentVariable("MIGRATOR_CONN") ??
            //    "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=migratordb";



            var config = ConfigLoader.Load(args);

            DbProviderFactories.RegisterFactory("SqlClient", SqlClientFactory.Instance );
            DbProviderFactories.RegisterFactory("Npgsql",NpgsqlFactory.Instance );


            var engine = new MigrationEngine(migrationsPath, config.Provider, config.ConnectionString);

            switch (command)
            {
                case "create":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: migrator create \"Add users table\"");
                        return 1;
                    }
                    var desc = string.Join(' ', args, 1, args.Length - 1);
                    var created = engine.CreateMigrationFile(desc);
                    Console.WriteLine($"Created migration: {created}");
                    return 0;

                case "status":
                    var status = await engine.GetStatusAsync();
                    Console.WriteLine(status);
                    return 0;

                case "apply":
                    await engine.ApplyMigrationsAsync();
                    return 0;
                case "rollback":
                    await engine.RollbackLastAsync();
                    return 0;
                case "redo":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: migrator redo \"20251211001611_c4128f\"");
                        return 1;
                    }
                    var version = args[1];
                    await engine.RedoAsync(version);
                    return 0;
                case "status":
                    await engine.GetStatusAsync();
                    return 0;
                default:
                    Console.WriteLine("Unknown command");
                    return 1;
            }
        }
    }
}










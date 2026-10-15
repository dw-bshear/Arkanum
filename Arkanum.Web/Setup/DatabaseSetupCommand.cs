using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Setup;

// Never include exception details, connection strings, identifiers or key material in diagnostics.
public sealed class SetupFailure(string category) : Exception
{
    public string Category { get; } = category;
}

// Intentionally not a record: the generated record ToString would disclose credentials.
public sealed class DatabaseSetupOptions
{
    public bool Provision { get; init; }
    public string RuntimeConnection { get; init; } = "";
    public string? SetupConnection { get; init; }
    public string? RuntimeLogin { get; init; }
    public string? RuntimePassword { get; init; }
    public string? MasterKeyFile { get; init; }
    public string Database { get; init; } = "";
}

public interface IDatabaseSetupBackend
{
    Task ExecuteAsync(DatabaseSetupOptions options, CancellationToken cancellationToken);
}

public static class DatabaseSetupCommand
{
    public static bool IsRequested(string[] args) => args.Any(a => a.Equals("--database-setup", StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(string[] args, Func<string, string?> environment, TextWriter output, IDatabaseSetupBackend? backend = null)
    {
        try
        {
            var options = ReadOptions(args, environment);
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await (backend ?? new SqlDatabaseSetupBackend()).ExecuteAsync(options, deadline.Token);
            await output.WriteLineAsync("Database setup completed.");
            return 0;
        }
        catch (Exception ex)
        {
            var category = ex switch
            {
                SetupFailure failure when failure.Category is "configuration" or "database" or "schema" or "permission" or "recovery-key" or "timeout" => failure.Category,
                SqlException sql when sql.Number is 229 or 230 or 262 or 297 or 15151 => "permission",
                SqlException => "database",
                OperationCanceledException => "timeout",
                _ => "operation"
            };
            await output.WriteLineAsync($"Database setup failed: {category}.");
            return category switch { "configuration" => 2, "database" => 3, "schema" => 4, "permission" => 5, "recovery-key" => 6, _ => 7 };
        }
    }

    private static DatabaseSetupOptions ReadOptions(string[] args, Func<string, string?> environment)
    {
        if (args.Length != 2 || args[0] != "--database-setup" || args[1] is not ("provision" or "preflight"))
            throw new SetupFailure("configuration");
        var provision = args[1] == "provision";
        string Required(string name) => !string.IsNullOrWhiteSpace(environment(name)) ? environment(name)! : throw new SetupFailure("configuration");
        try
        {
            var runtime = new SqlConnectionStringBuilder(Required("ARKANUM_RUNTIME_CONNECTION"));
            SetupSql.QuoteIdentifier(runtime.InitialCatalog);
            if (string.IsNullOrWhiteSpace(runtime.DataSource) || runtime.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase)
                || runtime.InitialCatalog.Equals("model", StringComparison.OrdinalIgnoreCase) || runtime.InitialCatalog.Equals("msdb", StringComparison.OrdinalIgnoreCase)
                || runtime.InitialCatalog.Equals("tempdb", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(runtime.AttachDBFilename))
                throw new SetupFailure("configuration");
            string? setup = null, login = null, password = null;
            if (provision)
            {
                setup = Required("ARKANUM_SETUP_CONNECTION");
                var admin = new SqlConnectionStringBuilder(setup);
                login = Required("ARKANUM_RUNTIME_LOGIN");
                password = Required("ARKANUM_RUNTIME_PASSWORD");
                SetupSql.QuoteIdentifier(login);
                if (password.Length > 128 || password.Contains('\0') || runtime.IntegratedSecurity || runtime.UserID != login || runtime.Password != password
                    || !admin.DataSource.Equals(runtime.DataSource, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(admin.AttachDBFilename))
                    throw new SetupFailure("configuration");
            }
            return new DatabaseSetupOptions
            {
                Provision = provision, RuntimeConnection = runtime.ConnectionString, SetupConnection = setup,
                RuntimeLogin = login, RuntimePassword = password, Database = runtime.InitialCatalog,
                MasterKeyFile = environment("ARKANUM_MASTER_KEY_FILE")
            };
        }
        catch (ArgumentException) { throw new SetupFailure("configuration"); }
    }

    public static async Task ValidateSentinelAsync(ArkanumDbContext db, string key, CancellationToken cancellationToken = default)
    {
        if (!SiteRecoveryKeyService.TryValidateRecoveryKeyFormat(key, out _)
            || !await db.RecoveryStates.AsNoTracking().AnyAsync(s => s.Id == SiteRecoveryKeyService.SingletonStateId, cancellationToken))
            throw new SetupFailure("recovery-key");
        var result = await new SiteRecoveryKeyService(new StaticMasterKeyProvider(key)).ValidateRecoverySentinelAsync(db, cancellationToken);
        if (!result.IsValid) throw new SetupFailure("recovery-key");
    }
}

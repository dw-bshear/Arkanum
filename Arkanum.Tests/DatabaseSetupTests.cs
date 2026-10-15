using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Arkanum.Web.Setup;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class DatabaseSetupTests
{
    private static Dictionary<string, string?> EnvironmentFor(string mode = "preflight") => new()
    {
        ["ARKANUM_RUNTIME_CONNECTION"] = "Server=localhost;Database=Arkanum;User ID=runtime;Password=unit-test-password;Encrypt=True",
        ["ARKANUM_SETUP_CONNECTION"] = mode == "provision" ? "Server=localhost;Database=master;Integrated Security=True" : null,
        ["ARKANUM_RUNTIME_LOGIN"] = "runtime",
        ["ARKANUM_RUNTIME_PASSWORD"] = "unit-test-password"
    };

    [Theory]
    [InlineData("--database-setup")]
    [InlineData("--database-setup", "unknown")]
    [InlineData("--database-setup", "preflight", "Password=secret")]
    public async Task Invalid_arguments_fail_without_exposing_input(params string[] args)
    {
        var output = new StringWriter();
        var backend = new RecordingBackend();
        Assert.Equal(2, await DatabaseSetupCommand.RunAsync(args, _ => null, output, backend));
        Assert.Equal("Database setup failed: configuration." + System.Environment.NewLine, output.ToString());
        Assert.False(backend.Called);
    }

    [Theory]
    [InlineData("ARKANUM_RUNTIME_CONNECTION")]
    [InlineData("ARKANUM_SETUP_CONNECTION")]
    [InlineData("ARKANUM_RUNTIME_LOGIN")]
    [InlineData("ARKANUM_RUNTIME_PASSWORD")]
    public async Task Provision_requires_environment_before_connecting(string missing)
    {
        var env = EnvironmentFor("provision");
        env.Remove(missing);
        var backend = new RecordingBackend();
        Assert.Equal(2, await DatabaseSetupCommand.RunAsync(["--database-setup", "provision"], key => env.GetValueOrDefault(key), new StringWriter(), backend));
        Assert.False(backend.Called);
    }

    [Fact]
    public async Task Preflight_needs_only_runtime_connection_and_never_provisions()
    {
        var env = EnvironmentFor();
        env.Remove("ARKANUM_RUNTIME_LOGIN");
        env.Remove("ARKANUM_RUNTIME_PASSWORD");
        var backend = new RecordingBackend();
        Assert.Equal(0, await DatabaseSetupCommand.RunAsync(["--database-setup", "preflight"], key => env.GetValueOrDefault(key), new StringWriter(), backend));
        Assert.True(backend.Called);
        Assert.False(backend.Provision);
    }

    [Theory]
    [InlineData("Server=localhost;Database=master;User ID=runtime;Password=unit-test-password")]
    [InlineData("Server=localhost;Database=Arkanum;User ID=other;Password=unit-test-password")]
    [InlineData("Server=localhost;Database=Arkanum;User ID=runtime;Password=wrong")]
    [InlineData("Server=localhost;Database=Arkanum;Integrated Security=True")]
    public async Task Provision_rejects_unsafe_or_mismatched_runtime_connection(string connection)
    {
        var env = EnvironmentFor("provision");
        env["ARKANUM_RUNTIME_CONNECTION"] = connection;
        var backend = new RecordingBackend();
        Assert.Equal(2, await DatabaseSetupCommand.RunAsync(["--database-setup", "provision"], key => env.GetValueOrDefault(key), new StringWriter(), backend));
        Assert.False(backend.Called);
    }

    [Fact]
    public async Task Backend_errors_are_categorized_without_exception_or_credentials()
    {
        var env = EnvironmentFor();
        var output = new StringWriter();
        var backend = new RecordingBackend { Failure = new InvalidOperationException("unit-test-password Server=secret") };
        Assert.NotEqual(0, await DatabaseSetupCommand.RunAsync(["--database-setup", "preflight"], key => env.GetValueOrDefault(key), output, backend));
        Assert.Equal("Database setup failed: operation." + System.Environment.NewLine, output.ToString());
    }

    [Fact]
    public void Identifiers_are_quoted_and_invalid_names_rejected()
    {
        Assert.Equal("[a]]b';--]", SetupSql.QuoteIdentifier("a]b';--"));
        Assert.Throws<SetupFailure>(() => SetupSql.QuoteIdentifier("bad\nGO"));
        Assert.Throws<SetupFailure>(() => SetupSql.QuoteIdentifier(new string('a', 129)));
        Assert.Contains("@database", SetupSql.CreateDatabase);
        Assert.Contains("QUOTENAME(@database)", SetupSql.CreateDatabase);
        Assert.Contains("PWDCOMPARE(@password", SetupSql.CreateLogin);
        Assert.Contains("QUOTENAME(@password", SetupSql.CreateLogin);
        Assert.DoesNotContain("ALTER LOGIN", SetupSql.CreateLogin);
        Assert.Contains("THROW", SetupSql.CreateLogin);
    }

    [Fact]
    public void Go_parser_substitutes_database_as_identifier_and_preserves_strings_and_comments()
    {
        var batches = SetupSql.ParseBatches(":setvar DatabaseName \"Arkanum\"\nUSE [$(DatabaseName)];\nGO -- separator\nSELECT 'line\nGO\nline';\n/*\nGO\n*/\nGO\nSELECT 2", "a]b';--");
        Assert.Equal(3, batches.Count);
        Assert.Equal("USE [a]]b';--];", batches[0]);
        Assert.Contains("'line\nGO\nline'", batches[1]);
        Assert.Contains("/*\nGO\n*/", batches[1]);
    }

    [Theory]
    [InlineData(":r secrets.sql")]
    [InlineData(":setvar Other \"value\"")]
    [InlineData("!! whoami")]
    [InlineData("SELECT '$(Other)'")]
    [InlineData("SELECT 1\nGO 2")]
    [InlineData("SELECT 1\nGO invalid")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("/* unterminated")]
    public void Unsupported_sqlcmd_or_malformed_batches_fail_closed(string sql) =>
        Assert.Throws<SetupFailure>(() => SetupSql.ParseBatches(sql, "Arkanum"));

    [Fact]
    public void Schema_contract_has_expected_columns_keys_indexes_and_object_permissions()
    {
        var schema = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "setup", "arkanum-schema.sql"));
        var contract = SetupSql.ReadSchemaContract(schema);
        Assert.Equal(12, contract.Tables.Count);
        Assert.Contains(contract.Columns, c => c.Table == "Users" && c.Name == "LockoutEndAt" && c.Nullable && c.Type == "datetimeoffset");
        Assert.Contains(contract.Columns, c => c.Table == "VaultItems" && c.Name == "ResetNotificationDate" && c.Type == "date");
        Assert.Contains(contract.Columns, c => c.Table == "AuditEvents" && c.Name == "IntegrityHash" && c.Length == -1);
        Assert.Contains(contract.Indexes, i => i.Name == "PK_VaultMembers" && i.Columns.SequenceEqual(new[] { "VaultId", "UserKey" }) && i.Primary);
        Assert.Contains(contract.Indexes, i => i.Name == "IX_Users_Username" && i.Unique);
        var permissions = SetupSql.RuntimePermissions("runtime]x", contract);
        Assert.Contains("DENY UPDATE, DELETE ON OBJECT::[dbo].[AuditEvents] TO [runtime]]x]", permissions);
        Assert.Contains("GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::[dbo].[Users]", permissions);
        Assert.DoesNotContain("ON SCHEMA", permissions);
        Assert.DoesNotContain("db_owner", permissions);
        Assert.Contains("HAS_PERMS_BY_NAME", SetupSql.ObjectPermissionsQuery);
        Assert.Contains("'OBJECT'", SetupSql.ObjectPermissionsQuery);
    }

    [Fact]
    public async Task Sentinel_check_requires_existing_matching_sentinel_and_never_creates_it()
    {
        await using var db = new ArkanumDbContext(new DbContextOptionsBuilder<ArkanumDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        const string key = "ark-recovery-v1.test-material-long-enough-for-format-validation";
        await Assert.ThrowsAsync<SetupFailure>(() => DatabaseSetupCommand.ValidateSentinelAsync(db, key));
        Assert.Empty(db.RecoveryStates);
        await new SiteRecoveryKeyService(new StaticMasterKeyProvider(key)).EnsureRecoverySentinelAsync(db, DateTimeOffset.UtcNow);
        db.ChangeTracker.Clear();
        await DatabaseSetupCommand.ValidateSentinelAsync(db, key);
        await Assert.ThrowsAsync<SetupFailure>(() => DatabaseSetupCommand.ValidateSentinelAsync(db, "ark-recovery-v1.wrong-material-long-enough-for-format-validation"));
        Assert.Single(await db.RecoveryStates.AsNoTracking().ToListAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("SELECT '[$(DatabaseName)]'")]
    [InlineData("-- [$(DatabaseName)]")]
    [InlineData("SELECT [$(DatabaseName)]")]
    public void Database_variable_is_only_allowed_in_controlled_use_statement(string sql) =>
        Assert.Throws<SetupFailure>(() => SetupSql.ParseBatches(sql, "a'; DROP TABLE Users;--"));

    [Fact]
    public async Task Invalid_connection_string_is_redacted_before_backend()
    {
        var env = EnvironmentFor();
        env["ARKANUM_RUNTIME_CONNECTION"] = "Password=very-secret;NotAKeyword=yes";
        var backend = new RecordingBackend();
        var output = new StringWriter();
        Assert.Equal(2, await DatabaseSetupCommand.RunAsync(["--database-setup", "preflight"], k => env.GetValueOrDefault(k), output, backend));
        Assert.False(backend.Called);
        Assert.DoesNotContain("very-secret", output.ToString());
    }

    [Fact]
    public async Task Unknown_failure_categories_cannot_leak_secret_text()
    {
        var env = EnvironmentFor();
        var output = new StringWriter();
        var backend = new RecordingBackend { Failure = new SetupFailure("Password=very-secret") };
        Assert.Equal(7, await DatabaseSetupCommand.RunAsync(["--database-setup", "preflight"], k => env.GetValueOrDefault(k), output, backend));
        Assert.Equal("Database setup failed: operation." + System.Environment.NewLine, output.ToString());
    }

    [Theory]
    [InlineData("missing-column")]
    [InlineData("wrong-type")]
    [InlineData("wrong-nullability")]
    [InlineData("wrong-length")]
    [InlineData("wrong-key-order")]
    [InlineData("missing-index")]
    public void Metadata_validation_fails_on_schema_drift(string drift)
    {
        var schema = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "setup", "arkanum-schema.sql"));
        var expected = SetupSql.ReadSchemaContract(schema);
        var columns = expected.Columns.ToList();
        var indexes = expected.Indexes.ToList();
        if (drift == "missing-column") columns.RemoveAt(0);
        if (drift == "wrong-type") columns[0] = columns[0] with { Type = "varchar" };
        if (drift == "wrong-nullability") columns[0] = columns[0] with { Nullable = true };
        if (drift == "wrong-length") columns[0] = columns[0] with { Length = 1 };
        if (drift == "missing-index") indexes.RemoveAt(0);
        if (drift == "wrong-key-order")
        {
            var position = indexes.FindIndex(i => i.Name == "PK_VaultMembers");
            indexes[position] = indexes[position] with { Columns = ["UserKey", "VaultId"] };
        }
        Assert.Throws<SetupFailure>(() => SqlDatabaseSetupBackend.VerifySchema(expected, new(expected.Tables, columns, indexes)));
        SqlDatabaseSetupBackend.VerifySchema(expected, expected);
    }

    [Theory]
    [InlineData("Users", new int[] { 1, 1, 1, 1, 0, 0, 0 }, true)]
    [InlineData("AuditEvents", new int[] { 1, 1, 0, 0, 0, 0, 0 }, true)]
    [InlineData("AuditEvents", new int[] { 1, 1, 1, 0, 0, 0, 0 }, false)]
    [InlineData("AuditEvents", new int[] { 1, 1, 0, 1, 0, 0, 0 }, false)]
    [InlineData("Users", new int[] { 1, 1, 0, 1, 0, 0, 0 }, false)]
    [InlineData("Users", new int[] { 1, 1, 1, 1, 1, 0, 0 }, false)]
    [InlineData("Users", new int[] { 1, 1, 1, 1, 0, 1, 0 }, false)]
    public void Effective_object_permission_validation_respects_audit_deny(string table, int[] permissions, bool valid)
    {
        var actual = permissions.Select(v => (int?)v).ToArray();
        if (valid) SqlDatabaseSetupBackend.VerifyObjectPermissions(table, actual);
        else Assert.Throws<SetupFailure>(() => SqlDatabaseSetupBackend.VerifyObjectPermissions(table, actual));
    }

    [Fact]
    public void Indeterminate_permissions_fail_closed() => Assert.Throws<SetupFailure>(() =>
        SqlDatabaseSetupBackend.VerifyObjectPermissions("Users", [1, 1, 1, 1, 0, null, 0]));

    private sealed class RecordingBackend : IDatabaseSetupBackend
    {
        public bool Called { get; private set; }
        public bool Provision { get; private set; }
        public Exception? Failure { get; init; }
        public Task ExecuteAsync(DatabaseSetupOptions options, CancellationToken cancellationToken)
        {
            Called = true;
            Provision = options.Provision;
            if (Failure is not null) throw Failure;
            return Task.CompletedTask;
        }
    }
}

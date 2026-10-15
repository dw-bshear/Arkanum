using System.Data;
using Arkanum.Web.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Setup;

public sealed class SqlDatabaseSetupBackend : IDatabaseSetupBackend
{
    public async Task ExecuteAsync(DatabaseSetupOptions options, CancellationToken cancellationToken)
    {
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "setup", "arkanum-schema.sql"), cancellationToken);
        var batches = SetupSql.ParseBatches(schema, options.Database);
        var contract = SetupSql.ReadSchemaContract(schema);
        // Validate key-file readability/format before any provisioning mutation.
        string? key = null;
        if (!string.IsNullOrWhiteSpace(options.MasterKeyFile))
        {
            try { key = (await File.ReadAllTextAsync(options.MasterKeyFile, cancellationToken)).Trim(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { throw new SetupFailure("recovery-key"); }
            if (!Services.SiteRecoveryKeyService.TryValidateRecoveryKeyFormat(key, out _)) throw new SetupFailure("recovery-key");
        }
        if (options.Provision)
        {
            await using var admin = Connection(options.SetupConnection!, "master");
            await admin.OpenAsync(cancellationToken);
            // Validate an existing password before creating/modifying the database.
            await ExecuteAsync(admin, SetupSql.CreateLogin, cancellationToken, ("@login", options.RuntimeLogin!), ("@password", options.RuntimePassword!));
            await ExecuteAsync(admin, SetupSql.CreateDatabase, cancellationToken, ("@database", options.Database));
            admin.ChangeDatabase(options.Database);
            foreach (var batch in batches) await ExecuteAsync(admin, batch, cancellationToken);
            await ExecuteAsync(admin, SetupSql.CreateUser, cancellationToken, ("@login", options.RuntimeLogin!));
            await ExecuteAsync(admin, SetupSql.RuntimePermissions(options.RuntimeLogin!, contract), cancellationToken);
        }
        await using var runtime = Connection(options.RuntimeConnection);
        await runtime.OpenAsync(cancellationToken); // Also proves that the supplied password actually authenticates.
        await CheckSchemaAsync(runtime, contract, cancellationToken);
        await CheckPermissionsAsync(runtime, contract, cancellationToken);
        if (key is not null)
        {
            var efOptions = new DbContextOptionsBuilder<ArkanumDbContext>().UseSqlServer(runtime).Options;
            await using var db = new ArkanumDbContext(efOptions);
            await DatabaseSetupCommand.ValidateSentinelAsync(db, key, cancellationToken);
        }
    }

    private static SqlConnection Connection(string connection, string? database = null)
    {
        var builder = new SqlConnectionStringBuilder(connection) { Pooling = false, PersistSecurityInfo = false, ConnectTimeout = 15 };
        if (database is not null) builder.InitialCatalog = database;
        return new SqlConnection(builder.ConnectionString);
    }

    private static SqlCommand Command(SqlConnection connection, string text, params (string Name, string Value)[] parameters)
    {
        var command = new SqlCommand(text, connection) { CommandTimeout = 60 };
        foreach (var (name, value) in parameters) command.Parameters.Add(name, SqlDbType.NVarChar, name == "@password" ? 128 : 256).Value = value;
        return command;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string text, CancellationToken token, params (string Name, string Value)[] parameters)
    {
        using var command = Command(connection, text, parameters);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task CheckSchemaAsync(SqlConnection connection, SchemaContract contract, CancellationToken token)
    {
        const string columnSql = """
            SELECT t.name, c.name, TYPE_NAME(c.system_type_id), c.max_length, c.is_nullable, c.is_computed, c.is_identity
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
            WHERE s.name=N'dbo';
            """;
        var actualColumns = new List<SchemaColumn>();
        using (var command = Command(connection, columnSql))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                var table = reader.GetString(0);
                if (!contract.Tables.Contains(table, StringComparer.Ordinal)) continue;
                var type = reader.GetString(2);
                var bytes = (int)reader.GetInt16(3);
                int? length = type is "nvarchar" or "nchar" ? bytes == -1 ? -1 : bytes / 2 : type is "varchar" or "char" or "binary" or "varbinary" ? bytes : null;
                if (reader.GetBoolean(5) || reader.GetBoolean(6)) throw new SetupFailure("schema");
                actualColumns.Add(new(table, reader.GetString(1), type, length, reader.GetBoolean(4)));
            }
        }
        const string indexSql = """
            SELECT t.name, i.name, i.is_primary_key, i.is_unique, c.name, ic.key_ordinal
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            JOIN sys.indexes i ON i.object_id=t.object_id
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE s.name=N'dbo' AND i.is_disabled=0 AND i.has_filter=0 AND ic.key_ordinal>0
            ORDER BY t.name, i.name, ic.key_ordinal;
            """;
        var indexRows = new List<(string Table, string Name, bool Primary, bool Unique, string Column)>();
        using (var command = Command(connection, indexSql))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
                indexRows.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetString(4)));
        }
        var indexes = indexRows.GroupBy(i => (i.Table, i.Name, i.Primary, i.Unique))
            .Select(g => new SchemaIndex(g.Key.Table, g.Key.Name, g.Select(i => i.Column).ToArray(), g.Key.Primary, g.Key.Unique)).ToArray();
        VerifySchema(contract, new(actualColumns.Select(c => c.Table).Distinct().ToArray(), actualColumns, indexes));
    }

    public static void VerifySchema(SchemaContract expected, SchemaContract actual)
    {
        if (expected.Tables.Any(t => !actual.Tables.Contains(t, StringComparer.Ordinal))
            || expected.Columns.Any(c => !actual.Columns.Contains(c))
            || expected.Indexes.Any(i => !actual.Indexes.Any(a => a.Table == i.Table && a.Name == i.Name && a.Primary == i.Primary && a.Unique == i.Unique && a.Columns.SequenceEqual(i.Columns))))
            throw new SetupFailure("schema");
    }

    public static void VerifyObjectPermissions(string table, IReadOnlyList<int?> permissions)
    {
        if (permissions.Count != 7) throw new SetupFailure("permission");
        for (var i = 0; i < permissions.Count; i++)
        {
            var expected = i < 4 && !(table == "AuditEvents" && i is 2 or 3) ? 1 : 0;
            if (permissions[i] != expected) throw new SetupFailure("permission");
        }
    }

    private static async Task CheckPermissionsAsync(SqlConnection connection, SchemaContract contract, CancellationToken token)
    {
        const string elevatedSql = """
            SELECT CASE WHEN
                IS_SRVROLEMEMBER('sysadmin') = 1 OR IS_SRVROLEMEMBER('securityadmin') = 1 OR IS_SRVROLEMEMBER('dbcreator') = 1
                OR IS_ROLEMEMBER('db_owner') = 1 OR IS_ROLEMEMBER('db_securityadmin') = 1 OR IS_ROLEMEMBER('db_ddladmin') = 1
                OR HAS_PERMS_BY_NAME(NULL, NULL, 'CONTROL SERVER') = 1
                OR HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY LOGIN') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY USER') = 1
                OR HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'ALTER') = 1
                OR HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'CONTROL') = 1
                THEN 1 ELSE 0 END;
            """;
        using (var command = Command(connection, elevatedSql))
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture) != 0) throw new SetupFailure("permission");
        foreach (var table in contract.Tables)
        {
            using var command = Command(connection, SetupSql.ObjectPermissionsQuery, ("@object", "dbo." + SetupSql.QuoteIdentifier(table)));
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new SetupFailure("permission");
            VerifyObjectPermissions(table, Enumerable.Range(0, 7).Select(i => reader.IsDBNull(i) ? (int?)null : reader.GetInt32(i)).ToArray());
        }
        // SQL Server column GRANTs can override object DENY: check effective rights for every audit column too.
        foreach (var column in contract.Columns.Where(c => c.Table == "AuditEvents"))
        {
            using var command = Command(connection, "SELECT HAS_PERMS_BY_NAME('dbo.AuditEvents','OBJECT','UPDATE',@column,'COLUMN');", ("@column", column.Name));
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token), System.Globalization.CultureInfo.InvariantCulture) != 0) throw new SetupFailure("permission");
        }
    }
}

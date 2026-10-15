using System.Text;
using System.Text.RegularExpressions;

namespace Arkanum.Web.Setup;

public sealed record SchemaColumn(string Table, string Name, string Type, int? Length, bool Nullable);
public sealed record SchemaIndex(string Table, string Name, string[] Columns, bool Primary, bool Unique);
public sealed record SchemaContract(IReadOnlyList<string> Tables, IReadOnlyList<SchemaColumn> Columns, IReadOnlyList<SchemaIndex> Indexes);

public static class SetupSql
{
    public const string CreateDatabase = """
        IF DB_ID(@database) IS NULL
        BEGIN
            DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@database) + N';';
            EXEC (@sql);
        END;
        """;

    // Existing credentials are checked, never silently reset. All data enters through parameters.
    public const string CreateLogin = """
        IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = @login AND is_disabled = 0 AND PWDCOMPARE(@password, password_hash) = 1)
                THROW 51001, 'Runtime credential mismatch.', 1;
        END
        ELSE
        BEGIN
            DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login)
                + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;';
            EXEC (@sql);
        END;
        """;

    public const string CreateUser = """
        IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @login AND type <> 'S')
            THROW 51002, 'Runtime principal mismatch.', 1;
        DECLARE @sql nvarchar(max);
        IF USER_ID(@login) IS NULL
            SET @sql = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N' WITH DEFAULT_SCHEMA = [dbo];';
        ELSE
            SET @sql = N'ALTER USER ' + QUOTENAME(@login) + N' WITH LOGIN = ' + QUOTENAME(@login) + N', DEFAULT_SCHEMA = [dbo];';
        EXEC (@sql);
        """;

    public const string ObjectPermissionsQuery = """
        SELECT HAS_PERMS_BY_NAME(@object, 'OBJECT', 'SELECT'),
               HAS_PERMS_BY_NAME(@object, 'OBJECT', 'INSERT'),
               HAS_PERMS_BY_NAME(@object, 'OBJECT', 'UPDATE'),
               HAS_PERMS_BY_NAME(@object, 'OBJECT', 'DELETE'),
               HAS_PERMS_BY_NAME(@object, 'OBJECT', 'ALTER'),
               HAS_PERMS_BY_NAME(@object, 'OBJECT', 'CONTROL'),
               HAS_PERMS_BY_NAME(@object, 'OBJECT', 'TAKE OWNERSHIP');
        """;

    public static string QuoteIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) throw new SetupFailure("configuration");
        return "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    // Only the controlled DatabaseName directive is supported; never run arbitrary sqlcmd commands.
    // Lex before substituting the identifier, so database names cannot introduce batch separators.
    public static IReadOnlyList<string> ParseBatches(string script, string database)
    {
        var quotedDatabase = QuoteIdentifier(database);
        var result = new List<string>();
        var batch = new StringBuilder();
        var state = new SqlLexState();
        void Flush()
        {
            var text = batch.ToString().Trim();
            batch.Clear();
            if (text.Length == 0) return;
            if (text.Contains("$(", StringComparison.Ordinal)) throw new SetupFailure("schema");
            result.Add(text);
        }
        foreach (var line in script.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (state.Neutral)
            {
                if (trimmed.StartsWith(':') || trimmed.StartsWith("!!", StringComparison.Ordinal))
                {
                    if (trimmed != ":setvar DatabaseName \"Arkanum\"") throw new SetupFailure("schema");
                    continue;
                }
                if (Regex.IsMatch(trimmed, @"^GO\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    if (!Regex.IsMatch(trimmed, @"^GO\s*(--.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) throw new SetupFailure("schema");
                    Flush();
                    continue;
                }
            }
            batch.AppendLine(state.Neutral && trimmed == "USE [$(DatabaseName)];" ? "USE " + quotedDatabase + ";" : line);
            state.Scan(line);
        }
        if (!state.Neutral) throw new SetupFailure("schema");
        Flush();
        return result;
    }

    private sealed class SqlLexState
    {
        private char quote;
        private int comments;
        public bool Neutral => quote == '\0' && comments == 0;
        public void Scan(string line)
        {
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                var next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (comments > 0)
                {
                    if (c == '/' && next == '*') { comments++; i++; }
                    else if (c == '*' && next == '/') { comments--; i++; }
                }
                else if (quote != '\0')
                {
                    if (c != quote) continue;
                    if (next == quote) i++;
                    else quote = '\0';
                }
                else if (c == '-' && next == '-') break;
                else if (c == '/' && next == '*') { comments++; i++; }
                else if (c is '\'' or '"') quote = c;
                else if (c == '[') quote = ']';
            }
        }
    }

    // Derive metadata expectations from the exact versioned schema shipped with the application.
    // This deliberately accepts only that controlled CREATE TABLE/INDEX grammar, not general SQL.
    public static SchemaContract ReadSchemaContract(string schema)
    {
        var tables = new List<string>();
        var columns = new List<SchemaColumn>();
        var indexes = new List<SchemaIndex>();
        foreach (Match table in Regex.Matches(schema, @"CREATE TABLE \[(\w+)\] \((.*?)\n\);", RegexOptions.Singleline))
        {
            var name = table.Groups[1].Value;
            tables.Add(name);
            foreach (var line in table.Groups[2].Value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                var column = Regex.Match(line, @"^\[(\w+)\] (\w+)(?:\((max|\d+)\))? (NOT NULL|NULL)\b");
                if (column.Success)
                {
                    int? length = column.Groups[3].Success ? column.Groups[3].Value == "max" ? -1 : int.Parse(column.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
                    columns.Add(new(name, column.Groups[1].Value, column.Groups[2].Value, length, column.Groups[4].Value == "NULL"));
                    var key = Regex.Match(line, @"CONSTRAINT \[(\w+)\] PRIMARY KEY");
                    if (key.Success) indexes.Add(new(name, key.Groups[1].Value, [column.Groups[1].Value], true, true));
                }
                else
                {
                    var key = Regex.Match(line, @"^CONSTRAINT \[(\w+)\] PRIMARY KEY \((.*?)\)");
                    if (!key.Success) throw new SetupFailure("schema");
                    indexes.Add(new(name, key.Groups[1].Value, Names(key.Groups[2].Value), true, true));
                }
            }
        }
        foreach (Match index in Regex.Matches(schema, @"CREATE (UNIQUE )?INDEX \[(\w+)\] ON \[(\w+)\] \((.*?)\)"))
            indexes.Add(new(index.Groups[3].Value, index.Groups[2].Value, Names(index.Groups[4].Value), false, index.Groups[1].Success));
        if (tables.Count != 12 || columns.Count == 0 || indexes.Count != 16 || tables.Distinct(StringComparer.Ordinal).Count() != tables.Count)
            throw new SetupFailure("schema");
        return new(tables, columns, indexes);
    }

    private static string[] Names(string text) => Regex.Matches(text, @"\[(\w+)\]").Select(m => m.Groups[1].Value).ToArray();

    public static string RuntimePermissions(string login, SchemaContract contract)
    {
        var principal = QuoteIdentifier(login);
        var sql = new StringBuilder();
        foreach (var table in contract.Tables)
        {
            var permissions = table == "AuditEvents" ? "SELECT, INSERT" : "SELECT, INSERT, UPDATE, DELETE";
            sql.AppendLine($"GRANT {permissions} ON OBJECT::[dbo].{QuoteIdentifier(table)} TO {principal};");
        }
        sql.AppendLine($"DENY UPDATE, DELETE ON OBJECT::[dbo].[AuditEvents] TO {principal};");
        return sql.ToString();
    }
}

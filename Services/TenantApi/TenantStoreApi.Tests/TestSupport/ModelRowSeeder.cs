using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace TenantStoreApi.Tests.TestSupport;

/// <summary>
/// One owner's world for <see cref="ModelRowSeeder"/>: <see cref="Values"/> fills every column of that
/// name in every table (the owner's id in each <c>OwnerUserId</c>, the company in each
/// <c>TenantId</c>...), and <see cref="Keys"/> fixes the primary key of a table's row (the
/// subscription row's id is the subscription id the other rows point at).
/// </summary>
public sealed record SeedUnit(IReadOnlyDictionary<string, object> Values, IReadOnlyDictionary<string, object> Keys);

/// <summary>What <see cref="ModelRowSeeder.Seed"/> put in: the tables, and each one's primary key per unit.</summary>
public sealed record SeededRows(
    IReadOnlyList<string> Tables, IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Keys);

/// <summary>
/// Puts one row per <see cref="SeedUnit"/> into EVERY table of a context's EF model, read straight
/// from the model -- so a table added later is seeded, and must then be erased (or be declared
/// global), without anyone touching a test. Parents are seeded before children, and a foreign key
/// points at the parent row of the same unit. Foreign keys are switched off while seeding: the rows
/// only have to exist. SQLite only (the test database).
/// </summary>
public static class ModelRowSeeder
{
    /// <summary>Seeds every table but <paramref name="skip"/>; returns each table's seeded primary key per unit.</summary>
    public static SeededRows Seed(DbContext db, IReadOnlyList<SeedUnit> units, IReadOnlySet<string> skip)
    {
        var tables = InDependencyOrder(db.Model.GetRelationalModel().Tables.Where(t => !skip.Contains(t.Name)).ToList());
        var sql = db.GetService<ISqlGenerationHelper>();
        var connection = db.Database.GetDbConnection();
        db.Database.OpenConnection();
        var seeded = new Dictionary<(string Table, int Unit), Dictionary<string, object?>>();
        var counter = 0;
        try
        {
            Execute(connection, "PRAGMA foreign_keys = OFF");
            foreach (var table in tables)
            {
                for (var unit = 0; unit < units.Count; unit++)
                {
                    using var command = connection.CreateCommand();
                    var columns = new List<string>();
                    var values = new Dictionary<string, object?>();
                    foreach (var column in table.Columns)
                    {
                        var property = column.PropertyMappings.First().Property;
                        if (IsStoreGenerated(property)) continue;
                        counter++;
                        var value = ValueFor(table, column, property, units[unit], unit, seeded, counter);
                        values[column.Name] = value;
                        command.Parameters.Add(property.GetRelationalTypeMapping().CreateParameter(command, $"@p{columns.Count}", value, column.IsNullable));
                        columns.Add(column.Name);
                    }
                    command.CommandText =
                        $"INSERT INTO {sql.DelimitIdentifier(table.Name, table.Schema)} ({string.Join(", ", columns.Select(c => sql.DelimitIdentifier(c)))}) " +
                        $"VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})";
                    try
                    {
                        command.ExecuteNonQuery();
                    }
                    catch (SqliteException ex)
                    {
                        throw new InvalidOperationException($"Couldn't seed table {table.Name}: {ex.Message}", ex);
                    }
                    foreach (var key in table.PrimaryKey?.Columns ?? [])
                    {
                        if (!values.ContainsKey(key.Name)) values[key.Name] = Scalar(connection, "SELECT last_insert_rowid()");
                    }
                    seeded[(table.Name, unit)] = values;
                }
            }
        }
        finally
        {
            Execute(connection, "PRAGMA foreign_keys = ON");
            db.Database.CloseConnection();
        }
        return new SeededRows(tables.Select(t => t.Name).ToList(), tables.ToDictionary(
            t => t.Name,
            t => (IReadOnlyList<IReadOnlyDictionary<string, object?>>)Enumerable.Range(0, units.Count)
                .Select(u => (IReadOnlyDictionary<string, object?>)(t.PrimaryKey?.Columns ?? [])
                    .ToDictionary(c => c.Name, c => seeded[(t.Name, u)].GetValueOrDefault(c.Name)))
                .ToList()));
    }

    /// <summary>Whether the row seeded for one unit is still in its table.</summary>
    public static bool Exists(DbContext db, SeededRows rows, string table, int unit)
    {
        var relational = db.Model.GetRelationalModel().Tables.First(t => t.Name == table);
        var key = rows.Keys[table][unit];
        var sql = db.GetService<ISqlGenerationHelper>();
        var connection = db.Database.GetDbConnection();
        db.Database.OpenConnection();
        try
        {
            using var command = connection.CreateCommand();
            var where = new List<string>();
            foreach (var column in relational.PrimaryKey!.Columns)
            {
                var name = $"@k{where.Count}";
                where.Add($"{sql.DelimitIdentifier(column.Name)} = {name}");
                command.Parameters.Add(column.PropertyMappings.First().Property.GetRelationalTypeMapping()
                    .CreateParameter(command, name, key[column.Name], nullable: false));
            }
            command.CommandText = $"SELECT COUNT(*) FROM {sql.DelimitIdentifier(table, relational.Schema)} WHERE {string.Join(" AND ", where)}";
            return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    /// <summary>Rows left in a table, whoever they belong to.</summary>
    public static long Count(DbContext db, string table)
    {
        var sql = db.GetService<ISqlGenerationHelper>();
        var schema = db.Model.GetRelationalModel().Tables.First(t => t.Name == table).Schema;
        var connection = db.Database.GetDbConnection();
        db.Database.OpenConnection();
        try
        {
            return Convert.ToInt64(Scalar(connection, $"SELECT COUNT(*) FROM {sql.DelimitIdentifier(table, schema)}"),
                System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            db.Database.CloseConnection();
        }
    }

    /// <summary>The tables a table points at through its foreign keys (itself excluded).</summary>
    public static IEnumerable<string> Parents(ITable table) =>
        table.ForeignKeyConstraints.Select(f => f.PrincipalTable.Name).Where(n => n != table.Name).Distinct();

    private static List<ITable> InDependencyOrder(List<ITable> tables)
    {
        var byName = tables.ToDictionary(t => t.Name);
        var ordered = new List<ITable>();
        var visiting = new HashSet<string>();
        var done = new HashSet<string>();
        void Visit(ITable table)
        {
            if (done.Contains(table.Name) || !visiting.Add(table.Name)) return;
            foreach (var parent in Parents(table))
                if (byName.TryGetValue(parent, out var p)) Visit(p);
            visiting.Remove(table.Name);
            done.Add(table.Name);
            ordered.Add(table);
        }
        foreach (var table in tables.OrderBy(t => t.Name, StringComparer.Ordinal)) Visit(table);
        return ordered;
    }

    private static bool IsStoreGenerated(IProperty property)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        return property.ValueGenerated == ValueGenerated.OnAdd && property.IsPrimaryKey()
            && (type == typeof(int) || type == typeof(long) || type == typeof(short));
    }

    private static object? ValueFor(
        ITable table, IColumn column, IProperty property, SeedUnit unit, int unitIndex,
        Dictionary<(string Table, int Unit), Dictionary<string, object?>> seeded, int counter)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (property.IsPrimaryKey() && table.PrimaryKey?.Columns.Count == 1 && unit.Keys.TryGetValue(table.Name, out var key))
            return key;

        if (unit.Values.TryGetValue(column.Name, out var given) && Fits(given, type))
            return type == typeof(string) ? given.ToString() : given;

        var fk = table.ForeignKeyConstraints.FirstOrDefault(f =>
            f.Columns.Count == 1 && f.Columns[0].Name == column.Name && f.PrincipalTable.Name != table.Name);
        if (fk != null && seeded.TryGetValue((fk.PrincipalTable.Name, unitIndex), out var parent))
            return parent.GetValueOrDefault(fk.PrincipalColumns[0].Name);

        if (property.DeclaringType is IEntityType entity && entity.FindDiscriminatorProperty() == property)
            return entity.GetDiscriminatorValue();

        if (column.IsNullable && !property.IsKey()) return null;
        return Sample(type, counter);
    }

    private static bool Fits(object value, Type type) =>
        type == typeof(string) || type.IsInstanceOfType(value);

    private static object Sample(Type t, int n)
    {
        if (t.IsEnum) return Enum.GetValues(t).GetValue(0)!;
        if (t == typeof(string)) return $"s{n}";
        if (t == typeof(Guid)) return Guid.NewGuid();
        if (t == typeof(bool)) return false;
        if (t == typeof(int)) return n;
        if (t == typeof(long)) return (long)n;
        if (t == typeof(short)) return (short)(n % short.MaxValue);
        if (t == typeof(byte)) return (byte)(n % byte.MaxValue);
        if (t == typeof(decimal)) return (decimal)n;
        if (t == typeof(double)) return (double)n;
        if (t == typeof(float)) return (float)n;
        if (t == typeof(DateTime)) return new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(n);
        if (t == typeof(DateTimeOffset)) return new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(n);
        if (t == typeof(DateOnly)) return new DateOnly(2025, 1, 1).AddDays(n % 300);
        if (t == typeof(TimeOnly)) return new TimeOnly(8, 0);
        if (t == typeof(TimeSpan)) return TimeSpan.FromHours(8);
        if (t == typeof(byte[])) return new byte[] { 1 };
        return Activator.CreateInstance(t)
            ?? throw new NotSupportedException($"No sample value for {t.Name}; teach {nameof(ModelRowSeeder)} about it.");
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}

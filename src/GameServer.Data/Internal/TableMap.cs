using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace GameServer.Data.Internal;

internal sealed partial class TableMap
{
    internal sealed record Column(string Property, string Name, Type Type, bool Identity, bool Computed,
        Func<object, object?> Get, Action<object, object?> Set);
    private static readonly ConcurrentDictionary<Type, TableMap> Cache = new();
    public string Name { get; }
    public Column[] Columns { get; }
    public Column Key { get; }
    public static TableMap For(Type type) => Cache.GetOrAdd(type, static t => new(t));

    private TableMap(Type type)
    {
        var table = type.GetCustomAttribute<TableAttribute>() ??
            throw new InvalidOperationException($"{type.Name} requires a Table attribute.");
        if (!string.IsNullOrEmpty(table.Schema)) throw new NotSupportedException("Select the database/schema through its connection options.");
        Name = Identifier(table.Name);
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null && p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken).ToArray();
        var keys = properties.Where(p => p.GetCustomAttribute<KeyAttribute>() != null).ToArray();
        if (keys.Length == 0) keys = properties.Where(p => p.Name == "Id").ToArray();
        if (keys.Length != 1) throw new InvalidOperationException($"{type.Name} requires exactly one key property.");
        Columns = properties.Select(p =>
        {
            string name = Identifier(p.GetCustomAttribute<ColumnAttribute>()?.Name ?? SnakeCase(p.Name));
            var generated = p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption;
            bool identity = generated == DatabaseGeneratedOption.Identity;
            if (identity && p != keys[0]) throw new InvalidOperationException("Only the key may be an identity column.");
            var target = Expression.Parameter(typeof(object)); var value = Expression.Parameter(typeof(object));
            var member = Expression.Property(Expression.Convert(target, type), p);
            var getter = Expression.Lambda<Func<object, object?>>(Expression.Convert(member, typeof(object)), target).Compile();
            var setter = Expression.Lambda<Action<object, object?>>(Expression.Assign(member, Expression.Convert(value, p.PropertyType)), target, value).Compile();
            return new Column(p.Name, name, p.PropertyType, identity, generated == DatabaseGeneratedOption.Computed, getter, setter);
        }).ToArray();
        if (Columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Columns.Length)
            throw new InvalidOperationException("Duplicate database column names.");
        Key = Columns.Single(c => c.Property == keys[0].Name);
        if (Key.Identity && Key.Type != typeof(long) && Key.Type != typeof(int))
            throw new InvalidOperationException("Identity keys must use int or long.");
    }

    public Column Resolve(string property) => Columns.FirstOrDefault(c => c.Property == property) ??
        throw new ArgumentException("Unknown mapped property. Use nameof(Model.Property).", nameof(property));
    public T Read<T>(DbDataReader reader) where T : class, new()
    {
        var value = new T();
        for (int i = 0; i < Columns.Length; i++)
            Columns[i].Set(value, ConvertValue(reader.IsDBNull(i) ? null : reader.GetValue(i), Columns[i].Type));
        return value;
    }

    public static object? ConvertValue(object? value, Type destination)
    {
        Type type = Nullable.GetUnderlyingType(destination) ?? destination;
        if (value == null || value is DBNull)
        {
            if (destination.IsValueType && Nullable.GetUnderlyingType(destination) == null)
                throw new InvalidOperationException("Database NULL cannot populate a non-nullable value property.");
            return null;
        }
        if (type.IsInstanceOfType(value)) return value;
        if (type.IsEnum) return Enum.ToObject(type, Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture));
        if (type == typeof(Guid)) return value is byte[] bytes ? new Guid(bytes) : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        if (type == typeof(DateTime)) return DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(DateTimeOffset)) return DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);
        return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }

    public static string Quote(string name, DatabaseProvider provider) => provider == DatabaseProvider.MySql ? "`" + Identifier(name) + "`" : "\"" + Identifier(name) + "\"";
    private static string Identifier(string name) => IdentifierPattern().IsMatch(name) ? name : throw new ArgumentException("Unsafe database identifier.");
    private static string SnakeCase(string name) => Boundary().Replace(Acronym().Replace(name, "$1_$2"), "$1_$2").ToLowerInvariant();
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")] private static partial Regex IdentifierPattern();
    [GeneratedRegex("([a-z0-9])([A-Z])")] private static partial Regex Boundary();
    [GeneratedRegex("([A-Z])([A-Z][a-z])")] private static partial Regex Acronym();
}

using System.Text.RegularExpressions;

namespace GameServer.Data;

public sealed partial class SqlStatement
{
    public string Text { get; }
    public IReadOnlyList<(string Name, object? Value)> Parameters { get; }
    public SqlStatement(string text, params (string Name, object? Value)[] parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, _) in parameters)
            if (!ParameterName().IsMatch(name) || !names.Add(name))
                throw new ArgumentException("SQL parameters require unique identifier names without prefixes.", nameof(parameters));
        Text = text;
        Parameters = parameters.ToArray();
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ParameterName();
}

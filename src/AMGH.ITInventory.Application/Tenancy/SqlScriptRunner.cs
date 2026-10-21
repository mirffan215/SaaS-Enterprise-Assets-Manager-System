using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AMGH.ITInventory.Application.Tenancy;

/// <summary>
/// Runs the deployment scripts (written for sqlcmd) over a normal connection: batches are separated by GO,
/// sqlcmd commands (":on error", ":setvar", ":r") are skipped and $(Variable) references are replaced.
/// Variable values are escaped for use inside T-SQL string literals.
/// </summary>
internal static partial class SqlScriptRunner
{
    [GeneratedRegex(@"^\s*GO(?:\s+(\d+))?\s*(?:--.*)?$", RegexOptions.IgnoreCase)] private static partial Regex GoLine();
    [GeneratedRegex(@"\$\((\w+)\)")] private static partial Regex Variable();

    /// <summary>Splits a script into batches and substitutes variables.</summary>
    internal static IReadOnlyList<string> ToBatches(string script, IReadOnlyDictionary<string, string> variables)
    {
        var batches = new List<string>();
        var current = new StringBuilder();
        void Flush(int repeat)
        {
            var text = current.ToString().Trim();
            current.Clear();
            if (text.Length == 0) return;
            text = Variable().Replace(text, m => variables.TryGetValue(m.Groups[1].Value, out var v) ? v.Replace("'", "''", StringComparison.Ordinal) : m.Value);
            for (var i = 0; i < Math.Max(1, repeat); i++) batches.Add(text);
        }

        foreach (var line in script.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (GoLine().Match(l) is { Success: true } go)
            {
                Flush(go.Groups[1].Success ? int.Parse(go.Groups[1].Value, CultureInfo.InvariantCulture) : 1);
                continue;
            }
            if (l.TrimStart().StartsWith(':')) continue; // sqlcmd command
            current.AppendLine(l);
        }
        Flush(1);
        return batches;
    }

    internal static async Task RunAsync(SqlConnection connection, string script, IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken)
    {
        foreach (var batch in ToBatches(script, variables))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 600;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

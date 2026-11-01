using System.Text.RegularExpressions;

namespace FeedbackIntel.Integration.Tests.Contract;

/// <summary>
/// The data contract, read straight from <c>docs/data-dictionary.md</c> section 7, so the
/// document is the single source of truth. Every file the pipeline may write, and the exact
/// columns each one may contain (NFR-PRIV-02).
/// </summary>
internal sealed partial class DataContract
{
    private DataContract(
        IReadOnlyDictionary<string, IReadOnlySet<string>> published,
        IReadOnlyDictionary<string, IReadOnlySet<string>> history)
    {
        Published = published;
        History = history;
    }

    /// <summary>Files allowed on GitHub Pages, and their allowed columns.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> Published { get; }

    /// <summary>Files allowed on the data branch, and their allowed columns.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>> History { get; }

    public static DataContract Load()
    {
        var lines = File.ReadAllLines(FindDataDictionary());
        var tables = ReadTableColumns(lines);

        return new DataContract(
            ReadContractSection(lines, "### Output 1", tables),
            ReadContractSection(lines, "### Output 2", tables));
    }

    /// <summary>Every "### `schema.table`" section's column names, from its first table column.</summary>
    private static Dictionary<string, List<string>> ReadTableColumns(string[] lines)
    {
        var tables = new Dictionary<string, List<string>>();
        List<string>? current = null;

        foreach (var line in lines)
        {
            if (TableHeading().Match(line) is { Success: true } heading)
            {
                current = tables[heading.Groups[1].Value] = [];
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                current = null;
            }
            else if (current is not null && ColumnRow().Match(line) is { Success: true } column)
            {
                current.Add(column.Groups[1].Value);
            }
        }

        return tables;
    }

    /// <summary>
    /// Reads one contract table. Its rows name a file, its source table, and its fields:
    /// "All columns", "All columns except `x`", or an explicit list of `columns`.
    /// </summary>
    private static Dictionary<string, IReadOnlySet<string>> ReadContractSection(
        string[] lines, string heading, Dictionary<string, List<string>> tables)
    {
        var files = new Dictionary<string, IReadOnlySet<string>>();
        var start = Array.FindIndex(lines, line => line.StartsWith(heading, StringComparison.Ordinal));
        if (start < 0)
        {
            throw new InvalidOperationException($"The data dictionary has no '{heading}' section.");
        }

        foreach (var line in lines.Skip(start + 1).TakeWhile(line => !line.StartsWith('#')))
        {
            var cells = line.Split('|', StringSplitOptions.TrimEntries).Where(cell => cell.Length > 0).ToArray();
            if (cells.Length < 3 || !cells[0].StartsWith('`'))
            {
                continue;
            }

            var file = Unquote(cells[0]);
            var source = Unquote(cells[^2]);
            var fields = cells[^1];
            var columns = fields.StartsWith("All columns", StringComparison.Ordinal)
                ? tables[source].Except(Quoted().Matches(fields).Select(match => match.Groups[1].Value))
                : Quoted().Matches(fields).Select(match => match.Groups[1].Value);

            files[file] = columns.ToHashSet();
        }

        return files;
    }

    private static string FindDataDictionary()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "docs", "data-dictionary.md");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Couldn't find docs/data-dictionary.md above the test folder.");
    }

    private static string Unquote(string cell) => Quoted().Match(cell).Groups[1].Value;

    [GeneratedRegex(@"^### `([a-z_]+\.[a-z_]+)`")]
    private static partial Regex TableHeading();

    [GeneratedRegex(@"^\| `([a-z_]+)` \|")]
    private static partial Regex ColumnRow();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex Quoted();
}

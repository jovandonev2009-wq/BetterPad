using System.IO;
using System.Text.Json;

namespace BetterPad.Syntax;

public sealed class BlockCommentRule
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
}

public sealed class SyntaxColors
{
    public string Default { get; set; } = "#1F2937";
    public string Keyword { get; set; } = "#0B57D0";
    public string Type { get; set; } = "#0F766E";
    public string String { get; set; } = "#B42318";
    public string Comment { get; set; } = "#15803D";
    public string Number { get; set; } = "#7C3AED";
    public string Preprocessor { get; set; } = "#6B7280";
}

/// <summary>
/// A highlighting rule set, loaded from a JSON file in the local syntax folder.
/// </summary>
public sealed class SyntaxDefinition
{
    public string Name { get; set; } = "Plain Text";
    public string[] Extensions { get; set; } = [];
    public bool CaseSensitive { get; set; } = true;
    public string[] LineComments { get; set; } = [];
    public BlockCommentRule[] BlockComments { get; set; } = [];
    public string[] Strings { get; set; } = [];
    public string? Preprocessor { get; set; }
    public string[] Keywords { get; set; } = [];
    public string[] Types { get; set; } = [];
    public SyntaxColors Colors { get; set; } = new();

    public HashSet<string> KeywordSet { get; private set; } = new(StringComparer.Ordinal);

    public HashSet<string> TypeSet { get; private set; } = new(StringComparer.Ordinal);

    public string[] SortedLineComments { get; private set; } = [];

    public BlockCommentRule[] SortedBlockComments { get; private set; } = [];

    public string[] SortedStrings { get; private set; } = [];

    public void Prepare()
    {
        StringComparer comparer = CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        KeywordSet = new HashSet<string>(Keywords, comparer);
        TypeSet = new HashSet<string>(Types, comparer);
        SortedLineComments = LineComments.OrderByDescending(s => s.Length).ToArray();
        SortedBlockComments = BlockComments.OrderByDescending(b => b.Start.Length).ToArray();
        SortedStrings = Strings.OrderByDescending(s => s.Length).ToArray();
        Colors ??= new SyntaxColors();
    }

    public static SyntaxDefinition Plain() => new() { Name = "Plain Text" };
}

public sealed class SyntaxService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, SyntaxDefinition> _byExtension = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SyntaxDefinition> Definitions { get; private set; } = [];

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public SyntaxDefinition Plain { get; } = SyntaxDefinition.Plain();

    public SyntaxService()
    {
        Reload();
    }

    public void Reload()
    {
        var definitions = new List<SyntaxDefinition>();
        var errors = new List<string>();
        var byExtension = new Dictionary<string, SyntaxDefinition>(StringComparer.OrdinalIgnoreCase);

        try
        {
            AppPaths.EnsureFolders();
            if (Directory.Exists(AppPaths.SyntaxDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(AppPaths.SyntaxDirectory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        SyntaxDefinition? definition = JsonSerializer.Deserialize<SyntaxDefinition>(json, JsonOptions);
                        if (definition is null || string.IsNullOrWhiteSpace(definition.Name))
                        {
                            errors.Add($"{Path.GetFileName(file)}: missing rule name");
                            continue;
                        }

                        definition.Prepare();
                        definitions.Add(definition);
                        foreach (string extension in definition.Extensions)
                        {
                            string normalized = extension.StartsWith('.') ? extension : "." + extension;
                            byExtension[normalized] = definition;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        _byExtension.Clear();
        foreach (var pair in byExtension)
        {
            _byExtension[pair.Key] = pair.Value;
        }

        Definitions = definitions;
        Errors = errors;
    }

    public SyntaxDefinition ForFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            string extension = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(extension) && _byExtension.TryGetValue(extension, out SyntaxDefinition? definition))
            {
                return definition;
            }
        }

        return Plain;
    }
}

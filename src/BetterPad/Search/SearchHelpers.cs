using System.Text.RegularExpressions;

namespace BetterPad.Search;

public static class SearchHelpers
{
    public static Regex Build(string pattern, bool useRegex, bool matchCase, bool wholeWord)
    {
        string expression = useRegex ? pattern : Regex.Escape(pattern);
        if (wholeWord)
        {
            expression = $@"(?<!\w)(?:{expression})(?!\w)";
        }

        RegexOptions options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
        if (!matchCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(expression, options, TimeSpan.FromSeconds(2));
    }
}

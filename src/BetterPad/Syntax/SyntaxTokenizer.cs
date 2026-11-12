namespace BetterPad.Syntax;

public enum TokenKind
{
    Keyword,
    Type,
    String,
    Comment,
    Number,
    Preprocessor
}

public readonly record struct SyntaxToken(int Start, int Length, TokenKind Kind)
{
    public int End => Start + Length;
}

/// <summary>
/// Walks the document once and marks keywords, strings, comments and numbers.
/// The rule file decides what those constructs look like; no network is involved.
/// </summary>
public static class SyntaxTokenizer
{
    public static List<SyntaxToken> Tokenize(string text, SyntaxDefinition definition)
    {
        var tokens = new List<SyntaxToken>();
        if (text.Length == 0 || definition.Name == "Plain Text")
        {
            return tokens;
        }

        StringComparison comparison = definition.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int i = 0;
        bool atLineStart = true;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '\r' || c == '\n')
            {
                atLineStart = true;
                i++;
                continue;
            }

            if (atLineStart && char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (atLineStart && !string.IsNullOrEmpty(definition.Preprocessor) &&
                StartsWith(text, i, definition.Preprocessor, StringComparison.Ordinal))
            {
                int end = FindLineEnd(text, i);
                tokens.Add(new SyntaxToken(i, end - i, TokenKind.Preprocessor));
                i = end;
                atLineStart = false;
                continue;
            }

            BlockCommentRule? block = null;
            foreach (BlockCommentRule rule in definition.SortedBlockComments)
            {
                if (StartsWith(text, i, rule.Start, StringComparison.Ordinal))
                {
                    block = rule;
                    break;
                }
            }

            if (block != null)
            {
                int closing = text.IndexOf(block.End, i + block.Start.Length, StringComparison.Ordinal);
                int end = closing < 0 ? text.Length : closing + block.End.Length;
                tokens.Add(new SyntaxToken(i, end - i, TokenKind.Comment));
                atLineStart = false;
                i = end;
                continue;
            }

            bool isLineComment = false;
            foreach (string marker in definition.SortedLineComments)
            {
                if (StartsWith(text, i, marker, StringComparison.Ordinal))
                {
                    int end = FindLineEnd(text, i);
                    tokens.Add(new SyntaxToken(i, end - i, TokenKind.Comment));
                    i = end;
                    isLineComment = true;
                    atLineStart = false;
                    break;
                }
            }

            if (isLineComment)
            {
                continue;
            }

            bool isString = false;
            foreach (string delimiter in definition.SortedStrings)
            {
                if (!StartsWith(text, i, delimiter, StringComparison.Ordinal))
                {
                    continue;
                }

                int end = ScanString(text, i, delimiter);
                tokens.Add(new SyntaxToken(i, end - i, TokenKind.String));
                i = end;
                isString = true;
                atLineStart = false;
                break;
            }

            if (isString)
            {
                continue;
            }

            if (char.IsDigit(c))
            {
                int end = ScanNumber(text, i);
                tokens.Add(new SyntaxToken(i, end - i, TokenKind.Number));
                i = end;
                atLineStart = false;
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '$')
            {
                int end = i + 1;
                while (end < text.Length)
                {
                    char next = text[end];
                    if (char.IsLetterOrDigit(next) || next == '_' || next == '$')
                    {
                        end++;
                    }
                    else
                    {
                        break;
                    }
                }

                string word = text.Substring(i, end - i);
                if (definition.KeywordSet.Contains(word))
                {
                    tokens.Add(new SyntaxToken(i, end - i, TokenKind.Keyword));
                }
                else if (definition.TypeSet.Contains(word))
                {
                    tokens.Add(new SyntaxToken(i, end - i, TokenKind.Type));
                }

                i = end;
                atLineStart = false;
                continue;
            }

            atLineStart = false;
            i++;
        }

        return tokens;
    }

    private static int ScanString(string text, int start, string delimiter)
    {
        int i = start + delimiter.Length;
        bool singleChar = delimiter.Length == 1;

        while (i < text.Length)
        {
            if (text[i] == '\\' && i + 1 < text.Length && singleChar)
            {
                i += 2;
                continue;
            }

            if (StartsWith(text, i, delimiter, StringComparison.Ordinal))
            {
                int closing = i + delimiter.Length;

                // SQL-style escaped quote: 'it''s'
                if (singleChar && closing < text.Length && text[closing] == delimiter[0])
                {
                    i = closing + 1;
                    continue;
                }

                return closing;
            }

            i++;
        }

        return text.Length;
    }

    private static int ScanNumber(string text, int start)
    {
        int i = start + 1;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
            {
                i++;
                continue;
            }

            if ((c == '+' || c == '-') && i > 0 && (text[i - 1] == 'e' || text[i - 1] == 'E'))
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    private static int FindLineEnd(string text, int start)
    {
        int i = start;
        while (i < text.Length && text[i] != '\r' && text[i] != '\n')
        {
            i++;
        }

        return i;
    }

    private static bool StartsWith(string text, int index, string value, StringComparison comparison)
    {
        if (value.Length == 0 || index + value.Length > text.Length)
        {
            return false;
        }

        return string.Compare(text, index, value, 0, value.Length, comparison) == 0;
    }
}

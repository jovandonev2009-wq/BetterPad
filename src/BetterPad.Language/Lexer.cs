using System.Globalization;

namespace BetterPad.Language;

internal sealed class Lexer
{
    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
    {
        ["let"] = TokenType.Let,
        ["fn"] = TokenType.Fn,
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["while"] = TokenType.While,
        ["return"] = TokenType.Return,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False
    };

    private readonly string _source;
    private readonly List<Token> _tokens = [];
    private int _start;
    private int _current;
    private int _line = 1;
    private int _lineStart;
    private int _tokenLine;
    private int _tokenColumn;

    public Lexer(string source)
    {
        _source = source;
    }

    public List<Token> Tokenize()
    {
        while (!AtEnd)
        {
            _start = _current;
            _tokenLine = _line;
            _tokenColumn = _current - _lineStart + 1;
            ScanToken();
        }

        _tokens.Add(new Token(TokenType.EndOfFile, string.Empty, null!, _line, _current - _lineStart + 1));
        return _tokens;
    }

    private bool AtEnd => _current >= _source.Length;

    private char Peek => AtEnd ? '\0' : _source[_current];

    private char PeekNext => _current + 1 >= _source.Length ? '\0' : _source[_current + 1];

    private char Advance() => _source[_current++];

    private bool Match(char expected)
    {
        if (AtEnd || _source[_current] != expected)
        {
            return false;
        }

        _current++;
        return true;
    }

    private void ScanToken()
    {
        char c = Advance();
        switch (c)
        {
            case ' ':
            case '\r':
            case '\t':
                break;
            case '\n':
                _line++;
                _lineStart = _current;
                break;
            case '(':
                Add(TokenType.LeftParen);
                break;
            case ')':
                Add(TokenType.RightParen);
                break;
            case '{':
                Add(TokenType.LeftBrace);
                break;
            case '}':
                Add(TokenType.RightBrace);
                break;
            case ',':
                Add(TokenType.Comma);
                break;
            case ';':
                Add(TokenType.Semicolon);
                break;
            case '+':
                Add(TokenType.Plus);
                break;
            case '-':
                Add(TokenType.Minus);
                break;
            case '*':
                Add(TokenType.Star);
                break;
            case '%':
                Add(TokenType.Percent);
                break;
            case '/':
                if (Match('/'))
                {
                    while (!AtEnd && Peek != '\n')
                    {
                        _current++;
                    }
                }
                else
                {
                    Add(TokenType.Slash);
                }

                break;
            case '=':
                Add(Match('=') ? TokenType.EqualEqual : TokenType.Assign);
                break;
            case '!':
                Add(Match('=') ? TokenType.BangEqual : TokenType.Bang);
                break;
            case '<':
                Add(Match('=') ? TokenType.LessEqual : TokenType.Less);
                break;
            case '>':
                Add(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
                break;
            case '&':
                if (!Match('&'))
                {
                    throw Error("Use '&&' for logical and.");
                }

                Add(TokenType.AndAnd);
                break;
            case '|':
                if (!Match('|'))
                {
                    throw Error("Use '||' for logical or.");
                }

                Add(TokenType.OrOr);
                break;
            case '"':
                ScanString();
                break;
            default:
                if (char.IsDigit(c))
                {
                    ScanNumber();
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    ScanIdentifier();
                }
                else
                {
                    throw Error($"Unexpected character '{c}'.");
                }

                break;
        }
    }

    private void ScanString()
    {
        var value = new System.Text.StringBuilder();
        while (!AtEnd && Peek != '"')
        {
            char c = Advance();
            if (c == '\n')
            {
                throw Error("Unterminated string.");
            }

            if (c != '\\')
            {
                value.Append(c);
                continue;
            }

            if (AtEnd)
            {
                throw Error("Unterminated string.");
            }

            char escaped = Advance();
            value.Append(escaped switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '\\' => '\\',
                '"' => '"',
                _ => throw Error($"Unknown escape sequence '\\{escaped}'.")
            });
        }

        if (AtEnd)
        {
            throw Error("Unterminated string.");
        }

        Advance(); // closing quote
        Add(TokenType.String, value.ToString());
    }

    private void ScanNumber()
    {
        while (char.IsDigit(Peek))
        {
            _current++;
        }

        if (Peek == '.' && char.IsDigit(PeekNext))
        {
            _current++;
            while (char.IsDigit(Peek))
            {
                _current++;
            }
        }

        string text = _source[_start.._current];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
        {
            throw Error($"'{text}' is not a valid number.");
        }

        Add(TokenType.Number, number);
    }

    private void ScanIdentifier()
    {
        while (char.IsLetterOrDigit(Peek) || Peek == '_')
        {
            _current++;
        }

        string text = _source[_start.._current];
        Add(Keywords.TryGetValue(text, out TokenType type) ? type : TokenType.Identifier);
    }

    private void Add(TokenType type, object literal = null!)
    {
        _tokens.Add(new Token(type, _source[_start.._current], literal, _tokenLine, _tokenColumn));
    }

    private TinyThrownException Error(string message) => new(message, _tokenLine, _tokenColumn);
}

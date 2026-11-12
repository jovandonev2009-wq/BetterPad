namespace BetterPad.Language;

public enum TokenType
{
    // Literals and identifiers
    Number,
    String,
    Identifier,

    // Keywords
    Let,
    Fn,
    If,
    Else,
    While,
    Return,
    True,
    False,

    // Punctuation
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    Comma,
    Semicolon,

    // Operators
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Assign,
    EqualEqual,
    BangEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    AndAnd,
    OrOr,
    Bang,

    EndOfFile
}

public readonly record struct Token(TokenType Type, string Lexeme, object Literal, int Line, int Column);

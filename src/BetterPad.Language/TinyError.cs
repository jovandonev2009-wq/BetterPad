namespace BetterPad.Language;

/// <summary>A syntax or runtime error with a 1-based source location.</summary>
public sealed class TinyError
{
    public TinyError(string message, int line, int column)
    {
        Message = message;
        Line = line;
        Column = column;
    }

    public string Message { get; }

    public int Line { get; }

    public int Column { get; }

    public string Display => $"{Message} (line {Line}, column {Column})";

    public override string ToString() => Display;
}

/// <summary>Thrown inside the language implementation and converted to a TinyError.</summary>
internal sealed class TinyThrownException : Exception
{
    public TinyThrownException(string message, int line, int column)
        : base(message)
    {
        Error = new TinyError(message, line, column);
    }

    public TinyError Error { get; }
}

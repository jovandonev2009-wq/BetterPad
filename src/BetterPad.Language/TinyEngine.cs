namespace BetterPad.Language;

/// <summary>Outcome of running a Tiny program.</summary>
public sealed record TinyRunResult(bool Cancelled, TinyError? Error)
{
    public bool Success => !Cancelled && Error is null;
}

/// <summary>
/// Runs Tiny source. The language is interpreted locally; nothing is compiled to disk
/// and nothing touches the network.
/// </summary>
public static class TinyEngine
{
    public static TinyRunResult Run(string source, Action<string> write, CancellationToken cancellation = default)
    {
        try
        {
            List<Token> tokens = new Lexer(source).Tokenize();
            List<Stmt> program = new Parser(tokens).ParseProgram();
            new Interpreter(write, cancellation).Execute(program);
            return new TinyRunResult(false, null);
        }
        catch (TinyThrownException ex)
        {
            return new TinyRunResult(false, ex.Error);
        }
        catch (OperationCanceledException)
        {
            return new TinyRunResult(true, null);
        }
        catch (Exception ex)
        {
            return new TinyRunResult(false, new TinyError($"Internal interpreter error: {ex.Message}", 1, 1));
        }
    }
}

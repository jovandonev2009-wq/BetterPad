using BetterPad.Language;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: TinyLang <file.tiny>");
    return 2;
}

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 2;
}

string source = File.ReadAllText(path);
TinyRunResult result = TinyEngine.Run(source, Console.Write);

if (result.Cancelled)
{
    return 130;
}

if (result.Error is { } error)
{
    Console.Error.WriteLine($"{path}:{error.Line}:{error.Column}: {error.Message}");
    return 1;
}

return 0;

namespace SimpleParser;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h")) { ShowUsage(); return 0; }
        var showTokens = args.Contains("--tokens");
        var files = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (files.Length > 1) { Console.Error.WriteLine("Please provide at most one source file."); ShowUsage(); return 2; }

        string source;
        try
        {
            source = files.Length == 1 ? File.ReadAllText(files[0]) : ReadInput();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not read input: {exception.Message}"); return 2;
        }

        try
        {
            var tokens = new Lexer(source).ScanTokens();
            if (showTokens) foreach (var token in tokens) Console.WriteLine(token);
            var program = new Parser(tokens).ParseProgram();
            Console.WriteLine(new AstPrinter().Print(program));
            return 0;
        }
        catch (ParseException exception)
        {
            Console.Error.WriteLine($"Syntax error: {exception.Message}");
            return 1;
        }
    }

    private static string ReadInput()
    {
        if (!Console.IsInputRedirected) Console.WriteLine("Enter source code; press Ctrl+Z then Enter when finished:");
        return Console.In.ReadToEnd();
    }

    private static void ShowUsage() => Console.WriteLine("Usage: dotnet run --project src/SimpleParser [--tokens] [source-file]\nWith no file, source is read from standard input.");
}

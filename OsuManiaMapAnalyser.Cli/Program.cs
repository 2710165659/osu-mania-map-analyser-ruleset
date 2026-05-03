using System.Text.Json;
using OsuManiaMapAnalyser.Core;

return await ProgramEntry.RunAsync(args);

internal static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Any(static arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("Usage: dotnet run --project csharp/src/OsuManiaMapAnalyser.Cli -- [--input <path>]");
                Console.WriteLine("If --input is omitted, JSON is read from stdin.");
                return 0;
            }

            var inputJson = await ReadInputJsonAsync(args);
            var response = BeatmapAnalyzer.AnalyzeJson(inputJson);
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));

            return 0;
        }
        catch (Exception ex)
        {
            var error = new
            {
                error = new
                {
                    message = ex.Message,
                },
            };

            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(error, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));

            return 1;
        }
    }

    private static async Task<string> ReadInputJsonAsync(IReadOnlyList<string> args)
    {
        string? inputPath = null;
        for (var i = 0; i < args.Count; i += 1)
        {
            if (!string.Equals(args[i], "--input", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Count)
            {
                throw new InvalidOperationException("Missing value for --input.");
            }

            inputPath = args[i + 1];
            i += 1;
        }

        if (!string.IsNullOrWhiteSpace(inputPath))
        {
            return await File.ReadAllTextAsync(inputPath);
        }

        var stdin = await Console.In.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(stdin))
        {
            return stdin;
        }

        throw new InvalidOperationException("No input JSON provided. Use stdin or --input <path>.");
    }
}

using System.Text;
using JsonToCsv;

if (args.Length < 2)
{
    Console.WriteLine("Usage: JsonToCsv <input.json> <output.csv>");
    return 1;
}

string inputPath = Path.GetFullPath(args[0]);
string outputPath = Path.GetFullPath(args[1]);

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Error: input file not found: {inputPath}");
    return 2;
}

try
{
    using var input = File.OpenRead(inputPath);
    using var writer = new StreamWriter(outputPath, append: false, Encoding.UTF8, bufferSize: 1 << 16);

    int count = JsonToCsvConverter.Convert(input, writer);

    if (count == 0)
    {
        Console.WriteLine("No items found.");
        return 1;
    }

    Console.WriteLine($"Done – {count} rows written to {outputPath}");
    return 0;
}
catch (System.Text.Json.JsonException ex)
{
    Console.Error.WriteLine($"Error: '{inputPath}' is not valid JSON. {ex.Message}");
    return 3;
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Error: unsupported JSON structure. {ex.Message}");
    return 4;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 5;
}
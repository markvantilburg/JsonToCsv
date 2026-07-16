using System.Text;
using JsonToCsv;

if (args.Length < 2)
{
    Console.WriteLine("Usage: JsonToCsv <input.json> <output.csv>");
    return 1;
}

using var input = File.OpenRead(args[0]);
using var writer = new StreamWriter(args[1], append: false, Encoding.UTF8, bufferSize: 1 << 16);

int count = JsonToCsvConverter.Convert(input, writer);

if (count == 0)
{
    Console.WriteLine("No items found.");
    return 1;
}

Console.WriteLine($"Done – {count} rows written to {args[1]}");
return 0;
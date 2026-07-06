using System.Text.Json;

if (args.Length < 2)
{
	Console.WriteLine("Usage: JsonToCsv <input.json> <output.csv>");
	return 1;
}

var json = File.ReadAllText(args[0]);
var doc = JsonDocument.Parse(json);

// If root is an array use it directly, otherwise find the first array property
JsonElement arrayElement;
if (doc.RootElement.ValueKind == JsonValueKind.Array)
{
	arrayElement = doc.RootElement;
}
else
{
	arrayElement = doc.RootElement.EnumerateObject()
		.First(p => p.Value.ValueKind == JsonValueKind.Array).Value;
}

var items = arrayElement.EnumerateArray().ToList();
if (items.Count == 0)
{
	Console.WriteLine("No items found.");
	return 1;
}

var headers = items[0].EnumerateObject().Select(p => p.Name).ToList();
var lines = new List<string> { string.Join(";", headers) };

foreach (var item in items)
{
	var values = headers.Select(h =>
	{
		var val = item.GetProperty(h);
		var str = val.ValueKind == JsonValueKind.Null ? "" :
			val.ValueKind == JsonValueKind.String ? val.GetString()! :
			val.ToString();
		return str.Contains(';') || str.Contains('"') ? $"\"{str.Replace("\"", "\"\"")}\"" : str;
	});
	lines.Add(string.Join(";", values));
}

File.WriteAllLines(args[1], lines);
Console.WriteLine($"Done – {items.Count} rows written to {args[1]}");

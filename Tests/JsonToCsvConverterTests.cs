using System.Text;
using System.Text.Json;
using NUnit.Framework;
using JsonToCsv;

namespace JsonToCsv.Tests;

[TestFixture]
public class JsonToCsvConverterTests
{
	private static (int count, string[] lines) Run(string json)
	{
		using var writer = new StringWriter();
		int count = JsonToCsvConverter.Convert(json, writer);
		return (count, Lines(writer));
	}

	private static (int count, string[] lines) RunStream(string json, int bufferSize = 64 * 1024)
	{
		using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
		using var writer = new StringWriter();
		int count = JsonToCsvConverter.Convert(ms, writer, bufferSize);
		return (count, Lines(writer));
	}

	private static string[] Lines(StringWriter writer) =>
		writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

	[Test]
	public void RootArray_WritesHeaderAndRows()
	{
		var (count, lines) = Run("""[{"a":1,"b":"x"},{"a":2,"b":"y"}]""");

		Assert.That(count, Is.EqualTo(2));
		Assert.That(lines, Is.EqualTo(new[] { "a;b", "1;x", "2;y" }));
	}

	[Test]
	public void RootObject_UsesFirstArrayProperty()
	{
		var (count, lines) = Run("""{"meta":"ignored","items":[{"id":7}]}""");

		Assert.That(count, Is.EqualTo(1));
		Assert.That(lines, Is.EqualTo(new[] { "id", "7" }));
	}

	[Test]
	public void EmptyArray_ReturnsZeroAndWritesNothing()
	{
		var (count, lines) = Run("[]");

		Assert.That(count, Is.EqualTo(0));
		Assert.That(lines, Is.Empty);
	}

	[Test]
	public void RootObjectWithoutArray_Throws()
	{
		using var writer = new StringWriter();
		Assert.Throws<InvalidOperationException>(
			() => JsonToCsvConverter.Convert("""{"a":1}""", writer));
	}

	[Test]
	public void NullValue_BecomesEmptyField()
	{
		var (_, lines) = Run("""[{"a":null,"b":"x"}]""");

		Assert.That(lines[1], Is.EqualTo(";x"));
	}

	[Test]
	public void MissingPropertyInLaterRow_BecomesEmptyField()
	{
		var (count, lines) = Run("""[{"a":1,"b":2},{"a":3}]""");

		Assert.That(count, Is.EqualTo(2));
		Assert.That(lines[2], Is.EqualTo("3;"));
	}

	[Test]
	public void ExtraPropertyInLaterRow_IsIgnored()
	{
		var (_, lines) = Run("""[{"a":1},{"a":2,"z":9}]""");

		Assert.That(lines[0], Is.EqualTo("a"));
		Assert.That(lines[2], Is.EqualTo("2"));
	}

	[Test]
	public void Semicolon_IsQuoted()
	{
		var (_, lines) = Run("""[{"a":"x;y"}]""");

		Assert.That(lines[1], Is.EqualTo("\"x;y\""));
	}

	[Test]
	public void Quote_IsDoubledAndQuoted()
	{
		var (_, lines) = Run("""[{"a":"he said \"hi\""}]""");

		Assert.That(lines[1], Is.EqualTo("\"he said \"\"hi\"\"\""));
	}

	[Test]
	public void Newline_IsQuoted()
	{
		using var writer = new StringWriter();
		JsonToCsvConverter.Convert("""[{"a":"line1\nline2"}]""", writer);

		Assert.That(writer.ToString(), Does.Contain("\"line1\nline2\""));
	}

	[Test]
	public void NonStringValues_UseRawJson()
	{
		var (_, lines) = Run("""[{"n":1.5,"b":true,"o":{"x":1}}]""");

		Assert.That(lines[1], Is.EqualTo("1.5;true;\"{\"\"x\"\":1}\""));
	}

	[Test]
	public void HeaderContainingSeparator_IsQuoted()
	{
		var (_, lines) = Run("""[{"a;b":1}]""");

		Assert.That(lines[0], Is.EqualTo("\"a;b\""));
	}

	// ---- Streaming (Stream overload) ----

	[Test]
	public void Stream_RootArray_MatchesInMemoryResult()
	{
		const string json = """[{"a":1,"b":"x"},{"a":2,"b":"y;z"}]""";
		var expected = Run(json);
		var actual = RunStream(json);

		Assert.That(actual.count, Is.EqualTo(expected.count));
		Assert.That(actual.lines, Is.EqualTo(expected.lines));
	}

	[Test]
	public void Stream_RootObject_SkipsNonArrayPropertiesIncludingNested()
	{
		const string json = """
			{"meta":{"nested":[1,2,3],"deep":{"x":"y"}},"count":5,"items":[{"id":1},{"id":2}]}
			""";
		var (count, lines) = RunStream(json);

		Assert.That(count, Is.EqualTo(2));
		Assert.That(lines, Is.EqualTo(new[] { "id", "1", "2" }));
	}

	[Test]
	public void Stream_TinyBuffer_RefillsAndGrowsCorrectly()
	{
		// Elements are much larger than the 16-byte buffer, forcing repeated
		// refills and buffer growth mid-element.
		var big = new string('x', 500);
		string json = $$"""[{"a":"{{big}}","b":1},{"a":"{{big}}","b":2}]""";

		var (count, lines) = RunStream(json, bufferSize: 16);

		Assert.That(count, Is.EqualTo(2));
		Assert.That(lines[0], Is.EqualTo("a;b"));
		Assert.That(lines[1], Is.EqualTo(big + ";1"));
		Assert.That(lines[2], Is.EqualTo(big + ";2"));
	}

	[Test]
	public void Stream_ManyRows_AllWritten()
	{
		var sb = new StringBuilder("[");
		for (int i = 0; i < 10_000; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append($$"""{"i":{{i}},"s":"row {{i}}"}""");
		}
		sb.Append(']');

		var (count, lines) = RunStream(sb.ToString(), bufferSize: 256);

		Assert.That(count, Is.EqualTo(10_000));
		Assert.That(lines, Has.Length.EqualTo(10_001));
		Assert.That(lines[^1], Is.EqualTo("9999;row 9999"));
	}

	[Test]
	public void Stream_EmptyArray_ReturnsZero()
	{
		var (count, lines) = RunStream("[]");

		Assert.That(count, Is.EqualTo(0));
		Assert.That(lines, Is.Empty);
	}

	[Test]
	public void Stream_RootObjectWithoutArray_Throws()
	{
		using var ms = new MemoryStream(Encoding.UTF8.GetBytes("""{"a":1,"b":{"c":2}}"""));
		using var writer = new StringWriter();

		Assert.Throws<InvalidOperationException>(
			() => JsonToCsvConverter.Convert(ms, writer));
	}

	[Test]
	public void Stream_TruncatedJson_ThrowsJsonException()
	{
		using var ms = new MemoryStream(Encoding.UTF8.GetBytes("""[{"a":1},{"a":"""));
		using var writer = new StringWriter();

		Assert.Catch<JsonException>(
			() => JsonToCsvConverter.Convert(ms, writer, 16));
	}
}
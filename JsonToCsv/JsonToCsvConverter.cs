using System.Buffers;
using System.Text;
using System.Text.Json;

namespace JsonToCsv;

public static class JsonToCsvConverter
{
    /// <summary>
    /// Streaming conversion: reads the JSON incrementally with Utf8JsonReader and
    /// materializes one array element at a time, so memory use is bounded by the
    /// size of the largest single element (plus the read buffer) — not the size of
    /// the file. Works for a root array, or the first array-valued property of a
    /// root object. Returns the number of data rows written.
    /// </summary>
    public static int Convert(Stream jsonInput, TextWriter output, int initialBufferSize = 64 * 1024)
    {
        var buffer = new byte[Math.Max(initialBufferSize, 16)];
        int dataLength = jsonInput.Read(buffer, 0, buffer.Length);
        bool finalBlock = dataLength == 0;
        var reader = new Utf8JsonReader(buffer.AsSpan(0, dataLength), finalBlock, default);

        // Raw bytes of the element currently being read, when it spans buffer refills.
        var elementBytes = new ArrayBufferWriter<byte>();
        int captureFrom = -1; // >= 0 while inside an element: window offset where its uncaptured bytes begin

        // Discard consumed bytes (capturing any that belong to the current element),
        // refill from the stream — growing the buffer if a single token exceeds it —
        // and recreate the reader with its saved state.
        void Refill(ref Utf8JsonReader r)
        {
            int consumed = (int)r.BytesConsumed;
            if (captureFrom >= 0)
            {
                if (consumed > captureFrom)
                    elementBytes.Write(buffer.AsSpan(captureFrom, consumed - captureFrom));
                captureFrom = 0;
            }

            int leftover = dataLength - consumed;
            if (leftover == buffer.Length)
                Array.Resize(ref buffer, buffer.Length * 2);
            else if (consumed > 0)
                Array.Copy(buffer, consumed, buffer, 0, leftover);

            int read = jsonInput.Read(buffer, leftover, buffer.Length - leftover);
            finalBlock = read == 0;
            dataLength = leftover + read;
            r = new Utf8JsonReader(buffer.AsSpan(0, dataLength), finalBlock, r.CurrentState);
        }

        void ReadToken(ref Utf8JsonReader r)
        {
            while (!r.Read())
            {
                if (finalBlock) throw new JsonException("Unexpected end of JSON input.");
                Refill(ref r);
            }
        }

        // Consumes the rest of the value whose first token the reader is positioned on.
        void SkipValue(ref Utf8JsonReader r)
        {
            if (r.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
                return; // single-token value, already consumed

            int depth = 1;
            while (depth > 0)
            {
                ReadToken(ref r);
                if (r.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) depth++;
                else if (r.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) depth--;
            }
        }

        ReadToken(ref reader);

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Scan top-level properties for the first array value, skipping the rest.
            while (true)
            {
                ReadToken(ref reader);
                if (reader.TokenType == JsonTokenType.EndObject)
                    throw new InvalidOperationException("Root object contains no array property.");

                ReadToken(ref reader); // move from property name onto its value
                if (reader.TokenType == JsonTokenType.StartArray)
                    break;
                SkipValue(ref reader);
            }
        }
        else if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new InvalidOperationException("Root JSON value is neither an array nor an object.");
        }

        var rows = new CsvRowWriter(output);
        while (true)
        {
            ReadToken(ref reader);
            if (reader.TokenType == JsonTokenType.EndArray)
                break;

            // Walk over the element token-by-token, capturing its raw bytes,
            // then parse just that element into a short-lived JsonDocument.
            elementBytes.Clear();
            captureFrom = (int)reader.TokenStartIndex;
            SkipValue(ref reader);
            int end = (int)reader.BytesConsumed;

            JsonDocument element;
            if (elementBytes.WrittenCount == 0)
            {
                // Element fits entirely inside the current buffer window — parse in place.
                element = JsonDocument.Parse(new ReadOnlyMemory<byte>(buffer, captureFrom, end - captureFrom));
            }
            else
            {
                elementBytes.Write(buffer.AsSpan(captureFrom, end - captureFrom));
                element = JsonDocument.Parse(elementBytes.WrittenMemory);
            }
            captureFrom = -1;

            using (element)
                rows.WriteRow(element.RootElement);
        }

        return rows.Count;
    }

    /// <summary>In-memory conversion for JSON already held as a string.</summary>
    public static int Convert(string json, TextWriter output)
    {
        using var doc = JsonDocument.Parse(json);
        return Convert(doc.RootElement, output);
    }

    /// <summary>In-memory conversion for an already-parsed element.</summary>
    public static int Convert(JsonElement root, TextWriter output)
    {
        JsonElement arrayElement = root.ValueKind == JsonValueKind.Array
            ? root
            : root.EnumerateObject()
                .First(p => p.Value.ValueKind == JsonValueKind.Array).Value;

        var rows = new CsvRowWriter(output);
        foreach (var item in arrayElement.EnumerateArray())
            rows.WriteRow(item);

        return rows.Count;
    }

    private sealed class CsvRowWriter
    {
        private readonly TextWriter _output;
        private readonly Dictionary<string, JsonElement> _lookup = new();
        private readonly StringBuilder _sb = new(128);
        private string[]? _headers;

        public int Count { get; private set; }

        public CsvRowWriter(TextWriter output) => _output = output;

        public void WriteRow(JsonElement item)
        {
            if (_headers is null)
            {
                _headers = item.EnumerateObject().Select(p => p.Name).ToArray();
                for (int i = 0; i < _headers.Length; i++)
                {
                    if (i > 0) _output.Write(';');
                    WriteField(_headers[i]);
                }
                _output.WriteLine();
            }

            _lookup.Clear();
            foreach (var p in item.EnumerateObject())
                _lookup[p.Name] = p.Value;

            for (int i = 0; i < _headers.Length; i++)
            {
                if (i > 0) _output.Write(';');
                if (!_lookup.TryGetValue(_headers[i], out var val))
                    continue;

                WriteField(val.ValueKind switch
                {
                    JsonValueKind.Null => "",
                    JsonValueKind.String => val.GetString()!,
                    _ => val.GetRawText()
                });
            }
            _output.WriteLine();
            Count++;
        }

        private void WriteField(string str)
        {
            if (str.AsSpan().IndexOfAny(';', '"', '\n') < 0 && !str.Contains('\r'))
            {
                _output.Write(str);
                return;
            }

            _sb.Clear();
            _sb.Append('"');
            foreach (char c in str)
            {
                if (c == '"') _sb.Append('"');
                _sb.Append(c);
            }
            _sb.Append('"');
            _output.Write(_sb);
        }
    }
}
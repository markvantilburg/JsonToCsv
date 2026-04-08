# JsonToCsv

Command-line tool to convert a JSON file to a semicolon-separated CSV file.

## Usage

```
JsonToCsv <input.json> <output.csv>
```

## Example

```
JsonToCsv file.json file.csv
```

## Input format

Supports both a bare JSON array and an object containing an array property:

```json
[
  { "Name": "Alice", "Age": 30 },
  { "Name": "Bob", "Age": 25 }
]
```

```json
{
  "results": [
    { "Name": "Alice", "Age": 30 },
    { "Name": "Bob", "Age": 25 }
  ]
}
```

## Output format

- First row contains the field names as headers
- Fields are separated by `;`
- Values containing `;` or `"` are quoted
- `null` values are written as empty strings

## Requirements

- .NET 10 or later

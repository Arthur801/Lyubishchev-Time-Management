using System.Text;

namespace Lyubishchev_Time_Management.Infrastructure.Csv;

// Stateless RFC 4180 encoder: UTF-8 BOM (for Excel), CRLF records, comma delimiters, and
// spreadsheet-formula neutralization for values a viewer could otherwise execute as a formula.
// Callers pass already-formatted display strings; this type knows nothing about EF Core,
// ownership, time zones, or HTTP.
public static class CsvWriter
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var builder = new StringBuilder();
        AppendRecord(builder, headers);
        foreach (var row in rows)
        {
            AppendRecord(builder, row);
        }

        // Encoding.GetBytes never emits a BOM, even for a UTF8Encoding constructed with
        // encoderShouldEmitUTF8Identifier: true -- that flag only affects GetPreamble(), which is
        // normally consumed by a StreamWriter. Prepending it manually is what actually puts the
        // BOM at the start of these in-memory bytes.
        var preamble = Utf8WithBom.GetPreamble();
        var body = Utf8WithBom.GetBytes(builder.ToString());
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static void AppendRecord(StringBuilder builder, IReadOnlyList<string?> fields)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendField(builder, fields[index] ?? string.Empty);
        }

        builder.Append("\r\n");
    }

    private static void AppendField(StringBuilder builder, string value)
    {
        var safeValue = NeutralizeFormula(value);
        var requiresQuotes = safeValue.IndexOfAny([',', '\"', '\r', '\n']) >= 0;
        if (requiresQuotes)
        {
            builder.Append('\"');
        }

        builder.Append(safeValue.Replace("\"", "\"\"", StringComparison.Ordinal));

        if (requiresQuotes)
        {
            builder.Append('\"');
        }
    }

    private static string NeutralizeFormula(string value)
    {
        var firstNonWhitespace = value.FirstOrDefault(character => !char.IsWhiteSpace(character));
        return firstNonWhitespace is '=' or '+' or '-' or '@' ? $"'{value}" : value;
    }
}

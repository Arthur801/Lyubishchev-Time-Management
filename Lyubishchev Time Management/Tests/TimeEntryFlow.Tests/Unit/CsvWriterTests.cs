using System.Text;
using Lyubishchev_Time_Management.Infrastructure.Csv;
using Xunit;

namespace TimeEntryFlow.Tests.Unit;

public sealed class CsvWriterTests
{
    [Fact]
    public void Write_emits_utf8_bom_header_and_crlf_terminated_records()
    {
        var bytes = CsvWriter.Write(
            ["Date", "Name"],
            [["2026-09-17", "Focus"]]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Equal("Date,Name\r\n2026-09-17,Focus\r\n", Encoding.UTF8.GetString(bytes)[1..]);
    }

    [Fact]
    public void Write_quotes_rfc4180_special_characters_and_neutralizes_formulas()
    {
        var bytes = CsvWriter.Write(
            ["Name", "Notes"],
            [["=SUM(A1:A2)", "one, \"two\"\r\nthree"], ["  @danger", null]]);

        Assert.Equal(
            "Name,Notes\r\n'=SUM(A1:A2),\"one, \"\"two\"\"\r\nthree\"\r\n'  @danger,\r\n",
            Encoding.UTF8.GetString(bytes)[1..]);
    }

    [Fact]
    public void Write_returns_header_only_when_there_are_no_rows()
    {
        var bytes = CsvWriter.Write(["Date", "Name"], []);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Equal("Date,Name\r\n", Encoding.UTF8.GetString(bytes)[1..]);
    }
}

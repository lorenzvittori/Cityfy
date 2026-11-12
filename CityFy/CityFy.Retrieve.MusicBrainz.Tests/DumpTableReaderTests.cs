using CityFy.Retrieve.MusicBrainz.Services;

namespace CityFy.Retrieve.MusicBrainz.Tests;

public sealed class DumpTableReaderTests
{
    [Theory]
    [InlineData("Daft Punk", "Daft Punk")]
    [InlineData(@"AC\\DC", @"AC\DC")]
    [InlineData(@"a\tb", "a\tb")]
    [InlineData(@"line\nbreak", "line\nbreak")]
    public void Unescape_DecodesCopyEscapes(string raw, string expected)
    {
        Assert.Equal(expected, DumpTableReader.Unescape(raw));
    }

    [Fact]
    public void ReadRows_SplitsOnTabsOnly_AndSkipsEmptyLines()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, ["1\tDaft Punk\t\\N", "", "2\tThe Beatles\tx"]);

            var rows = DumpTableReader.ReadRows(path, TestContext.Current.CancellationToken).ToList();

            Assert.Equal(2, rows.Count);
            Assert.Equal(["1", "Daft Punk", @"\N"], rows[0]);
            Assert.True(DumpTableReader.IsNull(rows[0][2]));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

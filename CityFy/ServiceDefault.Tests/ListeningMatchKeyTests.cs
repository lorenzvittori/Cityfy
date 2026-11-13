using ServiceDefault.Listening;

namespace ServiceDefault.Tests;

/// <summary>The key that recognises the same play written by Spotify and by Last.fm.</summary>
public sealed class ListeningMatchKeyTests
{
    [Theory]
    [InlineData("Björk", "Jóga", "bjork|joga")]
    [InlineData("  Radiohead ", "Paranoid Android", "radiohead|paranoid android")]
    [InlineData("Simon & Garfunkel", "The Boxer", "simon and garfunkel|the boxer")]
    [InlineData("AC/DC", "Back In Black", "ac dc|back in black")]
    [InlineData("Daft Punk", "Get Lucky (feat. Pharrell Williams)", "daft punk|get lucky")]
    [InlineData("Daft Punk", "Get Lucky [with Pharrell]", "daft punk|get lucky")]
    [InlineData("Daft Punk", "Get Lucky ft. Pharrell", "daft punk|get lucky")]
    [InlineData("Muse", "Uprising - Live", "muse|uprising live")]
    public void SameSongWrittenDifferently_HasTheSameKey(string artist, string track, string expected) =>
        Assert.Equal(expected, ListeningEvent.BuildMatchKey(artist, track));

    [Theory]
    [InlineData(null, "Track")]
    [InlineData("Artist", null)]
    [InlineData(" ", "Track")]
    [InlineData("!!!", "???")]
    public void MissingNames_HaveNoKey(string? artist, string? track) =>
        Assert.Null(ListeningEvent.BuildMatchKey(artist, track));

    [Fact]
    public void FeaturingInsideTheTitle_IsNotRemoved()
    {
        // "with" only counts in brackets: it is part of many titles
        Assert.Equal("u2|with or without you", ListeningEvent.BuildMatchKey("U2", "With or Without You"));
    }
}

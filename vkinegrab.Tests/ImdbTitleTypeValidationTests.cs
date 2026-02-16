using vkinegrab.Services.Imdb;

namespace vkinegrab.Tests;

public class ImdbTitleTypeValidationTests
{
    [Theory]
    [InlineData("Movie", true)]
    [InlineData("TVMovie", true)]
    [InlineData("TVSpecial", true)]
    [InlineData("TVMiniSeries", true)]
    [InlineData("Short", true)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("PodcastSeries", false)]
    [InlineData("PodcastEpisode", false)]
    [InlineData("TVSeries", false)]
    [InlineData("TVEpisode", false)]
    [InlineData("VideoGame", false)]
    [InlineData("MusicVideoObject", false)]
    public void IsTitleTypeAcceptable_ReturnsExpected(string? titleType, bool expected)
    {
        var result = ImdbMetadataValidator.IsTitleTypeAcceptable(titleType);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("movie", true)]
    [InlineData("podcastseries", false)]
    [InlineData("TVSERIES", false)]
    [InlineData("tvepisode", false)]
    public void IsTitleTypeAcceptable_IsCaseInsensitive(string titleType, bool expected)
    {
        var result = ImdbMetadataValidator.IsTitleTypeAcceptable(titleType);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SomeNewType")]
    [InlineData("Documentary")]
    [InlineData("CreativeWork")]
    public void IsTitleTypeAcceptable_UnknownTypes_ArePermissive(string titleType)
    {
        // Unknown types not in the rejected set should be accepted (permissive)
        Assert.True(ImdbMetadataValidator.IsTitleTypeAcceptable(titleType));
    }

    [Fact]
    public void PodcastSeries_IsRejected_PreventsIncorrectMatch()
    {
        // This is the specific scenario: "Čaroděj z Kremlu" matches a podcast on IMDB.
        // A PodcastSeries result should be rejected by the validator.
        Assert.False(ImdbMetadataValidator.IsTitleTypeAcceptable("PodcastSeries"));
    }
}

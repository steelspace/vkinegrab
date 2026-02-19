using vkinegrab.Models;
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

    #region Year Validation with Search Result Fallback

    [Fact]
    public void IsYearValid_ExactMatch_ReturnsTrue()
    {
        var validator = CreateValidator();
        Assert.True(validator.IsYearValid("1995", "1995"));
    }

    [Fact]
    public void IsYearValid_OneYearTolerance_ReturnsTrue()
    {
        var validator = CreateValidator();
        Assert.True(validator.IsYearValid("1995", "1996"));
        Assert.True(validator.IsYearValid("1995", "1994"));
    }

    [Fact]
    public void IsYearValid_ThreeYearGap_WithoutSearchResult_ReturnsFalse()
    {
        var validator = CreateValidator();
        Assert.False(validator.IsYearValid("1995", "1998"));
    }

    [Fact]
    public void IsYearValid_ThreeYearGap_WithMatchingSearchResultYear_ReturnsTrue()
    {
        // This is the Fallen Angels scenario: CSFD=1995, IMDB datePublished=1998, search result=1995
        var validator = CreateValidator();
        Assert.True(validator.IsYearValid("1995", "1998", searchResultYear: "1995"));
    }

    [Fact]
    public void IsYearValid_NullImdbYear_WithMatchingSearchResultYear_ReturnsTrue()
    {
        var validator = CreateValidator();
        Assert.True(validator.IsYearValid("1995", null, searchResultYear: "1995"));
    }

    [Fact]
    public void IsYearValid_NullImdbYear_NullSearchResult_ReturnsFalse()
    {
        var validator = CreateValidator();
        Assert.False(validator.IsYearValid("1995", null, searchResultYear: null));
    }

    [Fact]
    public void IsYearValid_NullMovieYear_ReturnsTrue()
    {
        var validator = CreateValidator();
        Assert.True(validator.IsYearValid(null, "1998"));
        Assert.True(validator.IsYearValid(null, "1998", searchResultYear: "1995"));
    }

    #endregion

    #region Director Name Order-Independent Matching

    [Fact]
    public void AreDirectorsValid_SameNameDifferentOrder_ReturnsTrue()
    {
        // "Kar-wai Wong" (Western order) vs "Wong Kar-wai" (Eastern order)
        var validator = CreateValidator();
        var movieDirectors = new List<string> { "Kar-wai Wong" };
        var imdbDirectors = new List<string> { "Wong Kar-wai" };
        Assert.True(validator.AreDirectorsValid(movieDirectors, imdbDirectors));
    }

    [Fact]
    public void AreDirectorsValid_SameNameSameOrder_ReturnsTrue()
    {
        var validator = CreateValidator();
        var movieDirectors = new List<string> { "Christopher Nolan" };
        var imdbDirectors = new List<string> { "Christopher Nolan" };
        Assert.True(validator.AreDirectorsValid(movieDirectors, imdbDirectors));
    }

    [Fact]
    public void AreDirectorsValid_DifferentPeople_ReturnsFalse()
    {
        var validator = CreateValidator();
        var movieDirectors = new List<string> { "Kar-wai Wong" };
        var imdbDirectors = new List<string> { "Martin Scorsese" };
        Assert.False(validator.AreDirectorsValid(movieDirectors, imdbDirectors));
    }

    [Fact]
    public void AreDirectorsValid_NullMovieDirectors_ReturnsTrue()
    {
        var validator = CreateValidator();
        Assert.True(validator.AreDirectorsValid(null, new List<string> { "Some Director" }));
    }

    [Fact]
    public void AreDirectorsValid_EmptyMovieDirectors_ReturnsTrue()
    {
        var validator = CreateValidator();
        Assert.True(validator.AreDirectorsValid(new List<string>(), new List<string> { "Some Director" }));
    }

    [Fact]
    public void AreDirectorsValid_EmptyImdbDirectors_ReturnsFalse()
    {
        var validator = CreateValidator();
        Assert.False(validator.AreDirectorsValid(new List<string> { "Some Director" }, new List<string>()));
    }

    [Fact]
    public void AreDirectorsValid_MultipleDirectors_DifferentOrder_ReturnsTrue()
    {
        var validator = CreateValidator();
        var movieDirectors = new List<string> { "Kar-wai Wong", "Yimou Zhang" };
        var imdbDirectors = new List<string> { "Wong Kar-wai", "Zhang Yimou" };
        Assert.True(validator.AreDirectorsValid(movieDirectors, imdbDirectors));
    }

    [Fact]
    public void AreDirectorsValid_WithDiacritics_ReturnsTrue()
    {
        var validator = CreateValidator();
        var movieDirectors = new List<string> { "René Clément" };
        var imdbDirectors = new List<string> { "René Clément" };
        Assert.True(validator.AreDirectorsValid(movieDirectors, imdbDirectors));
    }

    #endregion

    #region Fuzzy Director Matching (Transliteration)

    [Fact]
    public void AreDirectorsValid_CzechVsEnglishTransliteration_ReturnsTrue()
    {
        // "Hajao Mijazaki" (Czech) vs "Hayao Miyazaki" (English) — j↔y transliteration
        var validator = CreateValidator();
        var movieDirectors = new List<string> { "Hajao Mijazaki" };
        var imdbDirectors = new List<string> { "Hayao Miyazaki" };
        Assert.True(validator.AreDirectorsValid(movieDirectors, imdbDirectors));
    }

    [Fact]
    public void AreDirectorsValid_CzechVsEnglishTransliteration_DifferentWordOrder_ReturnsTrue()
    {
        // "Tacuja Jošihara" (Czech) vs "Tatsuya Yoshihara" (English)
        // Both transliteration AND word-initial letter changes (j→y, c→ts)
        var validator = CreateValidator();
        var movieDirectors = new List<string> { "Tacuja Jošihara" };
        var imdbDirectors = new List<string> { "Tatsuya Yoshihara" };
        Assert.True(validator.AreDirectorsValid(movieDirectors, imdbDirectors));
    }

    [Fact]
    public void AreDirectorsValid_CompletelyDifferentNames_ReturnsFalse()
    {
        // Totally different names should not pass fuzzy matching
        var validator = CreateValidator();
        var movieDirectors = new List<string> { "Hajao Mijazaki" };
        var imdbDirectors = new List<string> { "Steven Spielberg" };
        Assert.False(validator.AreDirectorsValid(movieDirectors, imdbDirectors));
    }

    [Theory]
    [InlineData("hajao mijazaki", "hayao miyazaki", 0.85)] // 2 char diff in 15 chars
    [InlineData("christopher nolan", "christopher nolan", 1.0)]
    [InlineData("totally different", "xyzabc uvwdef", 0.0)]
    public void NameSimilarity_ReturnsExpectedRange(string a, string b, double minExpected)
    {
        var similarity = ImdbMetadataValidator.NameSimilarity(a, b);
        Assert.True(similarity >= minExpected, $"Expected >= {minExpected}, got {similarity}");
    }

    [Fact]
    public void NameSimilarity_IdenticalNames_ReturnsOne()
    {
        Assert.Equal(1.0, ImdbMetadataValidator.NameSimilarity("test", "test"));
    }

    [Fact]
    public void NameSimilarity_EmptyStrings_ReturnsOne()
    {
        Assert.Equal(1.0, ImdbMetadataValidator.NameSimilarity("", ""));
    }

    [Fact]
    public void BestPermutationSimilarity_WordOrderAndTransliteration_HighSimilarity()
    {
        // "tacuja josihara" vs "tatsuya yoshihara" — sorted would pair words incorrectly
        // but permutation matching should find "tacuja"↔"tatsuya" and "josihara"↔"yoshihara"
        var similarity = ImdbMetadataValidator.BestPermutationSimilarity("tacuja josihara", "tatsuya yoshihara");
        Assert.True(similarity >= 0.70, $"Expected >= 0.70, got {similarity}");
    }

    [Fact]
    public void BestPermutationSimilarity_IdenticalNames_ReturnsOne()
    {
        Assert.Equal(1.0, ImdbMetadataValidator.BestPermutationSimilarity("hayao miyazaki", "hayao miyazaki"));
    }

    [Fact]
    public void BestPermutationSimilarity_ReversedOrder_ReturnsOne()
    {
        Assert.Equal(1.0, ImdbMetadataValidator.BestPermutationSimilarity("wong karwai", "karwai wong"));
    }

    [Fact]
    public void BestPermutationSimilarity_CompletelyDifferent_LowSimilarity()
    {
        var similarity = ImdbMetadataValidator.BestPermutationSimilarity("steven spielberg", "martin scorsese");
        Assert.True(similarity < 0.75, $"Expected < 0.75, got {similarity}");
    }

    #endregion

    private static ImdbMetadataValidator CreateValidator()
    {
        var handler = new HttpClientHandler();
        var client = new HttpClient(handler);
        var matcher = new ImdbTitleMatcher();
        return new ImdbMetadataValidator(client, matcher);
    }
}

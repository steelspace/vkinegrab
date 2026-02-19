using vkinegrab.Services.Imdb;

namespace vkinegrab.Tests;

public class CzechRomanizationConverterTests
{
    #region Japanese — Polivka → Hepburn

    [Theory]
    [InlineData("hajao mijazaki", "hayao miyazaki")]
    [InlineData("tacuja jošihara", "tatsuya yoshihara")]
    [InlineData("akira kurosawa", "akira kurosawa")] // No Czech chars — unchanged
    [InlineData("džiró taniguči", "jirō taniguchi")]
    [InlineData("rjúnosuke kamiki", "ryūnosuke kamiki")]
    [InlineData("šigejuki tocugi", "shigeyuki totsugi")]
    [InlineData("čihiro", "chihiro")]
    [InlineData("šógo", "shōgo")]
    [InlineData("júja", "yūya")]
    public void JapaneseToHepburn_ConvertsCorrectly(string czech, string expected)
    {
        var result = CzechRomanizationConverter.JapaneseToHepburn(czech);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void JapaneseToHepburn_HandlesEmptyInput(string? input)
    {
        var result = CzechRomanizationConverter.JapaneseToHepburn(input!);
        Assert.Equal(input, result);
    }

    #endregion

    #region Korean — Czech phonetic → Revised Romanization

    [Theory]
    [InlineData("pak čan-uk", "pak jan-uk")]
    [InlineData("pong džun-ho", "pong džun-ho")] // dž is Japanese, not Korean — stays unchanged
    public void KoreanToRevised_ConvertsCorrectly(string czech, string expected)
    {
        var result = CzechRomanizationConverter.KoreanToRevised(czech);
        Assert.Equal(expected, result);
    }

    #endregion

    #region TransliterateToEnglish — auto-detect

    [Theory]
    [InlineData("hajao mijazaki", "hajao mijazaki")]      // Pure ASCII — returned unchanged
    [InlineData("tacuja jošihara", "tatsuya yoshihara")]   // Japanese (has š diacritic)
    [InlineData("christopher nolan", "christopher nolan")] // Western name — no change
    [InlineData("martin scorsese", "martin scorsese")]     // Western name — no change
    public void TransliterateToEnglish_SelectsBestMatch(string input, string expected)
    {
        var result = CzechRomanizationConverter.TransliterateToEnglish(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData(null)]
    public void TransliterateToEnglish_HandlesEmptyInput(string? input)
    {
        var result = CzechRomanizationConverter.TransliterateToEnglish(input!);
        Assert.Equal(input, result);
    }

    #endregion

    #region Integration with Director Matching

    [Fact]
    public void Transliteration_MakesMiyazakiMatchExact()
    {
        // After transliteration, "hajao mijazaki" should become "hayao miyazaki"
        // which means strict matching should succeed (no fuzzy needed)
        var czech = CzechRomanizationConverter.JapaneseToHepburn("hajao mijazaki");
        var english = "hayao miyazaki"; // IMDB already in Hepburn — JapaneseToHepburn is identity
        Assert.Equal(english, czech);
    }

    [Fact]
    public void Transliteration_MakesYoshiharaMatchExact()
    {
        var czech = CzechRomanizationConverter.JapaneseToHepburn("tacuja jošihara");
        var english = CzechRomanizationConverter.JapaneseToHepburn("tatsuya yoshihara");
        Assert.Equal(english, czech);
    }

    [Fact]
    public void WesternNames_UnchangedByTransliteration()
    {
        // Western names should pass through unmodified
        Assert.Equal("christopher nolan", CzechRomanizationConverter.TransliterateToEnglish("christopher nolan"));
        Assert.Equal("wong kar-wai", CzechRomanizationConverter.TransliterateToEnglish("wong kar-wai"));
        Assert.Equal("steven spielberg", CzechRomanizationConverter.TransliterateToEnglish("steven spielberg"));
    }

    #endregion
}

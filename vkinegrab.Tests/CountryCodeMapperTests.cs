using vkinegrab.Services;
using Xunit;

namespace vkinegrab.Tests;

public class CountryCodeMapperTests
{
    [Fact]
    public void MapToIsoAlpha2_MapsCzechNamesAndHistoricalNames()
    {
        var input = new[] { "Česko", "Československo", "Rakousko-Uhersko", "SSSR" };

        var mapped = CountryCodeMapper.MapToIsoAlpha2(input);

        Assert.Equal(new[] { "CZ", "CS", "AH", "SU" }, mapped);
    }

    [Fact]
    public void MapToIsoAlpha2_PreservesExistingCodesAndDeduplicates()
    {
        var input = new[] { "US", "usa", "Spojené státy", "US" };

        var mapped = CountryCodeMapper.MapToIsoAlpha2(input);

        Assert.Equal(new[] { "US" }, mapped);
    }

    [Fact]
    public void MapToIsoAlpha2_MapsRequestedHistoricalExamples()
    {
        var input = new[] { "Sovětský svaz", "Rakousko-Uhersko", "Německé císařství" };

        var mapped = CountryCodeMapper.MapToIsoAlpha2(input);

        Assert.Equal(new[] { "SU", "AH", "DE" }, mapped);
    }

    [Fact]
    public void MapToIsoAlpha2_MapsEnglishHistoricalAliases()
    {
        var input = new[] { "Soviet Union", "Austria Hungary", "German Empire", "Yugoslavia", "East Germany" };

        var mapped = CountryCodeMapper.MapToIsoAlpha2(input);

        Assert.Equal(new[] { "SU", "AH", "DE", "YU", "DD" }, mapped);
    }

    [Fact]
    public void MapToIsoAlpha2_MapsCzechCountryNamesFromDatabaseReport()
    {
        var input = new[] { "Singapur", "Nepál", "Indonésie", "Uganda" };

        var mapped = CountryCodeMapper.MapToIsoAlpha2(input);

        Assert.Equal(new[] { "SG", "NP", "ID", "UG" }, mapped);
    }
}

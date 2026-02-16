namespace vkinegrab.Services.Imdb.Models;

internal sealed record ImdbTitleMetadata(string? Year, IReadOnlyList<string> Directors, double? Rating = null, int? RatingCount = null, string? TitleType = null);

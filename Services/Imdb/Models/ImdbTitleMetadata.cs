namespace vkinegrab.Services.Imdb.Models;

internal sealed record ImdbTitleMetadata(string? Year, IReadOnlyList<string> Directors);

using System;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace vkinegrab.Services.Csfd;

public static class CsfdServiceCollectionExtensions
{
    public static IServiceCollection AddCsfdParsers(this IServiceCollection services)
    {
        // parsers and helpers
        services.AddSingleton<IBadgeExtractor, BadgeExtractor>();
        services.AddSingleton<IShowtimeExtractor, ShowtimeExtractor>();
        services.AddSingleton<ICsfdRowParser, CsfdRowParser>();

        // Register PerformancesService as a typed HTTP client so the HttpClient is configured centrally.
        services.AddHttpClient<IPerformancesService, PerformancesService>(client =>
        {
            client.BaseAddress = new Uri("https://www.csfd.cz/");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "vkinegrab/1.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "cs-CZ,cs;q=0.9,en;q=0.8");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        });

        return services;
    }
}
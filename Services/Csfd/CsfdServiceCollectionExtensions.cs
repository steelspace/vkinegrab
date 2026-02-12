using System;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Polly;
using Polly.Extensions.Http;

namespace vkinegrab.Services.Csfd;

public static class CsfdServiceCollectionExtensions
{
    public static IServiceCollection AddCsfdServices(this IServiceCollection services, string tmdbBearerToken)
    {
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

        services.AddHttpClient("Csfd")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All
            })
            .ConfigureHttpClient(client =>
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            })
            .AddPolicyHandler(retryPolicy);

        services.AddHttpClient("Tmdb")
            .AddPolicyHandler(retryPolicy)
            .AddPolicyHandler(circuitBreakerPolicy);

        services.AddSingleton<IBadgeExtractor, BadgeExtractor>();
        services.AddSingleton<IShowtimeExtractor, ShowtimeExtractor>();
        services.AddSingleton<ICsfdRowParser, CsfdRowParser>();
        services.AddSingleton<IPerformancesService, PerformancesService>();
        services.AddSingleton<ICsfdScraper>(sp => 
            new CsfdScraper(sp.GetRequiredService<IHttpClientFactory>(), tmdbBearerToken));
        services.AddSingleton<IMovieMetadataOrchestrator, MovieMetadataOrchestrator>();
        services.AddSingleton<SchedulesStoreService>();
        services.AddSingleton<MovieCollectorService>();

        return services;
    }
}
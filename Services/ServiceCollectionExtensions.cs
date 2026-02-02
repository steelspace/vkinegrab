using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using vkinegrab.Services.Csfd;
using vkinegrab.Services.Imdb;
using vkinegrab.Services.Tmdb;

namespace vkinegrab.Services
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers core vkinegrab services including database, CSFD scrapers and TMDB/IMDb resolvers.
        /// </summary>
        public static IServiceCollection AddVkinegrabServices(this IServiceCollection services, string mongoConnectionString, string tmdbBearerToken)
        {
            // Parsers and performances
            services.AddCsfdParsers();

            // Database
            services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
            services.AddScoped<IDatabaseService, DatabaseService>();

            // TMDB resolver (requires bearer token)
            services.AddHttpClient<ITmdbResolver, TmdbResolver>(client =>
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tmdbBearerToken);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });

            // IMDb resolver
            services.AddHttpClient<IImdbResolver, ImdbResolver>();

            // CSFD scraper - configure reasonable defaults (user-agent, accept) and automatic decompression
            services.AddHttpClient<ICsfdScraper, CsfdScraper>(client =>
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });

            // Other services
            services.AddScoped<MovieCollectorService>();
            services.AddScoped<SchedulesStoreService>();

            // Test utilities
            services.AddHttpClient<TestScraper>(client =>
            {
                // Use a reasonable user-agent when hitting CSFD pages for tests
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "vkinegrab-test/1.0");
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            });

            return services;
        }
    }
}

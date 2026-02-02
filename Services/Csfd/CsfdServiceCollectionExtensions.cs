using System;
using Microsoft.Extensions.DependencyInjection;

namespace vkinegrab.Services.Csfd;

public static class CsfdServiceCollectionExtensions
{
    public static IServiceCollection AddCsfdParsers(this IServiceCollection services)
    {
        services.AddSingleton<IBadgeExtractor, BadgeExtractor>();
        services.AddSingleton<IShowtimeExtractor, ShowtimeExtractor>();
        services.AddSingleton<ICsfdRowParser, CsfdRowParser>();
        services.AddSingleton<IPerformancesService, PerformancesService>();
        return services;
    }
}
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using vkinegrab.Services;

class Program
{
    static async Task Main(string[] args)
    {
        // Check if running tests
        if (args.Length > 0 && args[0].Equals("test", StringComparison.OrdinalIgnoreCase))
        {
            var testArgs = args.Skip(1).ToArray();
            await vkinegrab.TestScraper.Run(testArgs);
            return;
        }

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        var tmdbBearerToken = configuration["Tmdb:BearerToken"];
        
        if (string.IsNullOrWhiteSpace(tmdbBearerToken))
        {
            Console.WriteLine("ERROR: TMDB Bearer Token not found!");
            Console.WriteLine("Please set it using:");
            Console.WriteLine("  dotnet user-secrets set \"Tmdb:BearerToken\" \"your-bearer-token-here\"");
            return;
        }

        var scraper = new CsfdScraper(tmdbBearerToken);
        var movieId = 1580037;

        if (args.Length > 0)
        {
            if (!int.TryParse(args[0], out movieId) || movieId <= 0)
            {
                Console.WriteLine("Please provide a valid numeric CSFD movie ID as the first argument.");
                return;
            }
        }
        
        try 
        {
            var movie = await scraper.ScrapeMovie(movieId);
            var tmdbMovie = await scraper.ResolveTmdb(movie);
    
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine($"CSFD SOURCE ID: {movieId}");
            Console.WriteLine($"ID: {movie.Id}");
            Console.WriteLine($"TITLE: {movie.Title}");
            Console.WriteLine($"YEAR: {movie.Year}");
            Console.WriteLine($"ORIGIN: {movie.Origin}");
            Console.WriteLine($"DURATION: {movie.Duration}");
            Console.WriteLine($"RATING: {movie.Rating}");
            Console.WriteLine($"GENRES: {string.Join(", ", movie.Genres)}");
            Console.WriteLine($"DIRECTORS: {string.Join(", ", movie.Directors)}");
            Console.WriteLine($"CAST (First 10): {string.Join(", ", movie.Cast.Take(10))}");
            Console.WriteLine($"POSTER: {movie.PosterUrl}");
            Console.WriteLine($"IMDB: {movie.ImdbUrl ?? "Not found"}");
            
            if (tmdbMovie != null)
            {
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine("TMDB INFO:");
                Console.WriteLine($"  ID: {tmdbMovie.Id}");
                Console.WriteLine($"  URL: {tmdbMovie.Url}");
                Console.WriteLine($"  TITLE: {tmdbMovie.Title}");
                Console.WriteLine($"  ORIGINAL TITLE: {tmdbMovie.OriginalTitle}");
                Console.WriteLine($"  RELEASE DATE: {tmdbMovie.ReleaseDate}");
                Console.WriteLine($"  RATING: {tmdbMovie.VoteAverage:F1}/10 ({tmdbMovie.VoteCount} votes)");
                Console.WriteLine($"  POPULARITY: {tmdbMovie.Popularity:F1}");
                Console.WriteLine($"  LANGUAGE: {tmdbMovie.OriginalLanguage}");
                Console.WriteLine($"  ADULT: {tmdbMovie.Adult}");
                Console.WriteLine($"  GENRE IDS: {string.Join(", ", tmdbMovie.GenreIds)}");
                Console.WriteLine($"  POSTER: {tmdbMovie.FullPosterUrl ?? "N/A"}");
                Console.WriteLine($"  BACKDROP: {tmdbMovie.FullBackdropUrl ?? "N/A"}");
                if (!string.IsNullOrWhiteSpace(tmdbMovie.Overview))
                {
                    Console.WriteLine($"  OVERVIEW: {tmdbMovie.Overview}");
                }
            }
            
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine("TITLES BY COUNTRY:");
            foreach (var kvp in movie.LocalizedTitles)
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }
            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine($"DESC: {movie.Description}");
            Console.WriteLine("--------------------------------------------------");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error occurred: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
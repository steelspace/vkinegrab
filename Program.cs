using System;
using System.Linq;
using System.Threading.Tasks;
using vkinegrab.Services;

class Program
{
    static async Task Main(string[] args)
    {
        var scraper = new CsfdScraper();
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
            var movie = await scraper.ScrapeMovieAsync(movieId);
    
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
using Microsoft.Playwright;

namespace vkinegrab.Services.Csfd;

public sealed class PlaywrightHtmlFetcher : IHtmlFetcher, IAsyncDisposable
{
    private IPlaywright? playwright;
    private IBrowser? browser;
    private IBrowserContext? context;
    private readonly SemaphoreSlim semaphore = new(1, 1);

    // Script that removes automation signals checked by bot-detection services
    private const string StealthScript = """
        Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
        Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3] });
        Object.defineProperty(navigator, 'languages', { get: () => ['cs-CZ', 'cs', 'en-US', 'en'] });
        window.chrome = { runtime: {} };
        """;

    public async Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        await EnsureContextAsync(cancellationToken).ConfigureAwait(false);

        var page = await context!.NewPageAsync().ConfigureAwait(false);
        try
        {
            Console.WriteLine($"[PlaywrightHtmlFetcher] Navigating to {uri}");
            var startTime = DateTime.UtcNow;

            await page.GotoAsync(uri.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.Load,
                Timeout = 30_000
            }).ConfigureAwait(false);

            var title = await page.TitleAsync().ConfigureAwait(false);

            if (IsBotChallengePage(title))
            {
                Console.WriteLine($"[PlaywrightHtmlFetcher] ⚠ Bot challenge detected ('{title}'), waiting for real content...");
                try
                {
                    await page.WaitForFunctionAsync(
                        "() => !['making sure', 'oh noes', 'checking'].some(t => document.title.toLowerCase().includes(t))",
                        null, new PageWaitForFunctionOptions { Timeout = 45_000 }).ConfigureAwait(false);
                    title = await page.TitleAsync().ConfigureAwait(false);
                    Console.WriteLine($"[PlaywrightHtmlFetcher] ✓ Challenge passed, page title: '{title}'");
                }
                catch (TimeoutException)
                {
                    Console.WriteLine($"[PlaywrightHtmlFetcher] ✗ Bot challenge timed out for {uri}");
                    throw;
                }
            }

            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Console.WriteLine($"[PlaywrightHtmlFetcher] Content ready in {elapsed:F0}ms");

            var html = await page.ContentAsync().ConfigureAwait(false);
            return html;
        }
        finally
        {
            await page.CloseAsync().ConfigureAwait(false);
        }
    }

    private async Task EnsureContextAsync(CancellationToken cancellationToken)
    {
        if (context != null) return;

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (context != null) return;

            playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    "--no-sandbox",
                    "--disable-dev-shm-usage"
                }
            }).ConfigureAwait(false);
            Console.WriteLine("[PlaywrightHtmlFetcher] Browser launched");

            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["Accept-Language"] = "cs-CZ,cs;q=0.9,en-US;q=0.8,en;q=0.7"
                },
                Locale = "cs-CZ",
            }).ConfigureAwait(false);
            await context.AddInitScriptAsync(StealthScript).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static bool IsBotChallengePage(string title)
    {
        var lower = title.ToLowerInvariant();
        return lower.Contains("making sure") || lower.Contains("oh noes") || lower.Contains("checking");
    }

    public async ValueTask DisposeAsync()
    {
        if (context != null) await context.DisposeAsync().ConfigureAwait(false);
        if (browser != null) await browser.DisposeAsync().ConfigureAwait(false);
        playwright?.Dispose();
        semaphore.Dispose();
    }
}

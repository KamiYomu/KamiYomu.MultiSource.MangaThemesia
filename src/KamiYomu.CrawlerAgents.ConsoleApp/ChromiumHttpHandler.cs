using System.Net.Http.Headers;

using PuppeteerSharp;

/// <summary>
/// 
/// </summary>
/// <param name="innerHandler"></param>
/// <param name="options"></param>
public sealed class ChromiumHandler : DelegatingHandler
{
    // Shared browser instance
    private static IBrowser? _browser;
    private static readonly SemaphoreSlim _browserInitLock = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Try Chromium first
        try
        {
            IBrowser? browser = await GetOrCreateBrowserAsync(cancellationToken);
            return browser is null
                ? await base.SendAsync(request, cancellationToken)
                : await HandleWithChromiumAsync(browser, request, cancellationToken);
        }
        catch
        {
            // Fallback to default handler
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private async Task<IBrowser?> GetOrCreateBrowserAsync(CancellationToken ct)
    {
        if (_browser != null && !_browser.IsClosed)
        {
            return _browser;
        }

        await _browserInitLock.WaitAsync(ct);
        try
        {
            if (_browser != null && !_browser.IsClosed)
            {
                return _browser;
            }

            // Ensure Chromium is downloaded
            BrowserFetcher fetcher = new();
            _ = await fetcher.DownloadAsync(BrowserTag.Stable);

            // Launch Chromium
            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args = [
                    "--disable-gpu",
                    "--no-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-extensions",
                    "--disable-background-networking",
                    "--disable-sync",
                    "--disable-translate",
                    "--hide-scrollbars",
                    "--metrics-recording-only",
                    "--mute-audio",
                    "--no-first-run",
                    "--safebrowsing-disable-auto-update"
                ]
            });

            return _browser;
        }
        catch
        {
            return null;
        }
        finally
        {
            _ = _browserInitLock.Release();
        }
    }

    private async Task<HttpResponseMessage> HandleWithChromiumAsync(
        IBrowser browser,
        HttpRequestMessage request,
        CancellationToken ct)
    {
        await using IPage page = await browser.NewPageAsync();
        request.Headers.IfNoneMatch.Clear();
        request.Headers.IfModifiedSince = null;
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
            MaxAge = TimeSpan.Zero
        };
        // Copy headers to Chromium
        if (request.Headers != null)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                await page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
                {
                    [header.Key] = string.Join(",", header.Value)
                });
            }
        }

        string url = request.RequestUri!.ToString();

        // Navigate
        IResponse response = await page.GoToAsync(url, new NavigationOptions
        {
            Timeout = 60_000,
            WaitUntil = [
                WaitUntilNavigation.DOMContentLoaded,
                WaitUntilNavigation.Load,
                WaitUntilNavigation.Networkidle0
                ],
            Referer = request.Headers.Referrer?.ToString()
        });

        string content = await page.GetContentAsync();

        // Build HttpResponseMessage
        HttpResponseMessage httpResponse = new(response.Status)
        {
            Content = new StringContent(content)
        };

        // Copy Chromium response headers
        foreach (KeyValuePair<string, string> h in response.Headers)
        {
            _ = httpResponse.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }

        return httpResponse;
    }

    public async Task<HttpResponseMessage?> TrySendAsync(
    HttpRequestMessage request,
    CancellationToken ct)
    {
        try
        {
            IBrowser? browser = await GetOrCreateBrowserAsync(ct);
            return browser == null ? null : await HandleWithChromiumAsync(browser, request, ct);
        }
        catch
        {
            return null;
        }
    }

}

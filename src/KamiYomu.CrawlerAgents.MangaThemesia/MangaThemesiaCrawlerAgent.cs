using System;

using HtmlAgilityPack;

using KamiYomu.CrawlerAgents.Core;
using KamiYomu.CrawlerAgents.Core.Catalog;
using KamiYomu.CrawlerAgents.Core.Catalog.Builders;
using KamiYomu.CrawlerAgents.Core.Catalog.Definitions;

using Page = KamiYomu.CrawlerAgents.Core.Catalog.Page;

namespace KamiYomu.CrawlerAgents.MangaThemesia;

public abstract class MangaThemesiaCrawlerAgent : AbstractCrawlerAgent, ICrawlerAgent
{
    private readonly Lazy<HttpClient> _lazyHttpClient;
    protected HttpClient HttpClient => _lazyHttpClient.Value;
    protected readonly string BaseUrl;
    protected readonly string MangaDir;

    public MangaThemesiaCrawlerAgent(IDictionary<string, object> options, string mangaDirectory = "/manga") : base(options)
    {
        string mirrorUrl = Options.TryGetValue("Mirror", out object? mirror) && mirror is string mirrorValue ? mirrorValue : throw new ArgumentNullException("Mirror", "Mirror Url is required");
        MangaDir = mangaDirectory;
        BaseUrl = mirrorUrl.TrimEnd('/');

        HttpClientHandler httpClientHandler = Options.TryGetValue("FlareSolverrHttpHandler", out object? value)
        && value is HttpClientHandler handler
        ? handler
        : new HttpClientHandler();

        _lazyHttpClient = new Lazy<HttpClient>(() => new HttpClient(httpClientHandler)
        {
            BaseAddress = new Uri(BaseUrl)
        });
        HttpClient.DefaultRequestHeaders.Add("Referer", $"{BaseUrl}/");
    }

    public void Dispose()
    {
        if (_lazyHttpClient.IsValueCreated)
        {
            HttpClient.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task<Uri> GetFaviconAsync(CancellationToken cancellationToken)
    {
        Uri favicon = new($"{BaseUrl}/favicon.ico");
        return favicon;
    }

    /// <inheritdoc/>
    public async Task<PagedResult<Manga>> SearchAsync(
    string titleName,
    PaginationOptions paginationOptions,
    CancellationToken cancellationToken)
    {
        string page = paginationOptions.ContinuationToken ?? "1";
        string url = $"{BaseUrl}{MangaDir}?title={titleName}&page={page}";

        string html = await HttpClient.GetStringAsync(url, cancellationToken);
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        List<Manga> list = SearchMangaParse(doc);


        // MangaThemesia always has next page until empty results
        bool hasNextPage = list.Count > 0;

        return PagedResultBuilder<Manga>.Create()
            .WithData(list)
            .WithPaginationOptions(
                hasNextPage
                    ? new PaginationOptions((int.Parse(page) + 1).ToString())
                    : null
            )
            .Build();
    }

    protected virtual List<Manga> SearchMangaParse(HtmlDocument doc)
    {
        HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes(".//div[contains(@class,'bsx')]//a");
        List<Manga> list = [];
        if (nodes != null)
        {
            foreach (HtmlNode a in nodes)
            {
                // Raw extracted values
                string mangaUrl = a.GetAttributeValue("href", "");
                string title = a.GetAttributeValue("title", "").Trim();
                string coverUrl = ExtractImage(a);
                string id = ExtractIdFromUrl(mangaUrl);

                // MangaThemesia search results do NOT include:
                // - summary
                // - genres
                // - release date
                // - status
                // So we leave them empty/default.
                string summary = string.Empty;
                string releaseDate = string.Empty;
                string status = string.Empty;
                string[] genres = [];

                // Build Manga using your builder
                Manga manga = MangaBuilder.Create()
                    .WithId(id)
                    .WithTitle(title)
                    .WithDescription(summary)
                    .WithWebsiteUrl(mangaUrl)
                    .WithCoverFileName(Path.GetFileName(coverUrl))
                    .WithCoverUrl(new Uri(coverUrl))
                    .WithTags(genres)
                    .WithReleaseStatus(ReleaseStatus.Unreleased)
                    .WithYear(0)
                    .WithIsFamilySafe(true)
                    .Build();

                list.Add(manga);
            }
        }
        return list;
    }


    /// <inheritdoc/>
    public async Task<Manga> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        Uri url = new(new Uri(BaseUrl), $"{MangaDir}/{id}");

        string html = await HttpClient.GetStringAsync(url, cancellationToken);
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        return MangaDetailsParse(id, url, doc);
    }

    protected virtual Manga MangaDetailsParse(string id, Uri url, HtmlDocument doc)
    {
        HtmlNode container =
                    doc.DocumentNode.SelectSingleNode("//div[contains(@class,'bigcontent')]") ??
                    doc.DocumentNode.SelectSingleNode("//div[contains(@class,'animefull')]") ??
                    doc.DocumentNode.SelectSingleNode("//div[contains(@class,'animefull')]");

        if (container == null)
        {
            return null;
        }

        // Extract fields
        string title = container.SelectSingleNode(".//h1")?.InnerText.Trim() ?? string.Empty;
        string description = ExtractDescription(container);
        string author = ExtractField(container, "Author") ?? string.Empty;
        string artist = ExtractField(container, "Artist") ?? string.Empty;
        string genresRaw = ExtractGenres(container);
        string coverUrl = ExtractImage(container);
        string statusRaw = ExtractField(container, "Status") ?? string.Empty;

        // Convert genres into list
        string[] genres = [.. genresRaw
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(g => g.Trim())];

        // Convert status
        ReleaseStatus releaseStatus = ParseStatus(statusRaw.ToLower());

        // Build final Manga object
        Manga manga = MangaBuilder.Create()
            .WithId(id)
            .WithTitle(title)
            .WithDescription(description)
            .WithWebsiteUrl(url.ToString())
            .WithCoverFileName(Path.GetFileName(coverUrl))
            .WithCoverUrl(new Uri(coverUrl))
            .WithTags(genres)
            .WithReleaseStatus(releaseStatus)
            .WithYear(0)
            .WithIsFamilySafe(!genres.Any(ComicHelper.IsGenreNotFamilySafe))
            .Build();

        return manga;
    }


    /// <inheritdoc/>
    public async Task<PagedResult<Chapter>> GetChaptersAsync(
    Manga manga,
    PaginationOptions paginationOptions,
    CancellationToken cancellationToken)
    {
        string url = manga.Id.StartsWith("http")
            ? manga.Id
            : $"{BaseUrl}{manga.Id}";

        string html = await HttpClient.GetStringAsync(url, cancellationToken);
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        List<Chapter> chapters = ChaptersParse(manga, doc);

        // MangaThemesia does NOT paginate chapters → always null continuation token
        PaginationOptions nextPage = null;

        return PagedResultBuilder<Chapter>.Create()
            .WithData(chapters)
            .WithPaginationOptions(nextPage)
            .Build();
    }


    protected virtual List<Chapter> ChaptersParse(Manga manga, HtmlDocument doc)
    {
        HtmlNodeCollection nodes =
                    doc.DocumentNode.SelectNodes("//div[contains(@class,'bxcl')]//li") ??
                    doc.DocumentNode.SelectNodes("//li[contains(@class,'chapter')]");

        List<Chapter> chapters = [];

        if (nodes != null)
        {
            foreach (HtmlNode li in nodes)
            {
                HtmlNode a = li.SelectSingleNode(".//a");
                if (a == null)
                {
                    continue;
                }

                string chapterId = a.GetAttributeValue("href", "");
                string title = a.InnerText.Trim();

                string uri = chapterId.StartsWith("http")
                    ? chapterId
                    : $"{BaseUrl}{chapterId}";

                decimal number = ExtractChapterNumber(title);

                ChapterBuilder chapterBuilder = ChapterBuilder.Create();

                chapterBuilder = chapterBuilder
                     .WithId(chapterId)
                     .WithTitle(title)
                     .WithParentManga(manga)
                     .WithVolume(0)
                     .WithTranslatedLanguage("en")
                     .WithNumber(number)
                     .WithUri(new Uri(uri));

                chapters.Add(chapterBuilder.Build());
            }
        }

        return chapters;
    }

    private decimal ExtractChapterNumber(string title)
    {
        // Match integers or decimals: 12, 12.5, 0.1, 3.50, etc.
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(title, @"\d+(\.\d+)?");

        if (!match.Success)
        {
            return 0;
        }

        // Use invariant culture to avoid locale issues (e.g., commas vs dots)
        return decimal.TryParse(match.Value, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out decimal number)
            ? number
            : 0;
    }



    /// <inheritdoc/>
    public async Task<IEnumerable<Page>> GetChapterPagesAsync(
    Chapter chapter,
    CancellationToken cancellationToken)
    {
        string url = chapter.Id.StartsWith("http")
            ? chapter.Id
            : $"{BaseUrl}{chapter.Id}";

        string html = await HttpClient.GetStringAsync(url, cancellationToken);
        HtmlDocument doc = new();
        doc.LoadHtml(html);

        List<Page> pages = [];

        HtmlNodeCollection imgs = doc.DocumentNode.SelectNodes("//div[@id='readerarea']//img");

        if (imgs != null)
        {
            int index = 0;

            foreach (HtmlNode img in imgs)
            {
                string imageUrl = ExtractImage(img);
                int pageNumber = index++;
                Page page = PageBuilder.Create()
                             .WithChapterId(chapter.Id)
                             .WithId(pageNumber.ToString())
                             .WithPageNumber(pageNumber)
                             .WithImageUrl(new Uri(ComicHelper.NormalizeUrl(new Uri(BaseUrl), imageUrl)))
                             .WithParentChapter(chapter)
                             .Build();

                pages.Add(page);
            }

            return pages;
        }

        // JSON fallback
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
            doc.DocumentNode.InnerHtml,
            "\"images\"\\s*:\\s*(\\[.*?])"
        );

        if (match.Success)
        {
            string json = match.Groups[1].Value;
            List<string>? arr = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);

            int index = 0;

            foreach (string imgUrl in arr)
            {
                int pageNumber = index++;
                Page page = PageBuilder.Create()
                             .WithChapterId(chapter.Id)
                             .WithId(pageNumber.ToString())
                             .WithPageNumber(pageNumber)
                             .WithImageUrl(new Uri(ComicHelper.NormalizeUrl(new Uri(BaseUrl), imgUrl)))
                             .WithParentChapter(chapter)
                             .Build();

                pages.Add(page);
            }
        }

        return pages;
    }

    protected virtual string ChapterListSelector()
    {
        return "#chapterlist li:not(:has(svg))";
    }

    protected virtual string ExtractImage(HtmlNode node)
    {
        HtmlNode imgNode = node;

        // If the node is <a>, look for an <img> inside it
        if (node.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            imgNode = node.SelectSingleNode(".//img");
            if (imgNode == null)
            {
                return string.Empty;
            }
        }

        // Try lazy-load attributes first
        string url =
            imgNode.GetAttributeValue("data-src", null) ??
            imgNode.GetAttributeValue("data-lazy-src", null) ??
            imgNode.GetAttributeValue("data-cfsrc", null) ??
            imgNode.GetAttributeValue("src", null);

        return url ?? string.Empty;
    }


    protected virtual string ExtractDescription(HtmlNode container)
    {
        HtmlNode desc =
            container.SelectSingleNode(".//div[contains(@class,'desc')]") ??
            container.SelectSingleNode(".//*[@itemprop='description']");

        return desc?.InnerText.Trim() ?? "";
    }

    protected virtual string ExtractField(HtmlNode container, string field)
    {
        HtmlNode node = container.SelectSingleNode(
            $".//*[contains(text(),'{field}')]/following-sibling::*"
        );

        return node?.InnerText.Trim();
    }

    protected virtual string ExtractGenres(HtmlNode container)
    {
        HtmlNodeCollection nodes = container.SelectNodes(".//a[contains(@href,'genre')]");
        return nodes == null ? "" : string.Join(", ", nodes.Select(n => n.InnerText.Trim()));
    }

    protected virtual ReleaseStatus ParseStatus(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ReleaseStatus.Continuing;
        }

        text = text.ToLowerInvariant();

        return text.Contains("ongoing")
            ? ReleaseStatus.Continuing
            : text.Contains("completed")
            ? ReleaseStatus.Completed
            : text.Contains("cancel")
            ? ReleaseStatus.Cancelled
            : text.Contains("hiatus") || text.Contains("hold") ? ReleaseStatus.OnHiatus : ReleaseStatus.Continuing;
    }
    protected virtual long ParseDate(string date)
    {
        return string.IsNullOrWhiteSpace(date)
            ? 0
            : DateTime.TryParse(date, out DateTime dt) ? new DateTimeOffset(dt).ToUnixTimeMilliseconds() : 0;
    }

    protected virtual string ExtractIdFromUrl(string url)
    {
        // Remove query string
        int q = url.IndexOf('?');
        if (q >= 0)
        {
            url = url[..q];
        }

        // Trim trailing slash
        url = url.TrimEnd('/');

        // Extract last segment
        int lastSlash = url.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < url.Length - 1)
        {
            return url[(lastSlash + 1)..];
        }

        return url; // fallback
    }

}

using System.Net;

using HtmlAgilityPack;

using KamiYomu.CrawlerAgents.Core;
using KamiYomu.CrawlerAgents.Core.Catalog;
using KamiYomu.CrawlerAgents.Core.Catalog.Builders;
using KamiYomu.CrawlerAgents.Core.Catalog.Definitions;

using Page = KamiYomu.CrawlerAgents.Core.Catalog.Page;

namespace KamiYomu.MultiSource.MangaThemesia;

public abstract class MangaThemesiaCrawlerAgent : AbstractCrawlerAgent, ICrawlerAgent
{
    private readonly Lazy<HttpClient> _lazyHttpClient;
    protected HttpClient HttpClient => _lazyHttpClient.Value;
    protected readonly string BaseUrl;
    protected readonly string MangaDir;
    protected virtual string ProjectPageString => "/project";

    public MangaThemesiaCrawlerAgent(IDictionary<string, object> options, string mangaDirectory = "/manga") : base(options)
    {
        string mirrorUrl = Options.TryGetValue("Mirror", out object? mirror) && mirror is string mirrorValue ? mirrorValue : throw new ArgumentNullException("Mirror", "Mirror Url is required");
        MangaDir = mangaDirectory;
        BaseUrl = mirrorUrl.TrimEnd('/');

        HttpClientHandler httpClientHandler =
            Options.TryGetValue("SmartCrawlerHttpHandler", out object? smartCrawler)
                && smartCrawler is HttpClientHandler h1 ? h1 :
            Options.TryGetValue("FlareSolverrHttpHandler", out object? flareSolverr)
                && flareSolverr is HttpClientHandler h2 ? h2 :
            Options.TryGetValue("ChromiumHttpHandler", out object? chromium)
                && chromium is HttpClientHandler h3 ? h3 :
            new HttpClientHandler();


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
    public virtual async Task<Uri> GetFaviconAsync(CancellationToken cancellationToken)
    {
        Uri favicon = new($"{BaseUrl}/favicon.ico");
        return favicon;
    }

    /// <inheritdoc/>
    public virtual async Task<PagedResult<Manga>> SearchAsync(
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
        HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes(SearchMangaSelector());
        List<Manga> list = [];
        if (nodes != null)
        {
            foreach (HtmlNode element in nodes)
            {
                // Extract image from img tag
                HtmlNode imgNode = element.SelectSingleNode(".//img");
                string coverUrl = imgNode != null ? ExtractImage(imgNode) : string.Empty;

                // Extract link and title
                HtmlNode linkNode = element.SelectSingleNode(".//a");
                if (linkNode == null)
                {
                    continue;
                }

                string mangaUrl = linkNode.GetAttributeValue("href", "");
                string title = linkNode.GetAttributeValue("title", "").Trim();
                if (string.IsNullOrEmpty(title))
                {
                    title = linkNode.InnerText.Trim();
                }

                string id = ExtractIdFromUrl(mangaUrl);

                // MangaThemesia search results do NOT include:
                // - summary
                // - genres
                // - release date
                // - status
                // So we leave them empty/default.
                string summary = string.Empty;
                string[] genres = [];

                // Build Manga using your builder
                Manga manga = MangaBuilder.Create()
                    .WithId(id)
                    .WithTitle(title)
                    .WithDescription(string.IsNullOrWhiteSpace(summary) ? "no description" : summary)
                    .WithWebsiteUrl(mangaUrl)
                    .WithCoverFileName(Path.GetFileName(coverUrl))
                    .WithCoverUrl(string.IsNullOrEmpty(coverUrl) ? null : new Uri(coverUrl))
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

    protected virtual string SearchMangaSelector()
    {
        // XPath equivalent of ".utao .uta .imgu, .listupd .bs .bsx, .listo .bs .bsx"
        return "//div[@class='utao']//div[@class='uta']//div[@class='imgu'] | //div[@class='listupd']//div[@class='bs']//div[@class='bsx'] | //div[@class='listo']//div[@class='bs']//div[@class='bsx']";
    }
    protected virtual string SearchMangaNextPageSelector()
    {
        return "//div[contains(@class,'pagination')]//*[contains(@class,'next')] | //div[contains(@class,'hpage')]//*[contains(@class,'r')]";
    }

    /// <inheritdoc/>
    public virtual async Task<Manga> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        Uri url = new(new Uri(BaseUrl), $"{MangaDir}/{id}");

        string html = await HttpClient.GetStringAsync(url, cancellationToken);
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        return MangaDetailsParse(id, url, doc);
    }

    protected virtual Manga MangaDetailsParse(string id, Uri url, HtmlDocument doc)
    {
        HtmlNode container = doc.DocumentNode.SelectSingleNode(SeriesDetailsSelector());

        if (container == null)
        {
            return null;
        }

        // Extract fields
        string title = container.SelectSingleNode(SeriesTitleSelector())?.InnerText.Trim() ?? string.Empty;
        string description = ExtractDescription(container);
        string author = ExtractFieldFromContainer(container, SeriesAuthorSelector()) ?? string.Empty;
        string artist = ExtractFieldFromContainer(container, SeriesArtistSelector()) ?? string.Empty;

        // Extract genres
        HtmlNodeCollection genreNodes = container.SelectNodes(SeriesGenreSelector());
        List<string> genres = [];
        if (genreNodes != null)
        {
            foreach (HtmlNode genreNode in genreNodes)
            {
                string genreText = genreNode.InnerText.Trim();
                if (!string.IsNullOrEmpty(genreText))
                {
                    genres.Add(genreText);
                }
            }
        }

        // Extract type and add to genres
        string typeText = ExtractFieldFromContainer(container, SeriesTypeSelector());
        if (!string.IsNullOrEmpty(typeText))
        {
            typeText = RemoveEmptyPlaceholder(typeText);
            if (!string.IsNullOrEmpty(typeText))
            {
                genres.Add(typeText);
            }
        }

        // Extract status
        string statusRaw = ExtractFieldFromContainer(container, SeriesStatusSelector()) ?? string.Empty;
        ReleaseStatus releaseStatus = ParseStatus(statusRaw);

        // Extract cover image
        string coverUrl = ExtractCoverImage(container);

        // Build final Manga object
        Manga manga = MangaBuilder.Create()
            .WithId(id)
            .WithTitle(title)
            .WithDescription(description)
            .WithWebsiteUrl(url.ToString())
            .WithCoverFileName(Path.GetFileName(coverUrl))
            .WithCoverUrl(string.IsNullOrEmpty(coverUrl) ? null : new Uri(coverUrl))
            .WithTags([.. genres])
            .WithReleaseStatus(releaseStatus)
            .WithYear(0)
            .WithIsFamilySafe(!genres.Any(ComicHelper.IsGenreNotFamilySafe))
            .Build();

        return manga;
    }

    protected virtual string SeriesDetailsSelector()
    {
        return "//div[contains(@class,'bigcontent')] | //div[contains(@class,'animefull')] | //div[contains(@class,'main-info')] | //div[contains(@class,'postbody')]";
    }

    protected virtual string SeriesTitleSelector()
    {
        return "//h1[contains(@class,'entry-title')] | //div[contains(@class,'ts-breadcrumb')]//li[last()]/span"
;
    }

    protected virtual string SeriesArtistSelector()
    {
        string[] keywords =
        [
            "artist",
            "Artiste",
            "Artista",
            "الرسام",
            "الناشر",
            "İllüstratör",
            "Çizer",
            "Sanatçı",
        ];

        string trSelector = BuildSelector(".//tr[contains(., '%s')]//td[last()]", keywords);
        string spanISelector = BuildSelector(".//span[contains(., '%s')]/following-sibling::i", keywords);
        string bSpanSelector = BuildSelector(".//b[contains(., '%s')]/following-sibling::span", keywords);
        string spanSelector = BuildSelector(".//span[contains(., '%s')]", keywords);

        return string.Join(" | ",
        [
            trSelector,
            spanISelector,
            bSpanSelector,
            spanSelector
        ]);
    }


    protected virtual string SeriesAuthorSelector()
    {
        string[] keywords =
        [
            "Author",
            "Auteur",
            "autor",
            "المؤلف",
            "Mangaka",
            "seniman",
            "Pengarang",
            "Yazar",
        ];

        string trSelector = BuildSelector(".//tr[contains(., '%s')]//td[last()]", keywords);
        string spanISelector = BuildSelector(".//span[contains(., '%s')]/following-sibling::i", keywords);
        string bSpanSelector = BuildSelector(".//b[contains(., '%s')]/following-sibling::span", keywords);
        string spanSelector = BuildSelector(".//span[contains(., '%s')]", keywords);

        return string.Join(" | ",
        [
            trSelector,
            spanISelector,
            bSpanSelector,
            spanSelector
        ]);
    }


    protected virtual string SeriesDescriptionSelector()
    {
        return "//*[contains(@class,'desc')] | //*[(contains(@class,'entry-content') and @itemprop='description')]";
    }

    protected virtual string SeriesAltNameSelector()
    {
        return
            "//*[contains(@class,'alternative')]"
            + " | //*[contains(@class,'alter')]"
            + " | //*[contains(@class,'seriestualt')]"
            + " | " +
            BuildSelector(
                "//tr[contains(., '%s')]//td[last()]",
                [
                    "Alternative",
                    "Alternatif",
                    "الأسماء الثانوية",
                ]
            );
    }



    protected virtual string SeriesGenreSelector()
    {
        return "//div[@class='gnr']//a | .//a[@class='mgen'] | .//a[@class='seriestugenre'] | " +
        BuildSelector(
            ".//span[contains(., '%s')]",
            [
                "genre",
                "التصنيف",
            ]
        );
    }

    protected virtual string SeriesTypeSelector()
    {
        string[] keywords =
        [
            "type",
            "ประเภท",
            "النوع",
            "tipe",
            "Türü",
        ];

        string trSelector = BuildSelector("//tr[contains(., '%s')]//td[last()]", keywords);
        string spanISelector = BuildSelector("//span[contains(., '%s')]/following-sibling::i", keywords);
        string aSelector = BuildSelector("//a[contains(., '%s')]", keywords);
        string bSpanSelector = BuildSelector("//b[contains(., '%s')]/following-sibling::span", keywords);
        string spanASelector = BuildSelector("//span[contains(., '%s')]//a", keywords);

        return string.Join(" | ",
        [
            trSelector,
            spanISelector,
            aSelector,
            bSpanSelector,
            spanASelector,
            "//a[contains(@href, 'type=')]"
        ]);
    }


    protected virtual string SeriesStatusSelector()
    {
        string[] keywords =
        [
            "status",
            "Statut",
            "Durum",
            "连載状況",
            "Estado",
            "الحالة",
            "حالة العمل",
            "สถานะ",
            "stato",
            "Statüsü",
        ];

        string trSelector = BuildSelector("//tr[contains(., '%s')]//td[last()]", keywords);
        string spanISelector = BuildSelector("//span[contains(., '%s')]/following-sibling::i", keywords);
        string bSpanSelector = BuildSelector("//b[contains(., '%s')]/following-sibling::span", keywords);
        string spanSelector = BuildSelector("//span[contains(., '%s')]", keywords);

        return string.Join(" | ",
        [
            trSelector,
            spanISelector,
            bSpanSelector,
            spanSelector
        ]);
    }


    protected virtual string SeriesThumbnailSelector()
    {
        return "//*[contains(@class,'infomanga')]//div[@itemprop='image']/img | //*[contains(@class,'thumb')]//img";
    }

    protected virtual string BuildSelector(string selectorTemplate, string[] keywords)
    {
        return string.Join(" | ", keywords.Select(keyword => selectorTemplate.Replace("%s", keyword)));
    }

    /// <inheritdoc/>
    public virtual async Task<PagedResult<Chapter>> GetChaptersAsync(
    Manga manga,
    PaginationOptions paginationOptions,
    CancellationToken cancellationToken)
    {
        Uri url = new(new Uri(BaseUrl), $"{MangaDir}/{manga.Id}");

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
        HtmlNodeCollection nodes = doc.DocumentNode.SelectNodes(ChapterListSelector());

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

    protected virtual string ChapterListSelector()
    {
        return "//div[@class='bxcl']//li | //div[@class='cl']//li | //*[@id='chapterlist']//li | //ul//li[div[@class='chbox'] and div[@class='eph-num']]";
    }

    protected virtual decimal ExtractChapterNumber(string title)
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
    public virtual async Task<IEnumerable<Page>> GetChapterPagesAsync(
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

        HtmlNodeCollection imgs = doc.DocumentNode.SelectNodes(PageSelector());

        if (imgs != null && imgs.Count > 0)
        {
            int index = 0;

            foreach (HtmlNode img in imgs)
            {
                string imageUrl = ExtractImage(img);
                if (string.IsNullOrEmpty(imageUrl))
                {
                    continue;
                }

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

            if (pages.Count > 0)
            {
                return pages;
            }
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

            if (arr != null)
            {
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
        }

        return pages;
    }

    protected virtual string PageSelector()
    {
        return "//div[@id='readerarea']//img";
    }

    protected virtual string ExtractImage(HtmlNode node)
    {
        if (node == null)
        {
            return string.Empty;
        }

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

    protected virtual string ExtractCoverImage(HtmlNode container)
    {
        HtmlNode imgNode = container.SelectSingleNode(SeriesThumbnailSelector());
        return imgNode != null ? ExtractImage(imgNode) : string.Empty;
    }

    protected virtual string ExtractDescription(HtmlNode container)
    {
        HtmlNodeCollection descNodes = container.SelectNodes(SeriesDescriptionSelector());

        if (descNodes == null || descNodes.Count == 0)
        {
            return string.Empty;
        }

        List<string> descriptions = [];
        foreach (HtmlNode descNode in descNodes)
        {
            string text = descNode.InnerText.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                descriptions.Add(WebUtility.HtmlDecode(text));
            }
        }

        string description = string.Join("\n", descriptions).Trim();

        // Extract and add alternative names
        HtmlNode altNameNode = container.SelectSingleNode(SeriesAltNameSelector());
        string altName = altNameNode?.InnerText.Trim();
        altName = RemoveEmptyPlaceholder(altName);

        if (!string.IsNullOrEmpty(altName))
        {
            description = $"{description}\n\nAlternative Names: {altName}".Trim();
        }

        return description;
    }

    protected virtual string ExtractFieldFromContainer(HtmlNode container, string selector)
    {
        HtmlNode node = container.SelectSingleNode(selector);
        return node?.InnerText.Trim();
    }

    protected virtual string RemoveEmptyPlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-" || value == "N/A" || value == "n/a" || value == "Unknown")
        {
            return null;
        }

        return value;
    }

    protected virtual ReleaseStatus ParseStatus(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ReleaseStatus.Continuing;
        }

        text = text.ToLowerInvariant();

        // Ongoing status indicators
        string[] ongoingIndicators =
        [
            "مستمرة", "en curso", "ongoing", "on going", "ativo", "en cours", "en cours de publication",
            "đang tiến hành", "em lançamento", "онгоінг", "publishing", "devam ediyor", "em andamento",
            "in corso", "güncel", "berjalan", "продолжается", "updating", "lançando", "in arrivo",
            "emision", "en emision", "مستمر", "curso", "en marcha", "publicandose", "publicando",
            "連載中", "devam etmekte", "連載中",
        ];

        if (ongoingIndicators.Any(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase)))
        {
            return ReleaseStatus.Continuing;
        }

        // Completed status indicators
        string[] completedIndicators =
        [
            "completed", "completo", "complété", "fini", "achevé", "terminé", "tamamlandı", "đã hoàn thành",
            "hoàn thành", "مکتملة", "завершено", "finished", "finalizado", "completata", "one-shot",
            "bitti", "tamat", "completado", "concluído", "完結", "concluido", "已完结", "bitmiş",
        ];

        if (completedIndicators.Any(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase)))
        {
            return ReleaseStatus.Completed;
        }

        // Cancelled status indicators
        string[] cancelledIndicators = ["canceled", "cancelled", "cancelado", "cancellato", "cancelados", "dropped", "discontinued", "abandonné"];

        if (cancelledIndicators.Any(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase)))
        {
            return ReleaseStatus.Cancelled;
        }

        // On Hiatus status indicators
        string[] hiatusIndicators = ["hiatus", "on hold", "pausado", "en espera", "en pause", "en attente", "hiato"];

        if (hiatusIndicators.Any(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase)))
        {
            return ReleaseStatus.OnHiatus;
        }

        return ReleaseStatus.Continuing;
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

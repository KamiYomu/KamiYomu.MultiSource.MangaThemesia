using System.Globalization;
using System.Net;

using HtmlAgilityPack;

using KamiYomu.CrawlerAgents.Core;
using KamiYomu.CrawlerAgents.Core.Catalog;
using KamiYomu.CrawlerAgents.Core.Catalog.Builders;
using KamiYomu.CrawlerAgents.Core.Catalog.Definitions;

using Page = KamiYomu.CrawlerAgents.Core.Catalog.Page;

namespace KamiYomu.MultiSource.MangaThemesia;

/// <summary>
/// Abstract base class for crawling manga from MangaThemesia-based websites.
/// 
/// This agent provides common functionality for extracting manga data, chapters, and pages
/// from websites built using the MangaThemesia theme/engine. It handles HTML parsing,
/// lazy-loaded image extraction, and multi-language field detection.
/// </summary>
public abstract class MangaThemesiaCrawlerAgent : AbstractCrawlerAgent, ICrawlerAgent
{
    private readonly Lazy<HttpClient> _lazyHttpClient;
    /// <summary>
    /// Gets the HTTP client used for making requests to the manga source.
    /// </summary>
    protected HttpClient HttpClient => _lazyHttpClient.Value;
    /// <summary>
    /// The base URL of the manga source (e.g., https://example.com).
    /// </summary>
    protected readonly string BaseUrl;
    /// <summary>
    /// The directory path where manga are located (default: "/manga").
    /// </summary>
    protected readonly string MangaDir;
    /// <summary>
    /// Gets the project page URL path (default: "/project").
    /// </summary>
    protected virtual string ProjectPageString => "/project";
    /// <summary>
    /// DateTime format string used for parsing release dates (default: "MMMM dd, yyyy").
    /// </summary>
    protected virtual string DateTimeFormat => "MMMM dd, yyyy";
    /// <summary>
    /// DateTime format provider used for parsing release dates (default: en-US culture).
    /// </summary>
    protected virtual IFormatProvider DateTimeFormatProvider => CultureInfo.GetCultureInfo("en-US");


    /// <summary>
    /// Initializes a new instance of the <see cref="MangaThemesiaCrawlerAgent"/> class.
    /// </summary>
    /// <param name="options">Configuration options including:
    /// <list type="bullet">
    /// <item><description>"Mirror" (required): The base URL of the manga source</description></item>
    /// <item><description>"SmartCrawlerHttpHandler" (optional): HTTP handler for smart crawling</description></item>
    /// <item><description>"FlareSolverrHttpHandler" (optional): HTTP handler for Cloudflare bypassing</description></item>
    /// <item><description>"ChromiumHttpHandler" (optional): HTTP handler for Chromium-based requests</description></item>
    /// </list>
    /// </param>
    /// <param name="mangaDirectory">The directory path for manga (default: "/manga").</param>
    /// <exception cref="ArgumentNullException">Thrown when "Mirror" option is not provided.</exception>
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

    /// <summary>
    /// Disposes the HTTP client if it has been created.
    /// </summary>
    public void Dispose()
    {
        if (_lazyHttpClient.IsValueCreated)
        {
            HttpClient.Dispose();
        }
    }

    /// <inheritdoc />
    public virtual async Task<Uri> GetFaviconAsync(CancellationToken cancellationToken)
    {
        Uri favicon = new($"{BaseUrl}/favicon.ico");
        return favicon;
    }

    /// <inheritdoc />
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

    /// <summary>
    /// Parses the HTML search results page and extracts manga information.
    /// </summary>
    /// <param name="doc">The HTML document containing search results.</param>
    /// <returns>A list of manga extracted from the search results.</returns>
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
                    .WithTitle(WebUtility.HtmlDecode(title))
                    .WithDescription(string.IsNullOrWhiteSpace(summary) ? "no description" : WebUtility.HtmlDecode(summary))
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

    /// <summary>
    /// Gets the CSS/XPath selector for extracting manga items from search results.
    /// </summary>
    /// <returns>An XPath expression to select manga container elements.</returns>
    protected virtual string SearchMangaSelector()
    {
        // XPath equivalent of ".utao .uta .imgu, .listupd .bs .bsx, .listo .bs .bsx"
        return "//div[@class='utao']//div[@class='uta']//div[@class='imgu'] | //div[@class='listupd']//div[@class='bs']//div[@class='bsx'] | //div[@class='listo']//div[@class='bs']//div[@class='bsx']";
    }

    /// <summary>
    /// Gets the CSS/XPath selector for the next page button in search results.
    /// </summary>
    /// <returns>An XPath expression to select the next page element.</returns>
    protected virtual string SearchMangaNextPageSelector()
    {
        return "//div[contains(@class,'pagination')]//*[contains(@class,'next')] | //div[contains(@class,'hpage')]//*[contains(@class,'r')]";
    }

    /// <inheritdoc />
    public virtual async Task<Manga> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        Uri url = new(new Uri(BaseUrl), $"{MangaDir}/{id}");

        string html = await HttpClient.GetStringAsync(url, cancellationToken);
        HtmlDocument doc = new();
        doc.LoadHtml(html);
        return MangaDetailsParse(id, url, doc);
    }

    /// <summary>
    /// Parses the HTML manga detail page and extracts all metadata.
    /// </summary>
    /// <param name="id">The manga ID.</param>
    /// <param name="url">The URL of the manga detail page.</param>
    /// <param name="doc">The HTML document containing manga details.</param>
    /// <returns>A manga object with complete metadata, or null if parsing fails.</returns>
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
            .WithTitle(WebUtility.HtmlDecode(title))
            .WithDescription(WebUtility.HtmlDecode(description))
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

    /// <summary>
    /// Gets the CSS/XPath selector for the main series details container.
    /// </summary>
    /// <returns>An XPath expression to select the series details container.</returns>
    protected virtual string SeriesDetailsSelector()
    {
        return "//div[contains(@class,'bigcontent')] | //div[contains(@class,'animefull')] | //div[contains(@class,'main-info')] | //div[contains(@class,'postbody')]";
    }

    /// <summary>
    /// Gets the CSS/XPath selector for the series title element.
    /// </summary>
    /// <returns>An XPath expression to select the title element.</returns>
    protected virtual string SeriesTitleSelector()
    {
        return "//h1[contains(@class,'entry-title')] | //div[contains(@class,'ts-breadcrumb')]//li[last()]/span";
    }

    /// <summary>
    /// Gets the CSS/XPath selector for the series artist, supporting multiple languages and formats.
    /// </summary>
    /// <returns>An XPath expression to select the artist element.</returns>
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

    /// <summary>
    /// Gets the CSS/XPath selector for the series author, supporting multiple languages and formats.
    /// </summary>
    /// <returns>An XPath expression to select the author element.</returns>
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

    /// <summary>
    /// Gets the CSS/XPath selector for the series description.
    /// </summary>
    /// <returns>An XPath expression to select the description element.</returns>
    protected virtual string SeriesDescriptionSelector()
    {
        return "//*[contains(@class,'desc')] | //*[(contains(@class,'entry-content') and @itemprop='description')]";
    }

    /// <summary>
    /// Gets the CSS/XPath selector for alternative series names, supporting multiple languages and formats.
    /// </summary>
    /// <returns>An XPath expression to select the alternative names element.</returns>
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

    /// <summary>
    /// Gets the CSS/XPath selector for series genres.
    /// </summary>
    /// <returns>An XPath expression to select genre elements.</returns>
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

    /// <summary>
    /// Gets the CSS/XPath selector for the series type, supporting multiple languages and formats.
    /// </summary>
    /// <returns>An XPath expression to select the type element.</returns>
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

    /// <summary>
    /// Gets the CSS/XPath selector for the series status, supporting multiple languages and formats.
    /// </summary>
    /// <returns>An XPath expression to select the status element.</returns>
    protected virtual string SeriesStatusSelector()
    {
        string[] keywords =
        [
            "status",
            "Statut",
            "Durum",
            "連載状況",
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

    /// <summary>
    /// Gets the CSS/XPath selector for the series thumbnail/cover image.
    /// </summary>
    /// <returns>An XPath expression to select the thumbnail element.</returns>
    protected virtual string SeriesThumbnailSelector()
    {
        return "//*[contains(@class,'infomanga')]//div[@itemprop='image']/img | //*[contains(@class,'thumb')]//img";
    }

    /// <summary>
    /// Builds an XPath selector by replacing a template with multiple keyword variants.
    /// </summary>
    /// <param name="selectorTemplate">The XPath template with "%s" placeholder for keywords.</param>
    /// <param name="keywords">The list of keywords to substitute into the template.</param>
    /// <returns>An XPath expression combining all keyword variants with OR operators.</returns>
    protected virtual string BuildSelector(string selectorTemplate, string[] keywords)
    {
        return string.Join(" | ", keywords.Select(keyword => selectorTemplate.Replace("%s", keyword)));
    }

    /// <inheritdoc />
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

    /// <summary>
    /// Parses the manga detail page HTML and extracts all chapters.
    /// </summary>
    /// <param name="manga">The parent manga object.</param>
    /// <param name="doc">The HTML document containing the chapter list.</param>
    /// <returns>A list of chapters extracted from the page.</returns>
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
                string releaseDateText = li.SelectSingleNode(".//span[contains(@class,'chapterdate')]")?.InnerText.Trim() ?? string.Empty;

                string uri = chapterId.StartsWith("http")
                    ? chapterId
                    : $"{BaseUrl}{chapterId}";

                decimal number = ExtractChapterNumber(title);
                DateTime? releaseDate = ExtractChapterReleaseDate(releaseDateText);

                ChapterBuilder chapterBuilder = ChapterBuilder.Create();

                chapterBuilder = chapterBuilder
                     .WithId(chapterId)
                     .WithTitle(WebUtility.HtmlDecode(title))
                     .WithParentManga(manga)
                     .WithVolume(0)
                     .WithTranslatedLanguage("en")
                     .WithReleaseDate(releaseDate ?? default)
                     .WithNumber(number)
                     .WithUri(new Uri(uri));

                chapters.Add(chapterBuilder.Build());
            }
        }

        return chapters;
    }
    /// <summary>
    /// Extracts the release date of a chapter from the provided text, using a specific date format.
    /// </summary>
    /// <param name="releaseDateText">The text containing the release date.</param>
    /// <returns>The extracted release date, or null if parsing fails.</returns>
    protected virtual DateTime? ExtractChapterReleaseDate(string releaseDateText)
    {
        return DateTime.TryParseExact(
            releaseDateText,
            DateTimeFormat,
            DateTimeFormatProvider,
            DateTimeStyles.None,
            out DateTime releaseDate)
            ? releaseDate
            : null;
    }


    /// <summary>
    /// Gets the CSS/XPath selector for chapter list items.
    /// </summary>
    /// <returns>An XPath expression to select chapter elements.</returns>
    protected virtual string ChapterListSelector()
    {
        return "//div[@class='bxcl']//li | //div[@class='cl']//li | //*[@id='chapterlist']//li | //ul//li[div[@class='chbox'] and div[@class='eph-num']]";
    }

    /// <summary>
    /// Extracts the chapter number from the chapter title using regex pattern matching.
    /// </summary>
    /// <param name="title">The chapter title text.</param>
    /// <returns>The extracted chapter number, or 0 if no number is found.</returns>
    protected virtual decimal ExtractChapterNumber(string title)
    {
        // Match integers or decimals: 12, 12.5, 0.1, 3.50, etc.
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(title, @"\d+(\.\d+)?");

        if (!match.Success)
        {
            return 0;
        }

        // Use invariant culture to avoid locale issues (e.g., commas vs dots)
        return decimal.TryParse(match.Value, NumberStyles.Any,
                            CultureInfo.InvariantCulture,
                            out decimal number)
            ? number
            : 0;
    }

    /// <inheritdoc />
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

    /// <summary>
    /// Gets the CSS/XPath selector for page image elements within the reader area.
    /// </summary>
    /// <returns>An XPath expression to select image elements.</returns>
    protected virtual string PageSelector()
    {
        return "//div[@id='readerarea']//img";
    }

    /// <summary>
    /// Extracts the image URL from an HTML node, handling lazy-loaded images and various attribute names.
    /// </summary>
    /// <param name="node">The HTML node containing or referencing an image.</param>
    /// <returns>The image URL, or an empty string if no URL is found.</returns>
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

    /// <summary>
    /// Extracts the cover image URL from the series details container.
    /// </summary>
    /// <param name="container">The HTML container with series details.</param>
    /// <returns>The cover image URL, or an empty string if not found.</returns>
    protected virtual string ExtractCoverImage(HtmlNode container)
    {
        HtmlNode imgNode = container.SelectSingleNode(SeriesThumbnailSelector());
        return imgNode != null ? ExtractImage(imgNode) : string.Empty;
    }

    /// <summary>
    /// Extracts the full description from the series details container, including alternative names.
    /// </summary>
    /// <param name="container">The HTML container with series details.</param>
    /// <returns>The formatted description with alternative names appended, or an empty string if not found.</returns>
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
                descriptions.Add(text);
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

    /// <summary>
    /// Extracts text content from a specific field within the series details container.
    /// </summary>
    /// <param name="container">The HTML container with series details.</param>
    /// <param name="selector">The XPath selector for the field element.</param>
    /// <returns>The trimmed text content, or null if the element is not found.</returns>
    protected virtual string ExtractFieldFromContainer(HtmlNode container, string selector)
    {
        HtmlNode node = container.SelectSingleNode(selector);
        return node?.InnerText.Trim();
    }

    /// <summary>
    /// Removes common placeholder values that indicate missing or unavailable data.
    /// </summary>
    /// <param name="value">The value to clean.</param>
    /// <returns>The cleaned value, or null if it was a placeholder.</returns>
    protected virtual string RemoveEmptyPlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "-" || value == "N/A" || value == "n/a" || value == "Unknown")
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// Parses a release status string and returns the corresponding ReleaseStatus enum value.
    /// Supports multiple languages including English, Japanese, Arabic, Thai, and more.
    /// </summary>
    /// <param name="text">The status text to parse.</param>
    /// <returns>The corresponding ReleaseStatus value, or Continuing if no match is found.</returns>
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

    /// <summary>
    /// Extracts the manga ID from a URL by removing query parameters and extracting the last path segment.
    /// </summary>
    /// <param name="url">The manga URL.</param>
    /// <returns>The extracted manga ID, or the original URL if extraction fails.</returns>
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

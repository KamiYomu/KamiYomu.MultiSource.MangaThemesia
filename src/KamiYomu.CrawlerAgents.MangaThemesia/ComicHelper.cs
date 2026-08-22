namespace KamiYomu.CrawlerAgents.MangaThemesia;
public class ComicHelper
{
    public static bool IsGenreNotFamilySafe(string p)
    {
        return !string.IsNullOrWhiteSpace(p) && (p.Contains("adult", StringComparison.OrdinalIgnoreCase)
            || p.Contains("harem", StringComparison.OrdinalIgnoreCase)
            || p.Contains("hentai", StringComparison.OrdinalIgnoreCase)
            || p.Contains("ecchi", StringComparison.OrdinalIgnoreCase)
            || p.Contains("violence", StringComparison.OrdinalIgnoreCase)
            || p.Contains("smut", StringComparison.OrdinalIgnoreCase)
            || p.Contains("shota", StringComparison.OrdinalIgnoreCase)
            || p.Contains("sexual", StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeUrl(Uri baseUrl, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!url.StartsWith("/") && Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute))
        {
            return absolute.ToString();
        }

        Uri resolved = new(baseUrl, url);
        return resolved.ToString();
    }
}

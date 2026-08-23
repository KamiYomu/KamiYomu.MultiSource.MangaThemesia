using System.ComponentModel;

using KamiYomu.CrawlerAgents.Core.Inputs;
using KamiYomu.MultiSource.MangaThemesia;

namespace KamiYomu.CrawlerAgents.ConsoleApp;
[DisplayName("KamiYomu Crawler Agent – any crawler")]
[CrawlerSelect("Mirror", "MangaThemesia offers multiple mirror sites that may be online and useful.",
    true, 0, [
        "https://galaxymanga.io",
    ])]
internal class AnyCrawlerAgent(IDictionary<string, object> options) : MangaThemesiaCrawlerAgent(options)
{
}

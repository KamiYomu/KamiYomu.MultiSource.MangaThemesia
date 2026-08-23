# KamiYomu Multi-Source — MangaThemesia

A specialized crawler library for extracting manga data from MangaThemesia‑based websites.
Built on [KamiYomu.CrawlerAgents.Core](https://github.com/KamiYomu/KamiYomu.CrawlerAgents.Core), it delivers fast and reliable search, structured metadata parsing, and seamless integration with the KamiYomu ecosystem. The library standardizes interaction with MangaThemesia endpoints, ensuring consistent behavior across all supported sources.

---

## Features
- Search MangaThemesia sources (titles, authors, genres)
- Extract standardized metadata (titles, descriptions, artists, authors, tags)
- Retrieve chapter lists and page images
- Normalized output for KamiYomu ingestion
- Fully extensible parsing pipeline
- Targets **.NET 8**

---

## MangaThemesia Website Structure

MangaThemesia sites do **not** expose a formal API. Instead, they follow a consistent HTML-based structure across all MangaThemesia‑based platforms.

Your source implementation parses these pages.

---

## Page Structure & Selectors

### **1. Search Results Page**
URL: `/?s={query}` or `/search/?s={query}`

HTML Structure:
- Search results are displayed in a grid or list format
- Manga items are typically contained in `div.post-title` or `div.post-content` elements
- Manga links follow the pattern: `a[href*="/manga/"]`
- Thumbnail images: `img.post-image` or `img.thumbnail`
- Title and metadata are extracted from linked elements and surrounding containers

Optional filters (site‑dependent):
- Author filter through form inputs
- Status filter (ongoing|completed|hiatus|dropped)
- Type filter (Manga|Manhwa|Manhua|Comic)
- Genre filters through checkbox or dropdown elements
- Sort order (popular|update|latest|title)

---

### **2. Manga Details Page**
URL: `/manga/{slug}/`

HTML Structure:
- Manga title: `h1.post-title` or similar heading selectors
- Description/Synopsis: `div.post-content` or `div.description`
- Alternative names: `div.alternative-title` or metadata sections
- Genres: `a[rel="tag"]` or `span.genres`
- Type (manga/manhwa/manhua): metadata field in info box
- Status: metadata field in info box
- Thumbnail/Cover image: `img.post-image` or `figure img`
- Chapter list: contained in `div.bxcl` or `div.bxcl-top` structures

---

### **3. Chapter List**
Displayed on the Manga Details Page:
URL: `/manga/{slug}/`

HTML Selectors for chapters:
- List container: `div.bxcl` or `div.cl`
- Individual chapters: `li.bxcl-item`, `li.ch-row`, or `li` within chapter container
- Chapter links: `a[href*="/chapter-"]` or similar patterns
- Chapter title/number: text within chapter link
- Release date: `span.chapter-release-date` or metadata span

---

### **4. Chapter Reading/Pages**
URL: `/manga/{slug}/{chapter-slug}/`

HTML Structure:
- Reader container: `div#readerarea`, `div.reading-content`, or `div.chapter-container`
- Images: `img` tags within reader container, with `src` attributes pointing to image URLs
- Image container: individual `<img>` elements or `<picture>` tags with `<source>` elements
- Navigation buttons: links to previous/next chapters typically at top/bottom of page

Image source patterns:
- Direct image URLs in `src` attributes
- Lazy-loading attributes: `data-src`, `data-lazy-src`
- Some sites use CDN URLs with site referer requirements

---

### **5. Image Files**
URL: Direct image URL from chapter page

Required request headers:
- `Referer`: Chapter URL (required for hotlink protection on many sites)
- `User-Agent`: Standard browser user agent
- `Accept`: `image/*` or `*/*`

---

### **6. JavaScript/Dynamic Content**
Some MangaThemesia sites may load content dynamically:
- Chapter pages may use AJAX to load images
- View counters may be tracked via AJAX requests
- Additional metadata may be loaded asynchronously

---

## Installation

### Prerequisites
- **.NET 8** SDK or later
- Visual Studio 2022, Visual Studio Code, or compatible IDE
- An existing .NET project (Console, Web, or Library)

---

### Install via NuGet Package Manager (Visual Studio)

1. **Open your project** in Visual Studio 2022
2. **Right-click** on your project in Solution Explorer
3. Select **Manage NuGet Packages**
4. Click the **Browse** tab
5. Search for `KamiYomu.MultiSource.MangaThemesia`
6. Click the package and select **Install**
7. Review and accept the license agreement
8. Wait for installation to complete

---

### Install via Package Manager Console (Visual Studio)

1. **Open** Tools → NuGet Package Manager → Package Manager Console
2. **Ensure** your project is selected in the "Default project" dropdown
3. **Run** the following command:

```
dotnet add package KamiYomu.MultiSource.MangaThemesia
```

# Create a new C# file in your project and add the following using directive:
```csharp
using KamiYomu.CrawlerAgents.Core;
using KamiYomu.MultiSource.MangaThemesia;

[DisplayName("[Developer Name] Crawler Agent – MyWebSite")]
[CrawlerSelect("Mirror", "MangaThemesia offers multiple mirror sites that may be online and useful.",
    true, 0, [
        "https://mywebsite.com", // << Replace with the actual mirror site URL compatible with MangaThemesia
    ])]
public class MyWebSiteCrawlerAgent(IDictionary<string, object> options) : MangaThemesiaCrawlerAgent(options), ICrawlerAgent
{
}
```
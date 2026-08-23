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

## MangaThemesia API Structure

MangaThemesia sites do **not** expose a formal API. Instead, they follow a consistent set of HTTP endpoints used across all MangaThemesia‑based platforms.

Your source implementation uses these endpoints.

---

## Endpoints

### **1. Search Manga**
GET /manga?title={query}&page={page}

Optional filters (site‑dependent):
author={name}
yearx={year}
status={ongoing|completed|hiatus|dropped}
type={Manga|Manhwa|Manhua|Comic}
order={popular|update|latest|title|titlereverse}
genre[]={genreName}

---

### **2. Manga Details**
GET /manga/{slug}/

Includes:
- Title
- Description
- Alternative names
- Genres
- Type (manga/manhwa/manhua)
- Status
- Thumbnail
- Chapter list

---

### **3. Chapter List**
Same endpoint as manga details:
GET /manga/{slug}/

Chapters are extracted from HTML selectors such as:
- div.bxcl li
- div.cl li
- #chapterlist li

---

### **4. Chapter Pages**
GET /{chapter-slug}/

Images appear as:
<div id="readerarea"><img src="..."></div>

Some sites embed JSON:
"images": ["https://cdn.site/image1.jpg", ...]

---

### **5. Image Files**
GET {imageUrl}

Required headers:
- Referer: {chapterUrl}
- Accept: image/avif,image/webp,image/png,image/jpeg,*/*

---

### **6. View Counter (Optional)**
POST /wp-admin/admin-ajax.php
action=dynamic_view_ajax
post_id={id}

---

## Installation

### Via KamiYomu Add‑ons (Recommended)
1. Open KamiYomu Web.
2. Navigate to **Add-ons → Sources**.
3. Add a NuGet source:
   - Public: `https://api.nuget.org/v3/index.json`
   - GitHub: `https://nuget.pkg.github.com/KamiYomu/index.json`
4. Install **KamiYomu Multi-Source — MangaThemesia**.
5. Configure the source in the Add-ons UI.

---

### Via NuGet (Developers)
dotnet add package KamiYomu.MultiSource.MangaThemesia

GitHub Packages example:
<packageSources>
  <add key="github" value="https://nuget.pkg.github.com/KamiYomu/index.json" />
  <add key="nuget" value="https://api.nuget.org/v3/index.json" />
</packageSources>

---

## Quick Start

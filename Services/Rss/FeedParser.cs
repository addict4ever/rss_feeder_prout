using Rss_feeder_prout.Helpers;
using Rss_feeder_prout.Models;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Analyseur de flux maison : RSS 2.0 / 0.9x, RSS 1.0 (RDF), Atom 1.0 / 0.3 et JSON Feed 1.x.
    /// Gère content:encoded, dc:creator / dc:date, media:content / media:thumbnail / media:group,
    /// enclosures, itunes:image, liens Atom (alternate / enclosure), xml:base, entités HTML mal échappées.
    /// </summary>
    public static class FeedParser
    {
        private const string MediaNs = "search.yahoo.com/mrss";

        public static ParsedFeed Parse(byte[] data, string? contentType = null, string? baseUrl = null)
        {
            if (data == null || data.Length == 0)
                throw new FormatException("Flux vide.");

            // JSON Feed ?
            int first = FirstNonWhitespace(data);
            if (first >= 0 && data[first] == (byte)'{')
                return ParseJson(data, baseUrl);

            XDocument doc;
            try
            {
                doc = LoadXml(data);
            }
            catch (XmlException)
            {
                // Tentative de réparation : entités HTML non déclarées, '&' isolés
                var text = DecodeBytes(data);
                text = RepairXml(text);
                doc = XDocument.Parse(text, LoadOptions.None);
            }

            var root = doc.Root ?? throw new FormatException("Document XML sans racine.");
            var name = root.Name.LocalName.ToLowerInvariant();

            ParsedFeed feed = name switch
            {
                "rss" => ParseRss(root),
                "rdf" => ParseRdf(root),
                "feed" => ParseAtom(root),
                _ when root.Descendants().Any(e => e.Name.LocalName == "item") => ParseRdf(root),
                _ => throw new FormatException($"Format de flux inconnu ({root.Name.LocalName}). Est-ce bien un flux RSS/Atom ?")
            };

            // Liens relatifs -> absolus
            var origin = !string.IsNullOrWhiteSpace(feed.SiteLink) ? feed.SiteLink : baseUrl;
            if (!string.IsNullOrWhiteSpace(origin) && Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            {
                foreach (var item in feed.Items)
                {
                    item.Link = MakeAbsolute(originUri, item.Link);
                    item.ImageUrl = MakeAbsolute(originUri, item.ImageUrl);
                }
                feed.IconUrl = MakeAbsolute(originUri, feed.IconUrl);
            }

            foreach (var item in feed.Items)
            {
                if (string.IsNullOrWhiteSpace(item.ImageUrl))
                    item.ImageUrl = FirstImageInHtml(!string.IsNullOrWhiteSpace(item.ContentHtml) ? item.ContentHtml : item.SummaryHtml);

                if (string.IsNullOrWhiteSpace(item.Id))
                    item.Id = !string.IsNullOrWhiteSpace(item.Link) ? item.Link : FormatHelper.StableHash(item.Title + item.PublishedUtc);

                item.Title = HtmlHelper.StripHtmlSingleLine(item.Title);
                if (string.IsNullOrWhiteSpace(item.Title))
                    item.Title = HtmlHelper.Truncate(HtmlHelper.StripHtmlSingleLine(item.SummaryHtml), 90);
                if (string.IsNullOrWhiteSpace(item.Title))
                    item.Title = "(sans titre)";
            }

            return feed;
        }

        // ------------------------------------------------------------------
        // XML
        // ------------------------------------------------------------------

        private static XDocument LoadXml(byte[] data)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
                CheckCharacters = false
            };
            using var ms = new MemoryStream(data);
            using var reader = XmlReader.Create(ms, settings);
            return XDocument.Load(reader, LoadOptions.None);
        }

        private static string DecodeBytes(byte[] data)
        {
            // Détecte l'encodage déclaré dans le prologue XML
            var head = Encoding.ASCII.GetString(data, 0, Math.Min(200, data.Length));
            var m = Regex.Match(head, @"encoding\s*=\s*[""']([A-Za-z0-9_\-]+)[""']");
            if (m.Success)
            {
                try { return Encoding.GetEncoding(m.Groups[1].Value).GetString(data); } catch { }
            }
            return Encoding.UTF8.GetString(data);
        }

        private static string RepairXml(string text)
        {
            // Retire le prologue (l'encodage est déjà appliqué) et le BOM
            text = text.TrimStart('﻿');
            text = Regex.Replace(text, @"^\s*<\?xml[^>]*\?>", string.Empty);
            // Entités HTML courantes non définies en XML
            text = text.Replace("&nbsp;", "&#160;").Replace("&eacute;", "&#233;").Replace("&egrave;", "&#232;")
                       .Replace("&agrave;", "&#224;").Replace("&ccedil;", "&#231;").Replace("&rsquo;", "&#8217;")
                       .Replace("&lsquo;", "&#8216;").Replace("&rdquo;", "&#8221;").Replace("&ldquo;", "&#8220;")
                       .Replace("&hellip;", "&#8230;").Replace("&mdash;", "&#8212;").Replace("&ndash;", "&#8211;")
                       .Replace("&laquo;", "&#171;").Replace("&raquo;", "&#187;").Replace("&copy;", "&#169;");
            // '&' isolés
            text = Regex.Replace(text, @"&(?!(?:[a-zA-Z]+|#\d+|#x[0-9a-fA-F]+);)", "&amp;");
            // Caractères de contrôle interdits
            text = Regex.Replace(text, @"[\x00-\x08\x0B\x0C\x0E-\x1F]", string.Empty);
            return text;
        }

        private static ParsedFeed ParseRss(XElement root)
        {
            var channel = Child(root, "channel") ?? root;
            var feed = new ParsedFeed
            {
                Format = "RSS " + ((string?)root.Attribute("version") ?? "2.0"),
                Title = Text(Child(channel, "title")),
                SiteLink = RssLink(channel),
                Description = Text(Child(channel, "description")),
                IconUrl = Text(Child(Child(channel, "image"), "url"))
            };
            if (string.IsNullOrEmpty(feed.IconUrl))
                feed.IconUrl = (string?)ChildNs(channel, "image", "itunes")?.Attribute("href") ?? string.Empty;

            foreach (var e in channel.Elements().Where(x => x.Name.LocalName == "item"))
                feed.Items.Add(ParseRssItem(e));

            return feed;
        }

        private static ParsedFeed ParseRdf(XElement root)
        {
            var channel = Child(root, "channel");
            var feed = new ParsedFeed
            {
                Format = "RSS 1.0 (RDF)",
                Title = Text(Child(channel, "title")),
                SiteLink = Text(Child(channel, "link")),
                Description = Text(Child(channel, "description")),
                IconUrl = Text(Child(Child(root, "image"), "url"))
            };

            foreach (var e in root.Descendants().Where(x => x.Name.LocalName == "item"))
                feed.Items.Add(ParseRssItem(e));

            return feed;
        }

        private static string RssLink(XElement channel)
        {
            // <link> RSS (sans namespace) et non <atom:link>
            var link = channel.Elements().FirstOrDefault(e => e.Name.LocalName == "link" && string.IsNullOrEmpty(e.Name.NamespaceName));
            var value = Text(link);
            if (!string.IsNullOrWhiteSpace(value)) return value;
            var atom = channel.Elements().FirstOrDefault(e => e.Name.LocalName == "link" && (string?)e.Attribute("rel") == "alternate");
            return (string?)atom?.Attribute("href") ?? string.Empty;
        }

        private static ParsedFeedItem ParseRssItem(XElement e)
        {
            var item = new ParsedFeedItem
            {
                Title = Text(Child(e, "title")),
                SummaryHtml = Text(Child(e, "description")),
                ContentHtml = Text(ChildNs(e, "encoded", "purl.org/rss/1.0/modules/content")),
                Author = Text(ChildNs(e, "creator", "purl.org/dc")) ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(item.Author))
                item.Author = Text(Child(e, "author"));

            // Lien
            var link = e.Elements().FirstOrDefault(x => x.Name.LocalName == "link" && string.IsNullOrEmpty(x.Name.NamespaceName));
            item.Link = Text(link);
            if (string.IsNullOrWhiteSpace(item.Link))
                item.Link = (string?)e.Elements().FirstOrDefault(x => x.Name.LocalName == "link")?.Attribute("href") ?? string.Empty;

            // GUID
            var guid = Child(e, "guid");
            item.Id = Text(guid);
            if (string.IsNullOrWhiteSpace(item.Link) && guid != null &&
                !string.Equals((string?)guid.Attribute("isPermaLink"), "false", StringComparison.OrdinalIgnoreCase) &&
                item.Id.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                item.Link = item.Id;

            if (string.IsNullOrWhiteSpace(item.Id))
                item.Id = (string?)e.Attribute(XName.Get("about", "http://www.w3.org/1999/02/22-rdf-syntax-ns#")) ?? item.Link;

            // Date
            item.PublishedUtc = DateHelper.ParseFeedDate(Text(Child(e, "pubDate")))
                                ?? DateHelper.ParseFeedDate(Text(ChildNs(e, "date", "purl.org/dc")))
                                ?? DateHelper.ParseFeedDate(Text(Child(e, "published")))
                                ?? DateHelper.ParseFeedDate(Text(Child(e, "updated")));

            // Catégories
            foreach (var c in e.Elements().Where(x => x.Name.LocalName == "category" || (x.Name.LocalName == "subject" && x.Name.NamespaceName.Contains("purl.org/dc"))))
            {
                var t = Text(c);
                if (!string.IsNullOrWhiteSpace(t)) item.Categories.Add(t.Trim());
            }

            item.ImageUrl = FindMediaImage(e);
            return item;
        }

        private static ParsedFeed ParseAtom(XElement root)
        {
            var feed = new ParsedFeed
            {
                Format = "Atom",
                Title = Text(Child(root, "title")),
                Description = Text(Child(root, "subtitle")),
                IconUrl = Text(Child(root, "icon"))
            };
            if (string.IsNullOrEmpty(feed.IconUrl)) feed.IconUrl = Text(Child(root, "logo"));
            feed.SiteLink = AtomLink(root, "alternate");

            foreach (var e in root.Elements().Where(x => x.Name.LocalName == "entry"))
            {
                var item = new ParsedFeedItem
                {
                    Id = Text(Child(e, "id")),
                    Title = Text(Child(e, "title")),
                    Link = AtomLink(e, "alternate"),
                    SummaryHtml = Text(Child(e, "summary")),
                    ContentHtml = AtomContent(e),
                    Author = Text(Child(Child(e, "author"), "name")),
                    PublishedUtc = DateHelper.ParseFeedDate(Text(Child(e, "published")))
                                   ?? DateHelper.ParseFeedDate(Text(Child(e, "updated")))
                                   ?? DateHelper.ParseFeedDate(Text(Child(e, "issued")))
                                   ?? DateHelper.ParseFeedDate(Text(Child(e, "modified")))
                };

                foreach (var c in e.Elements().Where(x => x.Name.LocalName == "category"))
                {
                    var t = (string?)c.Attribute("label") ?? (string?)c.Attribute("term");
                    if (!string.IsNullOrWhiteSpace(t)) item.Categories.Add(t.Trim());
                }

                item.ImageUrl = FindMediaImage(e);
                if (string.IsNullOrEmpty(item.ImageUrl))
                {
                    var enc = e.Elements().FirstOrDefault(x => x.Name.LocalName == "link"
                              && (string?)x.Attribute("rel") == "enclosure"
                              && ((string?)x.Attribute("type") ?? string.Empty).StartsWith("image", StringComparison.OrdinalIgnoreCase));
                    item.ImageUrl = (string?)enc?.Attribute("href") ?? string.Empty;
                }

                feed.Items.Add(item);
            }

            return feed;
        }

        private static string AtomLink(XElement parent, string rel)
        {
            var links = parent.Elements().Where(x => x.Name.LocalName == "link").ToList();
            var match = links.FirstOrDefault(l => ((string?)l.Attribute("rel") ?? "alternate") == rel
                                                  && ((string?)l.Attribute("type") ?? "text/html").Contains("html"))
                        ?? links.FirstOrDefault(l => ((string?)l.Attribute("rel") ?? "alternate") == rel)
                        ?? links.FirstOrDefault();
            return (string?)match?.Attribute("href") ?? Text(match);
        }

        private static string AtomContent(XElement e)
        {
            var content = Child(e, "content");
            if (content == null) return string.Empty;
            var type = (string?)content.Attribute("type") ?? "text";
            if (type == "xhtml")
            {
                // Le contenu est du XHTML inclus : on le sérialise
                var div = content.Elements().FirstOrDefault();
                return div != null ? string.Concat(div.Nodes().Select(n => n.ToString())) : content.Value;
            }
            return content.Value;
        }

        private static string FindMediaImage(XElement e)
        {
            // media:thumbnail / media:content (y compris dans media:group)
            var mediaElements = e.Descendants().Where(x => x.Name.NamespaceName.Contains(MediaNs)).ToList();

            var content = mediaElements
                .Where(x => x.Name.LocalName == "content")
                .FirstOrDefault(x =>
                {
                    var medium = (string?)x.Attribute("medium") ?? string.Empty;
                    var type = (string?)x.Attribute("type") ?? string.Empty;
                    var url = (string?)x.Attribute("url") ?? string.Empty;
                    return medium == "image" || type.StartsWith("image", StringComparison.OrdinalIgnoreCase) || LooksLikeImage(url);
                });
            if (content != null) return (string?)content.Attribute("url") ?? string.Empty;

            var thumb = mediaElements.FirstOrDefault(x => x.Name.LocalName == "thumbnail");
            if (thumb != null) return (string?)thumb.Attribute("url") ?? string.Empty;

            // <enclosure url=... type="image/jpeg" />
            var enclosure = e.Elements().FirstOrDefault(x => x.Name.LocalName == "enclosure"
                && (((string?)x.Attribute("type") ?? string.Empty).StartsWith("image", StringComparison.OrdinalIgnoreCase)
                    || LooksLikeImage((string?)x.Attribute("url") ?? string.Empty)));
            if (enclosure != null) return (string?)enclosure.Attribute("url") ?? string.Empty;

            // itunes:image
            var itunes = e.Elements().FirstOrDefault(x => x.Name.LocalName == "image" && x.Name.NamespaceName.Contains("itunes"));
            if (itunes != null) return (string?)itunes.Attribute("href") ?? string.Empty;

            // <image>url</image> (certains flux)
            var img = Child(e, "image");
            var txt = Text(img);
            if (LooksLikeImage(txt)) return txt;

            return string.Empty;
        }

        // ------------------------------------------------------------------
        // JSON Feed
        // ------------------------------------------------------------------

        private static ParsedFeed ParseJson(byte[] data, string? baseUrl)
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            var feed = new ParsedFeed
            {
                Format = "JSON Feed",
                Title = Str(root, "title"),
                SiteLink = Str(root, "home_page_url"),
                Description = Str(root, "description"),
                IconUrl = Str(root, "icon")
            };
            if (string.IsNullOrEmpty(feed.IconUrl)) feed.IconUrl = Str(root, "favicon");

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in items.EnumerateArray())
                {
                    var item = new ParsedFeedItem
                    {
                        Id = Str(it, "id"),
                        Title = Str(it, "title"),
                        Link = Str(it, "url"),
                        ContentHtml = Str(it, "content_html"),
                        SummaryHtml = Str(it, "summary"),
                        ImageUrl = Str(it, "image"),
                        PublishedUtc = DateHelper.ParseFeedDate(Str(it, "date_published")) ?? DateHelper.ParseFeedDate(Str(it, "date_modified"))
                    };

                    if (string.IsNullOrEmpty(item.Link)) item.Link = Str(it, "external_url");
                    if (string.IsNullOrEmpty(item.ImageUrl)) item.ImageUrl = Str(it, "banner_image");
                    if (string.IsNullOrEmpty(item.ContentHtml))
                    {
                        var text = Str(it, "content_text");
                        if (!string.IsNullOrEmpty(text))
                            item.ContentHtml = "<p>" + HtmlHelper.Encode(text).Replace("\n\n", "</p><p>").Replace("\n", "<br/>") + "</p>";
                    }

                    if (it.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array && authors.GetArrayLength() > 0)
                        item.Author = Str(authors[0], "name");
                    else if (it.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object)
                        item.Author = Str(author, "name");

                    if (it.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in tags.EnumerateArray())
                            if (t.ValueKind == JsonValueKind.String) item.Categories.Add(t.GetString() ?? string.Empty);
                    }

                    feed.Items.Add(item);
                }
            }

            if (!string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(feed.SiteLink))
                feed.SiteLink = baseUrl;

            return feed;
        }

        private static string Str(JsonElement el, string name)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? string.Empty;
            return string.Empty;
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static XElement? Child(XElement? parent, string localName)
        {
            return parent?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
        }

        private static XElement? ChildNs(XElement? parent, string localName, string nsContains)
        {
            return parent?.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase)
                                                         && e.Name.NamespaceName.Contains(nsContains, StringComparison.OrdinalIgnoreCase));
        }

        private static string Text(XElement? e) => e?.Value?.Trim() ?? string.Empty;

        private static bool LooksLikeImage(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var path = url.Split('?')[0].ToLowerInvariant();
            return path.EndsWith(".jpg") || path.EndsWith(".jpeg") || path.EndsWith(".png") || path.EndsWith(".webp") || path.EndsWith(".gif") || path.EndsWith(".avif");
        }

        private static readonly Regex ImgRegex = new(@"<img\b[^>]*?\b(?:data-src|data-lazy-src|src)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string FirstImageInHtml(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            foreach (Match m in ImgRegex.Matches(html))
            {
                var src = WebUtilityDecode(m.Groups[1].Value);
                if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                if (src.Contains("pixel.wp.com") || src.Contains("feeds.feedburner.com") || src.Contains("/1x1") || src.Contains("tracking")) continue;
                if (src.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)) continue;
                return src;
            }
            return string.Empty;
        }

        private static string WebUtilityDecode(string s) => System.Net.WebUtility.HtmlDecode(s);

        private static string MakeAbsolute(Uri baseUri, string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            url = url.Trim();
            if (url.StartsWith("//")) return baseUri.Scheme + ":" + url;
            if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
                return abs.ToString();
            return Uri.TryCreate(baseUri, url, out var rel) ? rel.ToString() : url;
        }

        private static int FirstNonWhitespace(byte[] data)
        {
            int start = 0;
            // BOM UTF-8
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) start = 3;
            for (int i = start; i < Math.Min(data.Length, 512); i++)
            {
                if (data[i] != ' ' && data[i] != '\n' && data[i] != '\r' && data[i] != '\t') return i;
            }
            return -1;
        }
    }
}

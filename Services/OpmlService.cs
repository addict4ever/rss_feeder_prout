using Rss_feeder_prout.Models;
using System.Diagnostics;
using System.Xml.Linq;

namespace Rss_feeder_prout.Services
{
    // Modèle temporaire pour retourner les données d'importation
    public class ImportedPlaylistData
    {
        public string Name { get; set; } = string.Empty;
        // Liste des sites importés pour cette playlist
        public List<FeedSite> Sites { get; set; } = new List<FeedSite>();
    }

    public class OpmlService
    {
        // ----------------------------------------------------
        // EXPORTATION OPML (Playlists DB -> Fichier XML)
        // ----------------------------------------------------
        /// <summary>
        /// Exporte les playlists et leurs sites associés au format OPML 2.0.
        /// </summary>
        public string ExportPlaylistsToOpml(Dictionary<FeedPlaylist, List<FeedSite>> playlistsWithSites)
        {
            var opml = new XElement("opml", new XAttribute("version", "2.0"),
                new XElement("head",
                    new XElement("title", "Exportation Rss Prout"),
                    new XElement("dateCreated", DateTime.UtcNow.ToString("R"))),
                new XElement("body",
                    playlistsWithSites.Select(pair =>
                    {
                        var playlist = pair.Key;
                        var sites = pair.Value;

                        return new XElement("outline",
                            new XAttribute("text", playlist.Name),
                            new XAttribute("title", playlist.Name),
                            sites.Select(site =>
                                new XElement("outline",
                                    new XAttribute("text", site.DisplayName),   // Nom du site
                                    new XAttribute("title", site.DisplayName),
                                    new XAttribute("type", "rss"),
                                    new XAttribute("xmlUrl", site.FeedUrl), // URL du flux RSS
                                    new XAttribute("htmlUrl", string.IsNullOrWhiteSpace(site.SiteUrl) ? site.FeedUrl : site.SiteUrl))
                            )
                        );
                    })
                )
            );

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), opml);
            return doc.Declaration + Environment.NewLine + doc.ToString();
        }

        // ----------------------------------------------------
        // IMPORTATION OPML (Fichier XML -> Playlists)
        // ----------------------------------------------------
        /// <summary>
        /// Importe un fichier OPML et retourne une liste de données de playlist.
        /// AMÉLIORÉ : gère les catégories imbriquées sur plusieurs niveaux et les flux "à plat".
        /// </summary>
        public List<ImportedPlaylistData> ImportOpml(string opmlContent)
        {
            var result = new Dictionary<string, ImportedPlaylistData>(StringComparer.CurrentCultureIgnoreCase);
            try
            {
                var doc = XDocument.Parse(opmlContent);
                var body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "body");
                if (body == null) return new List<ImportedPlaylistData>();

                foreach (var feedElement in body.Descendants().Where(e => e.Name.LocalName == "outline" && !string.IsNullOrWhiteSpace((string?)e.Attribute("xmlUrl"))))
                {
                    string url = ((string?)feedElement.Attribute("xmlUrl") ?? string.Empty).Trim();
                    string? name = (string?)feedElement.Attribute("title") ?? (string?)feedElement.Attribute("text");
                    string htmlUrl = (string?)feedElement.Attribute("htmlUrl") ?? string.Empty;

                    // Catégorie = outline parent le plus proche sans xmlUrl
                    var category = feedElement.Ancestors()
                        .Where(a => a.Name.LocalName == "outline" && string.IsNullOrWhiteSpace((string?)a.Attribute("xmlUrl")))
                        .Select(a => (string?)a.Attribute("title") ?? (string?)a.Attribute("text"))
                        .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

                    // Attribut category="Tech/News" (certains lecteurs)
                    if (string.IsNullOrWhiteSpace(category))
                    {
                        var catAttr = (string?)feedElement.Attribute("category");
                        if (!string.IsNullOrWhiteSpace(catAttr))
                            category = catAttr.Split(new[] { ',', '/' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim();
                    }

                    category = string.IsNullOrWhiteSpace(category) ? "Flux importés" : category.Trim();

                    if (!result.TryGetValue(category, out var data))
                    {
                        data = new ImportedPlaylistData { Name = category };
                        result[category] = data;
                    }

                    if (data.Sites.Any(s => string.Equals(s.FeedUrl, url, StringComparison.OrdinalIgnoreCase))) continue;

                    string host = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : url;
                    data.Sites.Add(new FeedSite
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? host : name.Trim(),
                        FeedUrl = url,
                        SiteUrl = htmlUrl
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OPML Import Error: {ex.Message}");
            }

            return result.Values.Where(v => v.Sites.Count > 0).ToList();
        }
    }
}

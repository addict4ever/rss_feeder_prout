using Rss_feeder_prout.Models;
using System.Xml.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Rss_feeder_prout.Services
{
    // 🎯 NOUVEAU : Modèle temporaire pour retourner les données d'importation
    // car FeedPlaylist n'a plus la propriété Urls
    public class ImportedPlaylistData
    {
        public string Name { get; set; }
        // Liste des sites importés pour cette playlist
        public List<FeedSite> Sites { get; set; } = new List<FeedSite>();
    }

    public class OpmlService
    {
        // ----------------------------------------------------
        // EXPORTATION OPML (Playlists DB -> Fichier XML)
        // ----------------------------------------------------
        /// <summary>
        /// Exporte les playlists et leurs sites associés au format OPML.
        /// </summary>
        // 🎯 CORRECTION: La méthode doit maintenant prendre les sites séparément.
        public string ExportPlaylistsToOpml(Dictionary<FeedPlaylist, List<FeedSite>> playlistsWithSites)
        {
            // Création de la structure XML OPML
            var opml = new XElement("opml", new XAttribute("version", "1.0"),
                new XElement("head",
                    new XElement("title", "Exportation Rss_feeder_prout")),
                new XElement("body",
                    playlistsWithSites.Select(pair =>
                    {
                        var playlist = pair.Key;
                        var sites = pair.Value;

                        return new XElement("outline", new XAttribute("text", playlist.Name),
                            // 🎯 CORRECTION: Itérer sur la liste de FeedSite fournie
                            sites.Select(site =>
                                new XElement("outline",
                                    new XAttribute("text", site.Name), // Nom du site
                                    new XAttribute("type", "rss"),
                                    new XAttribute("xmlUrl", site.FeedUrl), // URL du flux RSS
                                    new XAttribute("htmlUrl", site.FeedUrl)) // URL HTML (on utilise l'URL du flux par défaut)
                            )
                        );
                    })
                )
            );

            var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), opml);
            return doc.ToString();
        }

        // ----------------------------------------------------
        // IMPORTATION OPML (Fichier XML -> Playlists)
        // ----------------------------------------------------
        /// <summary>
        /// Importe un fichier OPML et retourne une liste de données de playlist.
        /// </summary>
        // 🎯 CORRECTION: Retourne ImportedPlaylistData au lieu de FeedPlaylist
        public List<ImportedPlaylistData> ImportOpml(string opmlContent)
        {
            var importedData = new List<ImportedPlaylistData>();
            try
            {
                var doc = XDocument.Parse(opmlContent);

                // Rechercher les éléments <outline> de premier niveau (qui sont nos Playlists)
                var playlistElements = doc.Descendants("body").Elements("outline");

                foreach (var playlistElement in playlistElements)
                {
                    string playlistName = (string)playlistElement.Attribute("text") ?? "Playlist Importée";
                    var importedSites = new List<FeedSite>();

                    // Récupération de la liste des flux (éléments enfants)
                    var feedElements = playlistElement.Elements("outline");

                    // 1. Gérer les éléments enfants (structure par catégorie)
                    if (feedElements.Any())
                    {
                        foreach (var feedElement in feedElements)
                        {
                            string url = (string)feedElement.Attribute("xmlUrl");
                            string name = (string)feedElement.Attribute("text");

                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                importedSites.Add(new FeedSite
                                {
                                    Name = name ?? new Uri(url).Host,
                                    FeedUrl = url
                                });
                            }
                        }
                    }
                    // 2. Gérer le cas où l'élément de premier niveau est déjà un flux (OPML plat)
                    else if ((string)playlistElement.Attribute("xmlUrl") is string singleUrl && !string.IsNullOrWhiteSpace(singleUrl))
                    {
                        string name = (string)playlistElement.Attribute("text");
                        importedSites.Add(new FeedSite
                        {
                            Name = name ?? new Uri(singleUrl).Host,
                            FeedUrl = singleUrl
                        });
                        // On doit encapsuler le flux dans une playlist par défaut pour l'uniformité
                        playlistName = "Flux Importés (Plat)";
                    }

                    if (importedSites.Any())
                    {
                        importedData.Add(new ImportedPlaylistData { Name = playlistName, Sites = importedSites });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OPML Import Error: {ex.Message}");
                // En cas d'erreur de parsing, on retourne une liste vide
            }

            return importedData;
        }
    }
}
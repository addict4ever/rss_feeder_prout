using Rss_feeder_prout.Models;
using SQLite;
using System.Diagnostics;

namespace Rss_feeder_prout.Services
{
    public partial class SQLiteService
    {
        // =================================================================
        // PLAYLISTS (catégories)
        // =================================================================

        public async Task<List<FeedPlaylist>> GetPlaylistsAsync()
        {
            await Init();
            var list = await ConfigDb.Table<FeedPlaylist>().ToListAsync();
            return list.OrderBy(p => p.SortOrder).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public async Task<FeedPlaylist> GetPlaylistAsync(int id)
        {
            await Init();
            return await ConfigDb.Table<FeedPlaylist>().Where(p => p.Id == id).FirstOrDefaultAsync();
        }

        public async Task<int> SavePlaylistAsync(FeedPlaylist playlist)
        {
            await Init();
            if (playlist.Id != 0)
            {
                return await ConfigDb.UpdateAsync(playlist);
            }

            if (playlist.SortOrder == 0)
            {
                var max = await ConfigDb.ExecuteScalarAsync<int>("SELECT IFNULL(MAX(SortOrder), 0) FROM FeedPlaylist;");
                playlist.SortOrder = max + 1;
            }
            return await ConfigDb.InsertAsync(playlist);
        }

        /// <summary>Supprime la catégorie, ses sources et TOUS ses articles (les archives sont conservées).</summary>
        public async Task<int> DeletePlaylistAsync(FeedPlaylist playlist)
        {
            await Init();

            // Supprime les sites liés dans la config
            await ConfigDb.Table<FeedSite>().Where(s => s.PlaylistId == playlist.Id).DeleteAsync();

            // Supprime les articles liés (maintenant TOUS les articles, plus seulement ceux du jour)
            await ArticlesDb.ExecuteAsync("DELETE FROM RssItem WHERE PlaylistId = ?;", playlist.Id);
            RaiseArticlesChanged();

            return await ConfigDb.DeleteAsync(playlist);
        }

        /// <summary>Réordonne les catégories selon l'ordre de la liste fournie.</summary>
        public async Task SavePlaylistOrderAsync(IList<FeedPlaylist> ordered)
        {
            await Init();
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].SortOrder = i + 1;
                await ConfigDb.ExecuteAsync("UPDATE FeedPlaylist SET SortOrder = ? WHERE Id = ?;", i + 1, ordered[i].Id);
            }
        }

        private static async Task InsertPlaylistOrderedAsync(SQLiteAsyncConnection db, int order, FeedPlaylist playlist)
        {
            playlist.SortOrder = order;
            playlist.ColorHex = FeedPlaylist.ColorChoices[(order - 1) % FeedPlaylist.ColorChoices.Length];
            await db.InsertAsync(playlist);
        }

        // =================================================================
        // SITES (sources)
        // =================================================================

        public async Task<List<FeedSite>> GetSitesAsync()
        {
            // Appel de la méthode sécurisée qui initialise si nécessaire
            await EnsureInitialized();
            return await ConfigDb.Table<FeedSite>().ToListAsync();
        }

        public async Task<List<FeedSite>> GetSitesForPlaylistAsync(int playlistId)
        {
            await Init();
            var list = await ConfigDb.Table<FeedSite>().Where(s => s.PlaylistId == playlistId).ToListAsync();
            return list.OrderBy(s => s.SortOrder).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Conservé (alias utilisé par RssService).</summary>
        public Task<List<FeedSite>> GetSitesByPlaylistIdAsync(int playlistId) => GetSitesForPlaylistAsync(playlistId);

        public async Task<FeedSite> GetSiteByIdAsync(int siteId)
        {
            await EnsureInitialized();
            return await ConfigDb.Table<FeedSite>().Where(s => s.Id == siteId).FirstOrDefaultAsync();
        }

        public async Task<FeedSite?> FindSiteByUrlAsync(string feedUrl)
        {
            await EnsureInitialized();
            var all = await ConfigDb.Table<FeedSite>().ToListAsync();
            return all.FirstOrDefault(s => string.Equals(s.FeedUrl?.TrimEnd('/'), feedUrl?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        }

        public async Task<int> SaveSiteAsync(FeedSite site)
        {
            await Init();
            if (site.Id != 0)
            {
                return await ConfigDb.UpdateAsync(site);
            }
            return await ConfigDb.InsertAsync(site);
        }

        public async Task<int> UpdateSiteAsync(FeedSite site)
        {
            await EnsureInitialized();
            // Les sites sont dans la base de configuration
            return await ConfigDb.UpdateAsync(site);
        }

        /// <summary>Supprime la source et tous ses articles.</summary>
        public async Task<int> DeleteSiteAsync(FeedSite site)
        {
            await Init();
            await ArticlesDb.ExecuteAsync("DELETE FROM RssItem WHERE SiteId = ?;", site.Id);
            RaiseArticlesChanged();
            return await ConfigDb.DeleteAsync(site);
        }

        /// <summary>NOUVEAU : déplace une source (et ses articles) vers une autre catégorie.</summary>
        public async Task MoveSiteAsync(FeedSite site, int newPlaylistId)
        {
            await Init();
            site.PlaylistId = newPlaylistId;
            await ConfigDb.UpdateAsync(site);
            await ArticlesDb.ExecuteAsync("UPDATE RssItem SET PlaylistId = ? WHERE SiteId = ?;", newPlaylistId, site.Id);
            RaiseArticlesChanged();
        }

        /// <summary>Nombre de sources par catégorie.</summary>
        public async Task<Dictionary<int, int>> GetSiteCountsByPlaylistAsync()
        {
            await Init();
            var rows = await ConfigDb.QueryAsync<CountRow>("SELECT PlaylistId AS K, COUNT(*) AS V FROM FeedSite GROUP BY PlaylistId;");
            return rows.ToDictionary(r => r.K, r => r.V);
        }

        // =================================================================
        // JOURNAL DE SYNCHRONISATION
        // =================================================================

        public async Task AddSyncLogAsync(SyncLogEntry entry)
        {
            await Init();
            await ConfigDb.InsertAsync(entry);

            // On garde les 200 dernières entrées
            await ConfigDb.ExecuteAsync("DELETE FROM SyncLogEntry WHERE Id NOT IN (SELECT Id FROM SyncLogEntry ORDER BY StartedUtc DESC LIMIT 200);");
        }

        public async Task<List<SyncLogEntry>> GetSyncLogsAsync(int limit = 50)
        {
            await Init();
            return await ConfigDb.QueryAsync<SyncLogEntry>("SELECT * FROM SyncLogEntry ORDER BY StartedUtc DESC LIMIT ?;", limit);
        }

        public async Task ClearSyncLogsAsync()
        {
            await Init();
            await ConfigDb.ExecuteAsync("DELETE FROM SyncLogEntry;");
        }

        // =================================================================
        // DONNÉES PAR DÉFAUT (liste d'origine conservée)
        // =================================================================

        /// <summary>
        /// Crée les données par défaut (Playlists et Sites)
        /// (Ce code est conservé tel quel)
        /// </summary>
        private async Task CreateDefaultData()
        {
            // (Les connexions sont déjà ouvertes : l'ancien appel à Init() ici provoquait un blocage.)
            var configDb = ConfigDb;
            int order = 0;

            // Liste pour stocker les sites par défaut avant l'insertion massive
            var defaultSites = new List<FeedSite>();

            // --- 1. ACTUALITÉS TECH ---
            var playlistActuTech = new FeedPlaylist { Name = "Actualités Tech Générales", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistActuTech);

            defaultSites.Add(new FeedSite { Name = "01net", FeedUrl = "https://www.01net.com/rss/", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "Actualités 01net", FeedUrl = "https://www.01net.com/actualites/feed/", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "Clubic", FeedUrl = "https://www.clubic.com/feed/rss", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "Journal du Geek", FeedUrl = "https://www.journaldugeek.com/feed", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "Presse-Citron", FeedUrl = "https://www.presse-citron.net/feed/", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "Numerama", FeedUrl = "https://www.numerama.com/feed/", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "Tom's Guide", FeedUrl = "https://www.tomsguide.fr/feed/", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "ZDNet (Actualités Tech)", FeedUrl = "https://www.zdnet.fr/feeds/rss/actualites/", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "Frandroid", FeedUrl = "https://www.frandroid.com/feed", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "Korben", FeedUrl = "https://korben.info/feed", PlaylistId = playlistActuTech.Id });
            defaultSites.Add(new FeedSite { Name = "Les Numériques", FeedUrl = "https://www.lesnumeriques.com/rss.xml", PlaylistId = playlistActuTech.Id });

            // --- 2. CYBERSÉCURITÉ ---
            var playlistSecu = new FeedPlaylist { Name = "Cybersécurité", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistSecu);

            defaultSites.Add(new FeedSite
            {
                Name = "ZATAZ",
                FeedUrl = "https://www.zataz.com/feed/",
                PlaylistId = playlistSecu.Id,
            });
            defaultSites.Add(new FeedSite { Name = "UnderNews", FeedUrl = "https://www.undernews.fr/feed", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "BleepingComputer", FeedUrl = "https://www.bleepingcomputer.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "Dark Reading", FeedUrl = "https://www.darkreading.com/rss.xml", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "KrebsOnSecurity", FeedUrl = "https://krebsonsecurity.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "SecurityWeek", FeedUrl = "https://feeds.feedburner.com/Securityweek", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "The Hacker News", FeedUrl = "https://feeds.feedburner.com/TheHackersNews", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "Global Security Mag", FeedUrl = "https://www.globalsecuritymag.fr/spip.php?page=backend", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "ThreatPost", FeedUrl = "https://threatpost.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "WeLiveSecurity", FeedUrl = "https://www.welivesecurity.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "CERT-FR", FeedUrl = "https://www.cert.ssi.gouv.fr/feed/", PlaylistId = playlistSecu.Id });

            // --- 3. MATÉRIEL / CPU / GPU / INNOVATIONS ---
            var playlistHardware = new FeedPlaylist { Name = "Matériel & Innovations", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistHardware);

            defaultSites.Add(new FeedSite { Name = "Tom's Hardware FR", FeedUrl = "https://www.tomshardware.fr/feed/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "TechPowerUp", FeedUrl = "https://www.techpowerup.com/rss/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "WCCFTech", FeedUrl = "https://wccftech.com/feed/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "PC Gamer", FeedUrl = "https://www.pcgamer.com/rss/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "Digital Trends", FeedUrl = "https://www.digitaltrends.com/feed/", PlaylistId = playlistHardware.Id });

            // --- 4. IA / SCIENCE / TECHNOLOGIES AVANCÉES ---
            var playlistScience = new FeedPlaylist { Name = "IA & Sciences Avancées", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistScience);

            defaultSites.Add(new FeedSite { Name = "MIT Technology Review", FeedUrl = "https://www.technologyreview.com/feed/", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "VentureBeat AI", FeedUrl = "https://venturebeat.com/category/ai/feed/", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "New Scientist", FeedUrl = "https://www.newscientist.com/feed/home/", PlaylistId = playlistScience.Id });

            // --- 5. INTERNATIONAL TECH NEWS ---
            var playlistInternational = new FeedPlaylist { Name = "Actualités Tech Internationales", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistInternational);

            defaultSites.Add(new FeedSite { Name = "The Verge", FeedUrl = "https://www.theverge.com/rss/index.xml", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Engadget", FeedUrl = "https://www.engadget.com/rss.xml", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Wired", FeedUrl = "https://www.wired.com/feed/rss", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Gizmodo", FeedUrl = "https://gizmodo.com/rss", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "CNET", FeedUrl = "https://www.cnet.com/rss/news/", PlaylistId = playlistInternational.Id });

            // --- 6. ACTUALITÉS QUÉBEC & FRANCOPHONIE ---
            var playlistQuebec = new FeedPlaylist { Name = "Actualités Québec", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistQuebec);

            defaultSites.Add(new FeedSite { Name = "Radio-Canada (Actualités)", FeedUrl = "https://ici.radio-canada.ca/rss/4159", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "La Presse (Actualités)", FeedUrl = "https://www.lapresse.ca/actualites/rss", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Le Devoir (Accueil)", FeedUrl = "https://www.ledevoir.com/rss/manchettes.xml", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "TVA Nouvelles (Actualités)", FeedUrl = "https://www.tvanouvelles.ca/actualites/rss.xml", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Journal de Montréal", FeedUrl = "https://www.journaldemontreal.com/rss.xml", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Les Affaires", FeedUrl = "https://www.lesaffaires.com/rss", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Québec Science", FeedUrl = "https://www.quebecscience.qc.ca/feed/", PlaylistId = playlistQuebec.Id });

            // --- 7. PROGRAMMATION & DÉVELOPPEMENT ---
            var playlistDev = new FeedPlaylist { Name = "Programmation & Code", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistDev);

            defaultSites.Add(new FeedSite { Name = "Python.org News", FeedUrl = "https://blog.python.org/rss.xml", PlaylistId = playlistDev.Id });
            defaultSites.Add(new FeedSite { Name = "Real Python", FeedUrl = "https://realpython.com/atom.xml", PlaylistId = playlistDev.Id });
            defaultSites.Add(new FeedSite { Name = ".NET Blog (Microsoft)", FeedUrl = "https://devblogs.microsoft.com/dotnet/feed/", PlaylistId = playlistDev.Id });
            defaultSites.Add(new FeedSite { Name = "Standard C++ (isocpp)", FeedUrl = "https://isocpp.org/blog/rss", PlaylistId = playlistDev.Id });
            defaultSites.Add(new FeedSite { Name = "Smashing Magazine", FeedUrl = "https://www.smashingmagazine.com/feed/", PlaylistId = playlistDev.Id });
            defaultSites.Add(new FeedSite { Name = "Web.dev (Google)", FeedUrl = "https://web.dev/feed.xml", PlaylistId = playlistDev.Id });
            defaultSites.Add(new FeedSite { Name = "JavaScript Weekly", FeedUrl = "https://javascriptweekly.com/rss", PlaylistId = playlistDev.Id });
            defaultSites.Add(new FeedSite { Name = "Developpez.com", FeedUrl = "https://www.developpez.com/index/rss", PlaylistId = playlistDev.Id });
            defaultSites.Add(new FeedSite { Name = "The New Stack", FeedUrl = "https://thenewstack.io/feed/", PlaylistId = playlistDev.Id });

            // --- 8. SYSTÈMES, RÉSEAUX & DEVOPS ---
            var playlistSysAdmin = new FeedPlaylist { Name = "Systèmes & Réseaux", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistSysAdmin);

            defaultSites.Add(new FeedSite { Name = "IT-Connect", FeedUrl = "https://www.it-connect.fr/feed/", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Le Monde Informatique", FeedUrl = "https://www.lemondeinformatique.fr/flux-rss/general/rss.xml", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Cisco Blog", FeedUrl = "https://blogs.cisco.com/feed", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Veeam Blog (FR)", FeedUrl = "https://www.veeam.com/blog/fr/feed", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Docker Blog", FeedUrl = "https://www.docker.com/blog/feed/", PlaylistId = playlistSysAdmin.Id });

            // --- 9. ACTUALITÉS LOCALES (CHAUDIÈRE-APPALACHES) ---
            var playlistLocale = new FeedPlaylist { Name = "Nouvelles Locales", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistLocale);

            defaultSites.Add(new FeedSite { Name = "Courrier Frontenac", FeedUrl = "https://www.courrierfrontenac.qc.ca/feed/", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "Beauce Média", FeedUrl = "https://www.beaucemedia.ca/feed/", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "EnBeauce", FeedUrl = "https://www.enbeauce.com/rss.xml", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "L'Écho de Frontenac", FeedUrl = "https://echodefrontenac.com/rss.xml", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "Journal de Lévis", FeedUrl = "https://www.journaldelevis.com/rss.xml", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "ICI Québec", FeedUrl = "https://ici.radio-canada.ca/rss/4159", PlaylistId = playlistLocale.Id });

            // --- 10. LINUX & OPEN SOURCE ---
            var playlistLinux = new FeedPlaylist { Name = "Linux & Open Source", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistLinux);

            defaultSites.Add(new FeedSite { Name = "LinuxFR.org", FeedUrl = "https://linuxfr.org/news.atom", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "OMG! Ubuntu!", FeedUrl = "https://www.omgubuntu.co.uk/feed", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "It's FOSS", FeedUrl = "https://itsfoss.com/feed/", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "Framablog", FeedUrl = "https://framablog.org/feed/", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "9to5Linux", FeedUrl = "https://9to5linux.com/feed", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "LibreArts", FeedUrl = "https://librearts.org/feed/", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "Fedora Magazine", FeedUrl = "https://fedoramagazine.org/feed/", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "Ubuntu Blog", FeedUrl = "https://ubuntu.com/blog/feed", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "Arch Linux News", FeedUrl = "https://archlinux.org/feeds/news/", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "OpenSource.com", FeedUrl = "https://opensource.com/feed", PlaylistId = playlistLinux.Id });

            // --- 11. JEUX VIDÉO ---
            var playlistGaming = new FeedPlaylist { Name = "Jeux Vidéo", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistGaming);

            defaultSites.Add(new FeedSite { Name = "JeuxVideo.com", FeedUrl = "https://www.jeuxvideo.com/rss/rss.xml", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Kotaku", FeedUrl = "https://kotaku.com/rss", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "IGN France", FeedUrl = "https://fr.ign.com/feed.xml", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "GameSpot", FeedUrl = "https://www.gamespot.com/feeds/mashup/", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Polygon", FeedUrl = "https://www.polygon.com/rss/index.xml", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "PC Gamer", FeedUrl = "https://www.pcgamer.com/rss/", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Rock Paper Shotgun", FeedUrl = "https://www.rockpapershotgun.com/feed", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Destructoid", FeedUrl = "https://www.destructoid.com/feed/", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Nintendo Life", FeedUrl = "https://www.nintendolife.com/feeds/latest", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Push Square (PlayStation)", FeedUrl = "https://www.pushsquare.com/feeds/latest", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Pure Xbox", FeedUrl = "https://www.purexbox.com/feeds/latest", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Gematsu", FeedUrl = "https://www.gematsu.com/feed", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Eurogamer", FeedUrl = "https://www.eurogamer.net/feed", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "VG247", FeedUrl = "https://www.vg247.com/feed", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "GamingOnLinux", FeedUrl = "https://www.gamingonlinux.com/article_rss.php", PlaylistId = playlistGaming.Id });

            // --- 12. DOMOTIQUE & MAKER ---
            var playlistMaker = new FeedPlaylist { Name = "Domotique & Maker", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistMaker);

            defaultSites.Add(new FeedSite { Name = "Domo-Blog", FeedUrl = "https://www.domo-blog.fr/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Home Assistant Blog", FeedUrl = "https://www.home-assistant.io/atom.xml", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Hackaday", FeedUrl = "https://hackaday.com/blog/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Adafruit Blog", FeedUrl = "https://blog.adafruit.com/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Framboise 314", FeedUrl = "https://www.framboise314.fr/feed/", PlaylistId = playlistMaker.Id });

            // --- 13. HUMOUR & BD ---
            // --- 13. HUMOUR & BD ---
            var playlistHumour = new FeedPlaylist { Name = "Humour & Strips BD", IsActive = true };
            await InsertPlaylistOrderedAsync(configDb, ++order, playlistHumour);

            // Humour FR
            // Flux issus de ton image
            defaultSites.Add(new FeedSite { Name = "Le Gorafi", FeedUrl = "https://www.legorafi.fr/feed/", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Dans Ton Chat", FeedUrl = "https://danstonchat.com/rss.xml", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "SMBC Comics", FeedUrl = "https://www.smbc-comics.com/comic/rss", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Explosm.net (Cyanide & Happiness)", FeedUrl = "https://explosm.net/rss.xml", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "PBF Comics", FeedUrl = "https://pbfcomics.com/feed/", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Exocomics", FeedUrl = "https://www.exocomics.com/feed/", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Girl Genius", FeedUrl = "https://www.girlgeniusonline.com/ggmain/rss.xml", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "FeedBurner Blog", FeedUrl = "https://feeds.feedburner.com/blogspot/ITBMF", PlaylistId = playlistHumour.Id });

            // Sauvegarde finale de tous les sites dans la base de CONFIGURATION permanente
            await configDb.InsertAllAsync(defaultSites);

        }
    }
}

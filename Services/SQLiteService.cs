using Rss_feeder_prout.Models;
using SQLite;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Maui.Storage;
using System;
using AngleSharp;

namespace Rss_feeder_prout.Services
{
    public class SQLiteService
    {
        // Deux connexions distinctes
        private SQLiteAsyncConnection _configDb; // Pour les sites et playlists
        private SQLiteAsyncConnection _dailyDb;  // Pour les articles du jour

        // Chemins des fichiers
        private string ConfigDbPath => Path.Combine(FileSystem.AppDataDirectory, "RssConfig.db3");
        private string DailyDbName => $"RssProut_{DateTime.Now:yyyy_MM_dd}.db3";
        private string DailyDbPath => Path.Combine(FileSystem.AppDataDirectory, DailyDbName);

        private SQLiteAsyncConnection _articlesDb; // Index global des articles
        private SQLiteAsyncConnection _archiveDb;   // Archivage centralisé

        private string ArticlesDbPath => Path.Combine(FileSystem.AppDataDirectory, "Articles.db3");
        private string ArchiveDbPath => Path.Combine(FileSystem.AppDataDirectory, "Archives.db3");

        private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);
        private bool _isInitialized = false;


        public SQLiteService()
        {
        }

        private async Task Init()
        {
            // 1. Initialisation de la base de CONFIGURATION
            if (_configDb is null)
            {
                _configDb = new SQLiteAsyncConnection(ConfigDbPath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
                await _configDb.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;"); // Mode WAL actif
                await _configDb.CreateTableAsync<FeedPlaylist>();
                await _configDb.CreateTableAsync<FeedSite>();

                if (await _configDb.Table<FeedSite>().CountAsync() == 0)
                {
                    await CreateDefaultData();
                }
            }

            // 2. Initialisation de la base d'INDEX
            if (_articlesDb is null)
            {
                _articlesDb = new SQLiteAsyncConnection(ArticlesDbPath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
                await _articlesDb.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;"); // Mode WAL actif
                await _articlesDb.CreateTableAsync<ArticleIndex>();
            }

            // 3. Initialisation de la base d'ARCHIVES
            if (_archiveDb is null)
            {
                _archiveDb = new SQLiteAsyncConnection(ArchiveDbPath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
                await _archiveDb.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;"); // Mode WAL actif
                await _archiveDb.CreateTableAsync<ArchiveItem>();
            }

            // 4. Initialisation de la base QUOTIDIENNE
            if (_dailyDb is null || Path.GetFileName(_dailyDb.DatabasePath) != DailyDbName)
            {
                if (_dailyDb is not null) await _dailyDb.CloseAsync();

                _dailyDb = new SQLiteAsyncConnection(DailyDbPath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);
                await _dailyDb.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;"); // Mode WAL actif
                await _dailyDb.CreateTableAsync<RssItem>();
            }
        }

        private async Task EnsureInitialized()
        {
            if (_isInitialized) return;

            await _initSemaphore.WaitAsync();
            try
            {
                if (!_isInitialized)
                {
                    await Init();
                    _isInitialized = true;
                }
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        public async Task<bool> IsArticleAlreadyDownloadedAsync(string articleGuid)
        {
            return await _articlesDb.Table<ArticleIndex>().CountAsync(x => x.ArticleGuid == articleGuid) > 0;
        }

        public async Task AddToArticleIndexAsync(string articleGuid)
        {
            await _articlesDb.InsertAsync(new ArticleIndex { ArticleGuid = articleGuid });
        }

        public async Task ArchiveArticleAsync(ArchiveItem item)
        {
            await _archiveDb.InsertAsync(item);
        }

        public async Task AddToArchivesAsync(ArchiveItem item)
        {
            // On insère dans la base d'archives centrale
            await _archiveDb.InsertAsync(item);
        }

        public async Task<List<FeedSite>> GetSitesAsync()
        {
            // Appel de la méthode sécurisée qui initialise si nécessaire
            await EnsureInitialized();

            // Accès sécurisé à la base
            return await _configDb.Table<FeedSite>().ToListAsync();
        }

        /// <summary>
        /// Crée les données par défaut (Playlists et Sites)
        /// (Ce code est conservé tel quel)
        /// </summary>
        /// 
        private async Task CreateDefaultData()
        {
            // On s'assure que les connexions sont prêtes
            await Init();

            // Liste pour stocker les sites par défaut avant l'insertion massive
            var defaultSites = new List<FeedSite>();

            // --- 1. ACTUALITÉS TECH ---
            var playlistActuTech = new FeedPlaylist { Name = "Actualités Tech Générales", IsActive = true };
            await _configDb.InsertAsync(playlistActuTech);

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
            await _configDb.InsertAsync(playlistSecu);

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
            await _configDb.InsertAsync(playlistHardware);

            defaultSites.Add(new FeedSite { Name = "Tom's Hardware FR", FeedUrl = "https://www.tomshardware.fr/feed/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "TechPowerUp", FeedUrl = "https://www.techpowerup.com/rss/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "WCCFTech", FeedUrl = "https://wccftech.com/feed/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "PC Gamer", FeedUrl = "https://www.pcgamer.com/rss/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "Digital Trends", FeedUrl = "https://www.digitaltrends.com/feed/", PlaylistId = playlistHardware.Id });

            // --- 4. IA / SCIENCE / TECHNOLOGIES AVANCÉES ---
            var playlistScience = new FeedPlaylist { Name = "IA & Sciences Avancées", IsActive = true };
            await _configDb.InsertAsync(playlistScience);

            defaultSites.Add(new FeedSite { Name = "MIT Technology Review", FeedUrl = "https://www.technologyreview.com/feed/", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "VentureBeat AI", FeedUrl = "https://venturebeat.com/category/ai/feed/", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "New Scientist", FeedUrl = "https://www.newscientist.com/feed/home/", PlaylistId = playlistScience.Id });

            // --- 5. INTERNATIONAL TECH NEWS ---
            var playlistInternational = new FeedPlaylist { Name = "Actualités Tech Internationales", IsActive = true };
            await _configDb.InsertAsync(playlistInternational);

            defaultSites.Add(new FeedSite { Name = "The Verge", FeedUrl = "https://www.theverge.com/rss/index.xml", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Engadget", FeedUrl = "https://www.engadget.com/rss.xml", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Wired", FeedUrl = "https://www.wired.com/feed/rss", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Gizmodo", FeedUrl = "https://gizmodo.com/rss", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "CNET", FeedUrl = "https://www.cnet.com/rss/news/", PlaylistId = playlistInternational.Id });

            // --- 6. ACTUALITÉS QUÉBEC & FRANCOPHONIE ---
            var playlistQuebec = new FeedPlaylist { Name = "Actualités Québec", IsActive = true };
            await _configDb.InsertAsync(playlistQuebec);

            defaultSites.Add(new FeedSite { Name = "Radio-Canada (Actualités)", FeedUrl = "https://ici.radio-canada.ca/rss/4159", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "La Presse (Actualités)", FeedUrl = "https://www.lapresse.ca/actualites/rss", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Le Devoir (Accueil)", FeedUrl = "https://www.ledevoir.com/rss/manchettes.xml", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "TVA Nouvelles (Actualités)", FeedUrl = "https://www.tvanouvelles.ca/actualites/rss.xml", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Journal de Montréal", FeedUrl = "https://www.journaldemontreal.com/rss.xml", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Les Affaires", FeedUrl = "https://www.lesaffaires.com/rss", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Québec Science", FeedUrl = "https://www.quebecscience.qc.ca/feed/", PlaylistId = playlistQuebec.Id });

            // --- 7. PROGRAMMATION & DÉVELOPPEMENT ---
            var playlistDev = new FeedPlaylist { Name = "Programmation & Code", IsActive = true };
            await _configDb.InsertAsync(playlistDev);

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
            await _configDb.InsertAsync(playlistSysAdmin);

            defaultSites.Add(new FeedSite { Name = "IT-Connect", FeedUrl = "https://www.it-connect.fr/feed/", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Le Monde Informatique", FeedUrl = "https://www.lemondeinformatique.fr/flux-rss/general/rss.xml", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Cisco Blog", FeedUrl = "https://blogs.cisco.com/feed", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Veeam Blog (FR)", FeedUrl = "https://www.veeam.com/blog/fr/feed", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Docker Blog", FeedUrl = "https://www.docker.com/blog/feed/", PlaylistId = playlistSysAdmin.Id });

            // --- 9. ACTUALITÉS LOCALES (CHAUDIÈRE-APPALACHES) ---
            var playlistLocale = new FeedPlaylist { Name = "Nouvelles Locales", IsActive = true };
            await _configDb.InsertAsync(playlistLocale);

            defaultSites.Add(new FeedSite { Name = "Courrier Frontenac", FeedUrl = "https://www.courrierfrontenac.qc.ca/feed/", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "Beauce Média", FeedUrl = "https://www.beaucemedia.ca/feed/", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "EnBeauce", FeedUrl = "https://www.enbeauce.com/rss.xml", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "L'Écho de Frontenac", FeedUrl = "https://echodefrontenac.com/rss.xml", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "Journal de Lévis", FeedUrl = "https://www.journaldelevis.com/rss.xml", PlaylistId = playlistLocale.Id });

            defaultSites.Add(new FeedSite { Name = "ICI Québec", FeedUrl = "https://ici.radio-canada.ca/rss/4159", PlaylistId = playlistLocale.Id });

            // --- 10. LINUX & OPEN SOURCE ---
            var playlistLinux = new FeedPlaylist { Name = "Linux & Open Source", IsActive = true };
            await _configDb.InsertAsync(playlistLinux);

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
            await _configDb.InsertAsync(playlistGaming);

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
            await _configDb.InsertAsync(playlistMaker);

            defaultSites.Add(new FeedSite { Name = "Domo-Blog", FeedUrl = "https://www.domo-blog.fr/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Home Assistant Blog", FeedUrl = "https://www.home-assistant.io/atom.xml", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Hackaday", FeedUrl = "https://hackaday.com/blog/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Adafruit Blog", FeedUrl = "https://blog.adafruit.com/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Framboise 314", FeedUrl = "https://www.framboise314.fr/feed/", PlaylistId = playlistMaker.Id });

            // --- 13. HUMOUR & BD ---
            // --- 13. HUMOUR & BD ---
            var playlistHumour = new FeedPlaylist { Name = "Humour & Strips BD", IsActive = true };
            await _configDb.InsertAsync(playlistHumour);

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
            await _configDb.InsertAllAsync(defaultSites);
        }

        /// <summary>
        /// Vérifie si un article existe déjà dans n'importe laquelle des bases de données quotidiennes.
        /// </summary>
        /// <param name="articleGuid">L'identifiant unique de l'article (URL ou GUID)</param>
        /// <returns>True si l'article est trouvé dans un fichier existant</returns>
        public async Task<bool> ItemExistsInAnyDatabaseAsync(string articleGuid)
        {
            if (string.IsNullOrEmpty(articleGuid)) return false;

            try
            {
                // 1. Récupérer tous les fichiers de données quotidiennes
                string path = FileSystem.AppDataDirectory;
                var dailyFiles = Directory.GetFiles(path, "RssProut_*.db3");

                foreach (var filePath in dailyFiles)
                {
                    // On crée une connexion temporaire pour chaque fichier trouvé
                    var tempConn = new SQLiteAsyncConnection(filePath, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.SharedCache);

                    try
                    {
                        // On vérifie si la table existe dans ce fichier avant de chercher
                        var tableInfo = await tempConn.GetTableInfoAsync("RssItem");
                        if (tableInfo.Any())
                        {
                            // On cherche l'article par son GUID (ou Link)
                            var existing = await tempConn.Table<RssItem>()
                                                       .Where(i => i.ArticleGuid == articleGuid || i.Link == articleGuid)
                                                       .FirstOrDefaultAsync();

                            if (existing != null)
                            {
                                await tempConn.CloseAsync();
                                Debug.WriteLine($"[DUPLICATE] Article trouvé dans : {Path.GetFileName(filePath)}");
                                return true; // Trouvé, on arrête la recherche
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Erreur lecture fichier {Path.GetFileName(filePath)}: {ex.Message}");
                    }
                    finally
                    {
                        await tempConn.CloseAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur lors de la recherche globale d'articles : {ex.Message}");
            }

            return false; // Introuvable dans toutes les bases scannées
        }



        public async Task<int> UpdateRssItemAsync(RssItem item)
        {
            await Init();
            return await _dailyDb.UpdateAsync(item);
        }

        // -----------------------------------------------------
        // --- Opérations de Playlist (Utilise _configDb) ---
        // -----------------------------------------------------

        public async Task<List<FeedPlaylist>> GetPlaylistsAsync()
        {
            await Init();
            return await _configDb.Table<FeedPlaylist>().ToListAsync();
        }

        public async Task<FeedPlaylist> GetPlaylistAsync(int id)
        {
            await Init();
            return await _configDb.Table<FeedPlaylist>().Where(p => p.Id == id).FirstOrDefaultAsync();
        }

        public async Task<int> SavePlaylistAsync(FeedPlaylist playlist)
        {
            await Init();
            if (playlist.Id != 0)
            {
                return await _configDb.UpdateAsync(playlist);
            }
            else
            {
                return await _configDb.InsertAsync(playlist);
            }
        }

        public async Task<int> DeletePlaylistAsync(FeedPlaylist playlist)
        {
            await Init();

            // Supprime les sites liés dans la config
            await _configDb.Table<FeedSite>().Where(s => s.PlaylistId == playlist.Id).DeleteAsync();

            // Supprime les articles liés dans la DB du jour uniquement 
            // (Note : les articles des jours précédents resteront dans leurs fichiers respectifs)
            await _dailyDb.Table<RssItem>().Where(i => i.PlaylistId == playlist.Id).DeleteAsync();

            return await _configDb.DeleteAsync(playlist);
        }

        // -----------------------------------------------------
        // --- Opérations de FeedSite (Utilise _configDb) ---
        // -----------------------------------------------------

        public async Task<List<FeedSite>> GetSitesForPlaylistAsync(int playlistId)
        {
            await Init();
            return await _configDb.Table<FeedSite>().Where(s => s.PlaylistId == playlistId).ToListAsync();
        }

        public async Task<int> SaveSiteAsync(FeedSite site)
        {
            await Init();
            if (site.Id != 0)
            {
                return await _configDb.UpdateAsync(site);
            }
            else
            {
                return await _configDb.InsertAsync(site);
            }
        }

        public async Task<int> DeleteSiteAsync(FeedSite site)
        {
            await Init();

            // Supprime les articles du site dans la DB du jour
            await _dailyDb.Table<RssItem>().Where(i => i.SiteId == site.Id).DeleteAsync();

            // Supprime le site de la config
            return await _configDb.DeleteAsync(site);
        }

        // ---------------------------------------------------
        // --- Opérations d'Articles (Cache & Détail) ---
        // ---------------------------------------------------

        // --- MÉTHODES POUR LES ARTICLES (RssItem) -> Utilise _dailyDb et le scan multi-fichiers ---

        public async Task<List<RssItem>> GetItemsForPlaylistAsync(int playlistId, int? siteId = null)
        {
            // On récupère les articles de TOUTES les bases de données horodatées
            var allItems = await GetAllArticlesFromAllDatabasesAsync();

            var query = allItems.Where(i => i.PlaylistId == playlistId);

            if (siteId.HasValue)
            {
                query = query.Where(i => i.SiteId == siteId.Value);
            }

            return query.OrderByDescending(i => i.PublishDate).ToList();
        }

        public async Task<RssItem> GetRssItemAsync(int id)
        {
            await Init();
            // 1. Chercher d'abord dans la base du jour (plus rapide)
            var item = await _dailyDb.Table<RssItem>().Where(i => i.Id == id).FirstOrDefaultAsync();

            if (item == null)
            {
                // 2. Si pas trouvé, chercher dans l'historique complet
                var all = await GetAllArticlesFromAllDatabasesAsync();
                item = all.FirstOrDefault(i => i.Id == id);
            }
            return item;
        }

        public async Task SaveItemsAsync(IEnumerable<RssItem> items)
        {
            // 1. Appel du mécanisme d'initialisation sécurisé (non-bloquant)
            await EnsureInitialized();

            // 2. Utilisation de la connexion sécurisée via le service
            await _dailyDb.RunInTransactionAsync(conn =>
            {
                foreach (var item in items)
                {
                    if (item.Id != 0)
                        conn.Update(item);
                    else
                        conn.Insert(item);
                }
            });
        }
        public async Task UpdateItemsWithContentAsync(IEnumerable<RssItem> items)
        {
            // 1. Appel du mécanisme d'initialisation sécurisé (non-bloquant)
            await EnsureInitialized();

            // 2. Utilisation de la connexion sécurisée
            // Le mode WAL étant activé, cette mise à jour ne bloquera pas vos lectures UI
            await _dailyDb.RunInTransactionAsync(conn =>
            {
                conn.UpdateAll(items);
            });
        }

        public async Task SaveArticleContentAsync(RssItem item, string fullHtmlContent)
        {
            if (item == null) return;

            // 1. Appel du mécanisme d'initialisation sécurisé (non-bloquant)
            await EnsureInitialized();

            // 2. Mise à jour de l'objet et persistance
            item.ContentHtml = fullHtmlContent;
            item.IsDownloaded = true;

            // 3. Utilisation de la connexion sécurisée
            // Comme le mode WAL est activé, cette opération ne bloquera pas les lectures de l'UI
            await _dailyDb.UpdateAsync(item);
        }

        public async Task MarkItemAsReadAsync(RssItem item)
        {
            if (item == null) return;
            await EnsureInitialized();
            item.IsRead = true;
            await _dailyDb.UpdateAsync(item);
        }

        public async Task<List<RssItem>> GetItemsToDownloadAsync(int playlistId)
        {
            await EnsureInitialized();
            // On ne télécharge du contenu que pour les articles présents dans la DB du jour
            return await _dailyDb.Table<RssItem>()
                    .Where(i => i.PlaylistId == playlistId && i.IsDownloaded == false)
                    .ToListAsync();
        }

        public async Task<List<string>> GetCachedArticleGuidsAsync(int playlistId)
        {
            await EnsureInitialized();            // Pour éviter les doublons, on vérifie les GUIDs dans TOUTES les bases
            var allItems = await GetAllArticlesFromAllDatabasesAsync();
            return allItems.Where(i => i.PlaylistId == playlistId)
                           .Select(i => i.ArticleGuid)
                           .ToList();
        }

        // --- MÉTHODES POUR LES SITES (FeedSite) -> Utilise _configDb ---

        public async Task<List<FeedSite>> GetSitesByPlaylistIdAsync(int playlistId)
        {
            await EnsureInitialized();
            return await _configDb.Table<FeedSite>()
                                  .Where(s => s.PlaylistId == playlistId)
                                  .ToListAsync();
        }

        // --- Opérations de FeedSite (Utilise _configDb) ---

        public async Task<int> UpdateSiteAsync(FeedSite site)
        {
            await EnsureInitialized();
            // Les sites sont dans la base de configuration
            return await _configDb.UpdateAsync(site);
        }

        // --- Opérations d'Articles et Cache (Utilise _dailyDb) ---

        public async Task<int> ClearPlaylistCacheAsync(int playlistId)
        {
            await EnsureInitialized();
            // Supprime les articles de la playlist dans la DB du jour
            return await _dailyDb.Table<RssItem>().Where(i => i.PlaylistId == playlistId).DeleteAsync();
        }

        // ---------------------------------------------------
        // --- Opérations d'Administration (Ajustées) ---
        // ---------------------------------------------------

        public string GetDatabasePath()
        {
            // On retourne le chemin de la base de configuration par défaut
            return ConfigDbPath;
        }

        public async Task<int> InsertArchiveAsync(ArchiveItem archive)
        {
            await EnsureInitialized();

            // 🚩 VÉRIFIE BIEN QUE C'EST _archiveDb ET PAS _dailyDb
            var existing = await _archiveDb.Table<ArchiveItem>()
                                           .Where(x => x.ArticleGuid == archive.ArticleGuid)
                                           .FirstOrDefaultAsync();

            if (existing != null) return 0;

            // 🚩 VÉRIFIE BIEN QUE C'EST _archiveDb ICI AUSSI
            return await _archiveDb.InsertAsync(archive);
        }

        public async Task<List<ArchiveItem>> GetArchivesAsync()
        {
            await EnsureInitialized();

            // Débogage : affiche le chemin du fichier pour être sûr
            Debug.WriteLine($"[DEBUG] Lecture des archives dans : {_archiveDb.DatabasePath}");

            var list = await _archiveDb.Table<ArchiveItem>()
                                       .OrderByDescending(x => x.ArchivedAt)
                                       .ToListAsync();

            Debug.WriteLine($"[DEBUG] Nombre d'archives trouvées : {list.Count}");
            return list;
        }

        // Ajoutez ceci dans SQLiteService.cs
        public async Task CleanAllContentInDatabaseAsync()
        {
            // On récupère tous les articles
            var items = await _dailyDb.Table<RssItem>().ToListAsync();

            foreach (var item in items)
            {
                // VÉRIFICATION : Le contenu existe ET ne contient PAS notre marqueur
                if (!string.IsNullOrEmpty(item.ContentHtml) && !item.ContentHtml.Contains(""))
                {
                    // 1. Nettoyage avec AngleSharp
                    string cleanedHtml = await CleanHtmlContent(item.ContentHtml);

                    // 2. On ajoute le marqueur à la fin
                    item.ContentHtml = cleanedHtml + "";

                    // 3. Mise à jour de la base
                    await _dailyDb.UpdateAsync(item);
                }
            }
        }
        public async Task<string> CleanHtmlContent(string htmlContent)
        {
            if (string.IsNullOrWhiteSpace(htmlContent)) return htmlContent;

            // 1. Initialisation d'AngleSharp
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(req => req.Content(htmlContent));

            // 2. Liste des éléments parasites à supprimer
            var parasites = new[] {
        "header", "footer", "nav", "aside", "form",
        ".cm-header", ".cm-top-bar", ".breadcrumb-itc", ".bloc-partage",
        ".related-posts-wrapper", ".thumbnail-pagination", ".cm-comments-link",
        ".aioseo-author-bio-compact", ".search-form",
        "[class*='menu']", "[class*='widget']", "[class*='sidebar']",
        "[class*='ad-']", "[class*='ads']",
        "iframe", "script", "style", "noscript",
        ".cm-featured-image", ".cm-entry-header-meta", ".cm-below-entry-meta"
    };

            // 3. Isoler le contenu principal
            var mainContent = document.QuerySelector("article")
                              ?? document.QuerySelector(".cm-post-content")
                              ?? document.QuerySelector(".cm-entry-content")
                              ?? document.Body;

            // 4. Suppression des éléments parasites
            foreach (var selector in parasites)
            {
                var elements = mainContent.QuerySelectorAll(selector);
                foreach (var el in elements) el.Remove();
            }

            // 5. Nettoyage des attributs pour alléger le HTML
            var allElements = mainContent.QuerySelectorAll("*");
            foreach (var el in allElements)
            {
                el.RemoveAttribute("style");
                el.RemoveAttribute("class");
                el.RemoveAttribute("id");
            }

            // 6. Suppression des conteneurs vides (garder les images)
            var emptyElements = mainContent.QuerySelectorAll("p, div, span");
            foreach (var el in emptyElements)
            {
                if (string.IsNullOrWhiteSpace(el.TextContent) && !el.QuerySelectorAll("img").Any())
                {
                    el.Remove();
                }
            }

            return mainContent.InnerHtml;
        }

        public async Task<int> DeleteArchiveAsync(ArchiveItem archive)
        {
            await EnsureInitialized();

            // On supprime depuis _archiveDb
            return await _archiveDb.DeleteAsync(archive);
        }

        public async Task<int> DeleteReadItemsAsync()
        {
            await EnsureInitialized();
            // Supprime les articles lus de la base du jour
            return await _dailyDb.Table<RssItem>()
                                 .Where(i => i.IsRead == true)
                                 .DeleteAsync();
        }

        public async Task<int> CleanupByTimeAsync(int value, string unit)
        {
            await EnsureInitialized();

            DateTime limitDate = unit.ToLower() switch
            {
                "mois" => DateTime.Now.AddMonths(-value),
                _ => DateTime.Now.AddDays(-value)
            };

            // On convertit la date en format ISO standard pour SQLite (YYYY-MM-DD HH:MM:SS)
            string limitDateStr = limitDate.ToString("yyyy-MM-dd HH:mm:ss");

            // On utilise ExecuteAsync avec une requête SQL pour comparer les chaînes
            return await _dailyDb.ExecuteAsync("DELETE FROM RssItem WHERE PublishDate < ?", limitDateStr);
        }

        // Dans SQLiteService.cs
        public async Task<FeedSite> GetSiteByIdAsync(int siteId)
        {
            await EnsureInitialized();
            // Les sites sont dans la base de configuration (_configDb)
            return await _configDb.Table<FeedSite>()
                                  .Where(s => s.Id == siteId)
                                  .FirstOrDefaultAsync();
        }

        public async Task<int> CleanupArchivesAsync(int months)
        {
            await EnsureInitialized();
            DateTime limitDate = DateTime.Now.AddMonths(-months);

            return await _dailyDb.Table<ArchiveItem>()
                                 .Where(x => x.ArchivedAt < limitDate)
                                 .DeleteAsync();
        }

        /// <summary>
        /// Vide complètement une table spécifique dans la DB du jour.
        /// </summary>
        public async Task<int> ClearTableAsync(string tableName)
        {
            await EnsureInitialized();
            // Par sécurité, on exécute sur la DB quotidienne
            return await _dailyDb.ExecuteAsync($"DELETE FROM {tableName}");
        }

        public async Task<ArchiveItem> GetArchiveByIdAsync(int id)
        {
            await EnsureInitialized();

            // On cherche DIRECTEMENT dans la base d'archives (_archiveDb)
            return await _archiveDb.Table<ArchiveItem>()
                                   .FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task<int> CleanupByDaysAsync(int days)
        {
            await EnsureInitialized();
            DateTime limitDate = DateTime.Now.AddDays(-days);
            return await CleanupItemsOlderThanAsync(limitDate);
        }

        /// <summary>
        /// Vide le cache d'articles de la base de données du jour.
        /// </summary>
        public async Task<int> ClearAllCacheAsync()
        {
            await EnsureInitialized();
            return await _dailyDb.DeleteAllAsync<RssItem>();
        }

        public async Task<int> CleanupItemsOlderThanAsync(DateTime dateLimit)
        {
            await Init();
            string dateStr = dateLimit.ToString("yyyy-MM-dd HH:mm:ss");

            // Suppression dans la DB quotidienne active
            return await _dailyDb.ExecuteAsync("DELETE FROM RssItem WHERE PublishDate < ?", dateStr);
        }

        public async Task<RssItem> GetItemByIdAsync(int id)
        {
            // Utilise le mécanisme sécurisé non-bloquant
            await EnsureInitialized();

            // 1. Cherche d'abord dans la DB du jour (la connexion est déjà ouverte et prête)
            var item = await _dailyDb.Table<RssItem>()
                                     .Where(i => i.Id == id)
                                     .FirstOrDefaultAsync();

            if (item != null) return item;

            // 2. Si pas trouvé, scanne les autres bases horodatées
            var all = await GetAllArticlesFromAllDatabasesAsync();
            return all.FirstOrDefault(i => i.Id == id);
        }

        public async Task<List<RssItem>> GetAllArticlesFromAllDatabasesAsync()
        {
            // Utilise le mécanisme sécurisé
            await EnsureInitialized();

            var allItems = new List<RssItem>();
            var files = Directory.GetFiles(FileSystem.AppDataDirectory, "RssProut_*.db3");

            foreach (var file in files)
            {
                try
                {
                    // Note: Pour les bases de lecture seule temporaires, le mode WAL est moins critique,
                    // mais c'est une bonne pratique de toujours ouvrir les connexions proprement.
                    var tempConn = new SQLiteAsyncConnection(file, SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.SharedCache);

                    var items = await tempConn.Table<RssItem>().ToListAsync();
                    allItems.AddRange(items);

                    await tempConn.CloseAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur sur {file}: {ex.Message}");
                }
            }

            return allItems.OrderByDescending(i => i.PublishDate).ToList();
        }
        /// <summary>
        /// Supprime TOUTES les bases de données (Config et toutes les DB horodatées).
        /// </summary>
        public async Task DeleteDatabaseFileAsync()
        {
            // 1. Fermer TOUTES les connexions proprement
            // Si une connexion reste ouverte, File.Delete échouera (fichier verrouillé)
            var connections = new[] { _configDb, _dailyDb, _articlesDb, _archiveDb };

            foreach (var conn in connections)
            {
                if (conn is not null)
                {
                    try { await conn.CloseAsync(); } catch { }
                }
            }

            // Réinitialiser les variables de connexion
            _configDb = null;
            _dailyDb = null;
            _articlesDb = null;
            _archiveDb = null;

            // Réinitialiser le flag d'initialisation pour permettre une reconstruction
            _isInitialized = false;

            try
            {
                // 2. Supprimer la base de configuration
                if (File.Exists(ConfigDbPath))
                {
                    File.Delete(ConfigDbPath);
                    Debug.WriteLine("Fichier Config DB supprimé.");
                }

                // 3. Supprimer l'index des articles
                if (File.Exists(ArticlesDbPath))
                {
                    File.Delete(ArticlesDbPath);
                    Debug.WriteLine("Fichier Articles DB supprimé.");
                }

                // 4. Supprimer les archives
                if (File.Exists(ArchiveDbPath))
                {
                    File.Delete(ArchiveDbPath);
                    Debug.WriteLine("Fichier Archives DB supprimé.");
                }

                // 5. Supprimer TOUS les fichiers quotidiens (RssProut_*.db3)
                var dailyFiles = Directory.GetFiles(FileSystem.AppDataDirectory, "RssProut_*.db3");
                foreach (var file in dailyFiles)
                {
                    try
                    {
                        File.Delete(file);
                        Debug.WriteLine($"Fichier quotidien supprimé : {file}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Impossible de supprimer {file} : {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur lors de la suppression massive : {ex.Message}");
                throw;
            }
        }
    }
}
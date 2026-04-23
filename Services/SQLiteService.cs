using Rss_feeder_prout.Models;
using SQLite;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Maui.Storage;
using System;

namespace Rss_feeder_prout.Services
{
    public class SQLiteService
    {
        private SQLiteAsyncConnection _database;

        private const string DbName = "RssProutDB.db3";
        private string DatabasePath => Path.Combine(FileSystem.AppDataDirectory, DbName);

        public SQLiteService()
        {
            // Bloque le thread jusqu'à l'initialisation de la BD
            Task.Run(async () => await Init()).Wait();
        }

        private async Task Init()
        {
            if (_database is not null)
                return;

            _database = new SQLiteAsyncConnection(DatabasePath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);

            // 🎯 Création des tables
            var createPlaylist = _database.CreateTableAsync<FeedPlaylist>();
            var createSites = _database.CreateTableAsync<FeedSite>();
            var createItems = _database.CreateTableAsync<RssItem>();

            var createArchives = _database.CreateTableAsync<ArchiveItem>(); // <--- AJOUTE CECI

            await Task.WhenAll(createPlaylist, createSites, createItems, createArchives); // <--- INCLURE ICI

            // LOGIQUE D'INITIALISATION : Créer Playlists et Sites
            if (await GetPlaylistsAsync() is not { Count: > 0 })
            {
                await CreateDefaultData();
            }
        }

        /// <summary>
        /// Crée les données par défaut (Playlists et Sites)
        /// (Ce code est conservé tel quel)
        /// </summary>
        private async Task CreateDefaultData()
        {
            // Liste pour stocker les sites par défaut
            var defaultSites = new List<FeedSite>();

            // --- 1. ACTUALITÉS TECH ---
            var playlistActuTech = new FeedPlaylist { Name = "Actualités Tech Générales", IsActive = true };
            await SavePlaylistAsync(playlistActuTech);

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
            await SavePlaylistAsync(playlistSecu);

            defaultSites.Add(new FeedSite { Name = "ZATAZ", FeedUrl = "https://www.zataz.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "UnderNews", FeedUrl = "https://www.undernews.fr/feed", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "CyberSecurityNews (FR)", FeedUrl = "https://www.cybersecuritynews.fr/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "BleepingComputer", FeedUrl = "https://www.bleepingcomputer.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "Dark Reading", FeedUrl = "https://www.darkreading.com/rss.xml", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "KrebsOnSecurity", FeedUrl = "https://krebsonsecurity.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "SecurityWeek", FeedUrl = "https://feeds.feedburner.com/Securityweek", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "The Hacker News", FeedUrl = "https://feeds.feedburner.com/TheHackersNews", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "Global Security Mag", FeedUrl = "https://www.globalsecuritymag.fr/spip.php?page=backend", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "MalwareTips", FeedUrl = "https://malwaretips.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "ThreatPost", FeedUrl = "https://threatpost.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "SANS Internet Storm Center", FeedUrl = "https://isc.sans.edu/rssfeed.html", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "WeLiveSecurity", FeedUrl = "https://www.welivesecurity.com/feed/", PlaylistId = playlistSecu.Id });
            defaultSites.Add(new FeedSite { Name = "CERT-FR", FeedUrl = "https://www.cert.ssi.gouv.fr/feed/", PlaylistId = playlistSecu.Id });

            // --- 3. MATÉRIEL / CPU / GPU / INNOVATIONS ---
            var playlistHardware = new FeedPlaylist { Name = "Matériel & Innovations", IsActive = true };
            await SavePlaylistAsync(playlistHardware);

            defaultSites.Add(new FeedSite { Name = "AnandTech", FeedUrl = "https://www.anandtech.com/rss/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "Tom's Hardware FR", FeedUrl = "https://www.tomshardware.fr/feed/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "TechPowerUp", FeedUrl = "https://www.techpowerup.com/rss/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "NotebookCheck", FeedUrl = "https://www.notebookcheck.net/rss.2365.0.html", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "Guru3D", FeedUrl = "https://www.guru3d.com/rssfeed/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "Phoronix", FeedUrl = "https://www.phoronix.com/rss.php", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "WCCFTech", FeedUrl = "https://wccftech.com/feed/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "Hardware.fr", FeedUrl = "https://www.hardware.fr/feeds/news.xml", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "PC Gamer", FeedUrl = "https://www.pcgamer.com/rss/", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "TechRadar", FeedUrl = "https://www.techradar.com/rss", PlaylistId = playlistHardware.Id });
            defaultSites.Add(new FeedSite { Name = "Digital Trends", FeedUrl = "https://www.digitaltrends.com/feed/", PlaylistId = playlistHardware.Id });

            // --- 4. IA / SCIENCE / TECHNOLOGIES AVANCÉES ---
            var playlistScience = new FeedPlaylist { Name = "IA & Sciences Avancées", IsActive = true };
            await SavePlaylistAsync(playlistScience);

            defaultSites.Add(new FeedSite { Name = "Futura Sciences", FeedUrl = "https://www.futura-sciences.com/rss/", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "Ars Technica", FeedUrl = "http://feeds.arstechnica.com/arstechnica/index", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "MIT Technology Review", FeedUrl = "https://www.technologyreview.com/feed/", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "IEEE Spectrum", FeedUrl = "https://spectrum.ieee.org/rss/feed", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "VentureBeat AI", FeedUrl = "https://venturebeat.com/category/ai/feed/", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "Developpez.com", FeedUrl = "https://rss.developpez.com/", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "Science et Vie", FeedUrl = "https://www.science-et-vie.com/rss.xml", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "Le Journal de la Science", FeedUrl = "https://www.lejournaldelascience.fr/feed", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "Nature", FeedUrl = "https://www.nature.com/subjects/news/rss.xml", PlaylistId = playlistScience.Id });
            defaultSites.Add(new FeedSite { Name = "New Scientist", FeedUrl = "https://www.newscientist.com/feed/home/", PlaylistId = playlistScience.Id });

            // --- 5. INTERNATIONAL TECH NEWS ---
            var playlistInternational = new FeedPlaylist { Name = "Actualités Tech Internationales", IsActive = true };
            await SavePlaylistAsync(playlistInternational);

            defaultSites.Add(new FeedSite { Name = "The Verge", FeedUrl = "https://www.theverge.com/rss/index.xml", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Engadget", FeedUrl = "https://www.engadget.com/rss.xml", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Wired", FeedUrl = "https://www.wired.com/feed/rss", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Gizmodo", FeedUrl = "https://gizmodo.com/rss", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "CNET", FeedUrl = "https://www.cnet.com/rss/news/", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "TechCrunch", FeedUrl = "http://feeds.feedburner.com/TechCrunch/", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "The Next Web", FeedUrl = "https://thenextweb.com/feed/", PlaylistId = playlistInternational.Id });
            defaultSites.Add(new FeedSite { Name = "Engadget UK", FeedUrl = "https://www.engadget.com/uk/rss.xml", PlaylistId = playlistInternational.Id });

            // --- 6. ACTUALITÉS QUÉBEC & FRANCOPHONIE ---
            var playlistQuebec = new FeedPlaylist { Name = "Actualités Québec", IsActive = true };
            await SavePlaylistAsync(playlistQuebec);

            defaultSites.Add(new FeedSite { Name = "Radio-Canada (Actualités)", FeedUrl = "https://ici.radio-canada.ca/rss/4159", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "La Presse (Actualités)", FeedUrl = "https://www.lapresse.ca/actualites/rss", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Le Devoir (Accueil)", FeedUrl = "https://www.ledevoir.com/rss/manchettes.xml", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "TVA Nouvelles (Actualités)", FeedUrl = "https://www.tvanouvelles.ca/actualites/rss.xml", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Journal de Montréal", FeedUrl = "https://www.journaldemontreal.com/rss.xml", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Les Affaires", FeedUrl = "https://www.lesaffaires.com/rss", PlaylistId = playlistQuebec.Id });
            defaultSites.Add(new FeedSite { Name = "Québec Science", FeedUrl = "https://www.quebecscience.qc.ca/feed/", PlaylistId = playlistQuebec.Id });

            // --- 7. PROGRAMMATION & DÉVELOPPEMENT ---
            var playlistDev = new FeedPlaylist { Name = "Programmation & Code", IsActive = true };
            await SavePlaylistAsync(playlistDev);

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
            // Très utile pour le côté technicien réseau et l'automatisation
            var playlistSysAdmin = new FeedPlaylist { Name = "Systèmes & Réseaux", IsActive = true };
            await SavePlaylistAsync(playlistSysAdmin);

            defaultSites.Add(new FeedSite { Name = "IT-Connect", FeedUrl = "https://www.it-connect.fr/feed/", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Le Monde Informatique", FeedUrl = "https://www.lemondeinformatique.fr/flux-rss/general/rss.xml", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Cisco Blog", FeedUrl = "https://blogs.cisco.com/feed", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Veeam Blog (FR)", FeedUrl = "https://www.veeam.com/blog/fr/feed", PlaylistId = playlistSysAdmin.Id });
            defaultSites.Add(new FeedSite { Name = "Docker Blog", FeedUrl = "https://www.docker.com/blog/feed/", PlaylistId = playlistSysAdmin.Id });

            // --- 9. ACTUALITÉS LOCALES (CHAUDIÈRE-APPALACHES) ---
            // Pour rester au courant de ce qui se passe directement à Thetford et aux alentours
            var playlistLocale = new FeedPlaylist { Name = "Nouvelles Locales", IsActive = true };
            await SavePlaylistAsync(playlistLocale);

            defaultSites.Add(new FeedSite { Name = "Courrier Frontenac", FeedUrl = "https://www.courrierfrontenac.qc.ca/actualites/feed/", PlaylistId = playlistLocale.Id });
            defaultSites.Add(new FeedSite { Name = "Beauce Media", FeedUrl = "https://www.beaucemedia.ca/actualites/feed/", PlaylistId = playlistLocale.Id });
            defaultSites.Add(new FeedSite { Name = "En Beauce", FeedUrl = "https://www.enbeauce.com/rss/nouvelles", PlaylistId = playlistLocale.Id });
            defaultSites.Add(new FeedSite { Name = "Cool FM News", FeedUrl = "https://www.coolfm.biz/nouvelles/rss", PlaylistId = playlistLocale.Id });

            // --- 10. LINUX & OPEN SOURCE ---
            // Indispensable pour la culture logicielle libre
            var playlistLinux = new FeedPlaylist { Name = "Linux & Open Source", IsActive = true };
            await SavePlaylistAsync(playlistLinux);

            defaultSites.Add(new FeedSite { Name = "LinuxFR.org", FeedUrl = "https://linuxfr.org/news.atom", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "OMG! Ubuntu!", FeedUrl = "https://www.omgubuntu.co.uk/feed", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "It's FOSS", FeedUrl = "https://itsfoss.com/feed/", PlaylistId = playlistLinux.Id });
            defaultSites.Add(new FeedSite { Name = "Framablog", FeedUrl = "https://framablog.org/feed/", PlaylistId = playlistLinux.Id });

            // --- 11. JEUX VIDÉO & TECH GAMING ---
            var playlistGaming = new FeedPlaylist { Name = "Jeux Vidéo", IsActive = true };
            await SavePlaylistAsync(playlistGaming);

            defaultSites.Add(new FeedSite { Name = "JeuxVideo.com", FeedUrl = "https://www.jeuxvideo.com/rss/rss.xml", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Gamekult", FeedUrl = "https://www.gamekult.com/flux-rss.html", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "Kotaku", FeedUrl = "https://kotaku.com/rss", PlaylistId = playlistGaming.Id });
            defaultSites.Add(new FeedSite { Name = "IGN France", FeedUrl = "https://fr.ign.com/feed.xml", PlaylistId = playlistGaming.Id });

            // --- 12. DOMOTIQUE & AUTOMATISATION ---
            var playlistHomeAuto = new FeedPlaylist { Name = "Domotique & Smart Home", IsActive = true };
            await SavePlaylistAsync(playlistHomeAuto);

            defaultSites.Add(new FeedSite { Name = "Domo-Blog", FeedUrl = "https://www.domo-blog.fr/feed/", PlaylistId = playlistHomeAuto.Id });
            defaultSites.Add(new FeedSite { Name = "Abavala!", FeedUrl = "https://www.abavala.com/feed/", PlaylistId = playlistHomeAuto.Id });
            defaultSites.Add(new FeedSite { Name = "Home Assistant Blog", FeedUrl = "https://www.home-assistant.io/atom.xml", PlaylistId = playlistHomeAuto.Id });
            defaultSites.Add(new FeedSite { Name = "Toute la Domotique", FeedUrl = "https://www.touteladomotique.com/index.php?option=com_content&view=featured&format=feed&type=rss", PlaylistId = playlistHomeAuto.Id });

            // --- 13. ÉLECTRONIQUE & MAKER (ESP32 / ARDUINO) ---
            var playlistMaker = new FeedPlaylist { Name = "Électronique & Maker", IsActive = true };
            await SavePlaylistAsync(playlistMaker);

            defaultSites.Add(new FeedSite { Name = "Hackaday", FeedUrl = "https://hackaday.com/blog/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Adafruit Blog", FeedUrl = "https://blog.adafruit.com/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Make: Magazine", FeedUrl = "https://makezine.com/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Electronics Weekly", FeedUrl = "https://www.electronicsweekly.com/news/feed/", PlaylistId = playlistMaker.Id });
            defaultSites.Add(new FeedSite { Name = "Framboise 314", FeedUrl = "https://www.framboise314.fr/feed/", PlaylistId = playlistMaker.Id });

            // --- 14. DRONES & ROBOTIQUE ---
            var playlistDrones = new FeedPlaylist { Name = "Drones & Robotique", IsActive = true };
            await SavePlaylistAsync(playlistDrones);

            defaultSites.Add(new FeedSite { Name = "HelicoMicro", FeedUrl = "https://www.helicomicro.com/feed/", PlaylistId = playlistDrones.Id });
            defaultSites.Add(new FeedSite { Name = "DroneDJ", FeedUrl = "https://dronedj.com/feed/", PlaylistId = playlistDrones.Id });
            defaultSites.Add(new FeedSite { Name = "RobotShop Community", FeedUrl = "https://www.robotshop.com/community/blog/feed", PlaylistId = playlistDrones.Id });
            defaultSites.Add(new FeedSite { Name = "sUAS News", FeedUrl = "https://www.suasnews.com/feed/", PlaylistId = playlistDrones.Id });

            // --- 15. HUMOUR & BD DU JOUR ---
            var playlistHumour = new FeedPlaylist { Name = "Humour & Strips BD", IsActive = true };
            await SavePlaylistAsync(playlistHumour);

            // Humour Francophone
            defaultSites.Add(new FeedSite { Name = "Viedemerde (VDM)", FeedUrl = "https://www.viedemerde.fr/rss", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "DTC (Dans Ton Chat)", FeedUrl = "https://danstonchat.com/rss.xml", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Le Gorafi (Parodie)", FeedUrl = "https://www.legorafi.fr/feed/", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Bouletcorp (BD)", FeedUrl = "https://www.bouletcorp.com/feed/", PlaylistId = playlistHumour.Id });

            // Comics & Mini Strips Internationaux
            defaultSites.Add(new FeedSite { Name = "xkcd (Humour Geek)", FeedUrl = "https://xkcd.com/rss.xml", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "CommitStrip (Vie de Dev)", FeedUrl = "https://www.commitstrip.com/fr/feed/", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Cyanide & Happiness", FeedUrl = "https://feeds.feedburner.com/Explosm", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Dilbert (Vie de bureau)", FeedUrl = "https://dilbert.com/feed", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Garfield", FeedUrl = "https://www.gocomics.com/garfield.rss", PlaylistId = playlistHumour.Id });
            defaultSites.Add(new FeedSite { Name = "Calvin and Hobbes", FeedUrl = "https://www.gocomics.com/calvinandhobbes.rss", PlaylistId = playlistHumour.Id });
            
            // Sauvegarder tous les sites
            await _database.InsertAllAsync(defaultSites);
        }

        public async Task<int> UpdateRssItemAsync(RssItem item)
        {
            await Init();
            return await _database.UpdateAsync(item);
        }



        // -----------------------------------------------------
        // --- Opérations de Playlist (Inchangées) ---
        // -----------------------------------------------------

        public Task<List<FeedPlaylist>> GetPlaylistsAsync()
        {
            return _database.Table<FeedPlaylist>().ToListAsync();
        }

        public Task<FeedPlaylist> GetPlaylistAsync(int id)
        {
            return _database.Table<FeedPlaylist>().Where(p => p.Id == id).FirstOrDefaultAsync();
        }

        public Task<int> SavePlaylistAsync(FeedPlaylist playlist)
        {
            if (playlist.Id != 0)
            {
                return _database.UpdateAsync(playlist);
            }
            else
            {
                return _database.InsertAsync(playlist);
            }
        }

        public Task<int> DeletePlaylistAsync(FeedPlaylist playlist)
        {
            Task.WaitAll(
              _database.Table<FeedSite>().Where(s => s.PlaylistId == playlist.Id).DeleteAsync(),
              _database.Table<RssItem>().Where(i => i.PlaylistId == playlist.Id).DeleteAsync()
            );

            return _database.DeleteAsync(playlist);
        }

        // -----------------------------------------------------
        // --- Opérations de FeedSite (Inchangées) ---
        // -----------------------------------------------------

        public Task<List<FeedSite>> GetSitesForPlaylistAsync(int playlistId)
        {
            return _database.Table<FeedSite>().Where(s => s.PlaylistId == playlistId).ToListAsync();
        }

        public Task<int> SaveSiteAsync(FeedSite site)
        {
            if (site.Id != 0)
            {
                return _database.UpdateAsync(site);
            }
            else
            {
                return _database.InsertAsync(site);
            }
        }

        public Task<int> DeleteSiteAsync(FeedSite site)
        {
            _database.Table<RssItem>().Where(i => i.SiteId == site.Id).DeleteAsync();
            return _database.DeleteAsync(site);
        }

        // ---------------------------------------------------
        // --- Opérations d'Articles (Cache & Détail) ---
        // ---------------------------------------------------

        public Task<List<RssItem>> GetItemsForPlaylistAsync(int playlistId, int? siteId = null)
        {
            var query = _database.Table<RssItem>().Where(i => i.PlaylistId == playlistId);

            if (siteId.HasValue)
            {
                query = query.Where(i => i.SiteId == siteId.Value);
            }

            return query.OrderByDescending(i => i.PublishDate).ToListAsync();
        }

        public Task<RssItem> GetRssItemAsync(int id)
        {
            return _database.Table<RssItem>().Where(i => i.Id == id).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Gère l'insertion ou la mise à jour (Upsert) pour une collection d'articles.
        /// </summary>
        public async Task SaveItemsAsync(IEnumerable<RssItem> items)
        {
            await _database.RunInTransactionAsync(conn =>
            {
                foreach (var item in items)
                {
                    if (item.Id != 0)
                    {
                        conn.Update(item);
                    }
                    else
                    {
                        conn.Insert(item);
                    }
                }
            });
        }

        /// <summary>
        /// 🎯 NOUVEAUTÉ : Gère la mise à jour par lots pour une collection d'articles TÉLÉCHARGÉS.
        /// Ceci est utilisé par RssService.DownloadAllContentForPlaylistAsync.
        /// </summary>
        public async Task UpdateItemsWithContentAsync(IEnumerable<RssItem> items)
        {
            await _database.RunInTransactionAsync(conn =>
            {
                // UpdateAll est plus rapide que des Update individuels dans une boucle
                conn.UpdateAll(items);
            });
        }

        /// <summary>
        /// 🎯 NOUVEAUTÉ : Enregistre le contenu détaillé (HTML) d'un article après téléchargement (utilisé pour les téléchargements individuels).
        /// </summary>
        public async Task SaveArticleContentAsync(RssItem item, string fullHtmlContent)
        {
            if (item == null) return;

            item.ContentHtml = fullHtmlContent;
            item.IsDownloaded = true;

            await _database.UpdateAsync(item);
        }

        /// <summary>
        /// Marque un article comme lu.
        /// </summary>
        public async Task MarkItemAsReadAsync(RssItem item)
        {
            if (item == null || item.Id == 0) return;
            item.IsRead = true;
            await _database.UpdateAsync(item);
        }

        /// <summary>
        /// 🎯 NOUVEAUTÉ : Récupère tous les articles qui n'ont pas encore été téléchargés (IsDownloaded == false) pour une playlist donnée.
        /// Ceci est utilisé par RssService.DownloadAllContentForPlaylistAsync.
        /// </summary>
        public Task<List<RssItem>> GetItemsToDownloadAsync(int playlistId)
        {
            return _database.Table<RssItem>()
                    .Where(i => i.PlaylistId == playlistId && i.IsDownloaded == false)
                    .ToListAsync();
        }

        /// <summary>
        /// Récupère la liste des GUIDs/URLs des articles déjà en cache.
        /// </summary>
        public async Task<List<string>> GetCachedArticleGuidsAsync(int playlistId)
        {
            var itemsInPlaylist = await _database.Table<RssItem>()
                                 .Where(i => i.PlaylistId == playlistId)
                                 .ToListAsync();

            return itemsInPlaylist.Select(i => i.ArticleGuid).ToList();
        }

        // Récupère tous les sites liés à une playlist spécifique
        public async Task<List<FeedSite>> GetSitesByPlaylistIdAsync(int playlistId)
        {
            await Init(); // Assurez-vous que la connexion est initialisée
            return await _database.Table<FeedSite>()
                                  .Where(s => s.PlaylistId == playlistId)
                                  .ToListAsync();
        }

        // Met à jour un site existant (pour sauvegarder le LocalIconPath par exemple)
        public async Task<int> UpdateSiteAsync(FeedSite site)
        {
            await Init();
            return await _database.UpdateAsync(site);
        }

        public Task ClearPlaylistCacheAsync(int playlistId)
        {
            return _database.Table<RssItem>().Where(i => i.PlaylistId == playlistId).DeleteAsync();
        }

        // ---------------------------------------------------
        // --- Opérations d'Administration (Inchangées) ---
        // ---------------------------------------------------

        public string GetDatabasePath()
        {
            return DatabasePath;
        }

        public async Task<int> InsertArchiveAsync(ArchiveItem archive)
        {
            await Init();

            // Vérification anti-doublon par le GUID de l'article original
            var existing = await _database.Table<ArchiveItem>()
                                          .Where(x => x.ArticleGuid == archive.ArticleGuid)
                                          .FirstOrDefaultAsync();

            if (existing != null)
                return 0; // L'article est déjà archivé

            return await _database.InsertAsync(archive);
        }

        /// <summary>
        /// Récupère la liste de tous les articles archivés, du plus récent au plus ancien.
        /// </summary>
        public async Task<List<ArchiveItem>> GetArchivesAsync()
        {
            await Init();
            return await _database.Table<ArchiveItem>()
                                  .OrderByDescending(x => x.ArchivedAt)
                                  .ToListAsync();
        }

        /// <summary>
        /// Supprime un article des archives.
        /// </summary>
        public async Task<int> DeleteArchiveAsync(ArchiveItem archive)
        {
            await Init();
            return await _database.DeleteAsync(archive);
        }

        public async Task<int> DeleteReadItemsAsync()
        {
            await Init();
            // Supprime uniquement les articles marqués comme lus dans la table principale
            return await _database.Table<RssItem>()
                                  .Where(i => i.IsRead == true)
                                  .DeleteAsync();
        }

        public async Task<int> CleanupByTimeAsync(int value, string unit)
        {
            await Init();
            DateTime limitDate = unit.ToLower() switch
            {
                "mois" => DateTime.Now.AddMonths(-value),
                _ => DateTime.Now.AddDays(-value)
            };

            string dateStr = limitDate.ToString("yyyy-MM-dd HH:mm:ss");
            return await _database.ExecuteAsync("DELETE FROM RssItem WHERE PublishDate < ?", dateStr); 
}

        /// <summary>
        /// Supprime les archives plus vieilles qu'un certain temps.
        /// </summary>
        public async Task<int> CleanupArchivesAsync(int months)
        {
            await Init();
            DateTime limitDate = DateTime.Now.AddMonths(-months);
            string dateStr = limitDate.ToString("yyyy-MM-dd HH:mm:ss");
            return await _database.ExecuteAsync("DELETE FROM ArchiveItem WHERE ArchivedAt < ?", dateStr); 
}

        /// <summary>
        /// Vide complètement une table spécifique.
        /// </summary>
        public async Task<int> ClearTableAsync(string tableName)
        {
            await Init();
            return await _database.ExecuteAsync($"DELETE FROM {tableName}"); 
        }

        public async Task<ArchiveItem> GetArchiveByIdAsync(int id)
        {
            await Init();
            return await _database.Table<ArchiveItem>().Where(a => a.Id == id).FirstOrDefaultAsync();
        }

        public async Task<int> CleanupByDaysAsync(int days)
        {
            await Init();
            DateTime limitDate = DateTime.Now.AddDays(-days);
            return await CleanupItemsOlderThanAsync(limitDate);
        }

        /// <summary>
        /// Supprime TOUS les articles de la table RssItem (le cache), 
        /// mais garde les Playlists, les Sites et les Archives.
        /// </summary>
        public async Task<int> ClearAllCacheAsync()
        {
            await Init();
            return await _database.DeleteAllAsync<RssItem>();
        }

        public async Task<int> CleanupItemsOlderThanAsync(DateTime dateLimit)
        {
            await Init();

            // SQLite-net peut comparer les dates si elles sont stockées correctement.
            // Sinon, on filtre par date via une requête SQL brute sécurisée :
            string dateStr = dateLimit.ToString("yyyy-MM-dd HH:mm:ss");

            // On ne supprime que dans la table RssItem
            // Les ArchiveItem sont dans une autre table, donc ils sont en sécurité.
            return await _database.ExecuteAsync("DELETE FROM RssItem WHERE PublishDate < ?", dateStr);
        }


        public async Task<RssItem> GetItemByIdAsync(int id)
        {
            await Init();

            // Utilise la méthode GetAsync<T> de SQLite-net pour chercher l'élément par clé primaire (Id)
            // Note : Si l'élément n'existe pas, GetAsync lèvera une exception.
            // On utilise TryGetAsync pour gérer l'absence sans exception si disponible, 
            // ou on utilise la requête LINQ ci-dessous.

            return await _database.Table<RssItem>()
                                  .Where(i => i.Id == id)
                                  .FirstOrDefaultAsync();
        }

        // 🎯 MÉTHODE OPTIONNELLE : Mise à jour de l'état de lecture
        // Elle est utilisée dans ArticleDetailViewModel, assurez-vous qu'elle existe aussi
        
        public async Task DeleteDatabaseFileAsync()
        {
            if (_database is not null)
            {
                await _database.CloseAsync();
                _database = null;
            }

            if (File.Exists(DatabasePath))
            {
                try
                {
                    File.Delete(DatabasePath);
                    Debug.WriteLine("Fichier de base de données supprimé.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Erreur lors de la suppression du fichier DB: {ex.Message}");
                    throw;
                }
            }
        }
    }
}
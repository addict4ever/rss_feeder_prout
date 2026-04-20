using SQLite;
using Rss_feeder_prout.ViewModels;

namespace Rss_feeder_prout.Models
{
    // Table pour stocker les groupes de flux RSS (les catégories)
    public class FeedPlaylist
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Name { get; set; }

        // 🎯 SUPPRIMÉES : Les propriétés UrlsSerialized, Urls et UrlCount
        // Ces informations sont maintenant stockées et gérées par la classe FeedSite.

        // L'état IsActive n'est pas utilisé dans le code fourni, mais est conservé
        // au cas où il aurait une utilité future pour l'interface utilisateur.
        public bool IsActive { get; internal set; }
    }
}
using SQLite;

/// <summary>
/// ANCIEN index global des articles (Articles.db3). Conservé pour compatibilité :
/// l'unicité est maintenant assurée directement par l'index (SiteId, ArticleGuid) de RssArticles.db3.
/// </summary>
public class ArticleIndex
{
    [PrimaryKey]
    public string ArticleGuid { get; set; } = string.Empty; // Identifiant unique
    public DateTime DateAdded { get; set; }
}

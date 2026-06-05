using SQLite;

public class ArticleIndex
{
    [PrimaryKey]
    public string ArticleGuid { get; set; } // Identifiant unique
    public DateTime DateAdded { get; set; }
}
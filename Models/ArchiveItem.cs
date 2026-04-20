using SQLite;
using Rss_feeder_prout.ViewModels;
using System;
using Rss_feeder_prout.Helpers;
using Microsoft.Maui.Graphics;

namespace Rss_feeder_prout.Models
{
    [Table("Archives")] // On spécifie un nom de table différent
    public class ArchiveItem : BaseViewModel
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Title { get; set; }
        public string Summary { get; set; }
        public string PublishDate { get; set; }
        public string ArticleGuid { get; set; }
        public string Link { get; set; }

        // Date à laquelle l'article a été archivé
        public DateTime ArchivedAt { get; set; } = DateTime.Now;

        public string Author { get; set; }
        public string ImageUrl { get; set; }

        [MaxLength(25000)]
        public string ContentHtml { get; set; }

        private string _siteName;
        [Ignore]
        public string SiteName
        {
            get => _siteName;
            set => SetProperty(ref _siteName, value);
        }

        [Ignore]
        public string PreviewText => !string.IsNullOrWhiteSpace(ContentHtml)
            ? HtmlHelper.StripHtml(ContentHtml)
            : Summary;
    }
}
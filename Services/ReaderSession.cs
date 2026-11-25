using Rss_feeder_prout.Models;

namespace Rss_feeder_prout.Services
{
    /// <summary>
    /// Mémorise la liste d'où l'on a ouvert un article, pour naviguer
    /// "article précédent / suivant" directement depuis le lecteur.
    /// </summary>
    public class ReaderSession
    {
        private readonly List<RssItem> _items = new();
        private readonly object _lock = new();

        public int CurrentIndex { get; private set; } = -1;

        public void SetList(IEnumerable<RssItem> items, RssItem current)
        {
            lock (_lock)
            {
                _items.Clear();
                _items.AddRange(items);
                CurrentIndex = _items.IndexOf(current);
                if (CurrentIndex < 0)
                {
                    _items.Insert(0, current);
                    CurrentIndex = 0;
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
                CurrentIndex = -1;
            }
        }

        public int Count
        {
            get { lock (_lock) return _items.Count; }
        }

        public bool HasPrevious
        {
            get { lock (_lock) return CurrentIndex > 0; }
        }

        public bool HasNext
        {
            get { lock (_lock) return CurrentIndex >= 0 && CurrentIndex < _items.Count - 1; }
        }

        public string PositionText
        {
            get { lock (_lock) return _items.Count > 1 && CurrentIndex >= 0 ? $"{CurrentIndex + 1} / {_items.Count}" : string.Empty; }
        }

        public RssItem? MovePrevious()
        {
            lock (_lock)
            {
                if (CurrentIndex <= 0) return null;
                CurrentIndex--;
                return _items[CurrentIndex];
            }
        }

        public RssItem? MoveNext()
        {
            lock (_lock)
            {
                if (CurrentIndex < 0 || CurrentIndex >= _items.Count - 1) return null;
                CurrentIndex++;
                return _items[CurrentIndex];
            }
        }

        /// <summary>Prochain article non lu après la position courante.</summary>
        public RssItem? MoveNextUnread()
        {
            lock (_lock)
            {
                for (int i = CurrentIndex + 1; i < _items.Count; i++)
                {
                    if (!_items[i].IsRead)
                    {
                        CurrentIndex = i;
                        return _items[i];
                    }
                }
                return null;
            }
        }
    }
}

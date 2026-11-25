using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rss_feeder_prout.Models
{
    /// <summary>
    /// Base légère pour les modèles (SQLite + UI). Contrairement à BaseViewModel,
    /// elle n'ajoute AUCUNE propriété publique (Title, IsBusy...) qui serait mappée par SQLite.
    /// </summary>
    public abstract class ObservableModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// CORRIGÉ : les téléchargements modifient les articles depuis des threads d'arrière-plan.
        /// Sur Android, toucher l'interface hors du thread UI fait planter l'app : on y revient donc toujours.
        /// </summary>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler == null) return;

            if (Microsoft.Maui.ApplicationModel.MainThread.IsMainThread)
                handler(this, new PropertyChangedEventArgs(propertyName));
            else
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => handler(this, new PropertyChangedEventArgs(propertyName)));
        }

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>Notifie plusieurs propriétés calculées d'un coup.</summary>
        protected void Notify(params string[] propertyNames)
        {
            foreach (var name in propertyNames)
                OnPropertyChanged(name);
        }
    }
}


using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;


namespace SiBallaImportingData.Service
{
    public class ObservableRangeCollection<T> : ObservableCollection<T>
    {
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            CheckReentrancy();

            // Aggiungi gli items alla collezione sottostante
            foreach (var item in items)
            {
                //for(int i = 0; i< 100;i++)
                //    Items.Add(item[i]);
                Items.Add(item);
            }

            // Notifica del cambiamento
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                new List<T>(items),
                Items.Count - items.Count()));

            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        }

        // Opzionale: implementare RemoveRange se necessario
        public void RemoveRange(IEnumerable<T> items)
        {
            // Implementazione simile
        }
    }
}

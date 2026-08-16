using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SiBallaImportingData.Model
{
    // NUOVA CLASSE: Rappresenta il gruppo della singola città e contiene gli eventi
    public class CityGroup : ObservableCollection<Event>
    {
        public string CityName { get; set; }

        public CityGroup(string cityName, IEnumerable<Event> events) : base(events)
        {
            CityName = cityName;
        }
    }
    public partial class GroupByDate : ObservableCollection<Event?>
    {
        public DateTime DancePartyDate { get; set; }
        public string Month { get; set; }
        public string MonthYear { get; set; }
        public GroupByDate(string month, IEnumerable<Event> parties) : base(parties)
        {
            MonthYear = month;
        }
    }

    public partial class GroupByDateNew : ObservableCollection<CityGroup>
    {
        public DateTime DancePartyDate { get; set; }
        public string Month { get; set; }
        public string MonthYear { get; set; }

        // 2. Il costruttore deve accettare IEnumerable<CityGroup> e passarlo al base()
        public GroupByDateNew(string month, IEnumerable<CityGroup> cities) : base(cities)
        {
            MonthYear = month;
        }
    }
}

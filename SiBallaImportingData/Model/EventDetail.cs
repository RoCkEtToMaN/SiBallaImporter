

using SiBallaImportingData.Model;

namespace SiBallaImportingData.Model
{
    public class EventDetail : Event
    {
        //public int Id { get; set; }        
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }
        public string? Teachers { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? WebSite { get; set; }
        public string When { get; set; }
        //public string Styles { get; set; }
        //public string City { get; set; }
        //public string Country { get; set; }
        //public Command<string> OpenWebsiteCommand { get; set; }      
        public bool IsRefreshing { get; set; }
        public bool IsBusy { get; set; }
        public List<StileEvento>? Styles { get; set; }
        public string? StylesToString { get; set; }
    }
    public enum StileEvento
    {
        Lindy,
        Blues,
        Balboa,
        Boogie,
        Shag,
        SoloJazz,
        Aerials,
        Afrodance,
        Charleston,
        SlowBal,
        StLouisShag
    }
}

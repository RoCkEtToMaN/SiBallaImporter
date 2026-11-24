
namespace SiBallaImportingData.Model
{
    public class EventDetail : Event
    {
        //public int Id { get; set; }        
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Teachers { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Address { get; set; }
        public string? WebSite { get; set; }        
        public string? ImageUrl { get; set; }
        // URL della miniatura WebP usata nelle card dell'app; il sito continua a usare ImageUrl.
        public string? ImageThumbnailUrl { get; set; }
        // colonna "Immagine" dello Sheet: l'immagine è su Appwrite Storage e ImageUrl ne contiene il link
        public bool HasImage { get; set; }
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
        StLouisShag,
        RhythmNBlues    // 11 (24/11/2026): l'app usa gli stessi numeri
    }
}

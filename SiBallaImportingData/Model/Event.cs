
namespace SiBallaImportingData.Model
{
    public class Event
    {        
        public int Id { get; set; }
        public EventDetail EventDetail { get; set; }       
        public string? PartyName { get; set; }
        public string? WorkshopName { get; set; }
        private DateTime dateStart;
        public DateTime DateStart 
        { 
            get { return dateStart; }
            set
            {
                dateStart = value;
                
                Month = MonthList[dateStart.Month - 1];
            }
        }      
        public DateTime? DateFinish { get; set; }     
        public TimeSpan Duration { get; set; }
        public string? City { get; set; }  
        public string? Country { get;set; }
        public Type? Type { get; set; }
        public string? Organizer { get; set; }
        public string? NameAndData { get; set; }
        public string? Month { get; set; }
        public string? MonthYear { get; set; }
        public bool TeachersConfirmed { get; set; }
        public bool ExchangeConfirmed { get; set; } 
        public bool Gratis { get; set; }

        public List<string> MonthList = new List<string> 
        { 
            "January",
            "February",
            "March",
            "April",
            "May",
            "June",
            "July",
            "August",
            "September",
            "October",
            "November",
            "December"
        };        
    }

    public enum Type
    {
        Event,
        Workshop
    }
}

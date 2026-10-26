using System.Text.Json.Serialization;

namespace SiBallaImportingData.Model
{
    public class Event
    {
        public int Id { get; set; }

        // posizione nello sheet: valorizzata solo per gli eventi letti dallo sheet, non va su AppWrite
        [JsonIgnore] public string? SheetName { get; set; }
        [JsonIgnore] public int? SheetRow { get; set; }             // numero di riga (1 = prima riga)
        [JsonIgnore] public string? SheetLocationRaw { get; set; }  // contenuto originale della colonna F

        // problema sul luogo trovato durante l'import (es. "coordinate del centro città"): da sistemare prima della mappa
        [JsonIgnore] public string? ProblemaLuogo { get; set; }
        // senza città non si sa nemmeno se è dentro il raggio: va nella sezione "Luogo da verificare" dell'email
        [JsonIgnore] public bool LuogoSconosciuto { get; set; }
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
        Workshop,
        Serate
    }
}

using SiBallaImportingData.Model;
using SiBallaImportingData.Utils;

Event Make(string title, string organizer, int day, int hours = 0) => new()
{
    PartyName = title, Organizer = organizer, City = "Genova",
    DateStart = new DateTime(2026, 11, day), Duration = TimeSpan.FromHours(hours)
};
void Check(bool success, string name)
{
    if (!success) throw new Exception(name);
    Console.WriteLine($"OK: {name}");
}
var luzzati = Make("Luzzati social swing dance", "Garage hoppers", 19);
var social = Make("Social Swing Dance", "Garage Hoppers", 19);
var moochers = Make("21 NOVEMBRE - THE MOOCHERS LIVE", "The Zenaswingers", 21);
var technicolor = Make("THE MOOCHERS LIVE - Swing in Technicolor", "The Zenaswingers", 21);
var shim = Make("Shim Sham + Social Dance con Your Swing!", "Your Swing", 22);
var complete = Make(shim.PartyName!, "Your Swing", 22, 4);
Check(EventIdentity.Same(luzzati, social), "Luzzati: titoli diversi");
Check(EventIdentity.Same(moochers, technicolor), "Moochers: data e sottotitolo");
Check(EventIdentity.Same(shim, complete), "Shim Sham: durata zero e quattro ore");
Check(EventIdentity.Unique(new[] { shim, complete }).Single() == complete, "Mantiene durata nota");
Check(EventIdentity.Unique(new[] { complete, shim }).Single() == complete, "Indipendente dall'ordine");
Check(EventIdentity.Unique(new[] { luzzati, social, moochers, technicolor, shim, complete }).Count == 3, "Sei righe diventano tre");
var otherDay = Make(social.PartyName!, social.Organizer!, 20);
Check(!EventIdentity.Same(social, otherDay), "Giorni distinti");
var otherCity = Make(social.PartyName!, social.Organizer!, 19);
otherCity.City = "Savona";
Check(!EventIdentity.Same(social, otherCity), "Citta distinte");
var otherOrganizer = Make(luzzati.PartyName!, "Altro organizzatore", 19);
Check(!EventIdentity.Same(social, otherOrganizer), "Varianti con organizzatori diversi");
var early = Make(shim.PartyName!, shim.Organizer!, 22);
var late = Make(shim.PartyName!, shim.Organizer!, 22);
early.DateStart = early.DateStart.AddHours(16);
late.DateStart = late.DateStart.AddHours(21);
Check(!EventIdentity.Same(early, late), "Orari espliciti distinti");
Check(!EventIdentity.Same(luzzati, Make("Social Swing Dance al porto", "Garage Hoppers", 19)), "Titoli con luoghi distinti");

// 18/11/2026: righe riaggiunte a ogni giro perche' nello Sheet la citta' era stata riscritta
Event Luogo(string city, double lat, double lng) => new()
{
    PartyName = "Soiree Westie Swing", Organizer = "Westie Montreal", City = city, DateStart = new DateTime(2026, 11, 20),
    EventDetail = new EventDetail { City = city, Latitude = lat, Longitude = lng }
};
Check(EventIdentity.Same(Luogo("Montreal (administrative region)", 45.5376, -73.6261), Luogo("Montreal", 45.5376, -73.6261)), "Citta riscritta con regione");
Check(EventIdentity.Same(Luogo("Estrie", 45.3993, -71.8887), Luogo("Sherbrooke", 45.3993, -71.8887)), "Citta diversa, stesse coordinate");
Check(EventIdentity.Same(Luogo("Ipswich, Massachusetts", 0, 0), Luogo("Ipswich", 0, 0)), "Citta con stato");
Check(!EventIdentity.Same(Luogo("Montreal", 45.5376, -73.6261), Luogo("Laval", 45.6066, -73.7124)), "Citta vicine ma distinte");
Check(!EventIdentity.Same(Luogo("Genova", 0, 0), Luogo("Savona", 0, 0)), "Coordinate mancanti non uniscono");


shim.EventDetail = new EventDetail { WebSite = "https://curated.example" };
complete.EventDetail = new EventDetail { WebSite = "https://online.example" };
var email = EventIdentity.CompleteEmailTiming(new[] { shim }, new[] { complete }).Single();
Check(email.Duration == TimeSpan.FromHours(4), "Sheet senza durata: recupera quattro ore online prima del filtro");
Check(email.EventDetail.WebSite == shim.EventDetail.WebSite, "Conserva riferimento curato");
Check(shim.Duration == TimeSpan.Zero, "Non modifica evento originale");
Check(EventIdentity.CompleteEmailTiming(new[] { complete }, new[] { shim }).Single().Duration == TimeSpan.FromHours(4), "Non cancella durata gia nota");
Check(EventIdentity.CompleteEmailTiming(new[] { shim }, new[] { otherDay }).Single().Duration == TimeSpan.Zero, "Nessun recupero da eventi distinti");

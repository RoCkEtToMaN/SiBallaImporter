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

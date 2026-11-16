using SiBallaImportingData.Model;
using SiBallaImportingData.Utils;

void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine($"OK: {name}"); }
var folder = Path.Combine(Path.GetTempPath(), "siballa-email-test-" + Guid.NewGuid());
DataPaths.Initialize(folder);
Event Make(string title, string city) => new() { PartyName = title, City = city, DateStart = DateTime.Today.AddDays(1), EventDetail = new() };
var old = Make("Serata nota", "Genova");
var fresh = Make("Serata nuova", "Genova");
var savona = Make("Swing Savona", "Savona");
UltimaEmail.Ricorda([old]);
var nuovi = UltimaEmail.Nuovi([old, fresh, savona]);
Check(nuovi.Count == 2, "Confronto con l'ultima email");
var settings = new AppSettings();
settings.Email.PreviewFile = Path.Combine(folder, "preview.html");
Check(await EmailSender.SendWithMailjetAnonymous([old, fresh, savona], settings, nuovi: nuovi), "Anteprima senza invio");
var html = File.ReadAllText(settings.Email.PreviewFile);
Check(html.Contains("2 eventi nuovi: Genova, Savona"), "Riepilogo città");
Check(html.Contains("·🆕 " + fresh.DateStart.Day), "Contrassegno evento nuovo");
Check(html.Contains("·" + old.DateStart.Day + "/"), "Evento precedente senza contrassegno");
Check(html.IndexOf("Serata nuova") < html.IndexOf("_*Savona*_"), "Nuovo evento dentro il gruppo Genova");
Check(UltimaEmail.Nuovi([old, fresh, savona]).Count == 2, "Anteprima non aggiorna storico");
UltimaEmail.Ricorda([old, fresh, savona]);
Check(UltimaEmail.Nuovi([old, fresh, savona]).Count == 0, "Nessun duplicato al giro successivo");
Check(UltimaEmail.Nuovi([fresh, savona]).Count == 0, "Eventi usciti dalla finestra non generano novità");
Console.WriteLine($"Anteprima: {settings.Email.PreviewFile}");

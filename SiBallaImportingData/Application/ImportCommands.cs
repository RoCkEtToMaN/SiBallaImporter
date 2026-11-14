using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using SiBallaImportingData.Utils;

namespace SiBallaImportingData.Application;

internal static class ImportCommands
{
    public static async Task<bool> ResetSheetAsync()
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        {
            Console.WriteLine("⛔ ResetExcel ignorato su GitHub Actions: si lancia solo dal PC");
        }
        else
        {
            Console.WriteLine("⚠️ ResetExcel: svuoto lo Sheet dalla riga 3 (copia di sicurezza prima)...");
            await ImportData.ResetSheetAsync();
            Console.WriteLine("Fatto. Rimetti \"ResetExcel\": false in appsettings.json");
            return true;
        }
        return false;
    }

    public static async Task TestImmaginiAsync(AppSettings settings, KnownVenueStore store, AddressResolver resolver)
    {
        var opzioni = settings.Import;
        var web = new ImportDataFromWeb();
        var testESwing = await EventFilter.FilterESwingItEventsByDateRange(await web.GetESwing(), store, resolver, opzioni.Radius);
        var testSwingPlanit = await EventFilter.FilterSwingPlanitEventsByDateRangeAsync(await web.GetSwingPlanit(), store, resolver, opzioni.Radius);

        void Report(string fonte, List<Event> eventi)
        {
            var conImmagine = eventi.Where(x => !string.IsNullOrEmpty(x.EventDetail?.ImageUrl)).ToList();
            Console.WriteLine($"\n===== {fonte}: {conImmagine.Count}/{eventi.Count} eventi con immagine =====");
            foreach (var ev in eventi)
                Console.WriteLine($"{(string.IsNullOrEmpty(ev.EventDetail?.ImageUrl) ? "❌" : "✅")} {ev.DateStart:yyyy-MM-dd} {ev.PartyName?.Trim()} -> {ev.EventDetail?.ImageUrl ?? ev.EventDetail?.WebSite}");
        }

        Report("e-swing", testESwing);
        Report("SwingPlanit", testSwingPlanit);
    }

    public static async Task TestSwingAsync(AppSettings settings, KnownVenueStore store, AddressResolver resolver)
    {
        var opzioni = settings.Import;
        var web = new ImportDataFromWeb();
        var trovati = new List<Event>();
        // solo i prossimi Giorni: e-swing arriva a un anno avanti, e cercare immagini e indirizzi per tutti richiede ore
        var fine = DateTime.Today.AddDays(opzioni.Giorni + 1);
        trovati.AddRange(await EventFilter.FilterESwingItEventsByDateRange((await web.GetESwing()).Where(x => x.Start < fine).ToList(), store, resolver, opzioni.Radius));
        trovati.AddRange(await EventFilter.FilterSwingPlanitEventsByDateRangeAsync((await web.GetSwingPlanit()).Where(x => x.DateStart < fine).ToList(), store, resolver, opzioni.Radius));

        var daClassificare = trovati.Where(x => DateTime.Today <= x.DateStart && x.DateStart <= DateTime.Today.AddDays(opzioni.Giorni)).ToList();
        var (swing, altri) = await SwingEventClassifier.ClassifyAsync(daClassificare, settings.Gemini);

        // stesse sezioni dell'email: bollettino (⚠️ = centro città) e luogo da verificare
        var bollettino = swing.Where(x => !x.LuogoSconosciuto).ToList();
        Console.WriteLine($"\n===== Swing: {bollettino.Count}/{daClassificare.Count} =====");
        foreach (var ev in bollettino.OrderBy(x => x.DateStart))
            Console.WriteLine($"{(ev.ProblemaLuogo is null ? "✅" : "⚠️")} {ev.DateStart:yyyy-MM-dd} {ev.City}: {ev.PartyName?.Trim()}{(ev.ProblemaLuogo is null ? "" : " -> " + ev.ProblemaLuogo)}");

        var senzaLuogo = swing.Where(x => x.LuogoSconosciuto).ToList();
        Console.WriteLine($"\n===== Luogo da verificare: {senzaLuogo.Count}/{daClassificare.Count} =====");
        foreach (var ev in senzaLuogo.OrderBy(x => x.DateStart))
            Console.WriteLine($"📍 {ev.DateStart:yyyy-MM-dd} {ev.Country}: {ev.PartyName?.Trim()} -> {ev.ProblemaLuogo} ({ev.EventDetail?.WebSite})");

        Console.WriteLine($"\n===== Da verificare: {altri.Count}/{daClassificare.Count} =====");
        foreach (var (ev, motivo) in altri.OrderBy(x => x.Evento.DateStart))

            Console.WriteLine($"❓ {ev.DateStart:yyyy-MM-dd} {ev.City}: {ev.PartyName?.Trim()} -> {motivo}");
    }

    public static async Task PulisciLezioniAsync(ImportOptions opzioni, List<Event> sheetEvents)
    {
        var lezioni = sheetEvents
            .Where(e => e.DateStart.Date >= DateTime.Today)
            .Where(e => EventFilter.IsLesson(e.PartyName, e.EventDetail?.WebSite))
            .OrderBy(e => e.SheetName).ThenBy(e => e.SheetRow)
            .ToList();

        Console.WriteLine($"\n===== Lezioni nello Sheet: {lezioni.Count} righe future =====");
        foreach (var g in lezioni.GroupBy(e => $"{e.Organizer?.Trim()} ({e.City})").OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"\n🎓 {g.Key}: {g.Count()} righe");
            foreach (var ev in g)
                Console.WriteLine($"   {ev.SheetName} riga {ev.SheetRow} | {ev.DateStart:yyyy-MM-dd} | {ev.PartyName?.Trim()}");
        }

        if (opzioni.PulisciLezioniScrivi)
            await ImportData.DeleteSheetRowsAsync(lezioni);
        else
            Console.WriteLine("\nAnteprima: niente cancellato. Per cancellarle: --pulisci-lezioni-scrivi");
    }

    // vecchie righe future "da verificare" mai classificate (senza colore né nota): le classifica Gemini.
    // --riclassifica-sheet mostra soltanto l'esito, --riclassifica-sheet-scrivi lo scrive nello Sheet
    public static async Task RiclassificaSheetAsync(AppSettings settings, List<Event> sheetEvents)
    {
        var scrivi = settings.Import.RiclassificaSheetScrivi;
        var future = sheetEvents.Where(e => e.DateStart.Date >= DateTime.Today).ToList();
        var righe = (await ImportData.DaVerificareSenzaColoreAsync(future))
            .OrderBy(e => e.SheetName).ThenBy(e => e.SheetRow).ToList();
        int tutte = future.Count(e => e.LuogoVerificato == ImportData.VerifiedPending);
        Console.WriteLine($"\n===== Riclassificazione: {righe.Count} righe future '{ImportData.VerifiedPending}' senza colore né nota (su {tutte}) =====");
        if (righe.Count == 0) return;
        // senza Gemini ogni riga risulterebbe "non classificata" e verrebbe colorata lo stesso: meglio fermarsi
        if (string.IsNullOrWhiteSpace(settings.Gemini.ApiKey) || string.IsNullOrWhiteSpace(settings.Gemini.Model))
        {
            Console.WriteLine("⚠️ Chiave Gemini (Gemini:ApiKey o GEMINI_API_KEY) o modello non impostati: riclassificazione non fatta");
            return;
        }

        await SwingEventClassifier.SegnaPerSheetAsync(righe, settings.Gemini);
        foreach (var ev in righe)
        {
            var esito = ev.StatoNuovaRiga == ImportData.VerifiedNew ? "✅ nuovo        " : "🟧 da verificare";
            Console.WriteLine($"   {esito} | {ev.SheetName} riga {ev.SheetRow} | {ev.DateStart:yyyy-MM-dd} | {ev.PartyName?.Trim()}" +
                              (ev.MotivoVerifica is null ? "" : $"  -> {ev.MotivoVerifica}"));
        }

        if (scrivi)
            await ImportData.ScriviRiclassificazioneAsync(righe);
        else
            Console.WriteLine("\nAnteprima: niente scritto nello Sheet. Per scriverlo: --riclassifica-sheet-scrivi");
    }

    public static async Task TestIndirizziAsync(AppSettings settings, List<Event> daCompilare, AddressResolver resolver)
    {
        var opzioni = settings.Import;
        var campione = daCompilare
            .Where(e => !string.IsNullOrWhiteSpace(e.Organizer) && !string.IsNullOrWhiteSpace(e.City))
            .GroupBy(e => KnownVenueStore.MakeKey(e.Organizer!, e.City!) + "|" + KnownVenueStore.NormalizeTitle(e.PartyName))
            .Select(g => g.OrderBy(e => e.DateStart).First())
            .Where(e => settings.Gemini.UseGoogleSearch || !string.IsNullOrWhiteSpace(e.EventDetail?.WebSite))   // senza ricerca Google serve il sito
            .OrderBy(e => e.DateStart)
            .Take(opzioni.TestIndirizziMax)
            .ToList();

        Console.WriteLine($"\n===== Ricerca indirizzi con Gemini: {campione.Count} eventi (su {daCompilare.Count} da compilare) =====");
        int trovati = 0, confermati = 0;
        foreach (var ev in campione)
        {
            Console.WriteLine($"\n🔎 {ev.DateStart:yyyy-MM-dd} {ev.City} | {ev.Organizer?.Trim()} | {ev.PartyName?.Trim()}");
            try
            {
                var r = await VenueAddressFinder.FindAsync(ev, settings.Gemini);
                if (r is null || !r.Found)
                {
                    Console.WriteLine($"   ❌ Gemini: non trovato{(r?.Motivo is null ? "" : " - " + r.Motivo)}");
                }
                else
                {
                    trovati++;
                    Console.WriteLine($"   🤖 {r.VenueName}, {r.Address}, {r.City} (confidenza {r.Confidence})");
                    Console.WriteLine($"      fonte: {r.SourceUrl} - {r.Motivo}");

                    var coords = await resolver.GeocodeAsync(r.Address!, r.City ?? ev.City, ev.Country);
                    if (coords is null)
                        Console.WriteLine("   ⚠️ Photon: indirizzo non trovato entro 10 km dalla città");
                    else
                    {
                        confermati++;
                        Console.WriteLine($"   ✅ Photon: {coords.Value.Lat:F7}, {coords.Value.Lng:F7}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❗ errore: {ex.Message}");
            }

            await Task.Delay(4000);   // piano gratuito: poche richieste al minuto
        }

        Console.WriteLine($"\n===== Gemini ha trovato {trovati}/{campione.Count}, confermati da Photon {confermati} =====");
    }
}

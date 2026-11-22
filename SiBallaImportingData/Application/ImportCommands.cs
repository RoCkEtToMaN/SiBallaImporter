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
            Console.WriteLine("⚠️ ResetExcel: svuoto Serate e Workshop dalla riga 3 (copia di sicurezza nello Sheet prima)...");
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

    // memoria delle serie sulle righe future già nello Sheet: "da verificare" di serie sempre confermate -> "nuovo",
    // "da verificare"/"nuovo"/"gemini" di serie che hai sempre scartato -> "no". Le righe decise da te non si toccano.
    // Anche: "da verificare" che Gemini ha scartato come mercatino (righe aggiunte prima dello stato "mercatino") -> "mercatino".
    // Da --memoria-serie (anteprima) / --memoria-serie-scrivi, e da sola in ogni giro con --excel (scrivi = true)
    // riga "da verificare" scartata da Gemini come mercatino: la nota comincia con "Gemini: Mercatino ..." (o simili)
    // oppure il titolo è da mercatino. Le righe con la nota della memoria o senza nota di Gemini non si toccano
    private static readonly System.Text.RegularExpressions.Regex NotaMercatino = new(
        @"^Gemini:\s*(si tratta di\s+)?(un[oa']?\s*)?(mercatin|flea market|vintage market|mercato (vintage|dell'usato|dell'antiquariato))",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex TitoloMercatino = new(
        @"mercatin|flea market|vintage market|mercato vintage", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string? MercatinoDaNota(Event e)
    {
        if (e.LuogoVerificato != ImportData.VerifiedPending) return null;
        var nota = e.NotaVerifica?.Trim() ?? "";
        if (!nota.StartsWith("Gemini:", StringComparison.OrdinalIgnoreCase)) return null;
        if (!NotaMercatino.IsMatch(nota) && !TitoloMercatino.IsMatch(e.PartyName ?? "")) return null;
        return $"Gemini: mercatino, va in Bacheca. {nota["Gemini:".Length..].Trim()}";
    }

    public static async Task ApplicaMemoriaAsync(List<Event> sheetEvents, MemoriaSerie memoria, bool scrivi)
    {
        var cambi = new List<Event>();
        foreach (var e in sheetEvents.Where(e => e.DateStart.Date >= DateTime.Today && e.SheetRow is not null && e.SheetVerifiedColumn is not null)
                                     .OrderBy(e => e.SheetName).ThenBy(e => e.SheetRow))
        {
            if (e.LuogoVerificato is not (ImportData.VerifiedPending or ImportData.VerifiedNew or ImportData.VerifiedGemini)) continue;
            if (MercatinoDaNota(e) is string motivo)
            {
                e.StatoNuovaRiga = ImportData.VerifiedMercatino;
                e.MotivoVerifica = motivo;
                cambi.Add(e);
                continue;
            }
            if (memoria.Decidi(e) is not { } d) continue;
            // "workshop" sposta le righe di Serate: quelle già nella scheda Workshop sono a posto
            if (d.Stato == ImportData.VerifiedWorkshop && e.SheetName != TipoEvento.Serate.ToString()) continue;
            // "nuovo" e "gemini" vanno già sul DB: la memoria le cambia solo per scartarle
            if (d.Stato == ImportData.VerifiedNew && e.LuogoVerificato != ImportData.VerifiedPending) continue;
            e.StatoNuovaRiga = d.Stato;
            e.MotivoVerifica = d.Motivo;
            cambi.Add(e);
        }

        Console.WriteLine($"\n===== Memoria delle serie: {cambi.Count} righe future da cambiare =====");
        foreach (var e in cambi)
            Console.WriteLine($"   {(e.StatoNuovaRiga == ImportData.VerifiedNo ? "🚫" : e.StatoNuovaRiga == ImportData.VerifiedMercatino ? "🛍️" : e.StatoNuovaRiga == ImportData.VerifiedWorkshop ? "🎓" : "✅")} {e.LuogoVerificato} -> {e.StatoNuovaRiga} | " +
                              $"{e.SheetName} riga {e.SheetRow} | {e.DateStart:yyyy-MM-dd} | {e.Organizer?.Trim()} | {e.PartyName?.Trim()}  ({e.MotivoVerifica})");
        if (!scrivi)
        {
            // anteprima: cosa riceverebbe Gemini per le righe "da verificare" che la memoria non decide
            var indizi = sheetEvents
                .Where(e => e.DateStart.Date >= DateTime.Today && e.LuogoVerificato == ImportData.VerifiedPending && !cambi.Contains(e))
                .Select(e => (Evento: e, Storia: memoria.StoriaOrganizzatore(e)))
                .Where(x => x.Storia is not null)
                .GroupBy(x => x.Evento.Organizer?.Trim())
                .ToList();
            Console.WriteLine($"\n===== Storia dell'organizzatore per Gemini: {indizi.Sum(g => g.Count())} righe future '{ImportData.VerifiedPending}' " +
                              $"di {indizi.Count} organizzatori =====");
            foreach (var g in indizi)
                Console.WriteLine($"   {g.Key} ({g.Count()} righe, es. {g.First().Evento.PartyName?.Trim()}): {g.First().Storia}");
        }
        if (cambi.Count == 0) return;

        if (!scrivi)
        {
            Console.WriteLine("\nAnteprima: niente scritto nello Sheet. Per scriverlo: --memoria-serie-scrivi (lo fa da solo anche ogni giro con --excel)");
            return;
        }
        try
        {
            await ImportData.ScriviMemoriaAsync(cambi);
            // anche in memoria, così DB ed email di questo giro le vedono già cambiate
            foreach (var e in cambi)
            {
                e.LuogoVerificato = e.StatoNuovaRiga;
                e.NotaVerifica = e.MotivoVerifica;
                e.RigaArancione = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[sheet] ⚠️ memoria delle serie non scritta nello Sheet: {ex.Message}");
        }
    }

    /// <summary>
    /// Prova di Gemini: classifica le righe che hai già deciso tu (workshop, mercatino, no, confermate) senza memoria né storia
    /// dell'organizzatore (gli suggerirebbero la risposta) e confronta con la tua decisione. Non scrive niente.
    /// Le righe dello Sheet non hanno la descrizione dell'evento: gli eventi online, che ce l'hanno, sono più facili
    /// </summary>
    public static async Task TestGeminiSheetAsync(AppSettings settings, List<Event> sheetEvents)
    {
        int max = Math.Max(1, settings.Import.TestGeminiSheetMax);
        static bool DelProgramma(Event e) => (e.NotaVerifica ?? "").TrimStart() is var n
            && (n.StartsWith("Gemini", StringComparison.OrdinalIgnoreCase) || n.StartsWith(MemoriaSerie.PrefissoNota, StringComparison.OrdinalIgnoreCase)
                || n.StartsWith(ImportData.NotaSpostatoDaSerate, StringComparison.OrdinalIgnoreCase));
        // la tua decisione: workshop, mercatino, no (scartato) o swing (confermato), null se non l'hai decisa tu.
        // Confermata nella scheda Workshop = workshop (corso, weekend, festival)
        static string? Tua(Event e) => e.LuogoVerificato switch
        {
            ImportData.VerifiedWorkshop or ImportData.VerifiedMercatino or ImportData.VerifiedNo => DelProgramma(e) && !e.RigaArancione ? null : e.LuogoVerificato,
            ImportData.VerifiedPending or ImportData.VerifiedNew or ImportData.VerifiedGemini or ImportData.VerifiedStorico => null,
            _ when e.SheetName == TipoEvento.Workshop.ToString() => DelProgramma(e) ? null : ImportData.VerifiedWorkshop,
            _ => "swing",
        };
        // stessa serie e stesso giorno contano una volta; per tipo le date più recenti
        var campione = sheetEvents
            .Select(e => (Evento: e, Tua: Tua(e)))
            .Where(x => x.Tua is not null && !string.IsNullOrWhiteSpace(x.Evento.PartyName))
            .GroupBy(x => (x.Tua, Titolo: MemoriaSerie.TitoloSerie(x.Evento.PartyName), x.Evento.DateStart.Date)).Select(g => g.First())
            .GroupBy(x => x.Tua)
            .SelectMany(g => g.OrderByDescending(x => x.Evento.DateStart).Take(max))
            .ToList();
        Console.WriteLine();
        Console.WriteLine($"===== Prova Gemini sullo Sheet: {campione.Count} righe decise da te (max {max} per tipo) =====");
        foreach (var g in campione.GroupBy(x => x.Tua)) Console.WriteLine($"   {g.Key}: {g.Count()}");

        await SwingEventClassifier.ClassificaSoloAsync(campione.Select(x => x.Evento).ToList(), settings.Gemini);

        static string Risposta(Event e) => SwingEventClassifier.EsitoCompleto(e) is not { } r ? "nessuna risposta" : r.Tipo switch
        {
            SwingEventClassifier.TipoSerata => "swing",
            SwingEventClassifier.TipoWorkshop => ImportData.VerifiedWorkshop,
            SwingEventClassifier.TipoMercatino => ImportData.VerifiedMercatino,
            _ => ImportData.VerifiedNo,
        };
        var esiti = campione.Select(x => (x.Evento, x.Tua, Gemini: Risposta(x.Evento))).ToList();
        int giusti = esiti.Count(x => x.Tua == x.Gemini);
        Console.WriteLine();
        Console.WriteLine($"===== Risultato: Gemini d'accordo con te su {giusti}/{esiti.Count} righe =====");
        Console.WriteLine("   (riga = tua decisione, colonne = risposta di Gemini; 'no' = tipo 'altro', per lo Sheet diventerebbe 'da verificare';");
        Console.WriteLine("    workshop = segnata workshop o confermata nella scheda Workshop)");
        var colonne = new[] { "swing", ImportData.VerifiedWorkshop, ImportData.VerifiedMercatino, ImportData.VerifiedNo, "nessuna risposta" };
        Console.WriteLine($"   {"tua / Gemini",-12}" + string.Concat(colonne.Select(c => $"{c,18}")));
        foreach (var g in esiti.GroupBy(x => x.Tua))
            Console.WriteLine($"   {g.Key,-12}" + string.Concat(colonne.Select(c => $"{g.Count(x => x.Gemini == c),18}")));

        Console.WriteLine();
        Console.WriteLine("===== Dove Gemini non è d'accordo =====");
        foreach (var x in esiti.Where(x => x.Tua != x.Gemini).OrderBy(x => x.Tua).ThenBy(x => x.Evento.DateStart))
            Console.WriteLine($"   ❌ tu: {x.Tua,-9} Gemini: {x.Gemini,-9} | {x.Evento.SheetName} riga {x.Evento.SheetRow} | {x.Evento.DateStart:yyyy-MM-dd} | " +
                              $"{x.Evento.Organizer?.Trim()} | {x.Evento.PartyName?.Trim()} -> {SwingEventClassifier.EsitoCompleto(x.Evento)?.Motivo}");
        Console.WriteLine();
        Console.WriteLine("Prova: niente scritto nello Sheet.");
    }

    /// <summary>
    /// Righe future di Serate segnate "workshop" (da te o dalla memoria) che non hanno ancora la copia nella scheda Workshop:
    /// le copia là come "nuovo" con la nota "Spostato da Serate ...". In Serate restano come traccia (fuori da DB, sito ed email).
    /// In anteprima (scrivi = false) conta anche le righe che la memoria segnerebbe "workshop" (StatoNuovaRiga)
    /// </summary>
    public static async Task SpostaInWorkshopAsync(Dictionary<TipoEvento, List<Event>> eventiSheet, bool scrivi)
    {
        if (!eventiSheet.TryGetValue(TipoEvento.Serate, out var serate)) return;
        if (!eventiSheet.TryGetValue(TipoEvento.Workshop, out var workshop))
            eventiSheet[TipoEvento.Workshop] = workshop = new List<Event>();

        var daCopiare = serate
            .Where(e => e.LuogoVerificato == ImportData.VerifiedWorkshop || (!scrivi && e.StatoNuovaRiga == ImportData.VerifiedWorkshop))
            .Where(e => (e.DateFinish ?? e.DateStart).Date >= DateTime.Today)
            .Where(e => !workshop.Any(x => EventIdentity.Same(x, e)))
            .OrderBy(e => e.DateStart)
            .ToList();
        Console.WriteLine();
        Console.WriteLine($"===== Workshop: {daCopiare.Count} righe di Serate da copiare nella scheda Workshop =====");
        foreach (var e in daCopiare)
            Console.WriteLine($"   🎓 Serate riga {e.SheetRow} | {e.DateStart:yyyy-MM-dd} | {e.Organizer?.Trim()} | {e.PartyName?.Trim()}");
        if (daCopiare.Count == 0) return;
        if (!scrivi)
        {
            Console.WriteLine("Anteprima: niente scritto nello Sheet (ogni giro con --excel lo fa da solo, oppure --memoria-serie-scrivi)");
            return;
        }

        var copie = daCopiare.Select(e =>
        {
            var chi = (e.NotaVerifica ?? e.MotivoVerifica ?? "").StartsWith(MemoriaSerie.PrefissoNota, StringComparison.OrdinalIgnoreCase)
                ? "segnata 'workshop' dalla memoria delle serie" : "segnata 'workshop' da te";
            return ImportData.CopiaPerWorkshop(e, $"{ImportData.NotaSpostatoDaSerate} (riga {e.SheetRow}): {chi}");
        }).ToList();
        try
        {
            await ImportData.AppendToGoogleSheet(copie, TipoEvento.Workshop.ToString());
            // anche in memoria, così DB ed email di questo giro le vedono già
            workshop.AddRange(copie);
            Console.WriteLine($"[sheet] {copie.Count} righe copiate da Serate a Workshop");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[sheet] ⚠️ righe 'workshop' non copiate nella scheda Workshop: {ex.Message}");
        }
    }

    // righe "storico" (eventi passati recuperati dalle vecchie versioni) mai controllate: le classifica Gemini.
    // --riclassifica-storico mostra soltanto l'esito, --riclassifica-storico-scrivi lo scrive nello Sheet
    public static async Task RiclassificaStoricoAsync(AppSettings settings, List<Event> sheetEvents)
    {
        var scrivi = settings.Import.RiclassificaStoricoScrivi;
        var righe = (await ImportData.DaVerificareSenzaColoreAsync(sheetEvents, ImportData.VerifiedStorico))
            .OrderBy(e => e.DateStart).ToList();
        int tutte = sheetEvents.Count(e => e.LuogoVerificato == ImportData.VerifiedStorico);
        Console.WriteLine($"\n===== Storico: {righe.Count} righe '{ImportData.VerifiedStorico}' ancora da controllare (su {tutte}) =====");
        if (righe.Count == 0) return;
        if (string.IsNullOrWhiteSpace(settings.Gemini.ApiKey) || string.IsNullOrWhiteSpace(settings.Gemini.Model))
        {
            Console.WriteLine("⚠️ Chiave Gemini (Gemini:ApiKey o GEMINI_API_KEY) o modello non impostati: controllo non fatto");
            return;
        }

        await SwingEventClassifier.ClassificaSoloAsync(righe, settings.Gemini);

        // 1. esito di Gemini; "informazioni insufficienti" e simili non diventano "no" ma "da controllare"
        var esiti = new List<(Event Ev, EsitoStorico Esito, string Motivo, List<string> Stili, bool Coerenza)>();
        int nonClassificate = 0;
        foreach (var ev in righe)
        {
            if (SwingEventClassifier.Esito(ev) is not { } e)
            {
                nonClassificate++;   // resta senza nota: il prossimo giro la riprova
                continue;
            }
            var esito = e.IsSwing ? EsitoStorico.Swing : MotivoIncerto.IsMatch(e.Motivo) ? EsitoStorico.DaControllare : EsitoStorico.No;
            esiti.Add((ev, esito, e.Motivo, SwingEventClassifier.StiliDaGemini(ev) ?? [], false));
        }

        // 2. coerenza sugli eventi che si ripetono (stesso organizzatore, città e titolo): vale la maggioranza delle date
        //    con un esito netto, a parità vince "swing"; segue la maggioranza anche chi era "da controllare"
        int cambiati = 0;
        foreach (var gruppo in esiti.Select((x, i) => (x, i))
                     .GroupBy(t => KnownVenueStore.MakeKey(t.x.Ev.Organizer ?? "", t.x.Ev.City ?? "") + "|" + KnownVenueStore.NormalizeTitle(t.x.Ev.PartyName))
                     .Where(g => g.Count() > 1 && KnownVenueStore.NormalizeTitle(g.First().x.Ev.PartyName).Length > 0))
        {
            int si = gruppo.Count(t => t.x.Esito == EsitoStorico.Swing), no = gruppo.Count(t => t.x.Esito == EsitoStorico.No);
            if (si + no == 0) continue;
            var maggioranza = si >= no ? EsitoStorico.Swing : EsitoStorico.No;
            foreach (var (x, i) in gruppo.Where(t => t.x.Esito != maggioranza))
            {
                esiti[i] = x with
                {
                    Esito = maggioranza,
                    Motivo = maggioranza == EsitoStorico.Swing ? "swing nelle altre date dello stesso evento" : $"{x.Motivo} (come le altre date dello stesso evento)",
                    Coerenza = true
                };
                cambiati++;
            }
        }

        foreach (var (ev, esito, motivo, stili, coerenza) in esiti)
            Console.WriteLine($"   {esito switch { EsitoStorico.Swing => "✅ swing", EsitoStorico.No => "🚫 no   ", _ => "❔ ?    " }} | " +
                              $"{ev.SheetName} riga {ev.SheetRow} | {ev.DateStart:yyyy-MM-dd} | {ev.PartyName?.Trim()}" +
                              (esito == EsitoStorico.Swing ? (stili.Count > 0 ? $"  [{StiliEvento.Testo(stili)}]" : "") : $"  -> {motivo}") +
                              (coerenza ? "  (coerenza)" : ""));
        Console.WriteLine($"\nSwing: {esiti.Count(x => x.Esito == EsitoStorico.Swing)}, non swing: {esiti.Count(x => x.Esito == EsitoStorico.No)}, " +
                          $"da controllare: {esiti.Count(x => x.Esito == EsitoStorico.DaControllare)}, non classificate (riprovare): {nonClassificate}");
        Console.WriteLine($"Cambiati per coerenza con le altre date dello stesso evento: {cambiati}");

        if (scrivi)
            await ImportData.ScriviEsitoStoricoAsync(esiti.Select(x => (x.Ev, x.Esito, x.Motivo, x.Stili)).ToList());
        else
            Console.WriteLine("Anteprima: niente scritto nello Sheet. Per scriverlo: --riclassifica-storico-scrivi");
    }

    // motivi di Gemini che dicono "non lo so" più che "non è swing": la riga resta "storico" con la nota da controllare
    private static readonly System.Text.RegularExpressions.Regex MotivoIncerto = new(
        @"insufficient|non (sono |è )?specificat|non specifican|senza (dettagli|informazioni)|non identificat|non (è )?chiar|" +
        @"non emerge|non si capisce|titolo generico|un semplice avviso|non ci sono informazioni",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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

using SiBallaImportingData.Model;
using SiBallaImportingData.Utils;
using System.Text;
using System.Text.Json;

namespace SiBallaImportingData.Service
{
    // Controllo finale con Gemini: separa gli eventi swing da quelli che probabilmente non lo sono.
    // Se l'API non è configurata o va in errore, tutti gli eventi restano nella lista swing (l'email parte comunque).
    public static class SwingEventClassifier
    {
        private const int DimensioneGruppo = 20;

        private static readonly string Istruzioni =
            "Sei un esperto di balli swing e curi un calendario di eventi swing. Ti passo una lista di eventi trovati online. " +
            "Per ognuno scegli UN SOLO 'tipo' tra questi: " +
            "'serata' = serata o festa di social dance swing (Lindy Hop, Balboa, Boogie Woogie, Collegiate Shag, St. Louis Shag, " +
            "Charleston, Solo Jazz, Blues dance, Tap, Rhythm 'n' Blues), con DJ o musica dal vivo, senza parte didattica. " +
            "'serata_e_workshop' = serata swing che è anche didattica: pratiche, social practice, practice night, ripassi, " +
            "serate con lezione o corso nel programma (es. 'Initiation/cours & soirée dansante', 'lezione + social', " +
            "'La Social Dance di Pratica', 'Serata di ripassi'); e anche weekend, festival, exchange e camp di più giorni con " +
            "serate o social dance (es. 'Swing Weekend', 'Lindy Exchange', 'Blues Weekender', 'Balboa Festival'): stanno sia tra " +
            "le serate sia tra i workshop. " +
            "'workshop' = evento solo didattico di stili swing, senza serate: lezioni, lezioni di prova, corsi, classi a livelli " +
            "(es. 'Livello aperto', 'Principianti', 'Primi passi'), open class, laboratori, atelier, intensivi, stage, workshop, " +
            "bootcamp, 'school', 'challenge' didattiche. " +
            "'mercatino' = mercatino, mercato o fiera di vintage, handmade, artigianato, usato, abbigliamento o dischi " +
            "(es. 'Mercatino del vintage', 'Flea market', 'Vintage market'), anche con musica o DJ di sottofondo; " +
            "una serata di ballo che si tiene in un mercato (es. 'Swing al Mercato Centrale') è invece 'serata'. " +
            "'altro' = tutto il resto: eventi non di ballo, liscio, balli di gruppo, tango, salsa, rock'n'roll acrobatico " +
            "sportivo, West Coast Swing se è l'unico stile; e anche concerti, raduni e festival della scena rockabilly, " +
            "rock'n'roll, jive o hot rod (es. 'Rockin' Jamboree', 'Rockabilly Festival', concerti di band rock'n'roll), " +
            "a meno che citino esplicitamente social dance di Lindy Hop, Balboa, Boogie Woogie o Shag: allora sono 'serata'. " +
            "Se un evento è misto ma ha una vera parte di social dance swing è 'serata'. " +
            "Per ogni evento rispondi con id, tipo e un motivo breve in italiano (max 15 parole). " +
            "Indica anche gli stili di ballo dell'evento in 'stili', usando solo questi nomi: " + string.Join(", ", StiliEvento.Nomi) + ". " +
            "Metti uno stile solo se è citato o evidente da titolo, organizzatore, stili o descrizione (rock'n'roll e jive -> Boogie Woogie); " +
            "se non si capisce lascia la lista vuota, non indovinare. " +
            "Alcuni eventi hanno 'storia organizzatore': le decisioni del curatore del calendario sugli altri eventi dello " +
            "stesso organizzatore (date confermate, pubblicate senza obiezioni o scartate, con qualche titolo d'esempio). " +
            "Usala come indizio, non come regola: un evento simile ai titoli scartati va probabilmente in 'altro', " +
            "uno simile ai confermati probabilmente no, uno simile a quelli segnati workshop/corso probabilmente è 'workshop'; " +
            "ma decidi sempre guardando l'evento stesso, e se la storia " +
            "ha influito sulla decisione dillo nel motivo.";

        public const string TipoSerata = "serata", TipoSerataWorkshop = "serata_e_workshop", TipoWorkshop = "workshop",
                            TipoMercatino = "mercatino", TipoAltro = "altro";
        private static readonly string[] Tipi = { TipoSerata, TipoSerataWorkshop, TipoWorkshop, TipoMercatino, TipoAltro };
        private static bool Ballo(string tipo) => tipo is TipoSerata or TipoSerataWorkshop or TipoWorkshop;

        // esiti di Gemini di questo giro, per evento: lo Sheet e l'email usano la stessa classificazione senza richiederla due volte
        private static readonly Dictionary<Event, (string Tipo, string Motivo)> _esiti = new(ReferenceEqualityComparer.Instance);
        // stili indicati da Gemini nella stessa risposta (solo nomi di StiliEvento.Nomi), per gli eventi senza stile dalla fonte
        private static readonly Dictionary<Event, List<string>> _stili = new(ReferenceEqualityComparer.Instance);

        /// <summary>Stili dell'evento secondo Gemini (dopo SegnaPerSheetAsync o ClassifyAsync), null se non classificato</summary>
        public static List<string>? StiliDaGemini(Event e) => _stili.TryGetValue(e, out var s) ? s : null;

        /// <summary>Classifica e basta (senza toccare StatoNuovaRiga): l'esito si legge con Esito() e StiliDaGemini()</summary>
        public static Task ClassificaSoloAsync(List<Event> eventi, GeminiOptions gemini) => ClassificaAsync(eventi, gemini);

        /// <summary>Esito di Gemini per l'evento (dopo SegnaPerSheetAsync o ClassifyAsync), null se non classificato.
        /// IsSwing = ballo swing: serata o workshop/weekend</summary>
        public static (bool IsSwing, string Motivo)? Esito(Event e) => _esiti.TryGetValue(e, out var x) ? (Ballo(x.Tipo), x.Motivo) : null;

        /// <summary>Esito completo: tipo (serata, workshop, mercatino, altro) e motivo, null se non classificato</summary>
        public static (string Tipo, string Motivo)? EsitoCompleto(Event e) => _esiti.TryGetValue(e, out var x) ? x : null;

        /// <summary>
        /// Per l'email: separa gli eventi swing dagli altri. Se Gemini non risponde l'evento resta tra gli swing (l'email parte comunque)
        /// </summary>
        public static async Task<(List<Event> Swing, List<(Event Evento, string Motivo)> Altri)> ClassifyAsync(List<Event> eventi, GeminiOptions gemini)
        {
            await ClassificaAsync(eventi, gemini);

            var swing = new List<Event>();
            var altri = new List<(Event, string)>();
            foreach (var ev in eventi)
            {
                if (_esiti.TryGetValue(ev, out var esito) && !Ballo(esito.Tipo))
                {
                    altri.Add((ev, esito.Motivo));
                    Console.WriteLine($"[AI] Non swing: {ev.PartyName} -> {esito.Motivo}");
                }
                else
                    swing.Add(ev);
            }

            Console.WriteLine($"[AI] Eventi swing: {swing.Count}, da verificare: {altri.Count}");
            return (swing, altri);
        }

        /// <summary>
        /// Per le righe nuove dello Sheet: swing -> "nuovo" (va sul DB), non swing o non classificato -> "da verificare"
        /// con il motivo (riga colorata, non va sul DB). Al contrario dell'email, nel dubbio la riga aspetta il tuo controllo.
        /// Con la memoria delle serie, le date di serie che hai già deciso prendono la stessa decisione senza chiedere a Gemini
        /// </summary>
        public static async Task SegnaPerSheetAsync(List<Event> eventi, GeminiOptions gemini, MemoriaSerie? memoria = null)
        {
            var daGemini = new List<Event>();
            foreach (var ev in eventi)
            {
                if (memoria?.Decidi(ev) is { } decisione)
                {
                    // "serata+workshop": in Serate come swing ("nuovo") più una copia in Workshop
                    ev.AncheWorkshop = decisione.Stato == ImportData.VerifiedSerataWorkshop;
                    ev.StatoNuovaRiga = ev.AncheWorkshop ? ImportData.VerifiedNew : decisione.Stato;
                    ev.MotivoVerifica = decisione.Motivo;
                    Console.WriteLine($"[memoria] {ev.DateStart:yyyy-MM-dd} {ev.PartyName?.Trim()} -> '{decisione.Stato}'");
                }
                else
                    daGemini.Add(ev);
            }

            // a Gemini anche le tue decisioni sugli altri eventi dello stesso organizzatore, come indizio
            await ClassificaAsync(daGemini, gemini, memoria);

            foreach (var ev in daGemini)
            {
                if (_esiti.TryGetValue(ev, out var esito))
                {
                    // mercatino senza ballo swing: va in Bacheca (sito) invece che in calendario
                    if (esito.Tipo == TipoMercatino)
                    {
                        ev.StatoNuovaRiga = ImportData.VerifiedMercatino;
                        ev.MotivoVerifica = $"Gemini: mercatino, va in Bacheca. {esito.Motivo}";
                        Console.WriteLine($"[AI] Mercatino: {ev.DateStart:yyyy-MM-dd} {ev.PartyName?.Trim()} -> Bacheca");
                    }
                    // corso/lezione/workshop/weekend di stili swing: "workshop", va nella scheda Workshop
                    // (di un giorno resta anche in Serate come traccia, vedi PrepareToAppendToGoogleSheet)
                    else if (esito.Tipo == TipoWorkshop)
                    {
                        ev.StatoNuovaRiga = ImportData.VerifiedWorkshop;
                        ev.MotivoVerifica = $"Gemini: workshop/corso, va nella scheda Workshop. {esito.Motivo}";
                        Console.WriteLine($"[AI] Workshop: {ev.DateStart:yyyy-MM-dd} {ev.PartyName?.Trim()} -> scheda Workshop");
                    }
                    else
                    {
                        bool serata = esito.Tipo is TipoSerata or TipoSerataWorkshop;
                        ev.StatoNuovaRiga = serata ? ImportData.VerifiedNew : ImportData.VerifiedPending;
                        ev.MotivoVerifica = serata ? null : $"Gemini: {esito.Motivo}";
                        // pratica, ripasso, lezione + serata: anche una copia nella scheda Workshop
                        if (esito.Tipo == TipoSerataWorkshop)
                        {
                            ev.AncheWorkshop = true;
                            ev.MotivoVerifica = $"Gemini: serata e anche workshop. {esito.Motivo}";
                        }
                    }
                }
                else
                {
                    ev.StatoNuovaRiga = ImportData.VerifiedPending;
                    ev.MotivoVerifica = "Gemini non ha classificato l'evento: controlla tu";
                }
            }

            int nuovi = eventi.Count(x => x.StatoNuovaRiga == ImportData.VerifiedNew);
            int scartati = eventi.Count(x => x.StatoNuovaRiga == ImportData.VerifiedNo);
            int mercatini = eventi.Count(x => x.StatoNuovaRiga == ImportData.VerifiedMercatino);
            int workshop = eventi.Count(x => x.StatoNuovaRiga == ImportData.VerifiedWorkshop);
            Console.WriteLine($"[AI] Righe nuove per lo Sheet: {nuovi} '{ImportData.VerifiedNew}' (vanno sul DB), " +
                              $"{eventi.Count - nuovi - scartati - mercatini - workshop} '{ImportData.VerifiedPending}' (colorate), {scartati} '{ImportData.VerifiedNo}' (memoria delle serie), " +
                              $"{mercatini} '{ImportData.VerifiedMercatino}' (Bacheca), {workshop} '{ImportData.VerifiedWorkshop}' (scheda Workshop); " +
                              $"decise dalla memoria: {eventi.Count - daGemini.Count}");
        }

        // classifica con Gemini gli eventi che in questo giro non lo sono ancora; quelli di un gruppo fallito restano senza esito
        private static async Task ClassificaAsync(List<Event> eventi, GeminiOptions gemini, MemoriaSerie? memoria = null)
        {
            var daFare = eventi.Where(e => !_esiti.ContainsKey(e)).Distinct(ReferenceEqualityComparer.Instance).Cast<Event>().ToList();
            if (daFare.Count == 0)
                return;

            var apiKey = gemini.ApiKey;
            var model = gemini.Model;
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
            {
                Console.WriteLine("[AI] Chiave Gemini (Gemini:ApiKey o GEMINI_API_KEY) o modello non impostati: controllo saltato.");
                return;
            }

            if (memoria is not null)
                Console.WriteLine($"[AI] storia dell'organizzatore passata a Gemini per {daFare.Count(e => memoria.StoriaOrganizzatore(e) is not null)}/{daFare.Count} eventi");

            // a gruppi: richieste brevi, e se un gruppo fallisce gli altri vanno avanti
            int gruppi = (daFare.Count + DimensioneGruppo - 1) / DimensioneGruppo;
            for (int g = 0; g < gruppi; g++)
            {
                int inizio = g * DimensioneGruppo;
                int quanti = Math.Min(DimensioneGruppo, daFare.Count - inizio);
                var cronometro = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    foreach (var (id, esito) in await ClassifyGroupAsync(daFare, inizio, quanti, apiKey, model, memoria))
                    {
                        _esiti[daFare[id]] = (esito.Tipo, esito.Motivo);
                        _stili[daFare[id]] = esito.Stili;
                    }
                    Console.WriteLine($"[AI] Gruppo {g + 1}/{gruppi}: {quanti} eventi in {cronometro.Elapsed.TotalSeconds:F1} s");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AI] Gruppo {g + 1}/{gruppi} non classificato dopo {cronometro.Elapsed.TotalSeconds:F1} s: {ex.Message}");
                }
            }
        }

        // classifica gli eventi [inizio, inizio + quanti) e restituisce l'esito di quelli a cui il modello ha risposto (id = indice nella lista)
        private static async Task<Dictionary<int, (string Tipo, string Motivo, List<string> Stili)>> ClassifyGroupAsync(List<Event> eventi, int inizio, int quanti, string apiKey, string model, MemoriaSerie? memoria)
        {
            var elenco = new StringBuilder();
            for (int i = inizio; i < inizio + quanti; i++)
            {
                var ev = eventi[i];
                var descrizione = ev.EventDetail?.Description ?? string.Empty;
                if (descrizione.Length > 600)
                    descrizione = descrizione[..600];

                elenco.AppendLine($"id: {i}");
                elenco.AppendLine($"titolo: {ev.PartyName}");
                elenco.AppendLine($"stili: {ev.EventDetail?.StylesToString}");
                elenco.AppendLine($"organizzatore: {ev.Organizer}");
                elenco.AppendLine($"sito: {ev.EventDetail?.WebSite}");
                elenco.AppendLine($"descrizione: {descrizione.Replace('\n', ' ')}");
                if (memoria?.StoriaOrganizzatore(ev) is string storia)
                    elenco.AppendLine($"storia organizzatore: {storia}");
                elenco.AppendLine("---");
            }

            var body = new
            {
                systemInstruction = new { parts = new[] { new { text = Istruzioni + RegoleCuratore.PerPrompt() } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = elenco.ToString() } } } },
                generationConfig = new
                {
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                id = new { type = "INTEGER" },
                                tipo = new { type = "STRING", format = "enum", @enum = Tipi },
                                motivo = new { type = "STRING" },
                                stili = new { type = "ARRAY", items = new { type = "STRING", format = "enum", @enum = StiliEvento.Nomi } }
                            },
                            required = new[] { "id", "tipo", "motivo", "stili" }
                        }
                    }
                }
            };

            // 429 e 5xx (es. 503 "high demand") vengono riprovati dentro GeminiHttp
            var responseBody = await GeminiHttp.GenerateContentAsync(model, apiKey, JsonSerializer.Serialize(body));

            using var doc = JsonDocument.Parse(responseBody);
            var text = doc.RootElement.GetProperty("candidates")[0]
                .GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString();

            using var risultati = JsonDocument.Parse(text ?? "[]");
            var esiti = new Dictionary<int, (string, string, List<string>)>();
            foreach (var r in risultati.RootElement.EnumerateArray())
            {
                int id = r.GetProperty("id").GetInt32();
                // ignora id fuori dal gruppo, se il modello ne inventa
                if (id >= inizio && id < inizio + quanti)
                {
                    // solo i nomi ammessi, anche se lo schema lo impone già
                    var stili = r.TryGetProperty("stili", out var st) && st.ValueKind == JsonValueKind.Array
                        ? st.EnumerateArray().Select(x => x.GetString()).OfType<string>()
                            .Where(x => StiliEvento.Nomi.Contains(x)).Distinct().ToList()
                        : new List<string>();
                    // tipo sconosciuto (con lo schema non dovrebbe succedere): l'evento resta senza esito, cioè "da verificare"
                    var tipo = r.TryGetProperty("tipo", out var t) ? t.GetString()?.Trim().ToLowerInvariant() : null;
                    if (tipo is null || !Tipi.Contains(tipo)) continue;
                    esiti[id] = (tipo, r.GetProperty("motivo").GetString() ?? string.Empty, stili);
                }
            }

            return esiti;
        }
    }
}

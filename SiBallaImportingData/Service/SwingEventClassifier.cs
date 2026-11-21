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
            "Sei un esperto di balli swing. Ti passo una lista di eventi trovati online. " +
            "Per ognuno decidi se è un evento di ballo swing o affine: Lindy Hop, Balboa, Boogie Woogie, Collegiate Shag, " +
            "St. Louis Shag, Charleston, Solo Jazz, Blues dance, Tap, serate o festival swing con musica dal vivo o DJ. " +
            "NON sono swing: liscio, balli di gruppo, rock'n'roll acrobatico sportivo, West Coast Swing se è l'unico stile, " +
            "fiere o mercatini vintage senza una parte di ballo, concerti rockabilly senza riferimenti al ballo swing, " +
            "eventi non legati al ballo. Se l'evento è misto ma include una parte swing, consideralo swing. " +
            "Indica anche 'mercatino': true se l'evento è un mercatino, mercato o fiera di vintage, handmade, artigianato, " +
            "usato, abbigliamento o dischi (es. 'Mercatino del vintage', 'Flea market', 'Vintage market'), anche con musica " +
            "o DJ di sottofondo; false per tutto il resto, comprese le serate di ballo che si tengono in un mercato " +
            "(es. 'Swing al Mercato Centrale'). Un mercatino con una vera parte di ballo swing è swing e anche mercatino. " +
            "Lezioni, lezioni di prova, corsi, classi a livelli (es. 'Livello aperto', 'Principianti') e open class " +
            "NON sono eventi: considerali non swing anche se sono di Lindy Hop, Charleston o altri stili swing. " +
            "Sono eventi solo serate sociali, feste, festival, stage/workshop weekend e serate con musica dal vivo o DJ. " +
            "Per ogni evento rispondi con id, isSwing e un motivo breve in italiano (max 15 parole). " +
            "Indica anche gli stili di ballo dell'evento in 'stili', usando solo questi nomi: " + string.Join(", ", StiliEvento.Nomi) + ". " +
            "Metti uno stile solo se è citato o evidente da titolo, organizzatore, stili o descrizione (rock'n'roll e jive -> Boogie Woogie); " +
            "se non si capisce lascia la lista vuota, non indovinare. " +
            "Alcuni eventi hanno 'storia organizzatore': le decisioni del curatore del calendario sugli altri eventi dello " +
            "stesso organizzatore (date confermate, pubblicate senza obiezioni o scartate, con qualche titolo d'esempio). " +
            "Usala come indizio, non come regola: un evento simile ai titoli scartati (spesso lezioni o corsi) va probabilmente " +
            "scartato, uno simile ai confermati probabilmente no; ma decidi sempre guardando l'evento stesso, e se la storia " +
            "ha influito sulla decisione dillo nel motivo.";

        // esiti di Gemini di questo giro, per evento: lo Sheet e l'email usano la stessa classificazione senza richiederla due volte
        private static readonly Dictionary<Event, (bool IsSwing, bool Mercatino, string Motivo)> _esiti = new(ReferenceEqualityComparer.Instance);
        // stili indicati da Gemini nella stessa risposta (solo nomi di StiliEvento.Nomi), per gli eventi senza stile dalla fonte
        private static readonly Dictionary<Event, List<string>> _stili = new(ReferenceEqualityComparer.Instance);

        /// <summary>Stili dell'evento secondo Gemini (dopo SegnaPerSheetAsync o ClassifyAsync), null se non classificato</summary>
        public static List<string>? StiliDaGemini(Event e) => _stili.TryGetValue(e, out var s) ? s : null;

        /// <summary>Classifica e basta (senza toccare StatoNuovaRiga): l'esito si legge con Esito() e StiliDaGemini()</summary>
        public static Task ClassificaSoloAsync(List<Event> eventi, GeminiOptions gemini) => ClassificaAsync(eventi, gemini);

        /// <summary>Esito di Gemini per l'evento (dopo SegnaPerSheetAsync o ClassifyAsync), null se non classificato</summary>
        public static (bool IsSwing, string Motivo)? Esito(Event e) => _esiti.TryGetValue(e, out var x) ? (x.IsSwing, x.Motivo) : null;

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
                if (_esiti.TryGetValue(ev, out var esito) && !esito.IsSwing)
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
                    ev.StatoNuovaRiga = decisione.Stato;
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
                    if (esito.Mercatino && !esito.IsSwing)
                    {
                        ev.StatoNuovaRiga = ImportData.VerifiedMercatino;
                        ev.MotivoVerifica = $"Gemini: mercatino, va in Bacheca. {esito.Motivo}";
                        Console.WriteLine($"[AI] Mercatino: {ev.DateStart:yyyy-MM-dd} {ev.PartyName?.Trim()} -> Bacheca");
                    }
                    else
                    {
                        ev.StatoNuovaRiga = esito.IsSwing ? ImportData.VerifiedNew : ImportData.VerifiedPending;
                        ev.MotivoVerifica = esito.IsSwing ? null : $"Gemini: {esito.Motivo}";
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
            Console.WriteLine($"[AI] Righe nuove per lo Sheet: {nuovi} '{ImportData.VerifiedNew}' (vanno sul DB), " +
                              $"{eventi.Count - nuovi - scartati - mercatini} '{ImportData.VerifiedPending}' (colorate), {scartati} '{ImportData.VerifiedNo}' (memoria delle serie), " +
                              $"{mercatini} '{ImportData.VerifiedMercatino}' (Bacheca); " +
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
                        _esiti[daFare[id]] = (esito.IsSwing, esito.Mercatino, esito.Motivo);
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
        private static async Task<Dictionary<int, (bool IsSwing, bool Mercatino, string Motivo, List<string> Stili)>> ClassifyGroupAsync(List<Event> eventi, int inizio, int quanti, string apiKey, string model, MemoriaSerie? memoria)
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
                systemInstruction = new { parts = new[] { new { text = Istruzioni } } },
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
                                isSwing = new { type = "BOOLEAN" },
                                mercatino = new { type = "BOOLEAN" },
                                motivo = new { type = "STRING" },
                                stili = new { type = "ARRAY", items = new { type = "STRING", format = "enum", @enum = StiliEvento.Nomi } }
                            },
                            required = new[] { "id", "isSwing", "mercatino", "motivo", "stili" }
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
            var esiti = new Dictionary<int, (bool, bool, string, List<string>)>();
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
                    bool mercatino = r.TryGetProperty("mercatino", out var m) && m.ValueKind == JsonValueKind.True;
                    esiti[id] = (r.GetProperty("isSwing").GetBoolean(), mercatino, r.GetProperty("motivo").GetString() ?? string.Empty, stili);
                }
            }

            return esiti;
        }
    }
}

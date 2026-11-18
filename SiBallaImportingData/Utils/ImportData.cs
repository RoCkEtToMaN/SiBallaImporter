using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using OfficeOpenXml;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DateTime = System.DateTime;
using Type = SiBallaImportingData.Model.Type;

namespace SiBallaImportingData.Utils
{
    public static class ImportData
    {
        public static Dictionary<TipoEvento, List<Event>> LoadFile()
        {
            var file = GetFile();

            Dictionary<TipoEvento, List<Event>> eventiDic = new Dictionary<TipoEvento, List<Event>>();

            var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
            var assembly = Assembly.GetExecutingAssembly();           

            // Nome della risorsa (di solito: Namespace.NomeFile.Extensione)
            string resourceName = "MyProject.credentials.json";

            // Leggi la risorsa incorporata
            using var stream = assembly.GetManifestResourceStream(resourcePath);

            if (stream == null)
                throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");

            var credential = GoogleCredential.FromStream(stream)
                .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);

            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Google Sheets API .NET Quickstart",
            });

            var spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";
            var spreadsheet = service.Spreadsheets.Get(spreadsheetId).Execute();

            foreach (var sheet in spreadsheet.Sheets)
            {
                // Le schede editoriali (Contenuti, Musica, ...) non sono eventi.
                if (sheet.Properties.Title != TipoEvento.Serate.ToString() &&
                    sheet.Properties.Title != TipoEvento.Workshop.ToString())
                    continue;

                Console.WriteLine($"- '{sheet.Properties.Title}'");

                var eventList = new List<Event>();                

                // POI: Prova con il primo foglio
                if (spreadsheet.Sheets.Count > 0)
                {
                    string firstSheetName = sheet.Properties.Title;

                    // Se il nome contiene spazi, usa gli apostrofi
                    string range = firstSheetName.Contains(" ")
                        ? $"'{firstSheetName}'!A:Z"
                        : $"{firstSheetName}!A:Z";

                    Console.WriteLine($"Usando range: {range}");

                    var request = service.Spreadsheets.Values.Get(spreadsheetId, range);
                    var response = request.Execute();

                    var values = response.Values;

                    if (values != null && values.Count > 2)
                    {
                        // colonna "Luogo verificato": la cerco per nome nell'intestazione (riga 2), così non dipende dalla posizione
                        int? verifiedCol = FindHeaderColumn(values[1], VerifiedHeader);
                        if (verifiedCol is null)
                            Console.WriteLine($"[sheet] '{sheet.Properties.Title}': manca la colonna '{VerifiedHeader}' (scrivi l'intestazione nella riga 2, dopo l'ultima colonna)");
                        // colonna "Immagine": "sì" = l'immagine è su Appwrite Storage
                        int? imageCol = FindHeaderColumn(values[1], ImageHeader);
                        if (imageCol is null)
                            Console.WriteLine($"[sheet] '{sheet.Properties.Title}': manca la colonna '{ImageHeader}' (scrivi l'intestazione nella riga 2, dopo l'ultima colonna)");

                        foreach (var row in values)
                        {
                            //Console.WriteLine(string.Join(", ", row));
                            //eventItem.MonthYear = eventItem.Month + " " + eventItem.DateStart.Year;
                            //eventItem.NameAndData = eventItem.PartyName + " " + eventItem.DateStart.ToString();
                            if (values.IndexOf(row) > 1 && row.Count() > 1)
                            {
                                List<StileEvento> stiliEvento = new List<StileEvento>();
                                if (row != null && row.Count > 7 && row[7] != null)
                                {
                                    stiliEvento = ParseStili(row[7].ToString());
                                }

                                string webSite = "";

                                if (row != null && row.Count >= 13 && sheet.Properties.Title == TipoEvento.Serate.ToString() && row[13] != null)
                                {
                                    webSite = row[13].ToString();
                                }
                                else if (row != null && row.Count >= 17 && (sheet.Properties.Title == TipoEvento.Workshop.ToString() && row[17] != null))
                                {
                                    webSite = row[17].ToString();
                                }

                                //var latlong = getLocationInfo.GetCoordinatesFromCityAndNameLocation(
                                //    row[5].ToString().Split(",")[1].Replace(" ", ""),
                                //    row[5].ToString().Split(",")[2].Replace(" ", ""),
                                //    row[5].ToString().Split(",")[0]
                                //    ).Result;

                                //var latlong =  OpenStreetMapGeocoder.GetCoordinatesFromCityAndNameLocation(
                                //    row[5].ToString().Split(",")[1].Replace(" ", ""),
                                //    row[5].ToString().Split(",")[2].Replace(" ", ""),
                                //    row[5].ToString().Split(",")[0]
                                //    ).Result;

                                //Task.Delay(1100);

                                var duration = Convert.ToDateTime(row[1]) - Convert.ToDateTime(row[0]);



                                var raw = row[5]?.ToString() ?? "";

                                // 1. taglio le coordinate: tutto ciò che sta prima della '('
                                var withoutCoords = raw.Split('(')[0];

                                // 2. spezzo in al massimo 3 parti: paese, città, indirizzo (il resto, virgole comprese, resta nell'indirizzo)
                                var parts = withoutCoords.Split(',', 3, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                                //var country = parts.ElementAtOrDefault(0);
                                //var city = parts.ElementAtOrDefault(1);
                                //var address = parts.ElementAtOrDefault(2);     // null se non c'è
                                var loc = ParseLocationCell(row[5]?.ToString());
                                eventList.Add(new Event
                                {
                                    SheetName = sheet.Properties.Title,
                                    SheetRow = values.IndexOf(row) + 1,
                                    SheetLocationRaw = row[5]?.ToString(),
                                    SheetVerifiedColumn = verifiedCol,
                                    SheetImageColumn = imageCol,
                                    SheetImageRaw = imageCol is int ir && row.Count > ir ? row[ir]?.ToString()?.Trim() : null,
                                    SheetStiliRaw = row.Count > 7 ? row[7]?.ToString()?.Trim() : null,
                                    LuogoVerificato = verifiedCol is int vc && row.Count > vc ? row[vc]?.ToString()?.Trim().ToLowerInvariant() : null,
                                    DateStart = Convert.ToDateTime(row[0]),
                                    DateFinish = Convert.ToDateTime(row[1]),
                                    Duration = duration > TimeSpan.Zero ? duration : TimeSpan.Zero,
                                    // righe vecchie scritte con entità HTML ("&amp;"): in chiaro per l'app e per il confronto con gli eventi online
                                    PartyName = WebUtility.HtmlDecode(row[4].ToString()),
                                    //MonthYear = Month + " " + Convert.ToDateTime(row[0]).Year.ToString(),
                                    NameAndData = Convert.ToDateTime(row[0]).Date.Day.ToString() + "th: " + WebUtility.HtmlDecode(row[4].ToString()),// + " " + Convert.ToDateTime(row[0]).Date.ToString("dd/MM/YYYY"),
                                    EventDetail = new EventDetail
                                    {
                                        PartyName = WebUtility.HtmlDecode(row[4].ToString()),
                                        Location = loc.Address,
                                        Address = loc.Address,
                                        City = loc.City,
                                        Country = loc.Country,
                                        Latitude = loc.Lat,
                                        Longitude = loc.Lng,
                                        Styles = stiliEvento,
                                        WebSite = webSite,
                                        HasImage = imageCol is int ic && row.Count > ic && (IsYes(row[ic]?.ToString()) || IsLogo(row[ic]?.ToString()))
                                    },
                                    Organizer = (row != null && row.Count > 6 && row[6] != null)
                                        ? WebUtility.HtmlDecode(row[6].ToString())
                                        : string.Empty,
                                    City = row[5].ToString().Split(",")[1].Split('(')[0].Trim(),
                                    Country = row[5].ToString().Split(',')[0].Trim(),
                                    Gratis = sheet.Properties.Title == TipoEvento.Serate.ToString() && row.Count >= 13 ? DetermineIfFree(sheet.Properties.Title, row) : false,
                                    Type = sheet.Properties.Title == TipoEvento.Serate.ToString() ? Model.Type.Event : Model.Type.Workshop
                                });

                                eventList.LastOrDefault().MonthYear = eventList.LastOrDefault().Month + " " + eventList.LastOrDefault().DateStart.Year.ToString();
                            }                            
                        }
                    }
                    else
                    {
                        Console.WriteLine("No data found.");
                    }

                    if (sheet.Properties.Title == TipoEvento.Serate.ToString())
                        eventiDic.Add(TipoEvento.Serate, eventList);
                    else
                        eventiDic.Add(TipoEvento.Workshop, eventList);

                    Console.WriteLine("Successo!");
                }

            }
            
            return eventiDic;           
        }

        public static async Task PrepareToAppendToGoogleSheet(List<Event> events, Dictionary<TipoEvento, List<Event>> eventiFromExcel)
        {
            // Qui puoi fare eventuali preparazioni prima di chiamare AppendToGoogleSheet2
            // Ad esempio, filtrare o trasformare i dati in 'events' se necessario
            // Poi chiami AppendToGoogleSheet2 con i dati preparati
            int w = 0, e = 0;
            var eventsToAppend = new List<Event>();
            var workshopsToAppend = new List<Event>();
            var eventsToCheck = new List<Event>();
            var workshopsToCheck = new List<Event>();

            foreach (var ev in events)
            {
                var date = ev.DateFinish - ev.DateStart;

#if DEBUG
                Console.WriteLine($"ev.DateFinish {ev.DateFinish}; ev.DateStart {ev.DateStart}; howMuhch {date}");
#endif
                // Esempio di preparazione: aggiungi un prefisso al nome dell'evento
                //ev.PartyName = "[API] " + ev.PartyName;

                // Verifica se l'evento è già presente tra i Workshop su Excel
                bool giaPresenteInWorkshop = eventiFromExcel.TryGetValue(TipoEvento.Workshop, out var workshop)
                    && workshop.Any(x => EventIdentity.Same(x, ev));

                // Verifica se l'evento è già presente tra le Serate su Excel
                bool giaPresenteInSerate = eventiFromExcel.TryGetValue(TipoEvento.Serate, out var serate)
                    && serate.Any(x => EventIdentity.Same(x, ev));

                //if (date.Value.TotalDays > 1 && eventiFromExcel.TryGetValue(TipoEvento.Workshop, out var workshop) && !workshop.Any(x => x.PartyName == ev.PartyName
                //      && x.DateStart.Date == ev.DateStart.Date))
                //{
                //    w++;
                //    workshopsToAppend.Add(ev);
                //}
                //else if (date.Value.TotalDays <= 1 && eventiFromExcel.TryGetValue(TipoEvento.Serate, out var serate) && !serate.Any(x => x.PartyName == ev.PartyName
                //      && x.DateStart.Date == ev.DateStart.Date))
                //{
                //    e++;
                //    eventsToAppend.Add(ev);
                //}

                // Aggiungi solo se NON è già presente nel rispettivo foglio
                if (date.Value.TotalDays > 1 && !giaPresenteInWorkshop)
                {
                    w++;
                    workshopsToAppend.Add(ev);
                }
                else if (date.Value.TotalDays <= 1 && !giaPresenteInSerate)
                {
                    e++;
                    eventsToAppend.Add(ev);
                }
            }

            try
            {
#if DEBUG
                Console.WriteLine("Avvio programma...");
#endif

                if (eventsToAppend.Count() > 0)
                    await AppendToGoogleSheet(eventsToAppend, "Serate");
                if (workshopsToAppend.Count() > 0)
                    await AppendToGoogleSheet(workshopsToAppend, "Workshop");

#if DEBUG
                Console.WriteLine($"quanti e: {e}; quanti w: {w}");

                Console.WriteLine("Operazione completata con successo!");
#endif
            }
            catch (Exception ex)
            {
                // Questo catturerà QUALSIASI errore che fa chiudere l'app
                Console.WriteLine("\n[ERRORE CRITICO INTERCETTATO]:");
                Console.WriteLine(ex.ToString());
            }

           
            //AppendToGoogleSheet2(events, sheetName);
        }
        public static async Task AppendToGoogleSheet(List<Event> events, string sheetName)
        {
            var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
            var assembly = Assembly.GetExecutingAssembly();

            using var stream = assembly.GetManifestResourceStream(resourcePath);
            if (stream == null)
                throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");

            var credential = GoogleCredential.FromStream(stream)
                .CreateScoped(new[] { SheetsService.Scope.Spreadsheets });

            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Google Sheets API .NET"
            });

            string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";

            // 1. Costruisci il range dinamicamente includendo il nome del foglio
            // Se il nome del foglio contiene spazi, è buona norma racchiuderlo tra apici singoli, es: "'1-serate'!A:D"
            string range = $"'{sheetName}'!A:G";

            try
            {
#if DEBUG
                // 1. Test di connessione e permessi corretto con .Execute()
                Console.WriteLine("Verifico i permessi...");
#endif
                var testRequest = service.Spreadsheets.Get(spreadsheetId);
                var spreadsheet = testRequest.Execute(); // <-- Corretto da .Get() a .Execute()
#if DEBUG
                Console.WriteLine($"Accesso consentito al documento: {spreadsheet.Properties.Title}");
#endif


                // 🔍 STAMPA IN CONSOLE TUTTI I NOMI DEI FOGLI PRESENTi NEL DOCUMENTO
                //Console.WriteLine("--- FOGLI PRESENTI NEL TUO GOOGLE SHEET ---");

                //foreach (var sheet in spreadsheet.Sheets)
                //{
                //    Console.WriteLine($"> Nome esatto: [{sheet.Properties.Title}] (ID: {sheet.Properties.SheetId})");
                //}
                //Console.WriteLine("---------------------------------------------");

                // colonna "Immagine": la cerco nell'intestazione (riga 2) per scriverci "sì" se l'immagine è su Storage
                var header = service.Spreadsheets.Values.Get(spreadsheetId, $"'{sheetName}'!2:2").Execute().Values?.FirstOrDefault();
                int? imageCol = header is null ? null : FindHeaderColumn(header, ImageHeader);
                if (imageCol is null && events.Any(x => x.EventDetail?.HasImage == true))
                    Console.WriteLine($"[sheet] '{sheetName}': manca la colonna '{ImageHeader}', le immagini caricate non vengono segnate");
                // "Luogo verificato": "nuovo" se Gemini lo considera swing (va sul DB), altrimenti "da verificare" (resta fuori finché non scrivi "ok")
                int? verifiedCol = header is null ? null : FindHeaderColumn(header, VerifiedHeader);
                if (verifiedCol is null)
                    Console.WriteLine($"[sheet] '{sheetName}': manca la colonna '{VerifiedHeader}', le righe nuove non vengono segnate e andranno tutte sul DB");

                // 2. Mappatura e formattazione dei dati dagli eventi

                var newData = events.Select(ev =>
                {
                    var dateStart = ev.DateStart;
                    var datefinish = (DateTime)ev.DateFinish;

                    if(datefinish == null || datefinish < dateStart || datefinish == dateStart || Math.Abs((datefinish - dateStart).TotalDays) > 300)
                    {
                        //if(Type.Event.ToString().ToLower() == sheetName.ToLower())
                        if(Type.Serate.ToString().ToLower() == sheetName.ToLower())
                            datefinish = dateStart.AddDays(1);
                        if(Type.Workshop.ToString().ToLower() == sheetName.ToLower())
                            datefinish = dateStart.AddDays(3);
                    }

                    bool isWorkshop = Type.Serate.ToString().ToLower() != sheetName.ToLower() ? true: false;

                    var row = new List<object>
                    {
                        ev.DateStart.ToString("yyyy-MM-dd"),                                             // Colonna A
                        datefinish.ToString("yyyy-MM-dd"),                                               // Colonna B
                        "",                                                                              // Colonna C
                        "",                                                                              // Colonna D
                        ev.PartyName,                                                                    // Colonna E
                        // Colonna F: "Paese, Città, Indirizzo(lat;lng)"; senza indirizzo resta lo spazio dopo la virgola per scriverlo a mano
                        $"{ev.Country}, {ev.City}, {ev.EventDetail.Address}({ev.EventDetail.Latitude?.ToString(CultureInfo.InvariantCulture)};{ev.EventDetail.Longitude?.ToString(CultureInfo.InvariantCulture)})",
                        ev.Organizer,                                                                    // Colonna G
                        ev.EventDetail.StylesToString,                                                                              // Colonna H
                        "",                                                                              // Colonna I
                        "",                                                                              // Colonna J
                        "",                                                                              // Colonna K
                        "",                                                                              // Colonna L
                        "",                                                                              // Colonna M
                        !isWorkshop ? ev.EventDetail.WebSite:"",                                                                              // Colonna N
                        "",                                                                              // Colonna O
                        "",                                                                              // Colonna P
                        "",                                                                              // Colonna Q
                        isWorkshop ? ev.EventDetail.WebSite: "",
                        "",
                        "",
                        "",
                        "",// Colonna R
                    };

                    void Set(int col, string value)
                    {
                        while (row.Count <= col)
                            row.Add("");
                        row[col] = value;
                    }
                    if (imageCol is int ic && ev.EventDetail?.HasImage == true)
                        Set(ic, ImageYes);
                    if (verifiedCol is int vc)
                        Set(vc, ev.StatoNuovaRiga ?? VerifiedPending);
                    return (IList<object>)row;
                }).ToList();

                var valueRange = new ValueRange
                {
                    Values = newData
                };

                // 3. Configurazione dell'Append usando il nome del foglio o l'intervallo delle colonne
                // (es. "Foglio1!A:G" dice all'API di accodare i dati in fondo alla tabella senza forzare il grassetto)
                //string targetRange = "Serate!A:G"; // Sostituisci "Foglio1" con il nome esatto del tuo foglio se diverso

                var appendRequest = service.Spreadsheets.Values.Append(valueRange, spreadsheetId, range);
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

                // 4. Esecuzione asincrona dell'inserimento
                var response = appendRequest.Execute();

                // 5. righe "da verificare": colore di sfondo e motivo di Gemini nella nota della cella "Luogo verificato";
                //    stili dedotti: cella H colorata con la nota da dove vengono
                await EvidenziaDaVerificareAsync(service, spreadsheetId, spreadsheet, sheetName, response.Updates?.UpdatedRange,
                    events, verifiedCol, newData.Max(r => r.Count));

#if DEBUG

                // 5. VERIFICA DETTAGLIATA DELL'ESITO
                if (response != null)
                {
                    Console.WriteLine("=== RISULTATO OPERAZIONE GOOGLE SHEETS == > SUCCESSOPESANTE");
                    Console.WriteLine($"- Foglio aggiornato: {response.Updates.SpreadsheetId}");
                    Console.WriteLine($"- Range effettivo modificato: {response.Updates.UpdatedRange}");
                    Console.WriteLine($"- Righe totali aggiunte/aggiornate: {response.Updates.UpdatedRows}");
                    Console.WriteLine($"- Colonne totali interessate: {response.Updates.UpdatedColumns}");
                    Console.WriteLine($"- Celle totali modificate: {response.Updates.UpdatedCells}");

                    if (response.Updates.UpdatedRows > 0)
                    {
                        Console.WriteLine("✅ Conferma: I dati sono stati scritti con successo nel documento!");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ Attenzione: L'API ha risposto, ma il numero di righe aggiornate è 0. Controlla se la lista 'events' aveva elementi.");
                    }
                }
                else
                {
                    Console.WriteLine("❌ Errore critico: La risposta da Google Sheets è arrivata vuota (null).");
                }

                // 5. Output di riscontro
                Console.WriteLine($"Successo! Aggiunte {response.Updates.UpdatedRows} righe nel foglio.");
                Console.WriteLine($"Range aggiornato: {response.Updates.UpdatedRange}");
#endif
            }
            catch (GoogleApiException ex)
            {
                Console.WriteLine($"Errore Google API: {ex.Message}");
                if (ex.Error != null)
                {
                    Console.WriteLine($"Codice errore: {ex.Error.Code}");
                    Console.WriteLine($"Messaggio errore: {ex.Error.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore durante l'importazione su Google Sheets: {ex.Message}");
                throw;
            }
        }
        // arancione chiaro delle righe "da verificare": è anche il segno con cui le riconosco per togliere il colore
        private static readonly Color ColoreDaVerificare = new() { Red = 252 / 255f, Green = 229 / 255f, Blue = 205 / 255f };

        private static bool IsColoreDaVerificare(Color? c) =>
            c is not null
            && Math.Abs((c.Red ?? 0) - ColoreDaVerificare.Red!.Value) < 0.01
            && Math.Abs((c.Green ?? 0) - ColoreDaVerificare.Green!.Value) < 0.01
            && Math.Abs((c.Blue ?? 0) - ColoreDaVerificare.Blue!.Value) < 0.01;

        // colora le righe appena aggiunte che sono "da verificare" e mette il motivo nella nota (passandoci sopra col mouse);
        // poi colora la cella H degli stili dedotti (dopo, così resta lilla anche nelle righe "da verificare")
        private static async Task EvidenziaDaVerificareAsync(SheetsService service, string spreadsheetId, Spreadsheet spreadsheet,
            string sheetName, string? updatedRange, List<Event> events, int? verifiedCol, int colonne)
        {
            // es. "Serate!A15:V20" o "'Serate'!A15:V20": la prima riga scritta è la 15
            var m = Regex.Match(updatedRange ?? "", @"![A-Z]+(\d+)");
            var sheetId = spreadsheet.Sheets.FirstOrDefault(s => s.Properties.Title == sheetName)?.Properties.SheetId;
            if (!m.Success || sheetId is null)
            {
                Console.WriteLine($"[sheet] '{sheetName}': non so in che righe sono finiti gli eventi ({updatedRange}), righe da verificare non colorate e date non formattate");
                return;
            }
            int primaRiga = int.Parse(m.Groups[1].Value) - 1;   // l'API conta da 0

            // date di inizio e fine (A, B) come le altre righe dello Sheet: scritte "2026-11-18" (non ambiguo),
            // mostrate "18/11/2026"
            var requests = new List<Request>
            {
                new()
                {
                    RepeatCell = new RepeatCellRequest
                    {
                        Range = new GridRange { SheetId = sheetId, StartRowIndex = primaRiga, EndRowIndex = primaRiga + events.Count, StartColumnIndex = 0, EndColumnIndex = 2 },
                        Cell = new CellData { UserEnteredFormat = new CellFormat { NumberFormat = FormatoData } },
                        Fields = "userEnteredFormat.numberFormat"
                    }
                }
            };
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (verifiedCol is null || (ev.StatoNuovaRiga ?? VerifiedPending) != VerifiedPending) continue;

                requests.Add(new Request
                {
                    RepeatCell = new RepeatCellRequest
                    {
                        Range = new GridRange { SheetId = sheetId, StartRowIndex = primaRiga + i, EndRowIndex = primaRiga + i + 1, StartColumnIndex = 0, EndColumnIndex = colonne },
                        Cell = new CellData { UserEnteredFormat = new CellFormat { BackgroundColor = ColoreDaVerificare } },
                        Fields = "userEnteredFormat.backgroundColor"
                    }
                });
                if (!string.IsNullOrWhiteSpace(ev.MotivoVerifica))
                    requests.Add(new Request
                    {
                        UpdateCells = new UpdateCellsRequest
                        {
                            Start = new GridCoordinate { SheetId = sheetId, RowIndex = primaRiga + i, ColumnIndex = verifiedCol },
                            Rows = new List<RowData> { new() { Values = new List<CellData> { new() { Note = ev.MotivoVerifica } } } },
                            Fields = "note"
                        }
                    });
            }
            int righeColorate = requests.Count(r => r.RepeatCell is not null) - 1;   // meno il formato delle date

            int stiliDedotti = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].StileDedottoDa is not string fonte) continue;
                requests.Add(new Request
                {
                    UpdateCells = new UpdateCellsRequest
                    {
                        Start = new GridCoordinate { SheetId = sheetId, RowIndex = primaRiga + i, ColumnIndex = ColonnaStili },
                        Rows = new List<RowData> { new() { Values = new List<CellData> { new()
                        {
                            UserEnteredFormat = new CellFormat { BackgroundColor = ColoreStileDedotto },
                            Note = NotaStileDedotto(fonte, events[i].EventDetail?.StylesToString)
                        } } } },
                        Fields = "userEnteredFormat.backgroundColor,note"
                    }
                });
                stiliDedotti++;
            }
            await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, spreadsheetId).ExecuteAsync();
            Console.WriteLine($"[sheet] '{sheetName}': {righeColorate} righe '{VerifiedPending}' colorate, {stiliDedotti} stili dedotti da confermare");
        }

        // formato delle colonne A e B in tutto lo Sheet (uniformato il 18/11/2026: prima c'erano sia 2026-11-18 sia 18/11/2026)
        private static readonly NumberFormat FormatoData = new() { Type = "DATE", Pattern = "dd/MM/yyyy" };

        // ---------- Stili dedotti (colonna H) ----------

        private const int ColonnaStili = 7;   // H
        // lilla delle celle H con lo stile dedotto dal programma: è anche il segno che non l'hai ancora confermato
        private static readonly Color ColoreStileDedotto = new() { Red = 217 / 255f, Green = 210 / 255f, Blue = 233 / 255f };
        private static readonly Regex NotaStileRegex = new(@"^Stile dedotto \(([^)]*)\): (.*)$", RegexOptions.Multiline);

        private static string NotaStileDedotto(string fonte, string? valore) =>
            $"Stile dedotto ({fonte}): {valore}\n" +
            "Se è giusto togli il colore della cella, se è sbagliato correggilo: " +
            "il programma impara lo stile solo da celle non colorate o corrette.";

        private static bool IsColore(Color? c, Color atteso) =>
            c is not null
            && Math.Abs((c.Red ?? 0) - atteso.Red!.Value) < 0.01
            && Math.Abs((c.Green ?? 0) - atteso.Green!.Value) < 0.01
            && Math.Abs((c.Blue ?? 0) - atteso.Blue!.Value) < 0.01;

        /// <summary>
        /// Legge la colonna H e segna StileDedotto sulle righe con lo stile dedotto ancora da confermare (cella lilla e valore
        /// uguale a quello della nota). Le celle confermate (colore tolto o valore corretto) vengono ripulite da colore e nota.
        /// Restituisce false se la lettura non riesce: in quel caso gli stili non vanno imparati in questo giro
        /// </summary>
        public static async Task<bool> LeggiStiliDedottiAsync(IEnumerable<Event> sheetEvents)
        {
            var righe = sheetEvents.Where(e => e.SheetName is not null && e.SheetRow is not null).ToList();
            if (righe.Count == 0) return true;
            try
            {
                var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                    ?? throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");
                var service = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets),
                    ApplicationName = "Google Sheets API .NET"
                });
                const string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";

                var perFoglio = righe.GroupBy(e => e.SheetName!).ToDictionary(g => g.Key, g => g.ToDictionary(e => e.SheetRow!.Value));
                var get = service.Spreadsheets.Get(spreadsheetId);
                get.Ranges = perFoglio.Select(f => $"'{f.Key}'!H3:H{f.Value.Keys.Max()}").ToList();
                get.IncludeGridData = true;
                get.Fields = "sheets(properties(sheetId,title),data(startRow,rowData(values(formattedValue,note,userEnteredFormat(backgroundColor)))))";
                var spreadsheet = await get.ExecuteAsync();

                var pulizie = new List<Request>();
                int daConfermare = 0;
                foreach (var sheet in spreadsheet.Sheets)
                {
                    if (!perFoglio.TryGetValue(sheet.Properties.Title, out var eventi)) continue;
                    foreach (var data in sheet.Data ?? new List<GridData>())
                    {
                        int start = data.StartRow ?? 0;
                        for (int i = 0; i < (data.RowData?.Count ?? 0); i++)
                        {
                            int riga = start + i + 1;
                            var cella = data.RowData![i].Values?.FirstOrDefault();
                            if (cella is null || !eventi.TryGetValue(riga, out var ev)) continue;

                            var colorata = IsColore(cella.UserEnteredFormat?.BackgroundColor, ColoreStileDedotto);
                            var nota = cella.Note is null ? null : NotaStileRegex.Match(cella.Note);
                            var mia = nota is { Success: true };
                            if (!colorata && !mia) continue;

                            // valore cambiato rispetto a quello dedotto = l'hai corretto tu
                            var corretta = mia && !string.Equals(nota!.Groups[2].Value.Trim(), (cella.FormattedValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                            if (colorata && !corretta)
                            {
                                ev.StileDedotto = true;
                                daConfermare++;
                                continue;
                            }
                            // confermata: via colore (se è ancora il mio) e nota
                            pulizie.Add(new Request
                            {
                                UpdateCells = new UpdateCellsRequest
                                {
                                    Start = new GridCoordinate { SheetId = sheet.Properties.SheetId, RowIndex = riga - 1, ColumnIndex = ColonnaStili },
                                    Rows = new List<RowData> { new() { Values = new List<CellData> { new() { UserEnteredFormat = new CellFormat() } } } },
                                    Fields = mia && colorata ? "userEnteredFormat.backgroundColor,note" : mia ? "note" : "userEnteredFormat.backgroundColor"
                                }
                            });
                        }
                    }
                }

                if (pulizie.Count > 0)
                    await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = pulizie }, spreadsheetId).ExecuteAsync();
                Console.WriteLine($"[sheet] stili dedotti: {daConfermare} da confermare, {pulizie.Count} confermati e ripuliti");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[sheet] ⚠️ stili dedotti non letti ({ex.Message}): in questo giro il dizionario non impara gli stili");
                return false;
            }
        }

        /// <summary>
        /// Toglie colore e nota alle righe colorate "da verificare" che hai controllato (in "Luogo verificato" non c'è più "da verificare").
        /// Tocca solo le righe con il colore messo dal programma: gli altri colori dello Sheet restano
        /// </summary>
        public static async Task TogliEvidenziazioniAsync(IEnumerable<Event> sheetEvents)
        {
            var controllate = sheetEvents
                .Where(e => e.SheetName is not null && e.SheetRow is not null && e.SheetVerifiedColumn is not null && e.LuogoVerificato != VerifiedPending)
                .ToList();
            if (controllate.Count == 0) return;

            var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                ?? throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");
            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets),
                ApplicationName = "Google Sheets API .NET"
            });
            const string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";

            // leggo solo il formato della colonna "Luogo verificato" di ogni foglio, dalla riga 3 all'ultima
            var perFoglio = controllate.GroupBy(e => e.SheetName!).ToDictionary(g => g.Key, g => g.ToDictionary(e => e.SheetRow!.Value));
            var colonne = controllate.GroupBy(e => e.SheetName!).ToDictionary(g => g.Key, g => g.First().SheetVerifiedColumn!.Value);
            var get = service.Spreadsheets.Get(spreadsheetId);
            get.Ranges = perFoglio.Select(f => $"'{f.Key}'!{(char)('A' + colonne[f.Key])}3:{(char)('A' + colonne[f.Key])}{f.Value.Keys.Max()}").ToList();
            get.IncludeGridData = true;
            get.Fields = "sheets(properties(sheetId,title),data(startRow,rowData(values(userEnteredFormat(backgroundColor)))))";
            var spreadsheet = await get.ExecuteAsync();

            var requests = new List<Request>();
            foreach (var sheet in spreadsheet.Sheets)
            {
                if (!perFoglio.TryGetValue(sheet.Properties.Title, out var righe)) continue;
                foreach (var data in sheet.Data ?? new List<GridData>())
                {
                    int start = data.StartRow ?? 0;
                    for (int i = 0; i < (data.RowData?.Count ?? 0); i++)
                    {
                        int riga = start + i + 1;   // numero di riga come in SheetRow (1 = prima)
                        var colore = data.RowData![i].Values?.FirstOrDefault()?.UserEnteredFormat?.BackgroundColor;
                        if (!righe.ContainsKey(riga) || !IsColoreDaVerificare(colore)) continue;

                        // la cella H con lo stile dedotto non ancora confermato resta lilla
                        var tratti = righe[riga].StileDedotto
                            ? new[] { (0, ColonnaStili), (ColonnaStili + 1, 26) }
                            : new[] { (0, 26) };
                        foreach (var (da, a) in tratti)
                            requests.Add(new Request
                            {
                                RepeatCell = new RepeatCellRequest
                                {
                                    Range = new GridRange { SheetId = sheet.Properties.SheetId, StartRowIndex = riga - 1, EndRowIndex = riga, StartColumnIndex = da, EndColumnIndex = a },
                                    Cell = new CellData { UserEnteredFormat = new CellFormat() },
                                    Fields = "userEnteredFormat.backgroundColor"
                                }
                            });
                        requests.Add(new Request
                        {
                            UpdateCells = new UpdateCellsRequest
                            {
                                Start = new GridCoordinate { SheetId = sheet.Properties.SheetId, RowIndex = riga - 1, ColumnIndex = colonne[sheet.Properties.Title] },
                                Rows = new List<RowData> { new() { Values = new List<CellData> { new() } } },
                                Fields = "note"
                            }
                        });
                    }
                }
            }
            if (requests.Count == 0) return;

            await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, spreadsheetId).ExecuteAsync();
            Console.WriteLine($"[sheet] 🎨 tolto il colore a {requests.Count / 2} righe controllate");
        }

        // ---------- Formule dell'intestazione bloccate (#REF!) ----------

        // oltre questo numero di valori fissi in una colonna non è una riga spostata per sbaglio: non tocco niente
        private const int MaxValoriFissiPerColonna = 20;

        /// <summary>
        /// Le colonne con una formula a matrice nell'intestazione (riga 2: ARRAYFORMULA o {"Titolo"; ...}) riempiono da sole
        /// la colonna. Un valore scritto sotto (es. una riga copiata da Serate a Workshop con le "X"/"Y" già calcolate)
        /// le blocca e l'intestazione diventa #REF!. Qui trovo quei valori fissi e li svuoto, così la formula riparte
        /// </summary>
        public static async Task SbloccaFormuleIntestazioneAsync()
        {
            const string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";
            var service = ServizioSheet();
            var fogli = Enum.GetNames<TipoEvento>();
            var get = service.Spreadsheets.Values.BatchGet(spreadsheetId);
            get.Ranges = fogli.Select(f => $"'{f}'!A2:Z").ToList();
            get.ValueRenderOption = SpreadsheetsResource.ValuesResource.BatchGetRequest.ValueRenderOptionEnum.FORMULA;
            var risposta = await get.ExecuteAsync();

            var daSvuotare = new List<string>();
            for (int f = 0; f < fogli.Length; f++)
            {
                var righe = risposta.ValueRanges[f].Values;
                if (righe is null || righe.Count < 2) continue;
                var intestazione = righe[0];
                for (int c = 0; c < intestazione.Count; c++)
                {
                    var h = intestazione[c]?.ToString()?.Trim() ?? "";
                    bool aMatrice = h.StartsWith('=') && (h.Contains("ARRAYFORMULA", StringComparison.OrdinalIgnoreCase) || h.Contains('{'));
                    if (!aMatrice) continue;

                    // con FORMULA le celle riempite dalla formula risultano vuote: quelle piene sono valori (o formule) scritti a mano
                    var lettera = (char)('A' + c);
                    var piene = Enumerable.Range(1, righe.Count - 1)
                        .Where(r => c < righe[r].Count && !string.IsNullOrWhiteSpace(righe[r][c]?.ToString()))
                        .Select(r => r + 2)   // righe[0] è la riga 2 dello Sheet
                        .ToList();
                    if (piene.Count == 0) continue;
                    if (piene.Count > MaxValoriFissiPerColonna)
                    {
                        Console.WriteLine($"[sheet] ⚠️ '{fogli[f]}' colonna {lettera}: {piene.Count} valori scritti sotto la formula dell'intestazione, troppi per svuotarli da solo: controlla tu");
                        continue;
                    }
                    Console.WriteLine($"[sheet] 🔧 '{fogli[f]}' colonna {lettera} ({intestazione[c]?.ToString()?.Split('"').ElementAtOrDefault(1)}): " +
                                      $"svuoto i valori fissi che bloccavano la formula, righe {string.Join(", ", piene)}");
                    daSvuotare.AddRange(piene.Select(r => $"'{fogli[f]}'!{lettera}{r}"));
                }
            }
            if (daSvuotare.Count == 0) return;

            await service.Spreadsheets.Values.BatchClear(new BatchClearValuesRequest { Ranges = daSvuotare }, spreadsheetId).ExecuteAsync();
        }

        // ---------- Riclassificazione delle vecchie righe "da verificare" (--riclassifica-sheet) ----------

        private static SheetsService ServizioSheet()
        {
            var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                ?? throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");
            return new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets),
                ApplicationName = "Google Sheets API .NET"
            });
        }

        /// <summary>
        /// Tra le righe passate, quelle con lo stato indicato (di solito "da verificare") mai classificate: senza il colore
        /// arancione e senza nota nella colonna "Luogo verificato" (aggiunte prima che il programma colorasse le righe,
        /// o righe "storico" recuperate dalle vecchie versioni)
        /// </summary>
        public static async Task<List<Event>> DaVerificareSenzaColoreAsync(IEnumerable<Event> righe, string stato = VerifiedPending)
        {
            var candidate = righe.Where(e => e.LuogoVerificato == stato
                                             && e.SheetName is not null && e.SheetRow is not null && e.SheetVerifiedColumn is not null).ToList();
            if (candidate.Count == 0) return [];

            const string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";
            var service = ServizioSheet();
            var perFoglio = candidate.GroupBy(e => e.SheetName!).ToDictionary(g => g.Key, g => g.ToDictionary(e => e.SheetRow!.Value));
            var colonne = candidate.GroupBy(e => e.SheetName!).ToDictionary(g => g.Key, g => g.First().SheetVerifiedColumn!.Value);
            var get = service.Spreadsheets.Get(spreadsheetId);
            get.Ranges = perFoglio.Select(f => $"'{f.Key}'!{(char)('A' + colonne[f.Key])}3:{(char)('A' + colonne[f.Key])}{f.Value.Keys.Max()}").ToList();
            get.IncludeGridData = true;
            get.Fields = "sheets(properties(title),data(startRow,rowData(values(note,userEnteredFormat(backgroundColor)))))";
            var spreadsheet = await get.ExecuteAsync();

            var risultato = new List<Event>();
            foreach (var sheet in spreadsheet.Sheets)
            {
                if (!perFoglio.TryGetValue(sheet.Properties.Title, out var perRiga)) continue;
                foreach (var data in sheet.Data ?? new List<GridData>())
                {
                    int start = data.StartRow ?? 0;
                    for (int i = 0; i < (data.RowData?.Count ?? 0); i++)
                    {
                        int riga = start + i + 1;
                        if (!perRiga.TryGetValue(riga, out var ev)) continue;
                        var cella = data.RowData![i].Values?.FirstOrDefault();
                        if (IsColoreDaVerificare(cella?.UserEnteredFormat?.BackgroundColor) || !string.IsNullOrWhiteSpace(cella?.Note)) continue;
                        risultato.Add(ev);
                    }
                }
            }
            return risultato;
        }

        /// <summary>
        /// Esito di Gemini sulle righe "storico": swing -> resta "storico" con la nota "Gemini: swing" (e lo stile in H se mancava),
        /// non swing -> "no" con il motivo nella nota. La nota segna la riga come già controllata
        /// </summary>
        public static async Task ScriviEsitoStoricoAsync(List<(Event Evento, EsitoStorico Esito, string Motivo, List<string> Stili)> esiti)
        {
            if (esiti.Count == 0) return;
            const string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";
            var service = ServizioSheet();

            var valori = new List<ValueRange>();
            foreach (var (e, esito, _, stili) in esiti)
            {
                bool swing = esito == EsitoStorico.Swing;
                if (esito == EsitoStorico.No)
                    valori.Add(new ValueRange
                    {
                        Range = $"'{e.SheetName}'!{(char)('A' + e.SheetVerifiedColumn!.Value)}{e.SheetRow}",
                        Values = new List<IList<object>> { new List<object> { VerifiedNo } }
                    });
                else if (stili.Count > 0 && string.IsNullOrWhiteSpace(e.SheetStiliRaw))
                    valori.Add(new ValueRange
                    {
                        Range = $"'{e.SheetName}'!{(char)('A' + ColonnaStili)}{e.SheetRow}",
                        Values = new List<IList<object>> { new List<object> { StiliEvento.Testo(stili) } }
                    });
            }
            for (int i = 0; i < valori.Count; i += 500)
                await service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest { ValueInputOption = "RAW", Data = valori.Skip(i).Take(500).ToList() }, spreadsheetId).ExecuteAsync();

            var spreadsheet = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();
            var sheetIds = spreadsheet.Sheets.ToDictionary(s => s.Properties.Title, s => s.Properties.SheetId);
            var note = esiti.Where(x => sheetIds.ContainsKey(x.Evento.SheetName!)).Select(x => new Request
            {
                UpdateCells = new UpdateCellsRequest
                {
                    Start = new GridCoordinate { SheetId = sheetIds[x.Evento.SheetName!], RowIndex = x.Evento.SheetRow!.Value - 1, ColumnIndex = x.Evento.SheetVerifiedColumn },
                    Rows = new List<RowData> { new() { Values = new List<CellData> { new() { Note = x.Esito switch
                    {
                        EsitoStorico.Swing => "Gemini: swing",
                        EsitoStorico.No => $"Gemini: {x.Motivo}",
                        _ => $"Gemini: da controllare - {x.Motivo}"
                    } } } } },
                    Fields = "note"
                }
            }).ToList();
            for (int i = 0; i < note.Count; i += 500)
                await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = note.Skip(i).Take(500).ToList() }, spreadsheetId).ExecuteAsync();

            Console.WriteLine($"[sheet] storico controllato da Gemini: {esiti.Count(x => x.Esito == EsitoStorico.Swing)} swing, " +
                              $"{esiti.Count(x => x.Esito == EsitoStorico.No)} segnate '{VerifiedNo}', " +
                              $"{esiti.Count(x => x.Esito == EsitoStorico.DaControllare)} da controllare (restano '{VerifiedStorico}' con la nota)");
        }

        /// <summary>
        /// Scrive l'esito di Gemini (StatoNuovaRiga, MotivoVerifica): swing -> "nuovo" in "Luogo verificato" (al prossimo --db va sul DB),
        /// gli altri restano "da verificare" con la riga arancione e il motivo nella nota, come le righe aggiunte oggi dal programma
        /// </summary>
        public static async Task ScriviRiclassificazioneAsync(List<Event> righe)
        {
            if (righe.Count == 0) return;
            const string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";
            var service = ServizioSheet();

            var nuove = righe.Where(e => e.StatoNuovaRiga == VerifiedNew).ToList();
            if (nuove.Count > 0)
                await service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest
                {
                    ValueInputOption = "RAW",
                    Data = nuove.Select(e => new ValueRange
                    {
                        Range = $"'{e.SheetName}'!{(char)('A' + e.SheetVerifiedColumn!.Value)}{e.SheetRow}",
                        Values = new List<IList<object>> { new List<object> { VerifiedNew } }
                    }).ToList()
                }, spreadsheetId).ExecuteAsync();

            var spreadsheet = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();
            var sheetIds = spreadsheet.Sheets.ToDictionary(s => s.Properties.Title, s => s.Properties.SheetId);
            var requests = new List<Request>();
            foreach (var e in righe.Where(e => e.StatoNuovaRiga != VerifiedNew && sheetIds.ContainsKey(e.SheetName!)))
            {
                var sheetId = sheetIds[e.SheetName!];
                int r = e.SheetRow!.Value - 1;   // l'API conta da 0
                requests.Add(new Request
                {
                    RepeatCell = new RepeatCellRequest
                    {
                        Range = new GridRange { SheetId = sheetId, StartRowIndex = r, EndRowIndex = r + 1, StartColumnIndex = 0, EndColumnIndex = 26 },
                        Cell = new CellData { UserEnteredFormat = new CellFormat { BackgroundColor = ColoreDaVerificare } },
                        Fields = "userEnteredFormat.backgroundColor"
                    }
                });
                if (!string.IsNullOrWhiteSpace(e.MotivoVerifica))
                    requests.Add(new Request
                    {
                        UpdateCells = new UpdateCellsRequest
                        {
                            Start = new GridCoordinate { SheetId = sheetId, RowIndex = r, ColumnIndex = e.SheetVerifiedColumn },
                            Rows = new List<RowData> { new() { Values = new List<CellData> { new() { Note = e.MotivoVerifica } } } },
                            Fields = "note"
                        }
                    });
            }
            if (requests.Count > 0)
                await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, spreadsheetId).ExecuteAsync();

            Console.WriteLine($"[sheet] riclassificate {righe.Count} righe: {nuove.Count} '{VerifiedNew}', {righe.Count - nuove.Count} '{VerifiedPending}' colorate con il motivo");
        }

        /// <summary>
        /// Scrive sullo Sheet gli indirizzi trovati da Gemini: colonna F "Paese, Città, Indirizzo(lat;lng)" con la nota
        /// (locale, fonte, confidenza) e "gemini" in "Luogo verificato"
        /// </summary>
        public static async Task ScriviIndirizziGeminiAsync(IEnumerable<(Event Event, string Nota)> righe)
        {
            var lista = righe.Where(x => x.Event.SheetName is not null && x.Event.SheetRow is not null
                                         && x.Event.EventDetail?.Latitude is not null && x.Event.EventDetail.Longitude is not null).ToList();
            if (lista.Count == 0) return;

            var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                ?? throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");
            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets),
                ApplicationName = "Google Sheets API .NET"
            });
            const string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";

            var valori = new List<ValueRange>();
            foreach (var (ev, _) in lista)
            {
                var loc = ParseLocationCell(ev.SheetLocationRaw);
                var lat = ev.EventDetail.Latitude!.Value.ToString("0.0000000", CultureInfo.InvariantCulture);
                var lng = ev.EventDetail.Longitude!.Value.ToString("0.0000000", CultureInfo.InvariantCulture);
                valori.Add(new ValueRange
                {
                    Range = $"'{ev.SheetName}'!F{ev.SheetRow}",
                    Values = new List<IList<object>> { new List<object> { $"{loc.Country ?? ev.Country}, {loc.City ?? ev.City}, {ev.EventDetail.Address}({lat};{lng})" } }
                });
                if (ev.SheetVerifiedColumn is int vc)
                    valori.Add(new ValueRange
                    {
                        Range = $"'{ev.SheetName}'!{(char)('A' + vc)}{ev.SheetRow}",
                        Values = new List<IList<object>> { new List<object> { VerifiedGemini } }
                    });
            }
            await service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest { ValueInputOption = "RAW", Data = valori }, spreadsheetId).ExecuteAsync();

            // nota sulla cella F: da dove viene l'indirizzo (passandoci sopra col mouse)
            var spreadsheet = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();
            var sheetIds = spreadsheet.Sheets.ToDictionary(s => s.Properties.Title, s => s.Properties.SheetId);
            var note = lista
                .Where(x => sheetIds.ContainsKey(x.Event.SheetName!))
                .Select(x => new Request
                {
                    UpdateCells = new UpdateCellsRequest
                    {
                        Start = new GridCoordinate { SheetId = sheetIds[x.Event.SheetName!], RowIndex = x.Event.SheetRow!.Value - 1, ColumnIndex = 5 },
                        Rows = new List<RowData> { new() { Values = new List<CellData> { new() { Note = x.Nota } } } },
                        Fields = "note"
                    }
                }).ToList();
            if (note.Count > 0)
                await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = note }, spreadsheetId).ExecuteAsync();

            Console.WriteLine($"[sheet] 🤖 indirizzi di Gemini scritti su {lista.Count} righe ('{VerifiedGemini}' in '{VerifiedHeader}')");
        }

        private static bool DetermineIfFree(string sheetTitle, IList<object> row)
        {
            var conditions = new List<Func<bool>>
            {
                // Condizione per Serate
                () => sheetTitle == TipoEvento.Serate.ToString() &&
                        row[12] != null &&
                        row[12].ToString().ToLower().Equals("si"),
        
               
                () => sheetTitle == TipoEvento.Serate.ToString() &&
                        row[12] != null &&
                        row[12].ToString().ToLower().Equals("no"),
                // Condizione per Workshop
                () => sheetTitle == TipoEvento.Workshop.ToString() &&
                        row[17] != null &&
                        row[17].ToString().ToLower().Equals("si"),
        
                
                () => sheetTitle == TipoEvento.Workshop.ToString() &&
                        row[17] != null &&
                        row[17].ToString().ToLower().Equals("no"),
            };

            // Usa LINQ Any() per verificare se almeno una condizione è vera
            return conditions.Any(condition => condition());
        }
        private static List<StileEvento> ParseStili(string? stiliString)
        {
            var result = new List<StileEvento>();

            if (string.IsNullOrWhiteSpace(stiliString))
                return result;

            // Dividi la stringa per virgole, punti e virgola, o slash
            var stiliArray = stiliString.Split(new[] { ',', ';', '/', '|' },
                                              StringSplitOptions.RemoveEmptyEntries);

            foreach (var stile in stiliArray)
            {
                var stilePulito = stile.Trim().ToLower();

                switch (stilePulito)
                {
                    case "lindy":
                    case "lindy hop":
                    case "lindyhop":
                        result.Add(StileEvento.Lindy);
                        break;
                    case "balboa":
                    case "bal":
                        result.Add(StileEvento.Balboa);
                        break;
                    case "blues":
                    case "blues dance":
                        result.Add(StileEvento.Blues);
                        break;
                    case "solo jazz":
                    case "solojazz":
                    case "solo":
                    case "jazz":
                        result.Add(StileEvento.SoloJazz);
                        break;
                    //case "charleston":
                    //    result.Add(StileEvento.Charleston);
                    //    break;
                    case "shag":
                    case "collegiate shag":
                        result.Add(StileEvento.Shag);
                        break;
                    //case "authentic jazz":
                    //case "authentic":
                    //case "vintage jazz":
                    //    result.Add(StileEvento.AuthenticJazz);
                    //    break;
                    //default:
                    //    result.Add(StileEvento.Other);
                    //    break;
                }
            }

            return result;
        }
        public static string GetFile()
        {
            try
            {
                string filePath = "C:\\Users\\giaco\\Il mio Drive\\";
                string assemblyPath = AppContext.BaseDirectory;
                string appDirectory = AppContext.BaseDirectory;
                //Console.WriteLine($"📂 Directory dell'app: {appDirectory}");

                var dirInfo = new DirectoryInfo(filePath);
                string[] files = Directory.GetFiles(dirInfo.FullName, "SiBalla PDF.gsheet");

                if (!Directory.Exists(dirInfo.FullName))
                {
                    //Console.WriteLine("Directory non trovata: " + testaassdir.FullName);
                }
                else
                {
                    //Console.WriteLine("Directory trovata: " + testaassdir.FullName);
                }

                if (!Directory.Exists(dirInfo.FullName))
                {
                    //EventLogger.Log.e("error ", "directory not found");
                }
                else
                {
                    //Console.WriteLine("Directory trovata: " + testaassdir.FullName);
                }

                return files.FirstOrDefault();
            }
            catch (Exception ex)
            {
                //Console.WriteLine("Errore: " + ex.Message);
                return "";
            }
        }

        public const string VerifiedHeader = "Luogo verificato";
        public const string VerifiedAuto = "auto";   // indirizzo copiato dal dizionario
        public const string VerifiedOk = "ok";       // indirizzo e coordinate controllati a mano
        // riga aggiunta dal programma con l'evento trovato online: non va sul DB e non insegna niente al dizionario
        // finché non scrivi "ok" (o svuoti la cella)
        public const string VerifiedPending = "da verificare";
        // riga aggiunta dal programma che Gemini considera swing: va sul DB, ma non insegna niente al dizionario finché non scrivi "ok"
        public const string VerifiedNew = "nuovo";
        // indirizzo trovato da Gemini e confermato da Photon: va sul DB, ma non insegna niente al dizionario finché non scrivi "ok"
        public const string VerifiedGemini = "gemini";
        // evento scartato da te: la riga resta nello Sheet (così il programma non lo riaggiunge) ma non va da nessuna parte:
        // né sul DB né sul sito né nell'email, e non insegna niente al dizionario
        public const string VerifiedNo = "no";
        // evento passato recuperato dalle vecchie versioni dello Sheet (storico per il riepilogo dell'anno), mai controllato:
        // non insegna niente al dizionario (luoghi e stili). Essendo passato non va comunque su DB, sito o email
        public const string VerifiedStorico = "storico";
        public static bool Scartato(Event e) => e.LuogoVerificato == VerifiedNo;

        public const string ImageHeader = "Immagine";
        public const string ImageYes = "sì";
        public const string ImageNo = "no";   // cercata sul sito della riga senza trovarla: non la ricerco a ogni giro
        public const string ImageLogo = "logo";   // nessuna immagine dell'evento: logo dell'organizzatore (data/loghi)

        public static bool IsLogo(string? value) =>
            string.Equals(value?.Trim(), ImageLogo, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Scrive "sì"/"no" nella colonna "Immagine" delle righe indicate
        /// </summary>
        public static async Task UpdateSheetImageCellsAsync(IEnumerable<(Event Event, string Value)> cells)
        {
            var data = cells
                .Where(x => x.Event.SheetName is not null && x.Event.SheetRow is not null && x.Event.SheetImageColumn is not null)
                .Select(x => new ValueRange
                {
                    Range = $"'{x.Event.SheetName}'!{(char)('A' + x.Event.SheetImageColumn!.Value)}{x.Event.SheetRow}",
                    Values = new List<IList<object>> { new List<object> { x.Value } }
                }).ToList();
            if (data.Count == 0) return;

            var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                ?? throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");

            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets),
                ApplicationName = "Google Sheets API .NET"
            });

            var response = await service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest
            {
                ValueInputOption = "RAW",
                Data = data
            }, "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo").ExecuteAsync();
            Console.WriteLine($"[sheet] colonna '{ImageHeader}' aggiornata: {response.TotalUpdatedCells} celle");
        }

        /// <summary>
        /// Svuota tutti i fogli dalla riga 3 in giù (solo i valori): restano le intestazioni (righe 1-2),
        /// la formattazione, le convalide e le macro (Apps Script, che non sta nelle celle).
        /// Prima salva una copia di tutti i valori in un JSON nella cartella dati. Restituisce il percorso della copia.
        /// </summary>
        // nome delle schede di backup create dal reset: il sito (esporta_sheet.py) le salta
        public const string PrefissoBackup = "Backup ";

        public static async Task<string?> ResetSheetAsync()
        {
            var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                ?? throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");

            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets),
                ApplicationName = "Google Sheets API .NET"
            });

            const string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";
            var spreadsheet = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();

            // 1. copia di sicurezza: tutti i valori di tutti i fogli (con FORMULA restano anche le formule)
            var backup = new Dictionary<string, IList<IList<object>>>();
            foreach (var sheet in spreadsheet.Sheets)
            {
                var get = service.Spreadsheets.Values.Get(spreadsheetId, $"'{sheet.Properties.Title}'");
                get.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.FORMULA;
                backup[sheet.Properties.Title] = (await get.ExecuteAsync()).Values ?? new List<IList<object>>();
            }
            var backupPath = DataPaths.Get($"backup-sheet-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(backupPath, System.Text.Json.JsonSerializer.Serialize(backup));
            Console.WriteLine($"[sheet] 💾 copia dello Sheet salvata in {backupPath}");

            // solo le schede degli eventi: Contenuti, Musica, Scuole, ... e le schede "Backup ..." restano come sono
            var nomiEventi = Enum.GetNames<TipoEvento>();
            var fogliEventi = spreadsheet.Sheets.Where(s => nomiEventi.Contains(s.Properties.Title)).ToList();

            // 2. copia delle schede dentro lo Sheet stesso ("Backup Serate 2026-11-14 15.30"), in fondo: così lo storico
            //    resta su Drive e si vede da qualsiasi PC. Se la copia non riesce l'eccezione ferma il reset prima di svuotare
            var timbro = DateTime.Now.ToString("yyyy-MM-dd HH.mm");
            int indice = spreadsheet.Sheets.Count;
            await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest
            {
                Requests = fogliEventi.Select(s => new Request
                {
                    DuplicateSheet = new DuplicateSheetRequest
                    {
                        SourceSheetId = s.Properties.SheetId,
                        NewSheetName = $"{PrefissoBackup}{s.Properties.Title} {timbro}",
                        InsertSheetIndex = indice++
                    }
                }).ToList()
            }, spreadsheetId).ExecuteAsync();
            foreach (var s in fogliEventi)
                Console.WriteLine($"[sheet] 📑 copia della scheda '{s.Properties.Title}' salvata nello Sheet come '{PrefissoBackup}{s.Properties.Title} {timbro}'");

            // 3. svuoto dalla riga 3 all'ultima delle schede degli eventi
            var ranges = fogliEventi
                .Where(s => (s.Properties.GridProperties?.RowCount ?? 0) >= 3)
                .Select(s => $"'{s.Properties.Title}'!3:{s.Properties.GridProperties.RowCount}")
                .ToList();
            if (ranges.Count == 0) return backupPath;

            await service.Spreadsheets.Values.BatchClear(new BatchClearValuesRequest { Ranges = ranges }, spreadsheetId).ExecuteAsync();
            foreach (var s in fogliEventi)
                Console.WriteLine($"[sheet] 🧹 '{s.Properties.Title}': {Math.Max(0, backup[s.Properties.Title].Count - 2)} righe svuotate");
            return backupPath;
        }

        /// <summary>
        /// Cancella dallo Sheet le righe degli eventi indicati (intere righe, le successive salgono).
        /// Dal basso verso l'alto in un'unica richiesta, così i numeri di riga letti restano validi.
        /// </summary>
        public static async Task DeleteSheetRowsAsync(IEnumerable<Event> events)
        {
            var righe = events
                .Where(x => x.SheetName is not null && x.SheetRow is int r && r > 2)   // le prime due righe sono intestazioni
                .Select(x => (Sheet: x.SheetName!, Row: x.SheetRow!.Value))
                .Distinct()
                .ToList();
            if (righe.Count == 0) return;

            var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                ?? throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");

            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets),
                ApplicationName = "Google Sheets API .NET"
            });

            const string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";
            var spreadsheet = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();
            var sheetIds = spreadsheet.Sheets.ToDictionary(s => s.Properties.Title, s => s.Properties.SheetId);

            var requests = righe
                .Where(x => sheetIds.ContainsKey(x.Sheet))
                .OrderBy(x => x.Sheet).ThenByDescending(x => x.Row)
                .Select(x => new Request
                {
                    DeleteDimension = new DeleteDimensionRequest
                    {
                        Range = new DimensionRange
                        {
                            SheetId = sheetIds[x.Sheet],
                            Dimension = "ROWS",
                            StartIndex = x.Row - 1,   // SheetRow parte da 1, l'API da 0
                            EndIndex = x.Row
                        }
                    }
                }).ToList();

            await service.Spreadsheets.BatchUpdate(new BatchUpdateSpreadsheetRequest { Requests = requests }, spreadsheetId).ExecuteAsync();
            Console.WriteLine($"[sheet] 🗑️ cancellate {requests.Count} righe di lezioni");
        }

        // indice (0 = A) della colonna con questa intestazione, null se manca
        private static int? FindHeaderColumn(IList<object> headerRow, string name) =>
            headerRow.Select((c, i) => (c, i))
                .Where(x => string.Equals(x.c?.ToString()?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                .Select(x => (int?)x.i).FirstOrDefault();

        private static bool IsYes(string? value) =>
            value?.Trim().ToLowerInvariant() is "sì" or "si" or "x" or "yes" or "true" or "1";

        public record LocationCell(string? Country, string? City, string? Address, double? Lat, double? Lng);

        private static readonly Regex CoordsRe = new(
            @"\(\s*(?<lat>-?\d+(?:[.,]\d+)?)\s*[;,]\s*(?<lng>-?\d+(?:[.,]\d+)?)\s*\)\s*$");

        private static double ParseD(string s) =>
            double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

        public static LocationCell ParseLocationCell(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new(null, null, null, null, null);

            double? lat = null, lng = null;
            var m = CoordsRe.Match(raw);
            if (m.Success)
            {
                lat = ParseD(m.Groups["lat"].Value);
                lng = ParseD(m.Groups["lng"].Value);
                raw = raw[..m.Index];
            }

            var parts = raw.Split(',', 3, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return new(parts.ElementAtOrDefault(0), parts.ElementAtOrDefault(1), parts.ElementAtOrDefault(2), lat, lng);
        }

        /// <summary>
        /// Riscrive la colonna F degli eventi indicati: sostituisce la parte "(lat;lng)" e, per gli indirizzi copiati dal dizionario,
        /// scrive l'indirizzo dopo la città (e "auto" in "Luogo verificato"). Paese, città e indirizzi scritti da te restano come sono.
        /// Con write = false stampa solo l'anteprima.
        /// </summary>
        public static async Task UpdateSheetLocationsAsync(IEnumerable<Event> events, bool write)
        {
            var data = new List<ValueRange>();
            foreach (var ev in events)
            {
                if (ev.SheetName is null || ev.SheetRow is null || ev.SheetLocationRaw is null) continue;
                if (ev.EventDetail?.Latitude is not double lat || ev.EventDetail.Longitude is not double lng) continue;
                // senza la colonna non posso segnarlo "auto": al giro dopo sembrerebbe scritto da te e diventerebbe una fonte
                if (ev.IndirizzoDalDizionario && ev.SheetVerifiedColumn is null) continue;

                var raw = ev.SheetLocationRaw;
                var m = CoordsRe.Match(raw);
                var text = (m.Success ? raw[..m.Index] : raw).TrimEnd();
                // indirizzo dal dizionario: paese e città restano come li hai scritti, l'indirizzo viene aggiunto o sostituito
                if (ev.IndirizzoDalDizionario)
                {
                    var loc = ParseLocationCell(raw);
                    text = $"{loc.Country}, {loc.City}, {ev.EventDetail.Address}";
                }
                var cell = $"{text}({lat.ToString("0.0000000", CultureInfo.InvariantCulture)};{lng.ToString("0.0000000", CultureInfo.InvariantCulture)})";
                if (cell == raw) continue;

                data.Add(new ValueRange
                {
                    Range = $"'{ev.SheetName}'!F{ev.SheetRow}",
                    Values = new List<IList<object>> { new List<object> { cell } }
                });

                // indirizzo copiato: lo segno "auto", così al prossimo giro non diventa una fonte e lo puoi filtrare
                var mark = "";
                if (ev.IndirizzoDalDizionario && ev.SheetVerifiedColumn is int vc)
                {
                    data.Add(new ValueRange
                    {
                        Range = $"'{ev.SheetName}'!{(char)('A' + vc)}{ev.SheetRow}",
                        Values = new List<IList<object>> { new List<object> { VerifiedAuto } }
                    });
                    mark = $" + {VerifiedHeader} = {VerifiedAuto}";
                }
                Console.WriteLine($"[sheet{(write ? "" : " anteprima")}] {ev.SheetName} riga {ev.SheetRow}: '{raw}' -> '{cell}'{mark}");
            }

            if (data.Count == 0) return;
            if (!write)
            {
                Console.WriteLine($"[sheet] {data.Count} celle da aggiornare: anteprima, niente scritto (Import:UpdateSheetCoordinates = false)");
                return;
            }

            var resourcePath = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                ?? throw new FileNotFoundException($"Risorsa '{resourcePath}' non trovata");

            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = GoogleCredential.FromStream(stream).CreateScoped(SheetsService.Scope.Spreadsheets),
                ApplicationName = "Google Sheets API .NET"
            });

            var request = service.Spreadsheets.Values.BatchUpdate(new BatchUpdateValuesRequest
            {
                ValueInputOption = "RAW",
                Data = data
            }, "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo");

            var response = await request.ExecuteAsync();
            Console.WriteLine($"[sheet] coordinate aggiornate sullo sheet: {response.TotalUpdatedCells} celle");
        }
    }
}

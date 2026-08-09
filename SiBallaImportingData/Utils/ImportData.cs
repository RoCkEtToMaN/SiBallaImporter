using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Type;
using OfficeOpenXml;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DateTime = System.DateTime;

namespace SiBallaImportingData.Utils
{
    public static class ImportData
    {
        public static Dictionary<TipoEvento, List<Event>> LoadFile()
        {
            var file = GetFile();

            var getLocationInfo = new LocationInfo();
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
            int eventType = 0;

            foreach (var sheet in spreadsheet.Sheets)
            {
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

                    if (values != null && values.Count > 0)
                    {
                        foreach (var row in values)
                        {
                            //Console.WriteLine(string.Join(", ", row));
                            //eventItem.MonthYear = eventItem.Month + " " + eventItem.DateStart.Year;
                            //eventItem.NameAndData = eventItem.PartyName + " " + eventItem.DateStart.ToString();
                            if (values.IndexOf(row) > 1)
                            {
                                var stiliEvento = ParseStili(row[7].ToString());
                                var latlong = getLocationInfo.GetCoordinatesFromCityAndNameLocation(
                                    row[5].ToString().Split(",")[1].Replace(" ", ""),
                                    row[5].ToString().Split(",")[2].Replace(" ", ""),
                                    row[5].ToString().Split(",")[0]
                                    ).Result;


                                var duration = Convert.ToDateTime(row[1]) - Convert.ToDateTime(row[0]);

                                eventList.Add(new Event
                                {
                                    DateStart = Convert.ToDateTime(row[0]),
                                    DateFinish = Convert.ToDateTime(row[1]),
                                    PartyName = row[4].ToString(),
                                    //MonthYear = Month + " " + Convert.ToDateTime(row[0]).Year.ToString(),
                                    NameAndData = Convert.ToDateTime(row[0]).Date.Day.ToString() + "th: " + row[4].ToString(),// + " " + Convert.ToDateTime(row[0]).Date.ToString("dd/MM/YYYY"),
                                    EventDetail = new EventDetail
                                    {
                                        PartyName = row[4].ToString(),
                                        //When = Convert.ToDateTime(row[0]).ToString("dddd, dd MMMM yyyy"),   
                                        Latitude = latlong.Latitiude.ToString(),
                                        Longitude = latlong.Longitude.ToString(),
                                        Location = row[5].ToString().Split(",")[0],
                                        City = row[5].ToString().Split(",")[1].Replace(" ", ""),
                                        Country = row[5].ToString().Split(",")[2].Replace(" ", ""),
                                        Styles = stiliEvento,
                                        WebSite = sheet.Properties.Title == TipoEvento.Serate.ToString() ? row[13].ToString() : row[17].ToString()
                                    },
                                    Organizer = row[6].ToString(),
                                    City = row[5].ToString().Split(",")[1].Replace(" ", ""),
                                    Country = row[5].ToString().Split(",")[2].Replace(" ", ""),
                                    Gratis = DetermineIfFree(sheet.Properties.Title, row),
                                    Type = eventType == 0 ? Model.Type.Event: Model.Type.Workshop
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

                eventType++;
            }
            
            return eventiDic;           
        }
        public static void TestReadWritePermissions()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SiBallaImportingData.Resources.siballa-8c6d48649c13.json";

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                var credential = GoogleCredential.FromStream(stream)
                    .CreateScoped(SheetsService.Scope.Spreadsheets);

                var service = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "Test Permissions"
                });

                string spreadsheetId = "1USp-VdikKPfroZWQ3-f5B7tOb5PcoOb4iJTfx2WOQOo";

                try
                {
                    // TEST 1: Lettura (dovrebbe funzionare)
                    Console.WriteLine("Test LETTURA...");
                    var readRequest = service.Spreadsheets.Values.Get(spreadsheetId, "A1:C5");
                    var readResult = readRequest.Execute();
                    Console.WriteLine($"✅ Lettura OK - {readResult.Values?.Count ?? 0} righe lette");

                    // TEST 2: Scrittura (questo fallisce)
                    Console.WriteLine("\nTest SCRITTURA...");
                    var writeData = new ValueRange
                    {
                        Values = new List<IList<object>> { new List<object> { "TEST", System.DateTime.Now.ToString("HH:mm:ss") } }
                    };

                    // Prova a scrivere in una cella che probabilmente esiste
                    var writeRequest = service.Spreadsheets.Values.Update(
                        writeData,
                        spreadsheetId,
                        "Z100"); // Cella probabilmente vuota

                    writeRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

                    var writeResult = writeRequest.Execute();
                    Console.WriteLine($"✅ Scrittura OK - Celle aggiornate: {writeResult.UpdatedCells}");

                    // Se arriva qui, i permessi sono corretti!
                    Console.WriteLine("\n🎉 TUTTI I PERMESSI SONO OK!");
                }
                catch (GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Console.WriteLine($"\n❌ ERRORE: Il Service Account ha permessi di SOLO LETTURA");
                    Console.WriteLine($"Messaggio: {ex.Error?.Message}");

                    // Leggi l'email dal JSON
                    stream.Position = 0;
                    using (var reader = new StreamReader(stream))
                    {
                        string json = reader.ReadToEnd();
                        dynamic jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                        string serviceEmail = jsonObj.client_email;

                        Console.WriteLine("\n🔧 CORREGGI I PERMESSI:");
                        Console.WriteLine($"1. Apri il Google Sheet");
                        Console.WriteLine($"2. Clicca 'Condividi'");
                        Console.WriteLine($"3. Trova l'email: {serviceEmail}");
                        Console.WriteLine($"4. Cambia da 'Visualizzatore' a 'Editor'");
                        Console.WriteLine($"5. Clicca 'Salva'");
                        Console.WriteLine($"6. Aspetta 30 secondi");
                        Console.WriteLine($"7. Riprova!");
                    }
                }
            }
        }
        public static async Task PrepareToAppendToGoogleSheet(List<Event> events)
        {
            // Qui puoi fare eventuali preparazioni prima di chiamare AppendToGoogleSheet2
            // Ad esempio, filtrare o trasformare i dati in 'events' se necessario
            // Poi chiami AppendToGoogleSheet2 con i dati preparati
            int w = 0, e = 0;
            var eventsToAppend = new List<Event>();
            var workshopsToAppend = new List<Event>();

            foreach (var ev in events)
            {
                var date = ev.DateFinish - ev.DateStart;

                Console.WriteLine($"ev.DateFinish {ev.DateFinish}; ev.DateStart {ev.DateStart}; howMuhch {date}");
                // Esempio di preparazione: aggiungi un prefisso al nome dell'evento
                ev.PartyName = "[API] " + ev.PartyName;

                if(date.Value.TotalDays > 1)
                {
                    w++;
                    workshopsToAppend.Add(ev);
                }
                else if (date.Value.TotalDays <= 1)
                {
                    e++;
                    eventsToAppend.Add(ev);


                }
            }


            try
            {
                Console.WriteLine("Avvio programma...");

                if (eventsToAppend.Count() > 0)
                    await AppendToGoogleSheet(eventsToAppend, "Serate");
                if (workshopsToAppend.Count() > 0)
                    await AppendToGoogleSheet(workshopsToAppend, "Workshop");

                Console.WriteLine($"quanti e: {e}; quanti w: {w}");

                Console.WriteLine("Operazione completata con successo!");
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
                // 1. Test di connessione e permessi corretto con .Execute()
                Console.WriteLine("Verifico i permessi...");
                var testRequest = service.Spreadsheets.Get(spreadsheetId);
                var spreadsheet = testRequest.Execute(); // <-- Corretto da .Get() a .Execute()
                Console.WriteLine($"Accesso consentito al documento: {spreadsheet.Properties.Title}");



                //Console.WriteLine("Verifico i permessi e leggo i fogli disponibili...");
                //var testRequest = service.Spreadsheets.Get(spreadsheetId);
                //var spreadsheet = testRequest.Execute();

                // 🔍 STAMPA IN CONSOLE TUTTI I NOMI DEI FOGLI PRESENTi NEL DOCUMENTO
                Console.WriteLine("--- FOGLI PRESENTI NEL TUO GOOGLE SHEET ---");
                foreach (var sheet in spreadsheet.Sheets)
                {
                    Console.WriteLine($"> Nome esatto: [{sheet.Properties.Title}] (ID: {sheet.Properties.SheetId})");
                }
                Console.WriteLine("---------------------------------------------");

                // 2. Mappatura e formattazione dei dati dagli eventi
                var newData3 = events.Select(ev => {
                    var datefinish = (DateTime)ev.DateFinish;

                    string cleanPartyName = "";
                    if (!string.IsNullOrEmpty(ev.PartyName))
                    {
                        cleanPartyName = ev.PartyName.Replace("[API]", "").Trim();
                    }

                    return (IList<object>)new List<object>
                    {
                        ev.DateStart.ToString("yyyy-MM-dd"), // Colonna A
                        datefinish.ToString("yyyy-MM-dd"),   // Colonna B
                        "",                                  // Colonna C (lasciata vuota)
                        "",                                  // Colonna D (lasciata vuota)
                        cleanPartyName,                      // Colonna E
                        ev.Country + ", " + ev.City,         // Colonna F 
                        ev.Organizer                         // Colonna G
                    };
                }).ToList<IList<object>>();

                var valueRange = new ValueRange
                {
                    Values = newData3
                };

                // 3. Configurazione dell'Append usando il nome del foglio o l'intervallo delle colonne
                // (es. "Foglio1!A:G" dice all'API di accodare i dati in fondo alla tabella senza forzare il grassetto)
                //string targetRange = "Serate!A:G"; // Sostituisci "Foglio1" con il nome esatto del tuo foglio se diverso

                var appendRequest = service.Spreadsheets.Values.Append(valueRange, spreadsheetId, range);
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;

                // 4. Esecuzione asincrona dell'inserimento
                var response = appendRequest.Execute();

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
    }
}

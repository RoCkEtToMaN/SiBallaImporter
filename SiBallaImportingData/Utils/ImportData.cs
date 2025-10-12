using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using OfficeOpenXml;
using SiBallaImportingData.Model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SiBallaImportingData.Utils
{
    public static class ImportData
    {
        public static Dictionary<TipoEvento, List<ExcelTemplate>> LoadFile()
        {
            var file = GetFile();
            Dictionary<TipoEvento, List<ExcelTemplate>> eventiDic = new Dictionary<TipoEvento, List<ExcelTemplate>>();

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
                Console.WriteLine($"- '{sheet.Properties.Title}'");

                var eventList = new List<ExcelTemplate>();                

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

                            if(values.IndexOf(row) > 1)
                            {
                                var stiliEvento = ParseStili(row[7].ToString());

                                eventList.Add(new ExcelTemplate
                                {
                                    Inizio = Convert.ToDateTime(row[0]),
                                    Fine = Convert.ToDateTime(row[1]),
                                    Giorni = row[2].ToString(),
                                    N = Convert.ToInt32(row[3]),
                                    NomeEvento = row[4].ToString(),
                                    Luogo = row[5].ToString(),
                                    Organizzatore = row[6].ToString(),
                                    Stile = stiliEvento,
                                    Riferimento = sheet.Properties.Title == TipoEvento.Serate.ToString() ? row[13].ToString(): row[17].ToString(),
                                    Ordine = sheet.Properties.Title == TipoEvento.Serate.ToString() ? Convert.ToInt32(row[14]): Convert.ToInt32(row[18]),
                                    Gratis = DetermineIfFree(sheet.Properties.Title, row)
                                });
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

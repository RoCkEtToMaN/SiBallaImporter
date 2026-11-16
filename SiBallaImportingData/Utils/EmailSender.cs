using Newtonsoft.Json;
using SiBallaImportingData.Model;
using System.Net.Http.Headers;
using System.Text;

namespace SiBallaImportingData.Utils
{
    public static class EmailSender
    {
        /// <summary>true se l'email è partita (o è stata salvata l'anteprima)</summary>
        public static async Task<bool> SendWithMailjetAnonymous(List<Event> eventi, AppSettings settings, List<(Event Evento, string Motivo)>? daVerificare = null, List<Event>? luogoDaVerificare = null)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(settings.Email.PreviewFile))
                {
                    await File.WriteAllTextAsync(settings.Email.PreviewFile, TextEmail(eventi, settings.Import, daVerificare, luogoDaVerificare));
                    Console.WriteLine($"📄 Email non inviata, anteprima salvata in {settings.Email.PreviewFile}");
                    return true;
                }

                //var textEm = TextEmail(eventi);
                // chiavi da user-secrets (PC locale) o variabili d'ambiente Email__MailjetApiKey / Email__MailjetApiSecret
                var apiKeyGmail = settings.Email.MailjetApiKey;
                var apiSecretGmail = settings.Email.MailjetApiSecret;
                if (!settings.Email.HasMailjetKeys)
                {
                    Console.WriteLine("❌ Chiavi Mailjet mancanti: dotnet user-secrets set \"Email:MailjetApiKey\" \"...\" (e Email:MailjetApiSecret)");
                    return false;
                }

                //using var client = new HttpClient();
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30) // Aumenta timeout se necessario
                };
                var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKeyGmail}:{apiSecretGmail}"));
                client.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

                var emailData = new
                {
                    Messages = new[]
                    {
                        new
                        {
                            From = new {
                                //Email = "info@siballa.xyz",
                                Email = "siballanewsletter@gmail.com",
                                Name = "siballanewsletter"
                            },
                            To = settings.Email.Recipients
                                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(email => email.Trim())
                                .Where(email => !string.IsNullOrEmpty(email))
                                .Select(email => new { Email = email }).ToArray(),
                            Subject = $"Bollettino settimanale {DateTime.Today.ToString("dd/MM/yyyy")} - " +
                                $"{DateTime.Today.AddDays(settings.Import.Giorni).ToString("dd/MM/yyyy")}",
                            TextPart = TextEmail(eventi, settings.Import, daVerificare, luogoDaVerificare),
                            HTMLPart = TextEmail(eventi, settings.Import, daVerificare, luogoDaVerificare)
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(emailData, Formatting.Indented);
                Console.WriteLine($"JSON inviato:\n{json}");
                try
                {
                    var response = await client.PostAsync(
                    "https://api.mailjet.com/v3.1/send",
                    new StringContent(JsonConvert.SerializeObject(emailData),
                                     Encoding.UTF8, "application/json"));

                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"✅ Successo ({(int)response.StatusCode})");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"❌ Errore {(int)response.StatusCode}: {responseBody}");
                    }
                }
                catch (ThreadAbortException)
                {
                    Console.WriteLine("Thread abortito");
                }
                catch (OutOfMemoryException)
                {
                    Console.WriteLine("Memoria esaurita");
                }
                catch (StackOverflowException)
                {
                    Console.WriteLine("Stack overflow");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Errore: {e}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Eccezione: {ex.Message}");
            }
            return false;
        }
        private static string TextEmail(List<Event> eventi, ImportOptions opzioni, List<(Event Evento, string Motivo)>? daVerificare = null, List<Event>? luogoDaVerificare = null)
        {
            string text = $"Buongiorno ecco a voi il bollettino provvisorio per il range di date " +
                          $"{DateTime.Now} - {DateTime.Today.AddDays(opzioni.Giorni)} ,<br>" +
                          $"con raggio d'azione di {opzioni.Radius} Km.<br><br>" +
                          $"Bollettino ({DateTime.Today.Day}/{DateTime.Today.Month} - " +
                          $"{DateTime.Today.AddDays(opzioni.Giorni).Day}/" +
                          $"{DateTime.Today.AddDays(opzioni.Giorni).Month})<br><br>";

            text += $"*Bollettino della settimana*🗓️:<br>";

            int i = 1;            
          
            foreach (var city in EventIdentity.Unique(eventi).GroupBy(x => x.City).OrderByDescending(g => string.Equals(g.Key, "Genova", StringComparison.Ordinal)).ThenBy(g => g.Key))
            {
                text += $"_*{city.Key}*_:<br>";

                foreach (var ev in city.OrderBy(e => e.DateStart)) // Ordina eventi per data
                {
                    var duration = ev.Duration == TimeSpan.Zero
                        ? "orario non indicato"
                        : ev.Duration.Days != 0
                            ? $"{ev.Duration.Days} giorni"
                            : $"{ev.Duration.Hours} ore";

                    text += $"  ·{ev.DateStart.Day}/{ev.DateStart.Month}; {ev.PartyName}; {duration}; {ev.Organizer}; {ev.EventDetail.WebSite}<br>";
                    i++;
                }
            }

            if (daVerificare != null && daVerificare.Count > 0)
            {
                text += $"<br><br>*Eventi da verificare (secondo l'AI probabilmente non swing)*❓:<br>";

                foreach (var (ev, motivo) in daVerificare.OrderBy(x => x.Evento.DateStart))
                    text += $"  ·{ev.DateStart.Day}/{ev.DateStart.Month}; {ev.City}; {ev.PartyName}; {ev.Organizer}; {ev.EventDetail.WebSite} — <i>{motivo}</i><br>";
            }

            // eventi di cui non si sa la città: non so se sono dentro il raggio, li segnalo invece di perderli
            if (luogoDaVerificare != null && luogoDaVerificare.Count > 0)
            {
                text += $"<br><br>*Luogo da verificare (città sconosciuta)*📍:<br>";

                foreach (var ev in luogoDaVerificare.OrderBy(x => x.DateStart))
                    text += $"  ·{ev.DateStart.Day}/{ev.DateStart.Month}; {ev.Country}; {ev.PartyName}; {ev.Organizer}; {ev.EventDetail.WebSite} — <i>{ev.ProblemaLuogo}</i><br>";
            }

            //Togliere la data nel titolo e mettere durata in ore

            return text;
        }        
    }
}

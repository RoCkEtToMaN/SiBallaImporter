using Newtonsoft.Json;
using SiBallaImportingData.Model;
using System.Configuration;
using System.Net.Http.Headers;
using System.Text;

namespace SiBallaImportingData.Utils
{
    public static class EmailSender
    {
        public static async Task SendWithMailjetAnonymous(List<Event> eventi, List<(Event Evento, string Motivo)>? daVerificare = null)
        {
            try
            {
                //var textEm = TextEmail(eventi);
                // chiavi in secrets.config (non versionato), incluso da App.config
                var apiKeyGmail = ConfigurationManager.AppSettings["MailjetApiKey"];
                var apiSecretGmail = ConfigurationManager.AppSettings["MailjetApiSecret"];
                if (string.IsNullOrWhiteSpace(apiKeyGmail) || string.IsNullOrWhiteSpace(apiSecretGmail))
                {
                    Console.WriteLine("❌ MailjetApiKey/MailjetApiSecret mancanti: crea secrets.config partendo da secrets.config.example");
                    return;
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
                            To = ConfigurationManager.AppSettings["Email"]
                                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(email => email.Trim())
                                .Where(email => !string.IsNullOrEmpty(email))
                                .Select(email => new { Email = email }).ToArray(),
                            Subject = $"Bollettino settimanale {DateTime.Today.ToString("dd/MM/yyyy")} - " +
                                $"{DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])).ToString("dd/MM/yyyy")}",
                            TextPart = TextEmail(eventi, daVerificare),
                            HTMLPart = TextEmail(eventi, daVerificare)
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

        }
        private static string TextEmail(List<Event> eventi, List<(Event Evento, string Motivo)>? daVerificare = null)
        {
            string text = $"Buongiorno ecco a voi il bollettino provvisorio per il range di date " +
                          $"{DateTime.Now} - {DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"]))} ,<br>" +
                          $"con raggio d'azione di {(float)Convert.ToDouble(ConfigurationManager.AppSettings["Radius"])} Km.<br><br>" +
                          $"Bollettino ({DateTime.Today.Day}/{DateTime.Today.Month} - " +
                          $"{DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])).Day}/" +
                          $"{DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])).Month})<br><br>";

            text += $"*Bollettino della settimana*🗓️:<br>";

            int i = 1;            
          
            foreach (var city in eventi.GroupBy(x => x.City).OrderByDescending(g => string.Equals(g.Key, "Genova", StringComparison.Ordinal)).ThenBy(g => g.Key))
            {
                text += $"_*{city.Key}*_:<br>";

                foreach (var ev in city.OrderBy(e => e.DateStart)) // Ordina eventi per data
                {
                    var duration = ev.Duration.Days != 0
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

            //Togliere la data nel titolo e mettere durata in ore

            return text;
        }        
    }
}

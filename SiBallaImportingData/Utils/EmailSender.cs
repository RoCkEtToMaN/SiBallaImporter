using Newtonsoft.Json;
using SiBallaImportingData.Model;
using System.Configuration;
using System.Net.Http.Headers;
using System.Text;

namespace SiBallaImportingData.Utils
{
    public static class EmailSender
    {
        public static async Task SendWithMailjetAnonymous(List<Event> eventi)
        {
            try
            {
                var textEm = TextEmail(eventi);
                var apiKey = "***RIMOSSA***";
                var apiSecret = "***RIMOSSA***";

                //using var client = new HttpClient();
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30) // Aumenta timeout se necessario
                };
                var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:{apiSecret}"));
                client.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);

                var emailData = new
                {
                    Messages = new[]
                    {
                        new
                        {
                            From = new {
                                Email = "siballanewsletter@gmail.com",
                                Name = "siballanewsletter@gmail.com"
                            },
                              To = new[]
                                {
                                    new
                                    {
                                        Email = "giacomo.lo87@gmail.com",
                                        Name = "Giacomo"
                                    },
                                    new
                                    {
                                        Email = "c.vota87@gmail.com",
                                        Name = "Carlo"
                                    },
                                    new
                                    {
                                        Email = "alessio.carrega@gmail.com",
                                        Name = "Alessio"
                                    },
                                    new
                                    {
                                        Email = "piccolo.gv@gmail.com",
                                        Name = "Giulia"
                                    },
                                },
                            Subject = $"Bollettino settimanale {DateTime.Today.ToString("dd/MM/yyyy")} - " +
                                $"{DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])).ToString("dd/MM/yyyy")}",
                            TextPart = TextEmail(eventi),
                            HTMLPart = TextEmail(eventi)
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
        private static string TextEmail(List<Event> eventi)
        {
            string text = $"Buongiorno ecco a voi il bollettino provvisorio per il range di date " +
                          $"{DateTime.Now} - {DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"]))} ,<br>" +
                          $"con raggio d'azione di {(float)Convert.ToDouble(ConfigurationManager.AppSettings["Radius"])} Km.<br><br>" +
                          $"Bollettino ({DateTime.Today.Day}/{DateTime.Today.Month} - " +
                          $"{DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])).Day}/" +
                          $"{DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"])).Month})<br><br>";

            foreach (var ev in eventi)
            {
                text += $"·{ev.DateStart.DayOfWeek} {ev.DateStart.Day}; {ev.NameAndData}; {ev.City};{ev.Organizer} <br>";
            }

            //Togliere la data nel titolo e mettere durata in ore

            return text;
        }        
    }
}

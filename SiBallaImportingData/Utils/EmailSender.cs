using Newtonsoft.Json;
using SiBallaImportingData.Model;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SiBallaImportingData.Utils
{
    public static class EmailSender
    {
        public static async Task SendWithMailjetAnonymous(List<Event> eventi)
        {
            TextEmail(eventi);
            var apiKey = "***RIMOSSA***";
            var apiSecret = "***RIMOSSA***";
            var apiKeySina = "37aba9814c0aaf0ca5ffc14adaabae90";
            var apiSecretSina = "1cef5018b25f74ddc14c27547a886a40";

            using var client = new HttpClient();
            var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKeySina}:{apiSecretSina}"));
            client.DefaultRequestHeaders.Add("Authorization", $"Basic {auth}");
            //destinatari.Clear();
            // Aggiungi dinamicamente altri destinatari
            //destinatari.Add(new { Email = "giacomo.lo87@gmail.com" });
            //destinatari.Add(new { Email = "giacomo.lo@iroi.it" });
            //destinatari.Add(new { Email = "francesco.varone@sina.co.it" });
            //destinatari.Add(new { Email = "rachele.bianchi@sina.it" });

            var emailData = new
            {
                Messages = new[]
                {
                    new
                    {
                        From = new {
                            Email = "direzione.scientifica@sina.it",
                            Name = "noreply@sina.it"
                        },
                        //To = destinatari,
                        Bcc = new[]
                        {
                            //new { Email = "francesco.varone@sina.co.it" },
                            //new { Email = "rachele.bianchi@sina.it" },
                            //new { Email ="direzione.scientifica@sina.it" },
                            new { Email ="giacomo.lo87@gmail.com" }
                        },
                        Subject = "Avviso di esercitazione in scadenza",
                        TextPart = TextEmail(eventi),
                        HTMLPart = TextEmail(eventi)
                    }
                }
            };

            // Mailjet accetta mittenti non verificati (ma potrebbero finire in spam)
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

        private static string TextEmail(List<Event> eventi)
        {
            string text = $"Buongiorno ecco a voi il bollettino provvisorio per il range di date " +
                          $"{DateTime.Now} - {DateTime.Today.AddDays(Convert.ToInt32(ConfigurationManager.AppSettings["Giorni"]))} ,<br><br>" +
                          $"con raggio d'azione di {(float)Convert.ToDouble(ConfigurationManager.AppSettings["Radius"])} Km.<br><br>";

            foreach (var ev in eventi)
            {
                text += $"·{ev.DateStart.DayOfWeek} {ev.DateStart.Day}; {ev.NameAndData}; {ev.City};{ev.Organizer} <br>";
            }

            //Togliere la data nel titolo e mettere durata in ore

            return text;
        }
    }
}

using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SiBallaImportingData.Model;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SiBallaImportingData.Utils
{
    public class ImportDataFromWeb
    {
        private readonly string _accessToken;
        private readonly HttpClient _httpClient;
        public ImportDataFromWeb() { }

        // Metodo per ottenere informazioni di una pagina pubblica
        public async Task<JObject> GetFaceBookPageInfo(string pageIdOrUsername)
        {
            string url = $"https://graph.facebook.com/v18.0/{pageIdOrUsername}" +
                        $"?fields=id,name,about,fan_count,posts{{message,created_time}}" +
                        $"&access_token={"1228936242417680 | J3zfYwphpb0p1wldBSmMCR_t - Ic"}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }

        // Metodo per ottenere post di un gruppo (richiede permessi)
        public async Task<JObject> GetFaceBookGroupPosts(string groupId)
        {
            // NOTA: Richiede user_groups permission e membership nel gruppo
            string url = $"https://e-swing.it/index.html";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }
        // Metodo per ottenere post di un gruppo (richiede permessi)
        public async Task<JObject> GetTest()
        {
            string url = $"https://e-swing.it/php/get-events-full.php?nazione=Italia";

            using HttpClient client = new();
            HttpResponseMessage response = await client.GetAsync(url);
            var doc = new HtmlDocument();
            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                doc.LoadHtml(jsonResponse);
            }


            //var response = await _httpClient.GetAsync(url);
            //response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }

        #region ESwingIt
        public async Task<List<ESwingIt>> GetESwing()
        {
            //string url = "https://e-swing.it/php/get-events-full.php?nazione=Italia";
            string url = "https://e-swing.it/php/get-events-full.php";

            using HttpClient client = new();
            // Aggiungi tutti gli header necessari
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            client.DefaultRequestHeaders.Add("Accept-Language", "it-IT,it;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            client.DefaultRequestHeaders.Add("Referer", "https://e-swing.it/index.html");
            client.DefaultRequestHeaders.Add("X-Requested-With", "myXMLHttpRequest");
            client.DefaultRequestHeaders.Add("sec-ch-ua", "\"Google Chrome\";v=\"143\", \"Chromium\";v=\"143\", \"Not A(Brand\";v=\"24\"");
            client.DefaultRequestHeaders.Add("sec-ch-ua-mobile", "?0");
            client.DefaultRequestHeaders.Add("sec-ch-ua-platform", "\"Windows\"");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
            client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");

            var listOfEvents = new List<ESwingIt>();

            try
            {
                HttpResponseMessage response = await client.GetAsync(url);

                //Console.WriteLine($"Status Code: {(int)response.StatusCode} - {response.StatusCode}");
                //Console.WriteLine($"Headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");

                string responseContent = await response.Content.ReadAsStringAsync();
                //Console.WriteLine($"Response Content: {responseContent}");
                var doc = new HtmlDocument();
                if (response.IsSuccessStatusCode)
                {
                    
                    doc.LoadHtml(responseContent);
                    // ... processa il documento
                }

                //using HttpClient client = new();
                string text = doc.DocumentNode.InnerText;
                string html2 = doc.DocumentNode.InnerHtml;

                //var jsonString = await response.Content.ReadAsStringAsync();
                bool canExit = true;
                do 
                {
                    if (html2.EndsWith("]") && html2.StartsWith("["))
                    {
                        // Solo allora deserializzare
                        //var data = JsonSerializer.Deserialize<List<List<object>>>(jsonString);
                        var outerArray = JsonSerializer.Deserialize<JsonElement[][]>(html2);

                        foreach (var innerArray in outerArray)
                        {
                            DateTime end;

                            DateTime start = DateTime.Parse(innerArray[2].GetString() +" " + innerArray[3].GetString());

                            if(innerArray[5].GetString() != null)
                                end = DateTime.Parse(innerArray[5].GetString() +" " + innerArray[6].GetString());
                            else
                            {
                                var endTmp = start.AddDays(1);
                                end = new DateTime(endTmp.Year, endTmp.Month, endTmp.Day, 1, 0, 0);
                            }

                            if (start.Year < DateTime.Now.Year)
                                start = new DateTime(DateTime.Now.Year, start.Month, start.Day, 21, 0, 0);
                            if (start.Hour < 19)
                                start = new DateTime(DateTime.Now.Year, start.Month, start.Day, 21, 0, 0);
                            if (end.Year < DateTime.Now.Year)
                                end = new DateTime(DateTime.Now.Year, end.Month, end.Day);
                            if (end.Hour > 5)
                                end = new DateTime(DateTime.Now.Year, end.Month, end.Day, 2,0,0);

                            var duration = end - start;

                            string urls = GetUrl(innerArray);

                            //for (int i = 30; i < innerArray.Length - 1; i++)
                            //{
                            //    Console.WriteLine($"Elemento {i}: {innerArray[i]}");

                            //    string? urlValue = innerArray[i].GetString();

                            //    if (!string.IsNullOrEmpty(urlValue))                        
                            //        urls.Append(urlValue);                        
                            //}

                            string styles = string.Empty;
                            if (innerArray[28].GetString() != null)
                                styles = GetStile(innerArray[28].GetString());

                            listOfEvents.Add(
                                new ESwingIt 
                                { 
                                    Id = innerArray[0].GetInt32(),
                                    Title = innerArray[9].GetString() != null ? innerArray[9].GetString(): null,
                                    City = innerArray[14].GetString() != null ? innerArray[14].GetString() : null,
                                    State = innerArray[15].GetString() != null ? innerArray[15].GetString() : null,
                                    Country = innerArray[16].GetString() != null ? innerArray[16].GetString() : null,
                                    Start = start,
                                    End = end,
                                    Duration = end - start,
                                    Description = innerArray[17].GetString() != null ? innerArray[17].GetString() : null,
                                    Longitude = (float) double.Parse(innerArray[20].GetString(), CultureInfo.InvariantCulture),
                                    Latitude = (float) double.Parse(innerArray[19].GetString(), CultureInfo.InvariantCulture),
                                    Url = urls,
                                    Styles = styles
                                }
                            );                            
                        }

                        canExit = false;
                    }
                    else if (!html2.EndsWith("]"))
                    {
                        for (int i = html2.Length - 1; i >= 0; i--)
                        {
                            if (html2[i] == ']')
                            {
                                html2 = html2.Substring(0, i + 1);
                                canExit = true;
                                break;
                            }
                        }
                    }
                    else if (!html2.StartsWith("["))
                    {
                        html2 += "[" + html2;
                    }
                }
                while (canExit);                

                return listOfEvents;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return null;
            }
        }

        private string GetStile(string stili)
        {
            string styles = string.Empty;

            foreach (var stile in stili.Split(','))
            {
                switch (stile)
                {
                    case "LH":
                        styles += StileEvento.Lindy.ToString() + ", ";
                        break;
                    case "BL":
                        styles += StileEvento.Blues.ToString() + ", ";
                        break;

                    case "BA":
                        styles += StileEvento.Balboa.ToString() + ", ";
                        break;

                    case "BO":
                        styles += StileEvento.Boogie.ToString() + ", ";
                        break;

                    case "SH":
                        styles += StileEvento.Shag.ToString() + ", ";
                        break;

                    case "SJ":
                        styles += StileEvento.SoloJazz.ToString() + ", ";
                        break;
                   
                    case "CH":
                        styles += StileEvento.Charleston.ToString() + ", ";
                        break;
                    case "SB":
                        styles += StileEvento.SlowBal.ToString() + ", ";
                        break;
                    case "SL":
                        styles += StileEvento.StLouisShag.ToString() + ", ";
                        break;
                }

                //styles += ", ";
            }

            if (!string.IsNullOrEmpty(styles))
                styles = styles.Remove(styles.Count() - 2);


            return styles;
        }

        private string GetUrl(JsonElement[]? innerArray)
        {
            StringBuilder urls = new StringBuilder();
            for (int i = 30; i < innerArray.Length - 1; i++)
            {
                //Console.WriteLine($"Elemento {i}: {innerArray[i]}");
                string? urlValue = innerArray[i].GetString();
                if (!string.IsNullOrEmpty(urlValue))
                    urls.Append(urlValue + ";");
            }
            return urls.ToString();
        }


        #endregion

    }
}

using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using SiBallaDataImporting.Utils;
using SiBallaImportingData.Model;
using SiBallaImportingData.Service;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using static SiBallaImportingData.Service.LocationInfo;
using static System.Net.Mime.MediaTypeNames;

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


                            //VEDERE DI TOGLIERE ; dal LINK che non fa andare nella pagina corretta

                            string styles = string.Empty;
                            if (innerArray[28].GetString() != null)
                                styles = GetStile(innerArray[28].GetString());


                            

                            // 2. Estrazione pulita (GetString() restituisce già null se l'elemento è nullo)
                            string cityVal = innerArray[14].GetString()?.Trim();
                            string stateVal = innerArray[15].GetString()?.Trim();
                            string countryVal = innerArray[16].GetString()?.Trim();

                            (cityVal, stateVal, countryVal) = LocationHelper.NormalizeLocation(cityVal, stateVal, countryVal);

                            listOfEvents.Add(
                                new ESwingIt 
                                { 
                                    Id = innerArray[0].GetInt32(),
                                    Title = innerArray[9].GetString() != null ? innerArray[9].GetString(): null,
                                    City = innerArray[21].GetString() != null ? innerArray[21].GetString() : null,
                                    State = innerArray[15].GetString() != null ? innerArray[15].GetString() : null,
                                    Country = innerArray[16].GetString() != null ? innerArray[16].GetString() : null,
                                    Start = start,
                                    End = end,
                                    Duration = end - start,
                                    Description = innerArray[17].GetString() != null ? innerArray[17].GetString() : null,
                                    Longitude = (float) double.Parse(innerArray[20].GetString(), CultureInfo.InvariantCulture),
                                    Latitude = (float) double.Parse(innerArray[19].GetString(), CultureInfo.InvariantCulture),
                                    Url = urls,
                                    StylesString = styles
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

        #region SwingPlanint

        private ObservableRangeCollection<GroupByDate>? _workshopsListByDate2 = new ObservableRangeCollection<GroupByDate>();
        private List<Event> _workshops = new List<Event>();

        private int countMesi = 0;
        public async Task<List<Event>> GetSwingPlanit()
        {
            try
            {
                var workshops = new List<Event>();
                var initialData = await GetInitialData();

                var months = initialData?.DocumentNode.SelectNodes("//h3[contains(@class, 'swingtag')]");

                if (months == null || !months.Any()) return null;

                foreach (var month in months)
                {
                    workshops.AddRange(await SearchSwingPlanitAsync(initialData, month));

                    var groupedItems = new ObservableCollection<GroupByDate>(
                        workshops.GroupBy(x => x.MonthYear)
                                 .Select(x => new GroupByDate(x.Key, x)));

                    //await MainThread.InvokeOnMainThreadAsync(() =>
                    //{
                    _workshopsListByDate2.AddRange(groupedItems);
                    //});

                    //SetBusyState(false);
                }

                return workshops;
            }
            finally
            {
                //ActivityIndicatorVisibility = false;
                //IsBusyA = false;
            }
        }

        private async Task<List<Event>?> SearchSwingPlanitAsync(HtmlDocument doc, HtmlNode mese2)
        {
            try
            {
                //throw new Exception("asdasd");

                using HttpClient client = new();
                string text = doc.DocumentNode.InnerText;
                string html2 = doc.DocumentNode.InnerHtml;

                var nomeEvento = doc.DocumentNode.SelectNodes("//div[contains(@class, 'maintitle2')]");
                var mesi = doc.DocumentNode.SelectNodes("//h3[contains(@class, 'swingtag')]");

                var teacherConfirmed = doc.DocumentNode.SelectNodes("//div[contains(@class, 'circconf')]");
                var excangheConfirmed = doc.DocumentNode.SelectNodes("//div[contains(@class, 'circexc')]");

                var details = doc.DocumentNode.SelectNodes("//a[@href]");

                //< a href =\"https://www.swingplanit.com/event/asia-balboa-exchange-df-2025\ dettagli
                //
                //countMesi = 5;//11
                _workshops.Clear();

                if (mesi != null)
                {
                    int lastMonthLine = 0;

                    if (countMesi != 0 && countMesi < mesi.Count)
                        lastMonthLine = mesi[countMesi].Line;

                    List<HtmlNode> eventiQuestoMese = new List<HtmlNode>();

                    if (countMesi >= 0 && countMesi != mesi.Count - 1)
                    {
                        countMesi++;
                        eventiQuestoMese = nomeEvento.Where(x => x.Line > lastMonthLine && x.Line <= mesi[countMesi].Line).ToList();
                    }
                    else if (countMesi == mesi.Count - 1)
                        eventiQuestoMese = nomeEvento.Where(x => x.Line >= lastMonthLine).ToList();

                    var ev = new Event();
                    if (eventiQuestoMese != null)
                    {
                        int countEvento = 0;
                        foreach (var news in eventiQuestoMese)
                        {
                            //var ev = new Event();
                            ev.EventDetail = new EventDetail();

                            ev.Month = new string(mese2.InnerText.Where(c => !char.IsDigit(c)).ToArray()).Replace(" ", "");

                            var numeroNodoEvento = news.Line;

                            if ((countMesi) < mesi.Count() - 1 && numeroNodoEvento > mesi[countMesi].Line && numeroNodoEvento != mesi[countMesi].Line)
                                continue;

                            var titolo = news.InnerText;
                            var contenuto = news.InnerText;

                            ev.WorkshopName = titolo;

                            Console.WriteLine("Workshop Name : " + ev.WorkshopName);

                            bool lastEventOfMonth = false;

                            if (news == eventiQuestoMese.Last())
                                lastEventOfMonth = true;

                            var exchangedConfirmedRow = excangheConfirmed.Where(x => (int)eventiQuestoMese[countEvento].Line < (int)x.Line).Count() != 0 ?  //eventiQuestoMese
                                excangheConfirmed.Where(x => (int)eventiQuestoMese[countEvento].Line < (int)x.Line).FirstOrDefault().Line : 0;

                            if (!lastEventOfMonth && (countEvento + 1) <= eventiQuestoMese.Count() && exchangedConfirmedRow != 0 && exchangedConfirmedRow < (int)eventiQuestoMese[countEvento + 1].Line
                                 || lastEventOfMonth && (Math.Abs(exchangedConfirmedRow - (int)eventiQuestoMese[countEvento].Line) < 7))
                                ev.ExchangeConfirmed = true;

                            var teachersConfirmedRow = teacherConfirmed.Where(x => (int)eventiQuestoMese[countEvento].Line < (int)x.Line).Count() != 0 ?
                                teacherConfirmed.Where(x => (int)eventiQuestoMese[countEvento].Line < (int)x.Line).FirstOrDefault().Line : 0;

                            if (!lastEventOfMonth && (countEvento + 1) <= eventiQuestoMese.Count() && teachersConfirmedRow != 0 && teachersConfirmedRow < (int)eventiQuestoMese[countEvento + 1].Line
                                || lastEventOfMonth && (Math.Abs(teachersConfirmedRow - (int)eventiQuestoMese[countEvento].Line) < 7))
                                ev.TeachersConfirmed = true;

                            if (details.Where(x => x.InnerText.Contains(ev.WorkshopName)).Count() != 0)
                            {
                                HtmlNode detailEvent;
#if DEBUG

                                //if (WorkShopListNameToBeChecked.Where(x => x.Equals(ev.WorkshopName)).Count() > 0)
                                //    var provaMeseAnnoDopo = details.Where(x => x.InnerText.Contains(ev.WorkshopName)).LastOrDefault();


#endif

                                //if (WorkShopListNameToBeChecked.Where(x => x.Equals(ev.WorkshopName)).Count() == 0)//CONTROLLARE QUI
                                //    detailEvent = details.FirstOrDefault(x => x.InnerText.Contains(ev.WorkshopName) && x.Line >= (int)mese2.Line);
                                //else
                                    detailEvent = details.LastOrDefault(x => x.InnerText.Contains(ev.WorkshopName) && x.Line >= (int)mese2.Line);

                                ev.EventDetail.WebSite = detailEvent != null ? detailEvent.GetAttributeValue("href", string.Empty) : null;

                                // normalizza e valida l'URL prima della chiamata HTTP
                                string href = ev.EventDetail.WebSite ?? string.Empty;
                                if (string.IsNullOrWhiteSpace(href))
                                {
                                    // Nessun link valido: aggiungi l'evento senza fare la richiesta
                                    _workshops.Add(ev);
                                    //WorkShopListNameToBeChecked.Add(ev.WorkshopName);
#if DEBUG
                                    Console.WriteLine("Link vuoto o mancante, salto richiesta HTTP.");
#endif
                                    continue;
                                }

                                if (href.StartsWith("//"))
                                    href = "https:" + href;
                                else if (href.StartsWith("/"))
                                    href = "https://www.swingplanit.com" + href;
                                else if (!href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                                         !href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                                    href = "https://www.swingplanit.com/" + href;

                                ev.EventDetail.WebSite = href;

                                // chiamata HTTP sicura con gestione eccezioni
                                HttpResponseMessage responseEvent;
                                try
                                {
                                    responseEvent = await client.GetAsync(href);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Errore richiesta dettaglio evento per '{href}': {ex.Message}");
                                    // Aggiungi l'evento anche se non è possibile recuperare i dettagli
                                    _workshops.Add(ev);
                                    //WorkShopListNameToBeChecked.Add(ev.WorkshopName);
                                    continue;
                                }

                                // prosegui come prima solo se la risposta è OK
                                if (responseEvent.IsSuccessStatusCode)
                                {

                                    string jsonResponse2 = await responseEvent.Content.ReadAsStringAsync();
                                    var doc2 = new HtmlDocument();
                                    doc2.LoadHtml(jsonResponse2);

                                    var details2 = doc2.DocumentNode.SelectNodes("//a[@href]");
                                    var lowerSection = doc2.DocumentNode.SelectNodes("//div[contains(@class, 'lowersection')]");
                                    var nomeEvento2 = doc2.DocumentNode.SelectNodes("//div[contains(@class, 'cardtitle')]");
                                    var info = doc2.DocumentNode.SelectNodes("//div[contains(@class, 'postcardleft')]");
                                    var info2 = doc2.DocumentNode.SelectNodes("//div[contains(@class, 'postcardright Yes')]");

                                    if (details2 != null)
                                    {
#if DEBUG
                                        foreach (HtmlNode link2 in details2)
                                        {
                                            string hrefValue2 = link2.GetAttributeValue("href", string.Empty);
                                            Console.WriteLine($"Link trovato: {hrefValue2}");//Questo è il vero ev.EventDetail.WebSite
                                        }
#endif

                                        foreach (HtmlNode link2 in info)
                                        {

#if DEBUG
                                            string hrefValue2 = link2.GetAttributeValue("href", string.Empty);
                                            Console.WriteLine($"Link trovato: {hrefValue2}");
#endif

                                            // Estrai When?
                                            var whenNode = link2.SelectSingleNode(".//li[contains(., 'When?')]");
                                            if (whenNode != null)
                                            {
                                                string start = "st", end = "st";

                                                ev.EventDetail.When = whenNode.InnerText.Replace("When?", "").Trim();
                                                var dates = ev.EventDetail.When.Split('-');

                                                if (dates[0].Contains("nd"))
                                                    start = "nd";
                                                else if (dates[0].Contains("rd"))
                                                    start = "rd";
                                                else if (dates[0].Contains("th"))
                                                    start = "th";

                                                if (dates[1].Contains("nd"))
                                                    end = "nd";
                                                else if (dates[1].Contains("rd"))
                                                    end = "rd";
                                                else if (dates[1].Contains("th"))
                                                    end = "th";

                                                string formatStart = $"d'{start}' MMM yyyy";
                                                string formatEnd = $"d'{end}' MMM yyyy";

                                                if (DateTime.TryParseExact(dates[0].Trim(), formatStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime safeDateStart))
                                                {
                                                    ev.DateStart = safeDateStart;
                                                }
                                                if (dates.Length > 1 && DateTime.TryParseExact(dates[1].Trim(), formatEnd, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime safeDateFinish))
                                                {
                                                    ev.DateFinish = safeDateFinish;
                                                }

                                                ev.MonthYear = ev.Month + " " + ev.DateStart.Year;
                                                ev.NameAndData = ev.DateStart.Day + "th: " + ev.WorkshopName;

                                                var dateFinish = (DateTime) ev.DateFinish;
                                                ev.EventDetail.When = ev.DateStart.ToString("yyyy-MM-dd") + " - " + dateFinish.ToString("yyyy-MM-dd");

#if DEBUG
                                                Console.WriteLine("NameAndData: " + ev.NameAndData + ", " + ev.MonthYear);
#endif
                                            }


                                            var country = link2.SelectSingleNode(".//li[contains(., 'Country:')]");

                                            if (country != null)
                                                ev.EventDetail.Country = country.InnerText.Replace("Country:", "").Trim();

                                            var city = link2.SelectSingleNode(".//li[contains(., 'Town:')]");

                                            if (city != null)
                                                ev.EventDetail.City = city.InnerText.Replace("Town:", "").Trim();


                                            var latLong = await GetCoordinatesFromCityAndNameLocation(ev.EventDetail.City, ev.EventDetail.Country);

                                            if(latLong.Longitude != 0 && latLong.Latitude != 0)
                                            {
                                                ev.EventDetail.Latitude = latLong.Latitude.ToString("F6");
                                                ev.EventDetail.Longitude = latLong.Longitude.ToString("F6");
                                            }

                                            var webSite = link2.SelectSingleNode(".//li[contains(., 'Website:')]/a");

                                            if (webSite != null)
                                                ev.EventDetail.WebSite = webSite.InnerText.Replace("Styles:", "").Trim();

                                            var styles = link2.SelectSingleNode(".//li[contains(., 'Styles:')]");

                                            if (styles != null)
                                            {
                                                ev.EventDetail.Styles = new List<StileEvento>();
                                                ev.EventDetail.StylesToString = styles.InnerText.Replace("Styles:", "").Trim();

                                                foreach (var style in ev.EventDetail.StylesToString.Split(','))
                                                {
                                                    switch (style.ToLower().Trim())
                                                    {
                                                        case "lindy":
                                                        case "lindy hop":
                                                        case "lindyhop":
                                                            ev.EventDetail.Styles ??= new List<StileEvento>();
                                                            ev.EventDetail.Styles.Add(StileEvento.Lindy);
                                                            break;
                                                        case "blues":
                                                        case "blues dance":
                                                            ev.EventDetail.Styles ??= new List<StileEvento>();
                                                            ev.EventDetail.Styles.Add(StileEvento.Blues);
                                                            break;
                                                        case "balboa":
                                                        case "bal":
                                                        case "slow bal":
                                                            ev.EventDetail.Styles ??= new List<StileEvento>();
                                                            ev.EventDetail.Styles.Add(StileEvento.Balboa);
                                                            break;
                                                        case "boogie":
                                                            ev.EventDetail.Styles ??= new List<StileEvento>();
                                                            ev.EventDetail.Styles.Add(StileEvento.Boogie);
                                                            break;
                                                        case "shag":
                                                        case "collegiate shag":
                                                            ev.EventDetail.Styles ??= new List<StileEvento>();
                                                            ev.EventDetail.Styles.Add(StileEvento.Shag);
                                                            break;
                                                        case "solo jazz":
                                                        case "solo":
                                                        case "jazz":
                                                            ev.EventDetail.Styles ??= new List<StileEvento>();
                                                            ev.EventDetail.Styles.Add(StileEvento.SoloJazz);
                                                            break;
                                                        case "aerials":
                                                            ev.EventDetail.Styles ??= new List<StileEvento>();
                                                            ev.EventDetail.Styles.Add(StileEvento.Aerials);
                                                            break;
                                                        case "afrodance":
                                                            ev.EventDetail.Styles ??= new List<StileEvento>();
                                                            ev.EventDetail.Styles.Add(StileEvento.Afrodance);
                                                            break;
                                                    }
                                                }
                                            }

                                            var descriptionNode = link2.SelectSingleNode(".//div[@class='scroll-pane2']/p");

                                            if (descriptionNode != null)
                                                ev.EventDetail.Description = descriptionNode.InnerText.Trim();
                                        }


                                        if (ev.TeachersConfirmed && info2 != null)
                                            foreach (HtmlNode link2 in info2)
                                            {

                                                // Estrai teachers?
                                                var teachers2 = link2.SelectSingleNode(".//div[@class='scroll-pane']/p");

                                                if (teachers2 != null)
                                                    ev.EventDetail.Teachers = teachers2.InnerText;
                                            }
                                    }
                                }

                                string linkText = detailEvent.InnerText.Trim();
#if DEBUG
                                Console.WriteLine($"Testo del link: {linkText}");

#endif
                                _workshops.Add(ev);
                                //WorkShopListNameToBeChecked.Add(ev.WorkshopName);


#if DEBUG
                                Console.WriteLine($"Aggiunto evento: {ev.WorkshopName}");
#endif
                            }
                            else
                            {
#if DEBUG
                                Console.WriteLine("Nessun tag 'a' con attributo 'href' trovato.");
#endif
                            }

#if DEBUG

                            Console.WriteLine($"Titolo: {titolo}");
                            Console.WriteLine($"Contenuto: {contenuto}");
                            Console.WriteLine($"Mese: {mese2.InnerText}");

                            Console.WriteLine("--------------------------------");
#endif
                            countEvento++;
                        }
                    }

                    ev.TeachersConfirmed = false;
                    ev.ExchangeConfirmed = false;

                    return _workshops;
                }

                //IsBusyA = false;

                return _workshops;
            }
            catch (HttpRequestException httpEx)
            {
                // Log specifico per errori HTTP
                //Console.WriteLine($"Errore nella richiesta HTTP: {httpEx.Message}");
                //Debug.WriteLine($"Stack trace: {httpEx.StackTrace}");

                //var logger = Application.Current.Handler?.MauiContext?.Services.GetRequiredService<IExceptionLogger>();
                //logger.LogException(httpEx);

                //// Opzionale: invio immediato
                //await logger.SendLogsByEmail("giacomo.lo87@gmail.com", httpEx.StackTrace, httpEx.Message);

                throw;
            }
            catch (HtmlWebException htmlEx)
            {
                //// Log specifico per errori di parsing HTML
                //Console.WriteLine($"Errore nel parsing HTML: {htmlEx.Message}");
                //Debug.WriteLine($"Stack trace: {htmlEx.StackTrace}");

                //var logger = Application.Current.Handler?.MauiContext?.Services.GetRequiredService<IExceptionLogger>();
                //logger.LogException(htmlEx);

                //// Opzionale: invio immediato
                //await logger.SendLogsByEmail("giacomo.lo87@gmail.com", htmlEx.StackTrace, htmlEx.Message);

                throw;
            }
            catch (Exception e)
            {
                // Log per tutte le altre eccezioni
                //Console.WriteLine($"Errore non gestito: {e.Message}");
                //Debug.WriteLine($"Stack trace: {e.StackTrace}");

                //var logger = Application.Current.Handler?.MauiContext?.Services.GetRequiredService<IExceptionLogger>();
                //logger.LogException(e);

                //// Opzionale: invio immediato
                //await logger.SendLogsByEmail("giacomo.lo87@gmail.com", e.StackTrace, e.Message);

                // A seconda delle tue esigenze, potresti voler rigettare l'eccezione
                throw;
            }
        }


        private async Task<HtmlDocument?> GetInitialData()
        {
            try
            {
                //string apiKey = "***RIMOSSA***";
                //string searchEngineId = "06933566dfe984790";
                string requestUrl = $"https://www.swingplanit.com/";
                string requestUrl2 = $"https://www.swingplanit.com/event";

                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(requestUrl);
                var doc = new HtmlDocument();
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    doc.LoadHtml(jsonResponse);
                }

                return doc;
            }
            finally
            {
                //ActivityIndicatorVisibility = false;
                //IsBusyA = false;
            }
        }
        #endregion


        public async Task<(double Latitude, double Longitude)> GetCoordinatesFromCityAndNameLocation(string city, string state, string location = "")
        {
            //string apiKey = "***RIMOSSA***"; // Sostituisci con la tua API Key

            //string url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(location + "," + city + ", " + state)}&key={apiKey}";
            ////string url = $"https://maps.googleapis.com/maps/api/geocode/json?latlng={latitude},{longitude}&key={apiKey}";

            try
            {
                string address = string.Empty;
                if (!string.IsNullOrEmpty(location))
                    address = $"{location}, {city}, {state}";
                else
                    address = $"{city}, {state}";
                
                string apiKey = "***RIMOSSA***"; // Sostituisci con la tua API Key
                var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(address)}&key={apiKey}";

                var response = new HttpResponseMessage();

                using (var httpClient = new HttpClient())
                {

                    // Produttore (Apple, Samsung, ecc.)
                    //var manufacturer = DeviceInfo.Current.Manufacturer;

#if WINDOWS && DEBUG
                    //var handler = new WinHttpHandler();
                    //handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
                    //handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                    //var test = new HttpClient(handler);
                    //response = await test.GetAsync(url);
                    var handler = new HttpClientHandler();
                    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
                    var httpClient2 = new HttpClient(handler);

                    response = await httpClient2.GetAsync(url);
#else

                    response = await httpClient.GetAsync(url);
#endif

                    Console.WriteLine($"Status: {response.StatusCode}");
                    Console.WriteLine("Headers:");
                    foreach (var header in response.Headers)
                    {
                        Console.WriteLine($"{header.Key}: {string.Join(",", header.Value)}");
                    }

                    string rawContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"\nRaw Content (first 500 chars):\n{rawContent.Substring(0, Math.Min(500, rawContent.Length))}");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<GoogleGeocodeResponse>(content);

                        if (result?.Results?.Count > 0)
                        {
                            var location2 = result.Results[0].Geometry.Location;

                            return (location2.Lat, location2.Lng);
                        }
                    }

                    return (0, 0);
                }
            }
            catch (HttpRequestException ex)
            {
                // Eccezioni specifiche di rete/HTTP (DNS, connessione, timeout, ecc.)
                Console.WriteLine($"Errore nella richiesta: {ex.Message}");
                return (0, 0);
            }
            catch (FormatException ex)
            {
                // Gestisci l'errore
                Console.WriteLine($"Errore: {ex.Message}");
                return (0, 0); // Ritorna valori di default in caso di errore
            }
            catch (Exception ex)
            {
                // Gestisci l'errore
                Console.WriteLine($"Errore: {ex.Message}");

                System.Diagnostics.Debugger.Break(); // Forza interruzione qui
                //throw;
                return (0, 0); // Ritorna valori di default in caso di errore
            }

            //return null;
        }

    }
}

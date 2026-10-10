using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SiBallaDataImporting.Utils
{
    public static class LocationHelper
    {
        private static readonly Regex Digit4Or5Regex = new(@"^\d{4,5}$", RegexOptions.Compiled);
        /// <summary>
        /// Verifica se la stringa è un numero intero di 4 o 5 cifre.
        /// </summary>
        public static bool Is4Or5DigitNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            return Digit4Or5Regex.IsMatch(value.Trim());
        }

        public static readonly Dictionary<string, string> CountryNameToIsoMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // ==================== EUROPA ====================
            { "Italia", "IT" }, { "Italy", "IT" }, { "ITA", "IT" },
            { "Francia", "FR" }, { "France", "FR" }, { "FRA", "FR" },
            { "Germania", "DE" }, { "Germany", "DE" }, { "Deutschland", "DE" }, { "DEU", "DE" },
            { "Spagna", "ES" }, { "Spain", "ES" }, { "España", "ES" }, { "ESP", "ES" },
            { "Regno Unito", "GB" }, { "United Kingdom", "GB" }, { "UK", "GB" }, { "Great Britain", "GB" }, { "Gran Bretagna", "GB" }, { "GBR", "GB" },
            { "Svizzera", "CH" }, { "Switzerland", "CH" }, { "Schweiz", "CH" }, { "Suisse", "CH" }, { "CHE", "CH" },
            { "Austria", "AT" }, { "AUT", "AT" },
            { "Paesi Bassi", "NL" }, { "Netherlands", "NL" }, { "Olanda", "NL" }, { "Holland", "NL" }, { "NLD", "NL" },
            { "Belgio", "BE" }, { "Belgium", "BE" }, { "BEL", "BE" },
            { "Portogallo", "PT" }, { "Portugal", "PT" }, { "PRT", "PT" },
            { "Grecia", "GR" }, { "Greece", "GR" }, { "GRC", "GR" },
            { "Polonia", "PL" }, { "Poland", "PL" }, { "Polska", "PL" }, { "POL", "PL" },
            { "Repubblica Ceca", "CZ" }, { "Czech Republic", "CZ" }, { "Czechia", "CZ" }, { "Cechia", "CZ" }, { "CZE", "CZ" },
            { "Romania", "RO" }, { "ROU", "RO" },
            { "Svezia", "SE" }, { "Sweden", "SE" }, { "SWE", "SE" },
            { "Norvegia", "NO" }, { "Norway", "NO" }, { "NOR", "NO" },
            { "Danimarca", "DK" }, { "Denmark", "DK" }, { "DNK", "DK" },
            { "Finlandia", "FI" }, { "Finland", "FI" }, { "FIN", "FI" },
            { "Irlanda", "IE" }, { "Ireland", "IE" }, { "Eire", "IE" }, { "IRL", "IE" },
            { "Croazia", "HR" }, { "Croatia", "HR" }, { "HRV", "HR" },
            { "Ungheria", "HU" }, { "Hungary", "HU" }, { "HUN", "HU" },
            { "Bulgaria", "BG" }, { "BGR", "BG" },
            { "Serbia", "RS" }, { "SRB", "RS" },
            { "Ucraina", "UA" }, { "Ukraine", "UA" }, { "UKR", "UA" },
            { "Russia", "RU" }, { "Russian Federation", "RU" }, { "Federazione Russa", "RU" }, { "RUS", "RU" },
            { "San Marino", "SM" }, { "SMR", "SM" },
            { "Città del Vaticano", "VA" }, { "Vatican City", "VA" }, { "Vaticano", "VA" }, { "VAT", "VA" },
            { "Malta", "MT" }, { "MLT", "MT" },
            { "Cipro", "CY" }, { "Cyprus", "CY" }, { "CYP", "CY" },

            // ==================== AMERICHE ====================
            { "Stati Uniti", "US" }, { "Stati Uniti d'America", "US" }, { "United States", "US" }, { "United States of America", "US" }, { "USA", "US" }, { "US", "US" }, { "840", "US" },
            { "Canada", "CA" }, { "CAN", "CA" },
            { "Messico", "MX" }, { "Mexico", "MX" }, { "MEX", "MX" },
            { "Brasile", "BR" }, { "Brazil", "BR" }, { "Brasil", "BR" }, { "BRA", "BR" },
            { "Argentina", "AR" }, { "ARG", "AR" },
            { "Colombia", "CO" }, { "COL", "CO" },
            { "Cile", "CL" }, { "Chile", "CL" }, { "CHL", "CL" },
            { "Perù", "PE" }, { "Peru", "PE" }, { "PER", "PE" },
            { "Venezuela", "VE" }, { "VEN", "VE" },
            { "Uruguay", "UY" }, { "URY", "UY" },
            { "Cuba", "CU" }, { "CUB", "CU" },

            // ==================== ASIA E OCEANIA ====================
            { "Cina", "CN" }, { "China", "CN" }, { "PRC", "CN" }, { "CHN", "CN" },
            { "Giappone", "JP" }, { "Japan", "JP" }, { "Nippon", "JP" }, { "JPN", "JP" },
            { "Corea del Sud", "KR" }, { "South Korea", "KR" }, { "Korea", "KR" }, { "Corea", "KR" }, { "KOR", "KR" },
            { "India", "IN" }, { "IND", "IN" },
            { "Thailandia", "TH" }, { "Thailand", "TH" }, { "Siam", "TH" }, { "THA", "TH" },
            { "Vietnam", "VN" }, { "Viet Nam", "VN" }, { "VNM", "VN" },
            { "Indonesia", "ID" }, { "IDN", "ID" },
            { "Filippine", "PH" }, { "Philippines", "PH" }, { "PHL", "PH" },
            { "Malesia", "MY" }, { "Malaysia", "MY" }, { "MYS", "MY" },
            { "Singapore", "SG" }, { "SGP", "SG" },
            { "Emirati Arabi Uniti", "AE" }, { "United Arab Emirates", "AE" }, { "UAE", "AE" }, { "Dubai", "AE" }, { "ARE", "AE" },
            { "Arabia Saudita", "SA" }, { "Saudi Arabia", "SA" }, { "SAU", "SA" },
            { "Israele", "IL" }, { "Israel", "IL" }, { "ISR", "IL" },
            { "Turchia", "TR" }, { "Turkey", "TR" }, { "Türkiye", "TR" }, { "TUR", "TR" },
            { "Australia", "AU" }, { "AUS", "AU" },
            { "Nuova Zelanda", "NZ" }, { "New Zealand", "NZ" }, { "NZL", "NZ" },

            // ==================== AFRICA ====================
            { "Egitto", "EG" }, { "Egypt", "EG" }, { "EGY", "EG" },
            { "Marocco", "MA" }, { "Morocco", "MA" }, { "MAR", "MA" },
            { "Sudafrica", "ZA" }, { "South Africa", "ZA" }, { "ZAF", "ZA" },
            { "Nigeria", "NG" }, { "NGA", "NG" },
            { "Kenia", "KE" }, { "Kenya", "KE" }, { "KEN", "KE" },
            { "Tunisia", "TN" }, { "TUN", "TN" },
            { "Algeria", "DZ" }, { "DZA", "DZ" }
        };

        // 1. Insieme dei Paesi noti (O(1) e case-insensitive)
        public static readonly HashSet<string> KnownCountries = new(StringComparer.OrdinalIgnoreCase)
        {
            // Europa
            "Italy", "Italia",
            "France", "Francia",
            "Spain", "Spagna",
            "Germany", "Germania",
            "United Kingdom", "UK", "Great Britain", "Regno Unito",
            "Switzerland", "Svizzera",
            "Austria",
            "Belgium", "Belgio",
            "Netherlands", "Paesi Bassi", "Olanda",
            "Portugal", "Portogallo",
            "Greece", "Grecia",
            "Poland", "Polonia",
            "Sweden", "Svezia",
            "Norway", "Norvegia",
            "Denmark", "Danimarca",
            "Finland", "Finlandia",
            "Ireland", "Irlanda",
            "Czech Republic", "Czechia", "Repubblica Ceca",
            "Hungary", "Ungheria",
            "Romania",
            "Bulgaria",
            "Croatia", "Croazia",
            "Serbia",
            "Slovakia", "Slovacchia",
            "Slovenia",
            "Estonia", "Latvia", "Lettonia", "Lithuania", "Lituania",
            "Luxembourg", "Lussemburgo",
            "Malta",
            "Cyprus", "Cipro",
            "Iceland", "Islanda",
            "Liechtenstein",
            "Monaco",
            "San Marino",
            "Vatican City", "Città del Vaticano",
            "Andorra", 

            // Nord America, Centro e Sud America
            "United States", "USA", "Stati Uniti",
            "Canada",
            "Mexico", "Messico",
            "Brazil", "Brasile",
            "Argentina",
            "Chile", "Cile",
            "Colombia",
            "Peru", "Perù",
            "Uruguay",
            "Venezuela",
            "Cuba",
            "Costa Rica",
            "Panama",
            "Dominican Republic", "Repubblica Dominicana",

            // Asia e Oceania
            "China", "Cina",
            "Taiwan", "Republic of China",
            "Japan", "Giappone",
            "South Korea", "Corea del Sud", "India",
            "Thailand", "Tailandia",
            "Vietnam",
            "Singapore",
            "Malaysia", "Malesia",
            "Indonesia",
            "Philippines", "Filippine",
            "Australia",
            "New Zealand", "Nuova Zelanda", 

            // Medio Oriente e Africa
            "United Arab Emirates", "UAE", "Emirati Arabi Uniti",
            "Saudi Arabia", "Arabia Saudita",
            "Israel", "Israele",
            "Turkey", "Turchia",
            "South Africa", "Sudafrica",
            "Egypt", "Egitto",
            "Morocco", "Marocco"
        };

        // 2. Insieme delle Regioni/Stati noti (O(1) e case-insensitive)
        public static readonly HashSet<string> KnownRegions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Italia (Regioni)
            "Abruzzo", "Basilicata", "Calabria", "Campania", "Emilia-Romagna", "Friuli Venezia Giulia",
            "Lazio", "Liguria", "Lombardia", "Marche", "Molise", "Piemonte", "Puglia", "Sardegna",
            "Sicilia", "Toscana", "Trentino-Alto Adige", "Umbria", "Valle d'Aosta", "Veneto",

            // Francia (Regioni Metropolitane)
            "Auvergne-Rhône-Alpes", "Bourgogne-Franche-Comté", "Bretagne", "Centre-Val de Loire",
            "Corse", "Grand Est", "Hauts-de-France", "Île-de-France", "Normandie", "Nouvelle-Aquitaine",
            "Occitanie", "Pays de la Loire", "Provence-Alpes-Côte d'Azur", "PACA", "Isère",

            // Spagna (Comunità Autonome)
            "Andalucía", "Aragón", "Asturias", "Baleares", "Canarias", "Cantabria", "Castilla-La Mancha",
            "Castilla y León", "Cataluña", "Catalunya", "Extremadura", "Galicia", "La Rioja",
            "Comunidad de Madrid", "Murcia", "Navarra", "País Vasco", "Comunitat Valenciana",

            // Germania (Bundesländer)
            "Baden-Württemberg", "Bayern", "Berlin", "Brandenburg", "Bremen", "Hamburg", "Hessen",
            "Mecklenburg-Vorpommern", "Niedersachsen", "Nordrhein-Westfalen", "Rheinland-Pfalz",
            "Saarland", "Sachsen", "Sachsen-Anhalt", "Schleswig-Holstein", "Thüringen",

            // Stati Uniti (Stati principali)
            "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut",
            "Delaware", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa",
            "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan",
            "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire",
            "New Jersey", "New Mexico", "New York", "North Carolina", "North Dakota", "Ohio",
            "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island", "South Carolina", "South Dakota",
            "Tennessee", "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia",
            "Wisconsin", "Wyoming",

            // Regno Unito (Nazioni e regioni)
            "England", "Scotland", "Wales", "Northern Ireland", "Greater London"
        };

        /// <summary>
        /// Verifica se una stringa corrisponde a un Paese noto.
        /// </summary>
        public static bool IsCountry(string value) =>
            !string.IsNullOrWhiteSpace(value) && KnownCountries.Contains(value.Trim());

        /// <summary>
        /// Verifica se una stringa corrisponde a una Regione/Stato noto.
        /// </summary>
        public static bool IsRegion(string value) =>
            !string.IsNullOrWhiteSpace(value) && KnownRegions.Contains(value.Trim());

        /// <summary>
        /// Corregge ed eventualmente inverte City, State e Country se l'HTML presenta dati scambiati.
        /// </summary>
        public static (string City, string State, string Country) NormalizeLocation(string rawCity, string rawState, string rawCountry)
        {
            string city = rawCity?.Trim();
            string state = rawState?.Trim();
            string country = rawCountry?.Trim();

            //ToIso2Code
            var valueee = CountryNameToIsoMap.ContainsKey(country) ? CountryNameToIsoMap[country] : state;
            if (Is4Or5DigitNumber(city))
                city = IstatDownloader.ZipToCityMap[valueee + ":"+ city];
            if (Is4Or5DigitNumber(state))
                state = IstatDownloader.ZipToCityMap[valueee + ":" + state];
            if (Is4Or5DigitNumber(country))
                country = IstatDownloader.ZipToCityMap[valueee + ":" + country];

            // 1. Se 'City' è in realtà un Paese noto (es. City="Italia", Country="Genova")
            if (IsCountry(city))
            {
                var temp = city;
                city = country;
                country = temp;
            }

            // 2. Se 'State' è in realtà un Paese noto (es. State="Italia", Country="Lombardia")
            if (IsCountry(state))
            {
                var temp = state;
                state = country;
                country = temp;
            }

            // 3. Se 'Country' è in realtà una Regione nota (es. State="Genova", Country="Liguria")
            if (IsRegion(country))
            {
                var temp = country;
                country = state;
                state = temp;
            }

            // 4. [NUOVO] Se 'City' è una Regione nota ma 'State' NO (es. City="Liguria", State="16044" o "Genova")
            if (IsRegion(city) && !IsRegion(state))
            {
                var temp = city;
                city = state; // "16044" diventa City
                state = temp; // "Liguria" diventa State
            }

            // 5. Fallback: Se la città è vuota ma lo State è presente
            if (string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(state))
            {
                city = state;
            }

            return (city, state, country);
        }
    }
}

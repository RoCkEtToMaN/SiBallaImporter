using System;
using System.Collections.Generic;

namespace SiBallaImportingData.Utils
{
    public static class CountryCodeHelper
    {
        public static readonly Dictionary<string, string> CountryNameToIsoMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // ==================== EUROPA ====================
            { "Italia", "IT" }, { "Italy", "IT" }, { "ITA", "IT" },
            { "Francia", "FR" }, { "France", "FR" }, { "FRA", "FR" },
            { "Germania", "DE" }, { "Germany", "DE" }, { "Deutschland", "DE" }, { "DEU", "DE" },
            { "Spagna", "ES" }, { "Spain", "ES" }, { "España", "ES" }, { "ESP", "ES" },
            { "Regno Unito", "GB" }, { "United Kingdom", "GB" }, { "UK", "GB" }, { "Great Britain", "GB" }, { "Gran Bretagna", "GB" }, { "GBR", "GB" }, { "England", "GB" }, { "Scotland", "GB" }, { "Wales", "GB" },
            { "Portogallo", "PT" }, { "Portugal", "PT" }, { "PRT", "PT" },
            { "Svizzera", "CH" }, { "Switzerland", "CH" }, { "Schweiz", "CH" }, { "Suisse", "CH" }, { "CHE", "CH" },
            { "Austria", "AT" }, { "AUT", "AT" },
            { "Paesi Bassi", "NL" }, { "Netherlands", "NL" }, { "Olanda", "NL" }, { "NLD", "NL" },
            { "Belgio", "BE" }, { "Belgium", "BE" }, { "BEL", "BE" },
            { "Polonia", "PL" }, { "Poland", "PL" }, { "Polska", "PL" }, { "POL", "PL" },
            { "Grecia", "GR" }, { "Greece", "GR" }, { "GRC", "GR" },
            { "Repubblica Ceca", "CZ" }, { "Czech Republic", "CZ" }, { "Czechia", "CZ" }, { "CZE", "CZ" },
            { "Romania", "RO" }, { "ROU", "RO" },
            { "Svezia", "SE" }, { "Sweden", "SE" }, { "SWE", "SE" },
            { "Norvegia", "NO" }, { "Norway", "NO" }, { "NOR", "NO" },
            { "Danimarca", "DK" }, { "Denmark", "DK" }, { "DNK", "DK" },
            { "Finlandia", "FI" }, { "Finland", "FI" }, { "FIN", "FI" },
            { "Irlanda", "IE" }, { "Ireland", "IE" }, { "IRL", "IE" },
            { "Croazia", "HR" }, { "Croatia", "HR" }, { "HRV", "HR" },
            { "Ungheria", "HU" }, { "Hungary", "HU" }, { "HUN", "HU" },
            { "Bulgaria", "BG" }, { "BGR", "BG" },
            { "Serbia", "RS" }, { "SRB", "RS" },
            { "Ucraina", "UA" }, { "Ukraine", "UA" }, { "UKR", "UA" },
            { "Russia", "RU" }, { "Russian Federation", "RU" }, { "RUS", "RU" },

            // ==================== AMERICHE ====================
            { "Stati Uniti", "US" }, { "Stati Uniti d'America", "US" }, { "United States", "US" }, { "United States of America", "US" }, { "USA", "US" }, { "US", "US" },
            { "Canada", "CA" }, { "CAN", "CA" },
            { "Messico", "MX" }, { "Mexico", "MX" }, { "MEX", "MX" },
            { "Brasile", "BR" }, { "Brazil", "BR" }, { "BRA", "BR" },
            { "Argentina", "AR" }, { "ARG", "AR" },
            { "Colombia", "CO" }, { "COL", "CO" },
            { "Cile", "CL" }, { "Chile", "CL" }, { "CHL", "CL" },

            // ==================== ASIA E OCEANIA ====================
            { "Giappone", "JP" }, { "Japan", "JP" }, { "JPN", "JP" },
            { "Cina", "CN" }, { "China", "CN" }, { "CHN", "CN" },
            { "Australia", "AU" }, { "AUS", "AU" },
            { "Nuova Zelanda", "NZ" }, { "New Zealand", "NZ" }, { "NZL", "NZ" },
            { "India", "IN" }, { "IND", "IN" },
            { "Thailandia", "TH" }, { "Thailand", "TH" }, { "THA", "TH" }
        };

        /// <summary>
        /// Converte il nome di una nazione (es. "Spain", "Spagna", "ESP") nel relativo codice ISO 2 (es. "ES").
        /// </summary>
        public static string ToIso2Code(string countryName)
        {
            if (string.IsNullOrWhiteSpace(countryName)) return null;

            string clean = countryName.Trim();

            // Se è già un codice a 2 lettere (es. "ES", "IT"), lo restituisce in maiuscolo
            if (clean.Length == 2) return clean.ToUpper();

            return CountryNameToIsoMap.TryGetValue(clean, out string isoCode)
                ? isoCode
                : null;
        }
    }
}
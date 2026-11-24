namespace SiBallaImportingData.Utils
{
    /// <summary>
    /// Regole del curatore in data/regole-curatore.md (nel repo, modificabile da GitHub senza toccare il codice): le righe che
    /// iniziano con "- " sotto un titolo "## …". Le aggiungono la classificazione di Gemini e l'agente alle loro istruzioni,
    /// e valgono più delle regole generali. Senza file (es. programma fuori dal repo) non aggiungono niente
    /// </summary>
    public static class RegoleCuratore
    {
        public const string RepoRelativePath = "data/regole-curatore.md";
        private const int MaxCaratteri = 6000;
        private static string? _testo;

        /// <summary>Le regole, pronte da aggiungere alle istruzioni di Gemini ("" se non ce ne sono)</summary>
        public static string PerPrompt()
        {
            if (_testo is not null) return _testo;
            _testo = "";
            var path = DictionaryRepoSync.RepoPath(RepoRelativePath);
            if (path is null || !File.Exists(path))
            {
                Console.WriteLine($"[regole] {RepoRelativePath} non trovato: nessuna regola del curatore");
                return _testo;
            }
            var regole = File.ReadAllLines(path)
                .Select(r => r.Trim())
                .Where(r => r.StartsWith("- ") && r.Length > 3)
                .Select(r => r[2..].Trim())
                // la guida in cima al file ("una riga che inizia con …") non è una regola: le regole stanno sotto i titoli "## …"
                .ToList();
            var sezioni = File.ReadAllLines(path).Select(r => r.Trim()).ToList();
            int primoTitolo = sezioni.FindIndex(r => r.StartsWith("## "));
            if (primoTitolo >= 0)
                regole = sezioni.Skip(primoTitolo).Where(r => r.StartsWith("- ") && r.Length > 3).Select(r => r[2..].Trim()).ToList();
            if (regole.Count == 0) return _testo;

            var testo = "\n\nREGOLE DEL CURATORE (valgono più di tutte le regole sopra; applicale quando l'evento rientra nel caso descritto):\n"
                        + string.Join("\n", regole.Select(r => "- " + r));
            if (testo.Length > MaxCaratteri) testo = testo[..MaxCaratteri];
            Console.WriteLine($"[regole] {regole.Count} regole del curatore lette da {RepoRelativePath}");
            return _testo = testo;
        }
    }
}

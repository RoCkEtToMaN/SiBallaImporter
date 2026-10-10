namespace SiBallaImportingData.Utils
{
    // Cartella dei file di dati (dizionario luoghi, cache città, GeoNames...), fuori da bin e dal repo:
    // una pulizia della soluzione o un cambio di branch non li tocca.
    // Default: %LOCALAPPDATA%\SiBallaImporter (Linux: ~/.local/share/SiBallaImporter); si cambia con Paths:DataDirectory.
    public static class DataPaths
    {
        public static string Directory { get; private set; } = DefaultDirectory;

        private static string DefaultDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiBallaImporter");

        public static void Initialize(string? configuredDirectory)
        {
            Directory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? DefaultDirectory
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredDirectory));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public static string Get(string fileName) => Path.Combine(Directory, fileName);
    }
}

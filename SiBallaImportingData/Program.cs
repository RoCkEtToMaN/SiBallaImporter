using System.Globalization;
using SiBallaImportingData.Application;
using SiBallaImportingData.Utils;

// Date e numeri dello Sheet usano il formato italiano anche sui runner Linux.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("it-IT");
CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("it-IT");

// Configurazione: appsettings.json -> user-secrets -> ambiente -> riga di comando.
var settings = AppSettings.Load(args);
DataPaths.Initialize(settings.Paths.DataDirectory);
settings.Print();

await ImportRunner.RunAsync(settings);

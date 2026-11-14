using System.Globalization;
using System.Text;
using SiBallaImportingData.Application;
using SiBallaImportingData.Utils;

// Accenti ed emoji leggibili anche nella console di Windows (che di suo usa la code page 850/1252).
// Su Linux/GitHub è già UTF-8: non cambia niente.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// Date e numeri dello Sheet usano il formato italiano anche sui runner Linux.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("it-IT");
CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("it-IT");

// Configurazione: appsettings.json -> user-secrets -> ambiente -> riga di comando.
var settings = AppSettings.Load(args);
DataPaths.Initialize(settings.Paths.DataDirectory);
settings.Print();

await ImportRunner.RunAsync(settings);

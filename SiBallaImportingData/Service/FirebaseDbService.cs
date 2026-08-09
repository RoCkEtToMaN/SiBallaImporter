
using Firebase.Database;
using Google.Cloud.Firestore;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Newtonsoft.Json.Linq;
using SiBallaImportingData.Model;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json;

namespace SiBallaImportingData.Service
{
    public class FirebaseDbService : Event
    {
        private readonly FirestoreDb _firestoreDb;

        private FirebaseClient _firebaseClient;
        //[cite_start]// Stai usando correttamente l'URL del tuo database [cite: 36]
        private readonly string _databaseUrl;
        public FirebaseDbService(string databaseUrl)
        {
            _databaseUrl = databaseUrl;
            _firebaseClient = new FirebaseClient(_databaseUrl);
            InitializeDb();
        }
        // --- 1. METODO PER INIZIALIZZARE IL DB ---
        public void InitializeDb()
        {
            //[cite_start]// Il FirebaseClient del Realtime Database ha bisogno principalmente dell'URL [cite: 29]
            _firebaseClient = new FirebaseClient(_databaseUrl);
            Console.WriteLine("Connessione al Realtime Database inizializzata.");
        }
        public async Task AddWorkshopAsync(string workshopData)
        {
            if (_firebaseClient == null)
                throw new InvalidOperationException("Devi prima chiamare InitializeDb()!");
            //var collection = _firestoreDb.Collection("workshops");
            //await collection.AddAsync(workshopData);
           // await _firebaseClient
           //.Child("Workshops")
           //.DeleteAsync();

            await _firebaseClient
           .Child("Workshops")
           .PostAsync(workshopData);
        }
        public async Task AddEventAsync(string eventData)
        {
           // await _firebaseClient
           //.Child("Eventi")
           //.DeleteAsync();

            await _firebaseClient
           .Child("Eventi")
           .PostAsync(eventData);
        }

        public async Task ResetDb()
        {
            if (_firebaseClient == null)
                throw new InvalidOperationException("Devi prima chiamare InitializeDb()!");

            await _firebaseClient
           .Child("Eventi")
           .DeleteAsync();
            await _firebaseClient
           .Child("Workshops")
           .DeleteAsync();
        }
        public async Task<ObservableCollection<Event>> LoadWorkshopAsync()
        {
            // Deserializza ciascuna stringa in un oggetto Event e aggiungili a una ObservableCollection<Event>
            var events = new ObservableCollection<Event>();

            try
            { // Ottieni la lista degli eventi come oggetti FirebaseObject<string>
                var firebaseObjects = await _firebaseClient
                    .Child("Workshops")
                    .OnceAsync<object>();

                // Estrai i valori stringa dagli oggetti FirebaseObject<string>
                var eventStrings = firebaseObjects.Select(obj => obj.Object);

                foreach (var eventString in eventStrings)
                {
                    if (!string.IsNullOrEmpty(eventString.ToString()))
                    {
                        var eventiColl = System.Text.Json.JsonSerializer.Deserialize<ObservableCollection<Event>>(eventString.ToString());
                        if (eventiColl != null)
                            foreach (var ev in eventiColl)
                                events.Add(ev);
                    }
                }

                return events;
            }
            catch (JsonException e)
            {
                return events;
            }
            catch (Exception e)
            {
                return events;
            }
        }
        public async Task<ObservableCollection<Event>> LoadEventAsync() 
        {

            // Deserializza ciascuna stringa in un oggetto Event e aggiungili a una ObservableCollection<Event>
            var events = new ObservableCollection<Event>();
            try
            {
                // Ottieni la lista degli eventi come oggetti FirebaseObject<string>
                var firebaseObjects = await _firebaseClient
                    .Child("Eventi")
                    .OnceAsync<object>();

                // Estrai i valori stringa dagli oggetti FirebaseObject<string>
                var eventStrings = firebaseObjects.Select(obj => obj.Object);


                foreach (var eventString in eventStrings)
                {
                    if (!string.IsNullOrEmpty(eventString.ToString()))
                    {
                        var eventiColl = System.Text.Json.JsonSerializer.Deserialize<ObservableCollection<Event>>(eventString.ToString());
                        if (eventiColl != null)
                            foreach (var ev in eventiColl)
                                events.Add(ev);
                    }
                }

                return events;
            }
            catch (JsonException e)
            {
                return events;
            }
            catch (Exception e)
            {
                return events;
            }
        }
        public async void GetInfo()
        {
            // Ottieni l'assembly corrente
            var assembly = Assembly.GetExecutingAssembly();
            // Definisci il percorso della risorsa embedded
            var resourcePath = "SiBallaImportingData.Resources.google-services.json"; // Sostituisci con il tuo percorso

            using (var stream = assembly.GetManifestResourceStream(resourcePath))
            {
                if (stream != null)
                {
                    // Usa il stream per leggere il file (ad esempio, con StreamReader)
                    using (var reader = new StreamReader(stream))
                    {
                        jsonContent = await reader.ReadToEndAsync();
                        // Ora puoi usare jsonContent per inizializzare Firebase
                    }

                    // Parse the JSON to get the project ID
                    var jsonObject = JObject.Parse(jsonContent);
                    var projectId = jsonObject["project_info"]?["project_id"]?.ToString();

                    if (string.IsNullOrEmpty(projectId))
                    {
                        throw new Exception("Could not find project ID in google-services.json");
                    }
                }
                else
                {
                    throw new FileNotFoundException($"Risorsa embedded non trovata: {resourcePath}");
                }
            }
        }
        private string jsonContent;        
    }
}

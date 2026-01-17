
using Firebase.Database;
using Google.Cloud.Firestore;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Newtonsoft.Json.Linq;
using SiBallaImportingData.Model;
using System.Collections.ObjectModel;
using System.Reflection;

namespace SiBallaImportingData.Service
{
    public class FirestoreService: Event
    {
        private readonly FirestoreDb _firestoreDb;       

        private FirebaseClient firebaseClient;
        public FirestoreService()
        {
            firebaseClient = new FirebaseClient("https://siballa-8fc2f-default-rtdb.europe-west1.firebasedatabase.app/");            
        }
        public async Task AddWorkshopAsync(string workshopData)
        {
            //var collection = _firestoreDb.Collection("workshops");
            //await collection.AddAsync(workshopData);
            await firebaseClient
           .Child("Workshops")
           .DeleteAsync();

            await firebaseClient
           .Child("Workshops")
           .PostAsync(workshopData);
        }
        public async Task AddEventAsync(string eventData)
        {
            await firebaseClient
           .Child("Eventi")
           .DeleteAsync();

            await firebaseClient
           .Child("Eventi")
           .PostAsync(eventData);
        }
        public async Task<List<Event>> LoadEventAsync()
        {
            // Ottieni la lista degli eventi come oggetti FirebaseObject<string>
            var firebaseObjects = await firebaseClient
                .Child("Eventi")                
                .OnceAsync<object>();

            // Estrai i valori stringa dagli oggetti FirebaseObject<string>
            var eventStrings = firebaseObjects.Select(obj => obj.Object);

            // Deserializza ciascuna stringa in un oggetto Event e aggiungili a una ObservableCollection<Event>
            var events = new List<Event>();
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
        public async Task<List<Event>> LoadWorkshopAsync()
        {
            // Ottieni la lista degli eventi come oggetti FirebaseObject<string>
            var firebaseObjects = await firebaseClient
                .Child("Workshops")                
                .OnceAsync<object>();

            // Estrai i valori stringa dagli oggetti FirebaseObject<string>
            var eventStrings = firebaseObjects.Select(obj => obj.Object);

            // Deserializza ciascuna stringa in un oggetto Event e aggiungili a una ObservableCollection<Event>
            var events = new List<Event>();
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
        public async void GetInfo()
        {
            // Ottieni l'assembly corrente
            var assembly = Assembly.GetExecutingAssembly();
            // Definisci il percorso della risorsa embedded
            var resourcePath = "SiBallaApp.Platforms.Android.Resources.google-services.json"; // Sostituisci con il tuo percorso

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
        public async void InitFirestoreDb()
        {
            var path = "SiBalla.Platforms.Android.Resources.siballa-7d5d59c4295f.json";
            // Ottieni l'assembly corrente
            var assembly = Assembly.GetExecutingAssembly();

            using (var stream = assembly.GetManifestResourceStream(path))
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
                    //var jsonObject = JObject.Parse(jsonContent);
                    //var projectId = jsonObject["project_info"]?["project_id"]?.ToString();

                    //if (string.IsNullOrEmpty(projectId))
                    //{
                    //    throw new Exception("Could not find project ID in google-services.json");
                    //}
                }
                else
                {
                    throw new FileNotFoundException($"Risorsa embedded non trovata: {path}");
                }
            }



            var test = GoogleCredential.FromJson(jsonContent);

            if(FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(new AppOptions()
                {
                    Credential = GoogleCredential.FromJson(jsonContent),
                });
            }

            // Carica manualmente le credenziali dal file JSON
            //GoogleCredential credential = GoogleCredential.FromFile("path/to/service-account-key.json");
            //FirestoreDb db = FirebaseApp.Create("your-project-id");

            // Crea un'istanza di Firestore
            FirestoreDb db = FirestoreDb.Create("siBalla");
            //FirestoreDb db = FirestoreDb.Create("your-project-id");

            Dictionary<string, object> data = new Dictionary<string, object>
            {
                { "Nome", "Mario" },
                { "Cognome", "Rossi" },
                { "Età", 30 },
                { "Attivo", true }
            };

            // Aggiunge un documento con ID auto-generato alla collezione "Utenti"
            DocumentReference addedDocRef = await db.Collection("Utenti").AddAsync(data);
            Console.WriteLine($"Documento aggiunto con ID: {addedDocRef.Id}");
#if ANDROID
            var context = Android.App.Application.Context;
            Firebase.FirebaseApp.InitializeApp(context);
#endif
           

        }        
    }
}

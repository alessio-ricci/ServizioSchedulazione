using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;
using System.Xml;


namespace ServizioSchedulazione
{
    public class MonitoraggioServer
    {
        private readonly List<string> _filtriTask;
        private readonly string _connectionString;
        private readonly SettaggioServer _server;
        private readonly string _nomeLog;
        private readonly string _filtroPath;
        private readonly ILogger _logger;

        private long _ultimoRecordId = 0;
        private EventLogSession? _sessione;
        private readonly Dictionary<string, AttivitaInMemoria> _attivitaInMemoria = new();
        private readonly Dictionary<string, DateTime> _taskIgnorati = new();
        private static readonly TimeSpan _durataIgnorati = TimeSpan.FromHours(24);

        public MonitoraggioServer(SettaggioServer server, string nomeLog, ILogger logger, string connectionString)
        {
            _server = server;
            _nomeLog = nomeLog;
            _logger = logger;
            _connectionString = connectionString;

            _filtriTask = RecuperaFiltriAction();
        }

        public async Task EseguiAsync(CancellationToken stoppingToken)
        {
            DateTime dataInizio = DateTime.Now;
            EventRecord evento = null;
            List<Attivita> listaAttivita = new List<Attivita>();

            _logger.LogDebug($"\n\nInizio lettura eventi da log: {_nomeLog} con filtroPath: {_filtroPath} e NomeServer: {_server.Nome}. Data: {DateTime.Now}\n\n");

            _logger.LogDebug(_attivitaInMemoria.Count > 0 ? $"Attività in memoria: {_attivitaInMemoria.Count > 0}" : "Nessuna attività in memoria.");

            while (!stoppingToken.IsCancellationRequested)
            {
                 
                listaAttivita.Clear();

                //rimuovo dai task ignorati quelli scaduti
                if (_taskIgnorati.Count > 0)
                {
                    DateTime limite = DateTime.Now - _durataIgnorati;
                    var scaduti = _taskIgnorati.Where(t => t.Value < limite).Select(t => t.Key).ToList();

                    foreach (var id in scaduti)
                    {
                        _taskIgnorati.Remove(id);
                    }

                    if (scaduti.Count > 0)
                    {
                        _logger.LogInformation($"Rimossi {scaduti.Count} task dall'elenco degli ignorati (scaduti).");
                    }
                }


                long maxRecordIdLetto = _ultimoRecordId;
                bool letturaOk = false;
                string? nomeTask = null;
                string? idTask = null;
                string? idEvento = null;
                string? pathAction = null;
                string? codiceRitorno = null;

                try
                {
                    //Filtro per ultimo recordId rimasto in memoria e per la data di oggi
                    //Non mi interessa sapere quello che è successo ieri, mi serve sapere cosa accade da dopo adesso.
                    //Una volta che abbiamo ultimo recordId ce ne freghiamo della data, andiamo per RecordID
                    string xPath = Utilita.CostruisciXPath(_ultimoRecordId, dataInizio);

                    //questo crea l'oggetto che serve dopo a EventLogQuery
                    _sessione ??= CreaSessione();
                    EventLogQuery query = new EventLogQuery(_nomeLog, PathType.LogName, xPath) { Session = _sessione };

                    #region Lettura eventi
                    using (EventLogReader reader = new EventLogReader(query))
                    {
                        while ((evento = reader.ReadEvent()) != null)
                        {
                            //Perché using(evento)? Perché non accumulo memoria inutilmente. Lo libera automaticamente
                            using (evento)
                            {
                                //Convertiamo quello che abbiamo appena letto nel reader in xml
                                string xml = evento.ToXml();

                                //creo subito il DOC senza farlo nella funziona "RecuperaValoreDaXMl" perché così lo creo una volta sola.
                                XmlDocument doc = new XmlDocument();
                                doc.LoadXml(xml);

                                //mettiamo in un try catch e recupera valore deve sollevare un eccezione se trova ActionName con più di un nodo.
                                try
                                {
                                    nomeTask = Utilita.RecuperaValoreDaXml(doc, "TaskName").Trim();

                                    //Ne devo mettere di più perché a seconda del tipo di evento cambia il nome dell'elemento che contiene l'id del task...
                                    idTask = Utilita.RecuperaValoreDaXml(doc, "InstanceId") ?? Utilita.RecuperaValoreDaXml(doc, "TaskInstanceId") ?? Utilita.RecuperaValoreDaXml(doc, "QueuedTaskInstanceId");

                                    codiceRitorno = Utilita.RecuperaValoreDaXml(doc, "ResultCode");
                                    idEvento = Utilita.RecuperaValoreDaXml(doc, "EventID");
                                    pathAction = Utilita.RecuperaValoreDaXml(doc, "ActionName");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Errore lettura nodi: {ex.Message}");
                                    continue;
                                }
                                
                                long recordId = evento.RecordId.HasValue ? (long)evento.RecordId.Value : 0;

                                //segnati l'ultimo record che hai letto.
                                if(recordId > maxRecordIdLetto)
                                {
                                    maxRecordIdLetto = recordId;
                                }

                                //solo se nomeTask è diverso da null ed è contenuto nei filtri iniziali
                                if (!string.IsNullOrEmpty(nomeTask) && _filtriTask.Any(filtro => nomeTask.Split('\\').Last().Equals(filtro.Trim(), StringComparison.OrdinalIgnoreCase)) && idTask != null)
                                {
                                    Attivita attivita = new Attivita
                                    {
                                        RecordId = recordId,
                                        IdTask = idTask,
                                        EventId = idEvento,
                                        DataCreazione = evento.TimeCreated,
                                        NomeTask = nomeTask,
                                        CodiceRitorno = codiceRitorno,
                                        Action = pathAction
                                    };

                                    listaAttivita.Add(attivita);
                                }
                                else
                                {
                                    continue; 
                                }
                            }
                        }
                    }
                    letturaOk = true;
                }
                catch (Exception ex)
                {
                    _sessione?.Dispose();
                    _sessione = null;
                    _logger.LogError(ex.ToString(), "Errore durante la lettura degli eventi.");
                }
                #endregion

                #region Algoritmo di popolamento della memoria
                try
                {
                    //Leggo dagli eventi secondo logica incrementale basandomi sulla DataOdierna e sull'ultimo record id letto.
                    //Il recordId di cui parlo è direttamente staccato da Windows Scheduler.


                    //Leggere in maniera incrementale fa si che:
                    // 1) Non leggo lo storico, quindi non sovraccarico il processo e di conseguenza il server (non ho idea di quanta roba giri li dentro).
                    // 2) Non leggo eventi che ho già letto e registrato, quindi mi copro da eventuali doppioni.


                    //La logica si basa su due strutture dati:
                    //La prima funziona come memoria temporanea e si occupa di registrare tutti gli eventi letti. Ad ogni inizio ciclo del while, viene pulita.
                    //La seconda funziona da memoria finchè il processo rimane attivo:
                    //  -E' un dizionario che parla con idTask.
                    //  -Contiene una data di inizio e una data fine.
                    //  -(IMPORTANTE) Contiene un codice di ritorno.
                    //  -(IMPORTANTE) Contiene un flag che mi dice se l'oggetto identificato dall'idTask è stato o meno usato per fare l'update su DB.
                    //      Il flag ad ogni registrazione viene settato a false.
                    //      Quando il processo di update su DB va a buon fine, il flag viene settato a true.
                    //      Il flag viene risettato a false ogni volta che il processo legge un nuovo evento e aggiorna l'oggetto in memoria.


                    //Da qui rimuoviamo quelli che hanno più action e lanciamo eccezione:
                    //Purtroppo quelli che hanno più action sono un problema perché non hanno più nodi nell'XML e quindi non possiamo capire quante ce ne siano
                    //La cosa che accade a livello di windows scheduler è che vengono creati più eventi con 200.
                    if(listaAttivita.Count > 0)
                    {
                        try
                        {
                            //1) scarto subito gli eventi dei task già marcati come "da ignorare" nei cicli precedenti
                            listaAttivita.RemoveAll(a => _taskIgnorati.ContainsKey(a.IdTask));

                            //2) cerco nuovi task con più action:
                            //   - più di un 200 nello stesso ciclo, oppure
                            //   - un 200 per un task già in memoria (il primo 200 è stato letto in un ciclo precedente)
                            var nuoviMultiAction = listaAttivita
                                .Where(a => a.EventId == "200")
                                .GroupBy(a => a.IdTask)
                                .Where(g => g.Count() > 1 || _attivitaInMemoria.ContainsKey(g.Key))
                                .Select(g => new { IdTask = g.Key, NomeTask = g.First().NomeTask })
                                .ToList();

                            if (nuoviMultiAction.Count > 0)
                            {
                                //3) pulizia
                                foreach (var task in nuoviMultiAction)
                                {
                                    _taskIgnorati[task.IdTask] = DateTime.Now;

                                    //se il primo 200 era già in memoria lo tolgo, così non viene più aggiornato su DB
                                    //_attivitaInMemoria.Remove(task.IdTask);

                                    //Non voglio rimuoverlo più lo voglio aggiornare su DB mettendo tutto null
                                    _attivitaInMemoria[task.IdTask].DataInizio = null;
                                    _attivitaInMemoria[task.IdTask].DataFine = null;
                                    _attivitaInMemoria[task.IdTask].CodiceRitorno = null;
                                    _attivitaInMemoria[task.IdTask].AggiornataSuDB = false;
                                }

                                //tolgo dalla lista tutti gli eventi (200, 201, 102, ...) di questi task
                                listaAttivita.RemoveAll(a => _taskIgnorati.ContainsKey(a.IdTask));

                                string dettaglio = string.Join(" | ", nuoviMultiAction.Select(t => $"NomeTask: {t.NomeTask}, IdTask: {t.IdTask}"));
                                throw new Exception($"Trovati task con più action (EventId=200 multiplo per lo stesso IdTask). Task ignorati: {dettaglio}");
                            }

                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Attenzione: {ex.ToString()}");
                        }
                    }

                    if (listaAttivita.Count > 0)
                    {
                        //Una volta che ho letto tutto e ho tutte le attività segnate, raggruppo per idTask
                        var attivitaRaggruppate = listaAttivita.OrderBy(a => a.RecordId).GroupBy(a => a.IdTask);

                        foreach (var gruppo in attivitaRaggruppate)
                        {
                            string idTaskTrovato = gruppo.Key;

                            if (_attivitaInMemoria.ContainsKey(idTaskTrovato))
                            {
                                //Se esiste, controlla se terminato o meno
                                if (!_attivitaInMemoria[idTaskTrovato].Terminata)
                                {
                                    //Se non è terminato, controlliamo se nel gruppo esiste un idEvento coerente che mi dica che il task è terminato.
                                    //Gli idEvento buoni che devo controllare su Windows Scheduler sono:
                        
                                    //102 TaskCompletato (non ha codice di ritorno)
                                    //201 ActionTerminata (ha il codice di ritorno)
                                    var taskCompletato = gruppo.FirstOrDefault(a => a.EventId == "102");
                                    var actionTerminata = gruppo.FirstOrDefault(a => a.EventId == "201");
                                    var taskTerminatoForzatamente = gruppo.FirstOrDefault(a => a.EventId == "111");
                                    var timeout = gruppo.FirstOrDefault(a => a.EventId == "329");


                                    //Ho scoperto che il task può anche terminare forzatamente. EventId = 111
                                    //In questo caso, task termianto forzatamente e quindi dobbiamo segnare attività terminata errore e fine
                                    if (taskTerminatoForzatamente != null)
                                    {
                                        _attivitaInMemoria[idTaskTrovato].DataFine = taskTerminatoForzatamente.DataCreazione;
                                        _attivitaInMemoria[idTaskTrovato].CodiceRitorno = "0"; //0 = errore
                                        _attivitaInMemoria[idTaskTrovato].AggiornataSuDB = false;

                                        continue;
                                    }

                                    if (timeout != null)
                                    {
                                        _attivitaInMemoria[idTaskTrovato].DataFine = timeout.DataCreazione;
                                        _attivitaInMemoria[idTaskTrovato].CodiceRitorno = "0"; //0 = errore
                                        _attivitaInMemoria[idTaskTrovato].AggiornataSuDB = false;

                                        continue;
                                    }

                                    if (actionTerminata != null)
                                    {
                                        _attivitaInMemoria[idTaskTrovato].CodiceRitorno = actionTerminata.CodiceRitorno == "0" ? "1" : "0";
                                        _attivitaInMemoria[idTaskTrovato].AggiornataSuDB = false;
                                    }

                                    if (taskCompletato != null)
                                    {
                                        _attivitaInMemoria[idTaskTrovato].DataFine = taskCompletato.DataCreazione;
                                        _attivitaInMemoria[idTaskTrovato].AggiornataSuDB = false;
                                    }
                                }
                                else
                                {
                                    //Se è terminato non fai niente per adesso, siamo in presenza di un doppione che viene gestito nell'else
                                    //Lo teniamo fuori dai giochi.
                                    continue;
                                }
                            }
                            else
                            {
                                //Gli idEvento buoni che devo controllare su Windows Scheduler sono:
                                //200 ActionIniziata (non hanno il codice di ritorno) -> Mi interessa la data registrata
                                var actionIniziata = gruppo.FirstOrDefault(a => a.EventId == "200");

                                if(actionIniziata != null)
                                {
                                    //allora è sicuro che per questo gruppo.Key il task è iniziato quindi lo aggiungo.
    
                                    //Lo Aggiungo ma dovrei prima rimuovere un eventuale doppione con stesso nome ma IdTask differente (SE ESISTE).
                                    //Mi accerto che non ci sia un qualcosa con lo stesso nome già segnato dentro _attivitaInMemoria altrimenti vado a fare doppione
                                    foreach (var attivita in _attivitaInMemoria.Values.ToList())
                                    {
                                        if (attivita.NomeTask == actionIniziata.NomeTask)
                                        {
                                            //allora è un attività obsoleta, non mi interessa se è finita o meno, la rimuovo.
                                            //La sostituirò con la nuova
                                            _attivitaInMemoria.Remove(attivita.IdTask);
                                        }
                                    }

                                    //ok mi sono assicurato che se esiste qualcosa con lo stesso nome ma idTask differente è stato rimosso
                                    //ora aggiungo tranquillamente l'elemento.
                                    _attivitaInMemoria[idTaskTrovato] = new AttivitaInMemoria()
                                    {
                                        Server = _server.Nome,
                                        DataInizio = actionIniziata.DataCreazione,
                                        AggiornataSuDB = false,
                                        CodiceRitorno = null,
                                        DataFine = null,
                                        IdTask = idTaskTrovato,
                                        Action = actionIniziata.Action,
                                        NomeAction = actionIniziata.NomeAction,
                                        NomeTask = actionIniziata.NomeTask,
                                    };


                                    //Con la lettura incrementale, se ci sono eventi associati a questo task con eventi di ActionTerminata o TaskCompletato o TaskTerminatoForzatamente,
                                    //li devo segnare subito altrimenti al prossimo giro li perdo.
                                    var actionTerminataNuova = gruppo.LastOrDefault(a => a.EventId == "201");
                                    var taskCompletatoNuovo = gruppo.LastOrDefault(a => a.EventId == "102");
                                    var taskTerminatoForzatamenteNuovo = gruppo.LastOrDefault(a => a.EventId == "111");
                                    var timeoutNuovo = gruppo.LastOrDefault(a => a.EventId == "329");

                                    if (taskTerminatoForzatamenteNuovo != null)
                                    {
                                        _attivitaInMemoria[idTaskTrovato].DataFine = taskTerminatoForzatamenteNuovo.DataCreazione;
                                        _attivitaInMemoria[idTaskTrovato].CodiceRitorno = "0"; //0 = errore
                                        _attivitaInMemoria[idTaskTrovato].AggiornataSuDB = false;
                                        continue;
                                    }

                                    if (timeoutNuovo != null)
                                    {
                                        _attivitaInMemoria[idTaskTrovato].DataFine = timeoutNuovo.DataCreazione;
                                        _attivitaInMemoria[idTaskTrovato].CodiceRitorno = "0"; //0 = errore
                                        _attivitaInMemoria[idTaskTrovato].AggiornataSuDB = false;
                                        continue;
                                    }

                                    if (actionTerminataNuova != null)
                                    {
                                        _attivitaInMemoria[idTaskTrovato].CodiceRitorno = actionTerminataNuova.CodiceRitorno == "0" ? "1" : "0";
                                        _attivitaInMemoria[idTaskTrovato].AggiornataSuDB = false;
                                    }

                                    if (taskCompletatoNuovo != null)
                                    {
                                        _attivitaInMemoria[idTaskTrovato].DataFine = taskCompletatoNuovo.DataCreazione;
                                        _attivitaInMemoria[idTaskTrovato].AggiornataSuDB = false;
                                    }

                                }
                                else
                                {
                                    //Se non ho trovato l'evento di inizio, potrei essere a cavallo da un giorno all'altro
                                    //Significa che io ho segnato come task il 200 quindi l'ho messo in memoria
                                    continue;
                                }
                            }
                        }
                    }

                    if (letturaOk)
                        _ultimoRecordId = maxRecordIdLetto;

                    //Devo scrivere un log dettagliato per ogni attività in memoria, così da capire se il processo funziona correttamente.
                    foreach (var attivita in _attivitaInMemoria.Values)
                    {
                        _logger.LogDebug($"Attività in memoria: Server: {attivita.Server} - {_server.Host} - {_server.NomeServer}, IdTask: {attivita.IdTask}, NomeTask: {attivita.NomeTask}, NomeAction: {attivita.NomeAction}, Action: {attivita.Action}, DataInizio: {attivita.DataInizio}, DataFine: {attivita.DataFine}, CodiceRitorno: {attivita.CodiceRitorno}, AggiornataSuDB: {attivita.AggiornataSuDB}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Errore durante l'algoritmo di popolamento della memoria.");
                }
                #endregion
    

                //Adesso facciamo la funzione che aggiorna su DB.
                //Quello che è importante è che abbiamo la struttura in memoria pronta per fare quello che ci serve
                
                //filtra per quelli che devono essere aggiornati.
                if(_attivitaInMemoria.Count > 0)
                {
                    try
                    {
                        var daAggiornare = _attivitaInMemoria.Values.Where(a => !a.AggiornataSuDB).ToList();
                        await AggiornaDB(daAggiornare, _server.Host + " - " + _server.NomeServer);

                        foreach (var attivita in daAggiornare)
                        {
                            _logger.LogInformation($"Aggiornamento attivita {attivita.NomeTask} DataInizio: {attivita.DataInizio} DataFine: {attivita.DataFine} CodiceRitorno: {attivita.CodiceRitorno}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Errore durante l'aggiornamento su DB.");
                    }
                }



                //poi capiamo il delay
                _logger.LogDebug($"\nMi fermo per 5 secondi prima di riprendere la lettura degli eventi.\n\n");
                _logger.LogDebug($"\nRiparto alle ore: {DateTime.Now.AddSeconds(5)}\n\n");
                await Task.Delay(5000, stoppingToken);
            }


        }

        private EventLogSession CreaSessione()
        {
            if (_server.Host is "." or "localhost")
            {
                return new EventLogSession();
            }

            var password = new System.Security.SecureString();
            foreach (char c in _server.Password)
            {
                password.AppendChar(c);
            }
            password.MakeReadOnly();

            return new EventLogSession(
                _server.Host,
                _server.Nome,      // workgroup: dominio = nome del server
                _server.UserName,  // utente locale del server remoto
                password,
                SessionAuthentication.Default);
        }

        private List<string> RecuperaFiltriAction()
        {
            var filtriAction = new List<string>();

            string query = @"select Descrizione from Schedulazioni where Disabilitata = 0 and CategoriaAttivita = 'Schedulazione Windows'";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                using (SqlCommand command = new SqlCommand(query, connection))
                {
                    
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            //potremmo avere la necessità di fare manipolazione sulla stringa
                            string filtroAction = reader.GetString(0);
                            filtriAction.Add(filtroAction);
                        }
                    }
                }
                connection.Close();
            }

            return filtriAction;

        }

        private async Task AggiornaDB(List<AttivitaInMemoria> daAggiornare, string NomeServer)
        {
            string query = @"UPDATE Schedulazioni SET EsecuzioneInizio = @Start, EsecuzioneFine = @Stop, SuccessoUltimaEsecuzione = @Esito, Nome = @NomeServer WHERE Descrizione = @Descrizione AND Disabilitata = 0";

            using (SqlConnection connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using(SqlCommand command = new SqlCommand(query, connection))
                {
                    command.Parameters.Add("@Descrizione", System.Data.SqlDbType.NVarChar);
                    command.Parameters.Add("@NomeServer", System.Data.SqlDbType.NVarChar);
                    command.Parameters.Add("@Start", System.Data.SqlDbType.DateTime);
                    command.Parameters.Add("@Stop", System.Data.SqlDbType.DateTime);
                    command.Parameters.Add("@Esito", System.Data.SqlDbType.Bit);
                    
                    foreach (var attivita in daAggiornare)
                    {
                        command.Parameters["@Descrizione"].Value = attivita.NomeTask.Split('\\').Last();
                        command.Parameters["@NomeServer"].Value = NomeServer;
                        command.Parameters["@Start"].Value = attivita.DataInizio ?? (object)DBNull.Value;
                        command.Parameters["@Stop"].Value = attivita.DataFine ?? (object)DBNull.Value;
                        command.Parameters["@Esito"].Value = attivita.CodiceRitorno == null ? DBNull.Value : attivita.CodiceRitorno == "1" ? true : false; 
                        try
                        {
                            await command.ExecuteNonQueryAsync();
                            attivita.AggiornataSuDB = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Errore durante l'aggiornamento del DB per NomeTask: {attivita.NomeTask}");
                        }
                    }
                }
            }
        }
    }
}

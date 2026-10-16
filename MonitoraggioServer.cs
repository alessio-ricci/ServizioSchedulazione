using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Diagnostics.Eventing.Reader;

namespace ServizioSchedulazione
{
    public class MonitoraggioServer
    {
        private readonly SettaggioServer _server;
        private readonly string _nomeLog;
        private readonly string _filtroPath;
        private readonly ILogger _logger;

        //stato PRIVATO di questo server
        private long _ultimoRecordId = 0;
        private readonly Dictionary<string, AttivitaInMemoria> _attivitaInMemoria = new();
        private EventLogSession? _sessione;

        public MonitoraggioServer(SettaggioServer server, string nomeLog, string filtroPath, ILogger logger)
        {
            _server = server;
            _nomeLog = nomeLog;
            _filtroPath = filtroPath;
            _logger = logger;
        }

        public async Task EseguiAsync(CancellationToken stoppingToken)
        {
            DateTime dataInizio = DateTime.Now;
            EventRecord evento = null;
            List<Attivita> listaAttivita = new List<Attivita>();

            _logger.LogInformation($"\n\nInizio lettura eventi da log: {_nomeLog} con filtroPath: {_filtroPath} e NomeServer: {_server.Nome}. Data: {DateTime.Now}\n\n");

            _logger.LogInformation(_attivitaInMemoria.Count > 0 ? $"Attività in memoria: {_attivitaInMemoria.Count > 0}" : "Nessuna attività in memoria.");

            while (!stoppingToken.IsCancellationRequested)
            {

                listaAttivita.Clear();
                long maxRecordIdLetto = _ultimoRecordId;
                bool letturaOk = false;

                try
                {
                    //Allora cosi facendo sto filtrando per ultimo recordId rimasto in memoria e per la data di oggi
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

                                string? nomeTask = Utilita.RecuperaValoreDaXml(doc, "TaskName");

                                //Ne devo mettere di più perché a seconda del tipo di evento cambia il nome dell'elemento che contiene l'id del task...
                                string? idTask = Utilita.RecuperaValoreDaXml(doc, "InstanceId") ?? Utilita.RecuperaValoreDaXml(doc, "TaskInstanceId") ?? Utilita.RecuperaValoreDaXml(doc, "QueuedTaskInstanceId");

                                string? codiceRitorno = Utilita.RecuperaValoreDaXml(doc, "ResultCode");
                                string? idEvento = Utilita.RecuperaValoreDaXml(doc, "EventID");
                                string? nomeAction = Utilita.RecuperaValoreDaXml(doc, "ActionName");
                                long recordId = evento.RecordId.HasValue ? (long)evento.RecordId.Value : 0;

                                //segnati l'ultimo record che hai letto.
                                if(recordId > maxRecordIdLetto)
                                {
                                    maxRecordIdLetto = recordId;
                                }

                                //solo se nomeTask è diverso da null e contiente il filtroPath
                                if (!string.IsNullOrEmpty(nomeTask) && nomeTask.Contains(_filtroPath) && idTask != null)
                                {

                                    Attivita attivita = new Attivita
                                    {
                                        RecordId = recordId,
                                        IdTask = idTask,
                                        EventId = idEvento,
                                        DataCreazione = evento.TimeCreated,
                                        Nome = nomeTask,
                                        CodiceRitorno = codiceRitorno,
                                        NomeAction = nomeAction
                                    };

                                    listaAttivita.Add(attivita);
                                }
                                else
                                {
                                    continue; //salta l'evento se non contiene il filtroPath
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
                    _logger.LogError(ex, "Errore durante la lettura degli eventi.");
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



                    if (listaAttivita.Count > 0)
                    {
                        //Una volta che ho letto tutto e ho tutte le attività segnate, raggruppo per idTask
                        var attivitaRaggruppate = listaAttivita.OrderBy(a => a.RecordId).GroupBy(a => a.IdTask);

                        foreach (var gruppo in attivitaRaggruppate)
                        {
                            string idTask = gruppo.Key;

                            if (_attivitaInMemoria.ContainsKey(idTask))
                            {
                                //Se esiste, controlla se terminato o meno
                                if (!_attivitaInMemoria[idTask].Terminata)
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
                                        _attivitaInMemoria[idTask].DataFine = taskTerminatoForzatamente.DataCreazione;
                                        _attivitaInMemoria[idTask].CodiceRitorno = "1"; //1 = errore
                                        _attivitaInMemoria[idTask].AggiornataSuDB = false;

                                        continue;
                                    }

                                    if (timeout != null)
                                    {
                                        _attivitaInMemoria[idTask].DataFine = timeout.DataCreazione;
                                        _attivitaInMemoria[idTask].CodiceRitorno = "1"; //1 = errore
                                        _attivitaInMemoria[idTask].AggiornataSuDB = false;

                                        continue;
                                    }

                                    if (actionTerminata != null)
                                    {
                                        _attivitaInMemoria[idTask].CodiceRitorno = actionTerminata.CodiceRitorno == "0" ? "1" : "0";
                                        _attivitaInMemoria[idTask].AggiornataSuDB = false;
                                    }

                                    if (taskCompletato != null)
                                    {
                                        _attivitaInMemoria[idTask].DataFine = taskCompletato.DataCreazione;
                                        _attivitaInMemoria[idTask].AggiornataSuDB = false;
                                    }
                                }
                                else
                                {
                                    //Se è terminato non fai niente per adesso, siamo in presenza di un doppione che non va gestito.
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
                                    foreach (var attivita in _attivitaInMemoria.Values.ToList())
                                    {
                                        if (attivita.Nome == gruppo.First().Nome)
                                        {
                                            //allora è un attività obsoleta, non mi interessa se è finita o meno, la rimuovo
                                            _attivitaInMemoria.Remove(attivita.IdTask);
                                        }
                                    }

                                    //ok mi sono assicurato che se esiste qualcosa con lo stesso nome ma idTask differente è stato rimosso
                                    //ora aggiungo tranquilamente l'elemento.
                                    _attivitaInMemoria[idTask] = new AttivitaInMemoria()
                                    {
                                        Server = _server.Nome,
                                        DataInizio = actionIniziata.DataCreazione,
                                        AggiornataSuDB = false,
                                        CodiceRitorno = null,
                                        DataFine = null,
                                        IdTask = idTask,
                                        Nome = gruppo.First().Nome
                                    };


                                    //Con la lettura incrementale, se ci sono eventi associati a questo task con eventi di ActionTerminata o TaskCompletato o TaskTerminatoForzatamente, li devo segnare subito altrimenti al prossimo giro li perdo.
                                    //li devo segnare subito altrimenti al prossimo giro li perdo.
                                    var actionTerminataNuova = gruppo.LastOrDefault(a => a.EventId == "201");
                                    var taskCompletatoNuovo = gruppo.LastOrDefault(a => a.EventId == "102");
                                    var taskTerminatoForzatamenteNuovo = gruppo.LastOrDefault(a => a.EventId == "111");
                                    var timeoutNuovo = gruppo.LastOrDefault(a => a.EventId == "329");

                                    if (taskTerminatoForzatamenteNuovo != null)
                                    {
                                        _attivitaInMemoria[idTask].DataFine = taskTerminatoForzatamenteNuovo.DataCreazione;
                                        _attivitaInMemoria[idTask].CodiceRitorno = "1"; //1 = errore
                                        _attivitaInMemoria[idTask].AggiornataSuDB = false;
                                        continue;
                                    }

                                    if (timeoutNuovo != null)
                                    {
                                        _attivitaInMemoria[idTask].DataFine = timeoutNuovo.DataCreazione;
                                        _attivitaInMemoria[idTask].CodiceRitorno = "1"; //1 = errore
                                        _attivitaInMemoria[idTask].AggiornataSuDB = false;
                                        continue;
                                    }

                                    if (actionTerminataNuova != null)
                                    {
                                        _attivitaInMemoria[idTask].CodiceRitorno = actionTerminataNuova.CodiceRitorno == "0" ? "1" : "0";
                                        _attivitaInMemoria[idTask].AggiornataSuDB = false;
                                    }

                                    if (taskCompletatoNuovo != null)
                                    {
                                        _attivitaInMemoria[idTask].DataFine = taskCompletatoNuovo.DataCreazione;
                                        _attivitaInMemoria[idTask].AggiornataSuDB = false;
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
                        _logger.LogInformation($"Attività in memoria: Server: {attivita.Server}, IdTask: {attivita.IdTask}, Nome: {attivita.Nome}, DataInizio: {attivita.DataInizio}, DataFine: {attivita.DataFine}, CodiceRitorno: {attivita.CodiceRitorno}, AggiornataSuDB: {attivita.AggiornataSuDB}");
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

    

                //poi capiamo il delay
                _logger.LogInformation("\n\nMi fermo per 2 minuti prima di riprendere la lettura degli eventi.\n\n");
                _logger.LogInformation($"\n\nRiparto alle ore: {DateTime.Now.AddMinutes(2)}\n\n");
                await Task.Delay(120000, stoppingToken);
            }


        }

        private EventLogSession CreaSessione()
        {
            return _server.Host is "." or "localhost"
                ? new EventLogSession()
                : new EventLogSession(_server.Host);
        }
    }
}

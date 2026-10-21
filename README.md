Requisiti:
  Deve essere un Windows Service che legge gli eventi/task censiti su una tabella e poi interroga la schedulazione di Windows Server intercettandoli e salvando:
    -NomeAction
    -DataInizio
    -DataFine
    -Esito
  Il servizio deve potersi connettere a più server.
  Ogni server ha impostato i propri task e le proprie schedulazioni
    NB: E' possibile che ci siano action con lo stesso nome ma in esecuzione su due server differenti.
    NB: Se un Task ha più Action sollevare eccezione.

Attività:
  --Leggere i task schedulati dal WindowsScheduler -->FATTO
  --Leggere i task schedulati in maniera incrementale -->FATTO
  --Creare una struttura di Attività registrate in memoria che tenga conto di cosa deve essere aggiornato su DB e cosa no in base a quello che ha letto. -->FATTO
  --Creare la parte di comunicazione con il DB con la query di update -->FATTO 
  --Impostare l'esecuzione simultanea di lettura e aggiornamento su più server -->FATTO
  --Salvare anche il nome della Action. -->FATTO
  --Gestire il fatto che il giro parte guardando le schedulazioni che abbiamo censito, non il contrario. -->FATTO
  --IMPORTANTE: Testare più connessioni alle macchine. -->FATTO
  --Gestito il caso in cui ci siano più ACTION sullo stesso TASK -->FATTO



---------------------------------------------------------------------------------------------------------------------------------------------------
SCRIPT PER CREARE LA TABELLA DEL DB --> DB Schedulazioni

USE [Schedulazioni]
GO
/****** Oggetto: Table [dbo].[Schedulazioni]    Data dello script 16/10/2026 16:55:42 ******/
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
CREATE TABLE [dbo].[Schedulazioni](
	[id] [int] IDENTITY(1,1) NOT NULL,
	[Descrizione] [varchar](50) NULL,
	[Parametri] [varchar](500) NULL,
	[Disabilitata] [bit] NOT NULL,
	[CategoriaAttivita] [varchar](50) NULL,
	[EsecuzioneInizio] [datetime] NULL,
	[SuccessoUltimaEsecuzione] [bit] NULL,
	[EsecuzioneFine] [datetime] NULL,
	[Ordine] [int] NOT NULL,
	[TimeoutMinuti] [int] NULL,
	[Percorso] [varchar](500) NULL,
	[TipoComando] [varchar](10) NULL,
	[Nome] [varchar](500) NULL,
	[KillMeNow] [bit] NOT NULL,
	[RunMeNow] [bit] NOT NULL,
	[CodaEsecuzione] [varchar](50) NULL,
	[Filtri] [varchar](50) NULL,
 CONSTRAINT [PK_Schedulazioni] PRIMARY KEY CLUSTERED 
(
	[id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
---------------------------------------------------------------------------------------------------------------------------------------------------

18-10-2026
Abbiamo correttamente testato la connessione su un PC remoto


Per testare la connessione su un pc remoto, da quello client eseguire questo
wevtutil qe Microsoft-Windows-TaskScheduler/Operational /r:INDIRIZZOIP /u:SERVER\UserName /p:PASSWORD /c:5 /rd:true /f:text
wevtutil qe Microsoft-Windows-TaskScheduler/Operational /r:192.168.1.57 /u:Alessio\MonitorUser /p: /c:5 /rd:true /f:text


**1. Servizio Event Log e Remote Registry non servono, ma serve RPC attivo**
PowerShell
```
Get-Service EventLog, RpcSs, RpcEptMapper | Select Name,Status
Set-Service EventLog -StartupType Automatic; Start-Service EventLog
```

2. Ripristina le regole firewall (quelle rimosse):
```
Enable-NetFirewallRule -DisplayGroup "Remote Event Log Management"
```

Se il gruppo non esiste/le regole sono state cancellate, ricreale:
PowerShell
```
New-NetFirewallRule -DisplayName "Remote Event Log (RPC-EPMAP)" -Direction Inbound -Protocol TCP -LocalPort 135 -Action Allow -Profile Any
New-NetFirewallRule -DisplayName "Remote Event Log (RPC dinamico)" -Direction Inbound -Protocol TCP -LocalPort 49152-65535 -Program "%SystemRoot%\system32\svchost.exe" -Service EventLog -Action Allow -Profile Any
New-NetFirewallRule -DisplayName "Remote Event Log (NP)" -Direction Inbound -Protocol TCP -LocalPort 445 -Actio
```

**3. Permessi: l'utente usato dal client deve essere nel gruppo "Event Log Readers":**
```
net localgroup "Event Log Readers" SERVER\utente /add
# oppure dominio: net localgroup "Event Log Readers" DOMINIO\utente /add
```

**4 Per vedere l'utente abilitati lato client
```
Get-LocalGroupMember -SID S-1-5-32-573 Lettori registri eventi
```

**5. Verifica ascolto:**
```
Get-NetFirewallRule -DisplayGroup "Remote Event Log Management" | Select DisplayName,Enabled,Profile
```

CLIENT
**1. Test connettività:**
```
Test-NetConnection <IP_SERVER> -Port 135
Test-NetConnection <IP_SERVER> -Port 445
```


## Sul SERVER
Trova il nome reale del PC e degli utenti:
```
hostname
Get-LocalUser | Select Name,Enabled
```

Se `hostname` restituisce `SRV01` e l'utente è `mario`, l'account è `SRV01\mario`.


Aggiungilo al gruppo senza dipendere dalla lingua (SID del gruppo Event Log Readers):
```
Add-LocalGroupMember -SID S-1-5-32-573 -Member "SRV01\mario"
```



## 19-10-2026
## Fabrizio ci dice che la chiave è il nome del Task e che sarà cura loro assicurasi che sul server i task siano chiamati ovviamente in maniera diversa.
## Quindi sul db la chiave sarà il nome del task non ci possiamo sbagliare.
## 
## Ho recuperato i nomi dei task di interesse sul DB
## Quando recupero i task dal server remoto, se il nome del task è contenuto nella lista dei task recuperata su DB allora lo segno.
## Faccio l'update su DB e la where condition la faccio come detto sul campo Nome della tabella schedulazioni e metto come condizione ulteriore Disabilitata = 0 perché non deve essere disabilitata.
## 
## Mancherebbe:
## -1 Lanciare eccezione qualora si trovi un task con più action. L'eccezione non deve bloccare l'esecuzione ma deve semplicemente saltare quell'evento.
## -2 Quando recupero i Task sul DB la where condition qual è? Perché devo identificare solo quelli che mettiamo noi, non quelli che già ci sono.
## 
## Possibili problemi nei test:
## -Il contains è infame. Se per esempio su nome task arriva \\MieiTest\\T1_OK_V3 e su filtri ho T1_OK allora il contains mi ritorna true. Non mi piace moltissimo
## 
## 
## Quando ci sono più action sul task, non è così semplice andarlo a tracciare.
## Perché lui non ha due nodi di Action
## 
## 
## Dovrei fare cosi:
## Vedere se esistono piu idEvento 200 per lo stesso IDTask.
## 
## Dovrei avere risolto correttamente. Testiamo.
## Sembra proprio che ho risolto.
## 
## Manca solo capire i task su DB quando li recupero all'inizio, la where condition qual è.
## NB: Il metodo di interazione con il DB è stato reso async


--Monitoraggio su DB
SELECT * FROM [Schedulazioni].[dbo].[Schedulazioni]
--update Schedulazioni set Disabilitata = 0 where Nome = '\MieiTest\Task3'
SELECT NOME, DATEDIFF(MILLISECOND, EsecuzioneInizio, EsecuzioneFine) as TempoEsecuzione from Schedulazioni


-20-10-2026
Oggi proviamo la connessione alla macchina remota.

Ho bisogno di 
Username abilitato al gruppo Event Log Readers
Hostname della macchina remota
Password dell'utente che mi hanno abilitato al gruppo Event Log Readers

Username: 



Per usare il servizio.
Da powershell lanciare:
```
	#crea tutto il necessario per il servizio
	dotnet publish -c Release -o C:\Servizi\ServizioSchedulazione 

	#Crea il servizio su windows
	sc.exe create "ServizioSchedulazione" binPath= "C:\Servizi\ServizioSchedulazione\ServizioSchedulazione.exe" start= auto obj= "safohp\a.ricci-ext" password= "Aleric.2026!"

	#Avvia il servizio
	sc.exe start "ServizioSchedulazione"


Gestione dei Log
Se vuoi verificare che il servizio stia girando correttamente e leggendo il file di configurazione:
	Apri Gestione Eventi (Event Viewer) su Windows.
	Vai su Registri di Windows -> Applicazione (Windows Logs -> Application).
	Troverai i log generati dal tuo servizio (con la sorgente pari al nome del servizio).
```


```
sc.exe create "ServizioSchedulazione" binPath= "C:\Servizi\ServizioSchedulazione\ServizioSchedulazione.exe" start= auto obj= "safohp\a.ricci-ext" password= "Aleric.2026!"
sc failure "ServizioSchedulazione" reset= 86400 actions= restart/60000/restart/60000/run/60000
	-reset= 86400: Dopo 24 ore (86400 secondi) senza crash, il contatore dei tentativi si azzera.
	-actions= restart/60000/...: Indica la sequenza delle azioni. In questo caso:
	-Al primo crash, riavvia il servizio dopo 60 secondi (60000 millisecondi).
	-Al secondo crash, riavvia il servizio dopo altri 60 secondi.
	-Dal terzo crash in poi, puoi decidere se continuare a riavviare o eseguire qualcos'altro.


#configurazione più semplice
sc.exe failure "ServizioSchedulazione" reset= 0 actions= restart/30000

#avviare il servizio
sc.exe start "ServizioSchedulazione"

#Stoppare il servizio
sc.exe stop "ServizioSchedulazione"

#Eliminare il servizio
sc.exe delete "ServizioSchedulazione"
```
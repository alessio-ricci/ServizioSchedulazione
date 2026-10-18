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
  -Leggere i task schedulati dal WindowsScheduler (fatto)
  -Leggere i task schedulati in maniera incrementale (fatto)
  -Creare una struttura di Attività registrate in memoria che tenga conto di cosa deve essere aggiornato su DB e cosa no in base a quello che ha letto.
  -Creare la parte di comunicazione con il DB con la query di update (fatto)
  -Impostare l'esecuzione simultanea di lettura e aggiornamento su più server (fatto)
  -Salvare anche il nome della Action.
  -Salvare quello sul DB al posto del nome Task.
  -Creare dei task che lanciano action diverse dalla classica "powershell.exe".
  -Gestire action con lo stesso nome in esecuzione su due server differenti.
  -Gestire il fatto che il giro parte guardando le schedulazioni che abbiamo censito, non il contrario.
  -IMPORTANTE: Testare più connessioni alle macchine.

16-10-2026
Domande:
  -Ci interessa salvare il nome della Action o del Task? Al 100% quello della Action
  -Se esistono più action uguali ma in running su server diversi come le distinguo sul DB per dire quale è partito e quale no?

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
wevtutil qe Microsoft-Windows-TaskScheduler/Operational /r:192.168.1.57 /u:Alessio\MonitorUser /p:h4mm0nd19e5 /c:5 /rd:true /f:text


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
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


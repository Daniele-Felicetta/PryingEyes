# Quartiere della Stazione: base giocabile verificata

La scena `Assets/_PryingEyes/Scenes/Citta.unity` viene generata dalla ricetta
`Assets/_PryingEyes/City/citta.json`. La Torre non viene sostituita.

1. Su un nuovo checkout, `python tools/citylab.py --build-assets` genera FBX
   e texture con Blender 4.5; `BLENDER_EXE` permette di indicare il percorso.
   `python tools/citylab.py` controlla le misure della ricetta e produce
   `unity_logs/city_preview.svg`: strade, edifici, porte, espositori e spawn.
   Produce anche `unity_logs/city_report.json` per l'agente.
2. Nell'Editor Unity: **Prying Eyes > City > Generate Preview Scene** costruisce
   la scena. Apri `Citta.unity` e premi Play per provare il ciclo a ondate della
   hall, con quattro edifici visitabili e tre espositori sulla strada.
3. In batch: `-executeMethod PryingEyes.EditorTools.BuildPlayer.City -batchmode -quit`
   crea un eseguibile distinto in `Builds/CityPreview`. Va lanciato con Unity
   6000.6.7f1, con una licenza attiva e senza un secondo Editor sul progetto.

La RONDA-9 e ARGINE .45 vengono modellate dalla famiglia parametrica in
`modelli/armi/armi_urbane.py`, cotte e impacchettate
come le altre armi; l'importazione in Unity avviene durante la generazione della
scena. Hanno ID e indici aggiunti in coda al catalogo; gli indici precedenti
restano stabili e la Torre la esclude dagli acquisti casuali finche' non importa
i relativi prefab. Nelle ondate urbane compaiono lo Spazzino (rapido, con attrezzo di presa)
e il Vigile (corazzato, con scudo): modelli procedurali e combattimento con la
logica esistente. La co-op urbana e' disabilitata fino all'integrazione della
sincronizzazione del nuovo livello.

Questa e' una prima fetta di produzione: edifici e arredi sono ancora blocchi,
e i nuovi nemici richiedono prove di movimento, collisione e leggibilita'.
La build Unity e la prova nativa ora riescono. La verifica automatica passa **45/45**:
ricetta e geometria incorporate, corrispondenza di quattro edifici, quattro spawn e tre espositori con il piano condiviso,
quattro percorsi dalla strada agli interni, quattro attraversamenti con il vero
CharacterController, quattro percorsi dagli spawn, acquisto e rifornimento dei tre
espositori, inseguimento e contatto in mischia di entrambi i nemici, dimensioni degli
agganci e tre ondate complete (24 nemici). Questo ultimo controllo elimina i nemici
con danno di prova: verifica la progressione, non il bilanciamento di una partita armata.

## Riproduzione e prove

```powershell
python tools/test_citylab.py
python tools/citylab.py --runtime unity/PryingEyes/Builds/CityPreview/PryingEyes.exe --timeout 240
```

Il secondo comando produce `unity_logs/city-runtime/run.json`, con esito del
processo, SHA256 dell'eseguibile e dell'assembly effettivamente provati, e i controlli
nativi. `report.json` contiene codice, soggetto, risultato osservato e risultato
atteso per ciascun controllo. Nella stessa cartella ci sono immagini della RONDA-9,
dello Spazzino, del Vigile e del quartiere. Un report positivo precedente viene
rimosso prima di avviare il processo: crash, timeout o report mancante falliscono.
La ricetta richiesta e' confrontata con lo SHA256 incorporato nella scena dalla
build; il piano calcolato esternamente e' confrontato con quello incorporato.
Una ricetta o una geometria diversa falliscono con `BUILD_LAYOUT_OR_RECIPE_MISMATCH`,
anche se il processo e i suoi controlli locali sono positivi.

`python tools/citylab.py --recipe percorso.json` permette di provare una variante
senza modificare la ricetta del progetto. Ricette malformate, numeri non finiti,
armi sconosciute e spawn troppo vicini ai confini sono rifiutati con report.
`python tools/test_citylab.py` verifica il caso valido, cinque varianti errate, una variante piu' piccola con spawn adattati,
il rifiuto di un vecchio report positivo quando manca l'eseguibile e il rifiuto di
report con ricetta o geometria diversa. Questi ultimi casi usano un processo simulato;
il caso completo usa la build reale.

Per aggiornare solo il codice di una CityPreview gia' completa, aggiungere
`-scriptsOnly` alla build City; le modifiche a ricetta, scena o asset richiedono
invece la build completa. Unity deve essere avviato con accesso ai suoi servizi
nativi: il precedente crash nel servizio BuildReport/Mono avveniva prima della
generazione della scena nell'ambiente ristretto, non nel generatore urbano.

## Laboratori riutilizzati nella citta'

`GameManager.LevelReady` espone quando il livello e il giocatore sono pronti sia
nella Torre sia nelle scene costruite. GunplayTest e OpticsTest usano questa
condizione e isolano le ondate della hall/citta' durante le misure.

Sulla CityPreview aggiornata:

- `-autotest -nofire -gunplaytest`: **297/297** controlli. Include prefab e agganci,
  ingresso ADS, modalita' di fuoco, ricariche, munizioni e acquisti della RONDA-9.
- `-autotest -nofire -opticstest -opticout <cartella>`: **37/37** controlli. Il
  laboratorio controlla le ottiche previste dal suo copione; non e' una valutazione
  artistica della mira metallica della RONDA-9.

Log riproducibili in `unity_logs/city-gunplay.log` e `unity_logs/city-optics.log`.
Questi due laboratori non hanno ancora lo schema JSON unico: vanno collegati alla
suite comune. Il test di gunplay usa anche bersagli isolati e Tick controllato;
non sostituisce una partita di bilanciamento.

## Difetto riprodotto e corretto

Gli accessori ereditavano la scala del braccio: attrezzo da **0,462 m** invece di
1,05 m e scudo da **0,143 m** invece di 0,65 m. La prova mirata
`-autotest -nofire -citytest -cityrigonly` falliva entrambi i controlli
`ATTACHMENT_SCALE` prima della correzione. I punti di aggancio ora restano a scala
unitaria e solo la mesh riceve le proporzioni; la prova completa conferma le due
misure. Braccia e gambe hanno movimento procedurale, con preparazione, colpo e
recupero distinti. I modelli restano sagome di prototipo, non asset finali.

## Lavoro ancora necessario per l'obiettivo completo

- Dare al quartiere identita' visiva: facciate, arredi, segnaletica, interni e
  illuminazione. Le immagini attuali mostrano esplicitamente un blockout.
- Portare la progressione urbana dal ciclo della hall a un survival urbano
  completo: aree a pagamento, percorsi alternativi e pressione delle orde.
- Completare la valutazione visiva e il bilanciamento della prima arma; aggiungere
  varianti: ARGINE .45 e' stata aggiunta tramite dati alla famiglia della RONDA-9,
  senza un nuovo ramo per ID nel runtime.
- Produrre modelli e animazioni definitivi dei due nemici, misurare prestazioni e
  bilanciamento con una partita reale. Lo scudo per ora caratterizza la sagoma;
  non e' ancora una meccanica distinta di protezione frontale.
- Proseguire l'unificazione di catalogo, report e comandi: il primo catalogo
  autorevole comprende ora tutte le 30 armi, con schema e comandi Studio
  (vedi `production-studio.md`). CityLab e' una
  prima filiera utilizzata davvero, non l'intera suite proposta. La geometria ora
  ha una sola implementazione nel pacchetto `com.pryingeyes.layout`, compilata
  direttamente da Unity e PlanLab e consumata dall'anteprima Python.
- Coordinare build/import concorrenti, risultati per lavoro, annullamento e
  provenienza completa delle sorgenti. Gli hash della build sono una prima
  protezione, non la prova completa di provenienza degli asset.
- Proseguire l'estrazione dei moduli di gioco: la geometria del quartiere e' ora
  un primo pacchetto riutilizzato nel progetto indipendente `unity/LayoutDemo`.
  Armi, stato del combattimento e resto della suite restano da estrarre.
- Integrare e verificare la cooperativa urbana; attualmente e' disabilitata.

La Torre e le sue build non vengono sostituite da CityPreview.


## Primo pacchetto e prova di riutilizzo

`unity/PryingEyes/Packages/com.pryingeyes.layout` contiene il calcolo autorevole
del quartiere, in un assembly senza riferimenti al motore. `CityBuilder` lo usa
per costruire la scena. PlanLab compila lo stesso file e produce i dati letti da
CityLab per anteprima e controlli: Python non ricalcola piu' edifici, porte o spawn.

`unity/LayoutDemo` installa il pacchetto locale e solo moduli incorporati di Unity.
La verifica nativa `DistrictDemo.Verify` passa determinismo, variazione delle misure,
rifiuto di misure invalide e generazione di una scena indipendente. Il suo
`unity_logs/layout-demo/layout.json` coincide con il piano della CLI e della build
urbana, confrontando i numeri con tolleranza di 0,01 mm.

Il refactoring conserva posizione, rotazione e scala di tutti i **77 elementi con trasformazione**
della scena del commit `022ded6`: confronto registrato in
`unity_logs/city-layout-refactor.json`. La scena include ora anche ricetta e piano
per identificare con precisione cosa e' stato costruito.

La variante piu' piccola adatta gli spawn ai limiti del quartiere: non conserva
piu' i punti fissi della prima bozza, che sarebbero finiti sulla recinzione.

Il pianificatore rifiuta anche dimensioni finite che produrrebbero un terreno
di larghezza infinita dopo il raddoppio. Il caso 3e38 metri falliva la verifica
prima della correzione; ora e' respinto sia nella CLI sia nel progetto indipendente.


## Officina e filiera delle stanze

L'isolato sud-ovest usa ora una ricetta arredata dal catalogo delle stanze: banconi,
banco di lavoro, armadi, fusti, insegna, illuminazione e uscita laterale. I percorsi
sono ampi e circolari attorno al banco: non sono generati spostando casualmente
blocchi fino a ottenere una stanza piena. Le altre tre costruzioni restano ancora
gusci generici e l'identità visiva del quartiere richiede ulteriore lavoro.

Il piano locale degli arredi è calcolato dal pacchetto senza Unity `RoomPlan`,
consumato da PlanLab, CityBuilder e dal progetto indipendente LayoutDemo.
La prima verifica nativa dell'officina è 65/65 (`unity_logs/workshop-native`),
incluso controller reale sulle otto tratte e inseguimento attraverso l'uscita
laterale. Questa prova non comprende ancora la successiva correzione delle insegne
specchiate attraverso i muri: la filiera completa verifica anche quel comportamento.
Comandi e limiti dell'adattatore sono in `production-studio.md`.

La filiera completa successiva passa **67/67** controlli, incluse le insegne,
e **297/297** verifiche armi. Job `unity_logs/studio/20261114T092523Z-94b35b15`:
192 file pubblicati e verificati in `Builds/CityPreview`. Il progetto indipendente
passa 8 controlli (`unity_logs/workshop-independent/report.json`).


## Ondate urbane configurabili

La ricetta ora contiene il profilo survival. Le prime tre ondate hanno 12, 16 e
20 nemici; la crescita arriva fino a 96 totali, con al massimo 24 presenti insieme.
Spazzino e Vigile inseguono subito il giocatore. Velocita' e resistenza crescono
con l'ondata; i Vigili mantengono il passo piu' lento del loro archetipo.

Il pacchetto senza Unity `Production.Survival` gestisce budget e ritmo delle
comparse. Il gioco mantiene scelta dei nemici, posizioni, difficolta' e ricompense.
La filiera `asset build room_workshop_station --publish` passa 71 controlli della
citta', 297 del gunplay e 24 del nuovo modulo, oltre ai controlli gia' presenti.
Aggiorna CityPreview con 194 file verificati. La prova indipendente Unity passa
14 controlli. Dettagli, limiti e percorsi delle prove sono in `production-studio.md`.

La citta' rimane incompleta: accessi a pagamento, altri interni caratterizzati,
verticalita' e qualita' visiva dei nemici devono ancora essere portati avanti.


## Accessi agli edifici

La strada e l'officina sono libere dall'inizio; deposito, uffici e posto di controllo
costano rispettivamente 750, 1000 e 1250 punti. Le serrande fermano il controller
e la navigazione dei nemici. Dopo l'acquisto si sollevano e liberano il percorso
solo alla fine dell'animazione. Il prezzo si paga una volta per edificio.

La logica di possesso e acquisto e' nel modulo senza Unity `AreaAccess`, verificato
anche nel progetto indipendente. Nella citta' le prove controllano fondi,
visibilita', lato di acquisto, collisioni, navigazione e addebito unico. La filiera
passa 103 verifiche native della citta', di cui 32 sugli accessi, e aggiorna
CityPreview con 194 file verificati. Il progetto indipendente passa 17 controlli.

Gli acquisti aprono gli interni dal reticolo stradale gia' disponibile. Tre edifici
sono ancora gusci generici: percorsi alternativi, arredi, verticalita' e identita'
visiva restano da completare. Questa fase non suddivide ancora l'intero quartiere
in settori acquistabili. Prove e limiti sono descritti in `production-studio.md`.

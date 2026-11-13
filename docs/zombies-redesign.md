# Revisione richiesta: ritmo da CoD Zombies

## Direzione attiva: sostituzione della progressione

Ultima richiesta dell'utente, 13 novembre 2026: eliminare le candele da accendere e le meccaniche stealth;
meno stanze per piano, più grandi e articolate internamente; apertura delle aree pagando punti; nemici più veloci.
Le verifiche delle passate sotto documentano il sistema precedente; il ciclo nuovo è provato dalla diciassettesima passata.

Requisiti della conversione:

- ondate globali automatiche, senza attivare candele e senza serrande che rinchiudono ogni singola arena;
- punti spesi per aprire nuove aree, con stato condiviso e acquisto convalidato dall'ospite in cooperativa;
- nessun bonus Fantasma, allerta, rilevamento legato alla luce o obiettivo stealth nella progressione;
- circa 7–9 aree principali, con grandi sale composte da più zone e quote, mantenendo scale e balconate percorribili;
- nemici più rapidi, spawn nelle sole aree aperte e tempi d'arrivo controllati anche attraverso le scale;
- armi e rifornimenti utilizzabili nelle aree aperte, obiettivi e guida riscritti per ondate e acquisti;
- regressioni di porte/collisioni, spesa dei punti, ondate, morte, cambio piano, entrata tardiva e cooperativa.

La conversione è completata (diciassettesima passata): il ciclo a ondate è il predefinito della modalità procedurale e la progressione
a candele resta solo con `-candles`. Le prove delle passate precedenti documentano il sistema a candele, salvo dove indicato;
quelle del ciclo nuovo sono nella diciassettesima passata e in `docs/quality-audit.md`. Nessuna prova umana: ritmo, prezzi e
difficoltà vanno ancora giudicati giocando.

Richiesta utente del 13 novembre 2026. Questa revisione estende l'audit: non è completata dai precedenti controlli.

- Riformulare la Veglia: figura votiva più credibile, evitando il volto cilindrico e la fiamma a cono.
- Ridisegnare l'HUD: gerarchia chiara per munizioni, vita, punti, round e obiettivi durante il combattimento.
- Ripensare la gestione degli spazi: molte più sale grandi, nuovi ambienti, alternanza con corridoi e passaggi;
  anelli di circolazione, vie di fuga, forme meno quadrate e sale con pareti curve.
- Verticalità effettiva nello stesso piano: doppie altezze, balconate percorribili, scale e più collegamenti fra quote;
  percorsi utilizzabili dal controller e dai nemici, senza balconate decorative senza uscita.
- Rifare la leggibilità degli attacchi: preparazione e recupero del corpo a corpo, animazione di contatto coerente,
  effetti dei proiettili e impatti meno approssimativi, feedback di danno chiaro e direzionale.

Verifica richiesta: mappe e misure dei piani, scene renderizzate dalle quote inferiore e superiore, camminata fisica
su scale e balconate, percorsi dei nemici, round e transizioni in cooperativa, immagini e prove degli attacchi.
Il riferimento riguarda ritmo, manovrabilità e feedback; l'identità resta quella dell'hotel e della chiesa.

## Prima passata: scala e percorsi verticali

Implementati moduli da 12,8–17,6 m, più sale a moduli multipli, più collegamenti ad anello e porte principali
da 2,4–3 m. Nelle sale ampie: soffitto almeno 8,2 m, balconata a quota 3,6 m, camminamento largo 2,4 m e due
scale da 18 alzate di 20 cm. Le scale sono staccate dalle pareti per mantenere libere le porte al piano terra.
Il percorso superiore è a U: si può salire da una scala e scendere dall'altra; non è un fondale decorativo.
Gli arredi casuali rispettano i percorsi e i nemici possono usare le quote superiori.

Verifiche: 1.000 piante pure senza fallimenti, 400 piani costruiti senza problemi residui, 9.456 punti superiori
raggiungibili dall'arrivo; camminata del controller su sei balconate nei tre temi (21/21 per ciascuna prova),
più dodici piani completi percorsi senza incastri. Vedere `unity_logs/architecture*` e `docs/quality-audit.md`.

## Seconda passata: nuove forme e funzioni

Il generatore ora sceglie una sala adatta come Rotonda, con quattro pareti curve fisiche, fontana bassa centrale,
isole di seduta e due fasce di circolazione. La curvatura rispetta le porte esistenti; le finestre restano sui tratti
rettilinei. Le sale allungate diventano Gallerie; nell’albergo compare anche un Giardino d’inverno con piante e sedute.
Le stanze curve usano la stessa impronta per appartenenza, arredi, punti dei nemici e mappa. Gli espositori restano
sui tratti rettilinei accessibili. Il lampadario della Rotonda illumina pavimento e ostacoli senza cambiare la luminosità
scelta dal giocatore. Le balconate della prima passata restano nelle altre sale ampie.

Verifiche: 400 piani costruiti, nessun problema residuo, 8.164 punti superiori raggiungibili; 399 sale curve senza
angoli esclusi attraversabili. Camminata fisica e collisioni nei tre temi: 42/42 per prova. Dodici piani completi
percorsi senza avvisi; interfaccia 92/92. Log in `unity_logs/curves-*`.

## Terza passata: gerarchia dell’HUD

Raggruppati vita, scudo, batterie e punti a sinistra; arma e munizioni a destra; piano, obiettivo e perk in alto.
I compagni occupano l’angolo superiore destro. I dati di combattimento usano un sans di sistema più netto,
mentre i titoli e il grande numero rosso del round mantengono il serif dell’hotel. Sfumature scure dietro le risorse
ne sostengono il contrasto anche sui pavimenti chiari. Vita e scudo hanno valori numerici; la vita critica rispetta
anche il massimo aumentato dal perk. La soglia del caricatore critico è il 20% della capacità dell’arma, incluse le
armi consacrate. Avvisi distinti per ricarica, caricatore vuoto e munizioni esaurite.

La verifica di presentazione include tutte le armi consacrate con due slot pieni, quattro perk, round animato,
punteggio a sette cifre, vita critica durante la rigenerazione e ricarica reale. Vedere `docs/quality-audit.md` per gli
esiti sulle risoluzioni verificate. Restano da valutare leggibilità durante combattimenti affollati e dispositivi fisici.

## Quarta passata: dardi, contatti e provenienza del danno

Sostituite le sfere con un nucleo sottile, alone morbido e scia rastremata. La carica sul bastone usa un sigillo
sfaccettato. I contatti di bastone e dardo producono scintille e pulviscolo brevi al posto del disco pieno che si
espandeva. I dardi e gli effetti del mondo rimangono visibili anche nella camera delle ottiche.

Fino a quattro archi attorno al mirino indicano da dove arriva il danno, seguono la rotazione dello sguardo e
svaniscono in 0,85 s; l’immunità non produce segnali falsi. La provenienza passa anche dal server al client.
Il dardo verifica il tratto intero percorso a ogni aggiornamento contro pareti e capsule dei giocatori, sceglie il
primo contatto e infligge danno una volta sola; la copia grafica non causa danno. Accovacciarsi riduce il bersaglio.

## Quinta passata: corpo a corpo degli umanoidi

Nuova animazione di 38 fotogrammi a 30 fps: caricamento laterale, affondo a quota torace al fotogramma 18,
recupero. Monaco, Abate, Maschera, Direttore, Infermiere e Primario usano lo stesso scheletro; l’Abate mantiene
la velocità ridotta a 0,85. L’incantesimo ha ora uno stato distinto, senza usare l’affondo della mischia.

Durante il gesto il nemico mantiene la direzione scelta. Il danno al contatto richiede ancora bersaglio vivo,
settore frontale, distanza, quota e linea libera. Arretrare o spostarsi alle spalle evita il colpo; una parete
sopraggiunta lo blocca. Il bersaglio non cambia durante il gesto, e la sua uscita annulla il contatto. Il recupero
non infligge altri danni. Fruscio dell’arma durante l’affondo, suono e particelle di contatto solo se il colpo arriva.

Verifica nel gioco: **62/62**, comprese le sei clip, altezza delle mesh animate, fase dell’affondo, movimento reale
del controller e contatto annullato. Pose frontali e laterali in `unity_logs/melee`. Cooperativa: **16/16 ospite,
28/28 client**, inclusa la visione della preparazione della nuova clip sul client.

## Sesta passata: torcia e costumi leggibili

Il fascio misura anche i nemici e le superfici ai lati del centro; attenua immediatamente luce e alone da vicino,
allarga il cono e recupera gradualmente la portata quando si guarda lontano. Il pavimento sfiorato non oscura
un corridoio libero. La torcia del compagno usa lo stesso adattamento e resta margine per due fasci sovrapposti.
Ribilanciato il colore diffuso dei tessuti del monaco, preservando texture, rugosità e dettaglio delle superfici.
La luminosità salvata dal giocatore resta invariata; la percezione dei nemici e il protocollo di rete non cambiano.

Verifica dei pixel renderizzati: **53/53**, a 0,9, 1,8 e 8 m, fuori asse e con torce cooperative. Confronto con
il bianco della stessa inquadratura, per rilevare la saturazione anche dopo la colorazione cinematografica.
Le prove della mischia passano **62/62 in ciascuno dei tre temi**, con immagini delle fasi frontali e laterali.

## Settima passata: effigie votiva e tempi della replica

La Veglia perde il corpo cilindrico e il velo a cono. Ora è una maschera di cera scavata attorno a un’unica
orbita, con colature sotto il mento, aureola in ottone brunito, tre ceri asimmetrici e un velo funebre a pieghe
morbide. Le fiamme hanno involucro ambrato e nucleo chiaro, oscillano e si spengono alla morte. Durante la
preparazione il corpo si inclina, l’iride si allarga e le fiamme si allungano; il collider del punto debole segue
la maschera animata. Non vengono aggiunte luci dinamiche per ogni creatura.

Corretti anche i tempi della mischia sul client: la preparazione veniva consumata due volte per fotogramma,
mentre il blocco del recupero non scadeva. La verifica distingue ora preparazione, contatto e fine del recupero.

## Ottava passata: affondo dei volanti

Occhio, Veglia e Medusa preparano per 0,55 s, scattano per 0,16 s nella direzione scelta all’inizio e recuperano
fino a 1,13 s. Durante il gesto non inseguono né ruotano verso il bersaglio. Il corpo avanza fino a 85 cm,
con verifica continua degli ostacoli; da molto vicino la corsa si accorcia per evitare di attraversare il giocatore.
Tra gli attacchi mantengono uno spazio di avvicinamento. Il danno al contatto richiede ancora il bersaglio
impegnato, vivo, nel settore frontale, entro 1,2 m in 3D e senza pareti. Schivare di lato, arretrare o passare
alle spalle può evitare il colpo; l’uscita del bersaglio annulla il contatto senza assegnarlo a un altro giocatore.

La Veglia passa dalla posa arretrata alla spinta in avanti e poi alla distensione; la Medusa contrae la campana
e raccoglie i tentacoli. L’Occhio mantiene il suo gesto delle palpebre con lo scatto fisico. Impatto e suono
solo sul colpo arrivato; le particelle nascono sulla superficie alta del bersaglio, visibile dalla prima persona,
anche per la mischia degli umanoidi. Le copie di rete mostrano gesto e contatto senza infliggere danno.

## Nona passata: densità, atri e pressione dell’orda

Moduli da 14,4–19,2 m e almeno un rettangolo di quattro celle previsto durante la crescita della pianta. Le sale
ampie possono avere tre quote: terreno, 3,6 m e 7,2 m, con soffitto da 11,6 m. Due U contrapposte, quattro
scale e corsia libera al livello intermedio consentono salita, attraversamento e discesa; parapetti bassi forniscono
riparo al livello alto. Il grande atrio viene preferito come prima arena della candela, anziché restare solo di passaggio.
La Rotonda occupa un’altra sala, anche ovale. Colonne, sedute e arredi del tema crescono con l’area; lampadario
e punti luce sostengono la lettura delle quote, con un riferimento centrale nel Giardino d’inverno.

Più pattuglie nelle stanze di guardia e ondate proporzionate anche all’area, fino a 24 vivi nelle grandi arene
(28 nella Processione). Primo ingresso a gruppi di quattro, poi rinforzi più frequenti; parte delle nascite usa le
balconate. I nemici ordinari cercano la mischia, i dardi restano al Direttore. Passo fisico fino a 36 cm insieme
all’affondo degli umanoidi, limitato da ostacoli e spazio davanti al bersaglio. Rimosso l’anello di arrivo dei nemici
e sostituiti gli archi dell’HUD con segni direzionali compatti.

Le creature procedurali condividono tre sagome preparate nel caricamento; materiali che lampeggiano o si
spengono restano indipendenti per creatura. La cartella di avvio confermata dall’utente è `Builds/Prova`:
la copia della build deve aggiornare quella cartella, oltre alla principale quando possibile.

Restano aperti: rifinitura ulteriore dei gesti, costo di rendering e ombre degli umanoidi nelle orde, verifica del
bilanciamento durante partite prolungate e gli altri ambiti della revisione generale.

## Decima passata: orde leggere

Il round porta fino a 24 umanoidi vivi, e ognuno ha 110–170 mila vertici: con tutto il costo pieno il fotogramma
arrivava a 113 ms. Fanno ombra solo i corpi più vicini (budget di peso 4,5 entro 14 m), il dettaglio scende con la
distanza e con la folla e, da lontano, la pelle elabora mesh compatte preparate dall'editor invece dei livelli di
Unity (che accorciano solo gli indici). Con 24 umanoidi il fotogramma costa 9,4 ms (stanza vuota 3,2 ms); da vicino
il corpo resta intero, quindi il gioco non cambia: cambia quanti nemici si possono permettere. Dettagli e misure in
`docs/quality-audit.md`.

## Undicesima passata: ripari nelle sale

Le sale erano diventate grandi ma erano ancora vuote: il censimento (`-roomcensus`, 90 piani) dava, nelle 585 sale
sopra i 150 m² (esclusi corridoi e stanze con impianto proprio), arredi solidi su appena il 3,7% del pavimento (3,1% nelle
stanze di guardia, 4,7% nelle arene) e più di metà delle Gallerie sotto il 3%. Nelle sale con balconata (circa tre su quattro) la causa era la riserva a terra: tutta la
fascia sotto i solai, 4,2 m per lato (6,8 nell'atrio) più la testata, cioè quasi tutto il pavimento.

Ora a terra restano riservate solo le scale (da x0+2 per 7,2 m) e le colonne dei solai; sotto i solai, a 3,6 m, restano
3,3 m liberi e ci stanno ripari bassi. Ogni sala sopra i 150 m² (arene, boss, guardie) riceve dopo le altre ricette
file di sedie o panche, letti, divani, banconi, armadi, paraventi, sarcofagi: pezzi del tema, soli o in fila di tre come
una barricata, sempre a 1,3 m dagli altri arredi e a 1,2 m dai muri, quindi con corsie larghe quanto servono alla
navmesh e alla capsula del giocatore. L'obiettivo per tipo di sala è 11% (arena), 10% (boss), 9% (guardia). Si arriva
al 7,0% nella media (sale basse 7,7%, con balconata 6,3%, atri a tre quote 7,8%): più o meno il doppio di prima, con
15 pezzi in più a sala (nessuna Galleria resta più vuota). Le file danno qualcosa dietro cui girare mentre l'orda
insegue; l'effetto sul bilanciamento non è stato provato a mano, solo con i bot e le suite automatiche.

Il generatore cambia (nuove sale, nuovi arredi), quindi la cooperativa passa al protocollo `pe-coop-12`: ospite e
ospitati devono avere la stessa versione. `-nocover` rende il generatore quello di prima (per confronti a parità di
seme).

## Dodicesima passata: contatto fisico

Bastone, strumenti e attacchi a terra lasciano un breve impatto opaco di polvere e piccoli frammenti, invece delle
scintille additive degli incantesimi. Il gesto, il suono di contatto e il segnale direzionale restano sincronizzati
al danno; un colpo schivato non produce l'impatto. I volanti mantengono il feedback magico. Nessun cambio alle regole
o al protocollo di rete. Restano da rifinire il gesto e la presentazione del nemico durante il combattimento ravvicinato.

## Quattordicesima passata: arma nella mano

Corretto l'affondo degli umanoidi: il bastone superava la portata del braccio e si staccava dalla mano durante il colpo.
La presa ora resta raggiungibile per tutta la traiettoria, anche tra i fotogrammi, con controllo nel generatore e sui
sei costumi del gioco. Restano gli stessi tempi, rotazioni e possibilità di schivata. Ritmo e varietà degli attacchi
richiedono ancora una valutazione durante le orde.

## Quindicesima passata: folla e balconate

Il corpo del giocatore ora entra nell'aggiramento degli agenti: la folla non deve sovrapporglisi e lasciare al
controller il compito di espellerlo. Vale anche per i compagni vivi nello stesso piano e si adatta all'accovacciamento.
I punti deboli animati servono ai colpi, senza spingere il giocatore; i corpi a terra continuano a bloccarne il passaggio.
La navmesh rimane intatta. Protocollo cooperativo `pe-coop-13`.

## Sedicesima passata: rifornimenti nelle arene

Ogni arena della pianta a griglia ha ora una TARANTOLA a muro: 500 punti per l'arma e 250 per rifornirla, secondo
le tariffe esistenti. Partenza e negozio conservano le proprie scelte. A serrande chiuse resta quindi una risorsa
raggiungibile senza dipendere dai potenziamenti casuali o dover aspettare il termine del round. Nessun rifornimento
gratuito: servono i punti guadagnati combattendo. Protocollo cooperativo `pe-coop-14`, con espositori inclusi nella
firma del piano.

## Diciassettesima passata: ciclo a ondate

Conversione chiusa. `SurvivalMode` (`Proc/SurvivalMode.cs`) è il ciclo predefinito; il ciclo a candele e lo stealth restano con
`-candles` (o `-legacycycle`, `-oldplan`) e con i flag delle prove che li verificano (`-stealthtest`, `-roundtest`, `-roundshots`,
`-knifetest`, `-droptest`, `-architecturesupply`). `-zombies` forza il nuovo; `-nowaves` ferma le ondate (le ferma anche `-noenemies`
e `-cooptest`, che le fa partire dal copione).

- **Aree a pagamento.** Pianta a griglia di circa 8 aree principali (media 8,0 su 1000 piani, 925 m² l'una, spesso con atrio a tre quote)
  più i due ascensori. Tutte chiuse da una serranda tranne la Partenza; `F` davanti alla porta compra l'area dietro: 500 punti la prima,
  250 in più per ogni area di distanza (al massimo 1.500). L'acquisto vale per tutti e per il piano; due porte fra le stesse aree si
  aprono insieme, gli ascensori con la loro area. Sui 1000 piani: al massimo 13 porte e 12 coppie di stanze su 29 aree della navmesh; l'ascensore di
  uscita è a 3,5 porte in media (massimo 8) e costa 2.908 punti per arrivarci (massimo 9.500).
- **Ondate.** Partono da sole: la prima 8 s dopo l'arrivo, le altre 10 s dopo la fine della precedente. Sedici nemici più quattro per
  ondata (più otto per ogni giocatore oltre il primo), da 20 a 100, con 12 più uno per ondata (più tre a giocatore) vivi insieme, da 14 a 28.
  Nascono solo nelle aree aperte, a 7–28 m da chi gioca e raggiungibili a piedi in pochi secondi, sempre all'attacco e più veloci (×1,6, i
  volanti ×1,15). A ondata finita 100 punti più 20 per ondata, per tutti; ogni quinta è la Processione dei Lumi. Tre ondate aprono l'ascensore
  di uscita, ma si può restare a combattere e comprare. Se muore chi gioca (in cooperativa: tutti) l'ondata salta senza premio e il numero torna
  indietro. Un'ondata ferma da 120 s si chiude da sola.
- **Fuori lo stealth.** Nessuna allerta, rilevamento legato alla luce, bonus Fantasma o presa alle spalle (il coltello è un colpo ravvicinato,
  130 punti se uccide); Velo e Passi di cenere non si offrono più nella Biblioteca. La TARANTOLA a muro della Partenza e le armi dei
  negozi sono il rifornimento.
- **Cooperativa.** Protocollo `pe-coop-15`: `BuyRoom` / `RoomReply` / `RoomsState`; compra chi sta davanti alla porta, l'ospite convalida
  (epoca, area di partenza collegata, prezzo, a non più di 4,5 m) e il client rimette i punti messi da parte se la richiesta cade. Chi entra
  tardi riceve aree comprate e stato dell'ondata.
- **Difetto trovato dalla camminata rigorosa.** La stanza Segreta si apre da un passaggio basso (architrave a 1,3 m) e il punto che si
  guardava per comprare la sua porta era proprio a 1,3 m: la vista sbatteva nel muro e quell'area non si poteva comprare, né da chi gioca né dal bot.
  Ora il punto sta sempre dentro l'apertura (`PaidRoomDoor.Point`) e `-architectureaccess` controlla che si veda da entrambi i lati di tutte le porte.
- Aggiornati menu (comandi e guida), obiettivi, guida con `V` (porte da comprare), `README.md` e `docs/multiplayer/README.md`.

Prove e numeri nel dettaglio: `docs/quality-audit.md`, «Diciassettesima passata».

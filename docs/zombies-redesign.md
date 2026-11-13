# Revisione richiesta: ritmo da CoD Zombies

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

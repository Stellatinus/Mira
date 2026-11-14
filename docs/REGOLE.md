# Regole di Mira

Come Mira conta, unisce e mostra le cose. Serve a leggere i numeri nel modo giusto e a tenere le scelte coerenti: se cambia una regola, si cambia qui e nel codice.

## Più account

Mira legge tutti gli account collegati e ne unisce i dati in un'unica vista. Gli avatar in basso a sinistra servono a restringere la vista:

- **Tutti gli account** (il pulsante con le due persone): si vede tutto, e sulle card compaiono gli avatar degli account coinvolti.
- **Un account** (clic sul suo avatar): elenco, avatar, stelle e conteggi riguardano solo quell'account.

## Repository e preferiti

1. **Una card per repository**, anche se lo vedono più account o se è anche tra i preferiti. Il repository si riconosce dal nome completo (`proprietario/nome`, senza distinguere maiuscole).
2. **Gli avatar sulla card** sono gli account collegati che lo vedono: o perché sono proprietari, collaboratori o membri dell'organizzazione, o perché gli hanno messo la stella. Con un solo account, o con un account scelto, gli avatar non compaiono.
3. **Preferito**: un repository è preferito se **almeno uno degli account in vista** gli ha messo la stella. La card ha una stella (senza numero) e il suo suggerimento dice quali account; il pannello dei dettagli lo ripete accanto alle stelle.
4. **Cosa c'è nell'elenco**: di base solo i repository «propri» (di cui un account è proprietario, collaboratore o membro dell'organizzazione). Il pulsante **«Mostra preferiti»** (spento di default, si ricorda) aggiunge anche i preferiti che non sono tra i propri. Scegliendo nel filtro «Preferiti → Solo preferiti» i preferiti si vedono comunque.
5. **Con un account scelto** la regola 3 vale per quell'account: se `saturn-codexcircle` ha messo la stella a `Stellatinus/Regulus` e `SugoGangotti` no, la stella compare scegliendo `saturn-codexcircle` o «Tutti», non scegliendo `SugoGangotti`.

Esempio: `Stellatinus/Regulus` è visto da `SugoGangotti` e da `saturn-codexcircle`, e ha la stella di `saturn-codexcircle`. In «Tutti» è una card con due avatar e una stella (suggerimento: «Preferito di saturn-codexcircle»).

## I numeri sui repository

- **PR e issue aperte** (indicatori blu e arancio): quante ce ne sono di aperte nel repository secondo GitHub, di chiunque, non solo tue.
- **Pieno o solo contorno**: l'indicatore è pieno se tra quelle aperte ce n'è almeno una che aspetta te (review richiesta, assegnata a te, menzione, notifica non letta), su tutti gli account; altrimenti ha solo il contorno.
- **«tue»** nel pannello dei dettagli («31 aperte (3 tue)»): quelle **assegnate a te**, non quelle aperte da te.
- **«non assegnate»**: issue aperte senza assegnatario, su tutto il repository (numero esatto di GitHub).
- **Filtri e ordinamenti** usano gli stessi numeri degli indicatori.

## La vista «Issue»

A differenza della dashboard, che si ferma alle 100 voci più recenti per sezione, queste due viste **scaricano tutte le pagine dei risultati** quando si aprono (la ricerca di GitHub arriva a 1000 risultati) e le tengono in cache per qualche minuto.

- **Issue**: **tutte** le issue aperte nei repository dei tuoi account e delle organizzazioni a cui appartengono (di chiunque siano e comunque assegnate), più quelle aperte da te o assegnate a te in repository di altri; senza doppioni, su tutti gli account in vista.
- Il filtro **«Assegnazione»** divide l'insieme senza resto: **assegnate a te** (a un account in vista) + **ad altri** (con assegnatari, nessuno dei quali sei tu) + **non assegnate** = **tutte**. Di default si apre su «Assegnate a te»; «Azzera i filtri» mostra tutte. Il filtro **«Assegnatario»** sceglie una persona precisa.
- Non esistono più le voci «Assegnate» e «Non assegnate» nella barra laterale: sono filtri di «Issue». Nella Home resta la sezione «Assegnate a te» (issue e PR).

## Visto e rimandato

Un elemento (issue, PR, notifica, menzione) si può **segnare come visto** o **rimandare a domani** dal pannello dei dettagli, senza toccare GitHub. I repository non si segnano.

- L'elemento si riconosce dal suo **indirizzo**: lo stesso elemento in più sezioni (per esempio «Review richieste» e «Assegnate a te») o visto da più account si nasconde ovunque insieme.
- **Visto:** resta nascosto finché non cambia. Se GitHub lo segna come aggiornato dopo l'ultima volta che l'hai visto (nuovo commento, nuovo commit…), **riappare da solo**.
- **Rimandato:** nascosto fino alle **8 del mattino del giorno dopo** (ora del tuo computer); se cambia prima, riappare comunque.
- I nascosti funzionano come un filtro: i conteggi diventano «14 di 36» e i totali di GitHub non valgono più. Il pulsante **«Mostra già visti»** (compare solo se ce ne sono, si ricorda tra una sessione e l'altra) li mostra con l'etichetta «Visto» o «Rimandato»; dal pannello si può farli tornare da vedere.
- Lo stato sta nel file `seen.json` della cartella di configurazione, accanto ai token ma **non cifrato** (contiene solo indirizzi e date). Le voci non toccate da 180 giorni si cancellano da sole.

## Limiti da conoscere

- Nella dashboard, per ogni sezione e account, si scaricano al massimo le **100** voci più recenti (ricerca e liste di GitHub); la vista «Issue» le scarica tutte, fino ai 1000 risultati della ricerca. Il totale vero si vede dove GitHub lo dà («100 di 213»). Chi supera i 100 può vedere un «tue» o un «aspettano te» più basso del vero.
- La ricerca di GitHub concede 30 richieste al minuto: se si aggiorna troppo spesso compare «limite raggiunto» e passa da solo.
- I filtri dei repository valgono per la vista Repo; quelli di issue, PR e notifiche per la vista in cui sono scelti e si azzerano cambiando vista.

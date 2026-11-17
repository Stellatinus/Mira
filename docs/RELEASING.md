# Come si fa una release

## Il numero di versione

Mira usa tre numeri, **MAJOR.MINOR.PATCH** (versionamento semantico). La prima release è la **1.0.0**.

| Cambia | Quando | Esempio |
|---|---|---|
| **PATCH** (la terza cifra) | correzioni che non cambiano come si usa Mira | 1.0.0 → 1.0.1 |
| **MINOR** (la seconda) | funzioni nuove che non rompono nulla | 1.0.1 → 1.1.0 |
| **MAJOR** (la prima) | cambiamenti incompatibili: chi aggiorna deve intervenire (per esempio cambia il formato dei token salvati o delle impostazioni) | 1.1.0 → 2.0.0 |

Quando sale una cifra, quelle a destra tornano a zero. Una versione di prova ha un trattino (`1.1.0-rc.1`) e sulla pagina delle release compare come **pre-release**.

Il **tag** è la versione preceduta da una `v`: `v1.0.0`.

## Fare una release

Il tag è l'etichetta che dice «questa è la versione 1.0.0»; quando viene creato, un workflow fa tutto il resto. Prima: `main` deve avere la CI verde.

**Da riga di comando** (da `main` aggiornato):

```bash
git checkout main && git pull
git tag v1.0.0
git push origin v1.0.0
```

**Dal sito di GitHub:** Releases → *Draft a new release* → in *Choose a tag* scrivi `v1.0.0` e scegli *Create new tag* (su `main`) → *Publish release*. Il workflow riconosce che la release esiste già e ci allega i pacchetti.

## Cosa fa il workflow

[`.github/workflows/release.yml`](../.github/workflows/release.yml), per ogni tag `v*.*.*`:

1. controlla che il tag sia nel formato giusto;
2. compila il frontend e pubblica il server come **programma autonomo** (non serve installare .NET) per **Windows** (`win-x64`, `.zip`), **Linux** (`linux-x64`, `.tar.gz`) e **macOS** su Apple Silicon (`osx-arm64`, `.tar.gz`); la versione dentro il programma è quella del tag;
3. **prova il pacchetto**: lo avvia da un'altra cartella, controlla che risponda con la versione giusta e che serva il frontend;
   In parallelo, un secondo job compila **Mira Desktop** per Windows (`Mira-Desktop-<versione>-win-x64.zip`, l'app con finestra e tray del server dentro, vedi #16), la avvia e controlla `/api/version` e `/api/desktop`;
4. crea la release con i pacchetti (i tre del server e quello desktop), un file `SHA256SUMS.txt` con le somme di controllo e le **note generate dalle PR fuse** dall'ultima release;
5. **firma** ogni pacchetto e `SHA256SUMS.txt` con Sigstore (vedi sotto) e **verifica le firme prima di pubblicare**: se la verifica fallisce non esce nessuna release.

Il pacchetto desktop contiene anche `Mira.Updater.exe`, il programma di appoggio per l'aggiornamento dall'interno (vedi la sezione più sotto).

macOS è compreso solo per **Apple Silicon** (non Intel) e solo nei pacchetti: la CI di compilazione resta su Linux e Windows. I programmi **non sono firmati come eseguibili** (nessun certificato di firma del codice): Windows può mostrare un avviso di SmartScreen la prima volta; su macOS il primo avvio è bloccato finché non si toglie la quarantena con `xattr -d com.apple.quarantine Mira.Server`. I **file scaricabili** sono invece firmati con Sigstore, come spiegato qui sotto.

La versione è visibile nell'app, in **Impostazioni → Informazioni**; compilando dal sorgente compare `0.0.0-dev`, per distinguerla da una release.

## Verificare un pacchetto

I pacchetti e `SHA256SUMS.txt` sono firmati con **Sigstore «keyless»**: non c'è una chiave da custodire (o da perdere). La firma dice che il file è uscito dal workflow `release.yml` di questo repository, a partire da un tag `v*.*.*`, ed è registrata in un registro pubblico. Accanto a ogni file c'è il suo bundle `<file>.sigstore.json`.

Serve [cosign](https://docs.sigstore.dev/cosign/system_config/installation/) (versione 2). Da una cartella con il pacchetto e il suo `.sigstore.json`:

```bash
cosign verify-blob Mira-1.0.0-linux-x64.tar.gz \
  --bundle Mira-1.0.0-linux-x64.tar.gz.sigstore.json \
  --certificate-identity-regexp '^https://github.com/Stellatinus/Mira/\.github/workflows/release\.yml@refs/tags/v' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

Se risponde `Verified OK` il file è quello pubblicato dal workflow. Poi, per un download corrotto, resta valido il controllo con `SHA256SUMS.txt` (anch'esso firmato).

**Cosa protegge:** da un file sostituito o manomesso dopo la pubblicazione e da un pacchetto che non viene dal workflow del repository. **Cosa non protegge:** da una release creata dal workflow dopo una compromissione del repository o di chi può creare tag (la firma attesta *da dove* esce il file, non che il codice sia buono); e Windows non riconosce questa firma: SmartScreen avvisa comunque, perché non è un certificato di firma del codice.

## Aggiornamento dall'interno (app desktop)

L'app desktop per Windows può aggiornarsi da sola, **solo quando l'utente lo sceglie** (pulsante «Aggiorna» nelle Impostazioni, con conferma) e **solo verso release stabili**. L'avviso di nuova versione si può anche attivare all'avvio, ma è spento di base e non installa mai niente.

### Come funziona

1. Il server (non il browser) rilegge l'ultima release da GitHub e accetta solo la versione che l'avviso ha indicato: stabile (niente bozze né `-rc`), più nuova di quella in uso, con i file nel repository `Stellatinus/Mira` (https, `github.com`).
2. Scarica `SHA256SUMS.txt` e il pacchetto `Mira-Desktop-<versione>-win-x64.zip` in una cartella temporanea e **verifica la somma SHA-256**. Se non torna, si ferma prima di estrarre qualunque cosa.
3. Estrae il pacchetto accanto all'installazione (`<cartella>.update`), senza toccare quella in uso.
4. Lancia **`Mira.Updater.exe`** da una copia in una cartella temporanea (non può stare dentro la cartella che sta per spostare) e chiude Mira.
5. Il programma di appoggio aspetta la chiusura (60 secondi al massimo: se Mira non si chiude non cambia niente), **sposta** la cartella vecchia in `<cartella>.previous` (non la copia né la cancella; ne tiene una sola), sposta la nuova al suo posto e **riavvia Mira**.
6. Se `/api/version` non risponde con la versione attesa entro 45 secondi (o Mira si chiude subito), ferma la nuova, la toglie e **rimette la vecchia**, che riavvia. L'esito (`update-result.json`) e il registro (`update.log`) stanno nella cartella dei dati, e Mira lo dice al prossimo avvio.
7. I dati (`%APPDATA%\Mira`: token cifrati, `config.json`, `settings.json`…) stanno fuori dalla cartella del programma e non si toccano mai.

Il programma di appoggio è il progetto [`src/Mira.Updater`](../src/Mira.Updater), pubblicato come file unico autonomo (~11 MB) e messo nel pacchetto desktop dal workflow.

### Dove si può aggiornare

La sostituzione sposta la **cartella** di Mira, quindi serve poter scrivere nella cartella che la contiene. Per non chiedere mai i permessi di amministratore, l'installazione prevista è per utente (`%LOCALAPPDATA%\Mira`). Se la cartella non è scrivibile (per esempio `Program Files`), se Mira non gira da un pacchetto installato (compilata dal sorgente) o se manca il programma di appoggio, **«Aggiorna» rifiuta e spiega perché**: l'aggiornamento a mano resta possibile.

### Cosa protegge la verifica, e cosa no

| | |
|---|---|
| **Protegge** | da un pacchetto corrotto o troncato nel download; da un pacchetto di un'altra versione o di un altro sistema; da file di una release che non stanno nel repository di Mira. |
| **Non protegge** | da una release compromessa: `SHA256SUMS.txt` sta sulla stessa pagina del pacchetto, quindi chi può cambiare il pacchetto può cambiare anche la somma. |
| **Firma Sigstore** | **Mira non la verifica.** I pacchetti sono firmati (vedi sopra) e la firma è controllabile a mano con `cosign`, ma da .NET, senza `cosign`, una verifica seria richiederebbe di incorporare le radici di fiducia di Sigstore (certificato di Fulcio e chiave di Rekor), che ruotano e andrebbero tenute aggiornate dentro Mira: una verifica «a metà» darebbe una falsa sicurezza. Resta un'idea per dopo. |
| **SmartScreen** | gli eseguibili non hanno una firma del codice: Windows può mostrare l'avviso anche per la versione aggiornata. |

### Provarlo

- **Test automatici** (`tests/Mira.Server.Tests`, senza rete): scelta del pacchetto, somma, rifiuto in cartella non scrivibile, estrazione e passaggio al programma di appoggio, sostituzione e ripristino con cartelle vere e processi finti.
- **Prova con processi veri su Windows**: [`tests/update-e2e/prova-aggiornamento.ps1`](../tests/update-e2e/prova-aggiornamento.ps1) pubblica due versioni (0.0.1 e 0.0.2) e il programma di appoggio, aggiorna da una all'altra e prova il ripristino quando la nuova non risponde. Gira nella CI (job «Aggiornamento (Windows)») e in locale con `pwsh tests/update-e2e/prova-aggiornamento.ps1` (con `-Desktop` usa l'app desktop vera).
- Per provare l'aggiornamento tra due release vere, le due release devono esistere: l'aggiornamento 1.3.1 → 1.3.2 è la prova a mano prevista.

## Provare il workflow senza fare una release

Una pull request che modifica `release.yml` o `Mira.Server.csproj` esegue la compilazione e la prova dei pacchetti su Linux e Windows, **senza** creare nessuna release.

## Se qualcosa va storto

- Il workflow fallisce prima di creare la release: correggi, poi cancella il tag e ricrealo: `git push origin :refs/tags/v1.0.0`, poi di nuovo `git tag -f v1.0.0 && git push origin v1.0.0`.
- La release è uscita ma è sbagliata: `gh release delete v1.0.0 --cleanup-tag --yes`, correggi e rifai la release. Se qualcuno l'ha già scaricata, meglio pubblicare una versione nuova (1.0.1) che riusare il numero.

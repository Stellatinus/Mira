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
2. compila il frontend e pubblica il server come **programma autonomo** (non serve installare .NET) per **Windows** (`win-x64`, `.zip`) e **Linux** (`linux-x64`, `.tar.gz`); la versione dentro il programma è quella del tag;
3. **prova il pacchetto**: lo avvia da un'altra cartella, controlla che risponda con la versione giusta e che serva il frontend;
4. crea la release con i due pacchetti, un file `SHA256SUMS.txt` con le somme di controllo e le **note generate dalle PR fuse** dall'ultima release;
5. **firma** ogni pacchetto e `SHA256SUMS.txt` con Sigstore (vedi sotto) e **verifica le firme prima di pubblicare**: se la verifica fallisce non esce nessuna release.

macOS non è compreso (come nella CI). I programmi **non sono firmati come eseguibili** (nessun certificato di firma del codice): Windows può mostrare un avviso di SmartScreen la prima volta. I **file scaricabili** sono invece firmati con Sigstore, come spiegato qui sotto.

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

## Provare il workflow senza fare una release

Una pull request che modifica `release.yml` o `Mira.Server.csproj` esegue la compilazione e la prova dei pacchetti su Linux e Windows, **senza** creare nessuna release.

## Se qualcosa va storto

- Il workflow fallisce prima di creare la release: correggi, poi cancella il tag e ricrealo: `git push origin :refs/tags/v1.0.0`, poi di nuovo `git tag -f v1.0.0 && git push origin v1.0.0`.
- La release è uscita ma è sbagliata: `gh release delete v1.0.0 --cleanup-tag --yes`, correggi e rifai la release. Se qualcuno l'ha già scaricata, meglio pubblicare una versione nuova (1.0.1) che riusare il numero.

# Contribuire a Mira

Grazie! Il progetto è in italiano (codice commentato, issue, PR e documentazione), ma le PR in inglese sono benvenute.

## Il flusso

1. **Parti da una issue** (o aprine una) e crea un branch da `main`: `feat/...` per una funzione, `fix/...` per un errore.
2. **Una PR per ogni modifica**, piccola e con un solo scopo. Scrivi nella descrizione `Closes #numero` per chiudere la issue al merge.
3. **La CI deve essere verde** prima del merge (vedi sotto).
4. **Si fonde con lo squash**: un solo commit per PR su `main`, con il titolo della PR.

La descrizione della PR ha un modello (`Cosa cambia`, `Dove guardare`, `Verificato`, `Da decidere`): compilarlo aiuta chi approva a sapere dove guardare.

## Prima del push: gli stessi controlli della CI

Servono [Node.js](https://nodejs.org) 24 e il [.NET SDK](https://dotnet.microsoft.com/download) 10.

```bash
# frontend: dipendenze, controllo dei tipi e compilazione
cd web
npm ci
npm run build

# backend: compilazione con gli avvisi trattati come errori
cd ..
dotnet build src/Mira.Server --configuration Release -warnaserror
```

Se questi due comandi passano, passa anche la CI.

## Cosa fa la CI

Il workflow [`.github/workflows/ci.yml`](.github/workflows/ci.yml) parte a ogni PR e a ogni push a `main`:

| Controllo | Dove gira | Cosa verifica |
|---|---|---|
| **Frontend** | Linux | `npm ci` e `npm run build` (compila TypeScript e Vite; un testo che manca in una lingua fa fallire la compilazione) |
| **Backend** | Linux e Windows | `dotnet build` con gli avvisi come errori |

**macOS non è controllato**, di proposito: per ora si controllano solo Linux e Windows. Per aggiungerlo si inserisce `macos-latest` nell'elenco `os` del job `backend`.

La CI non usa token di GitHub e non esegue il programma: controlla che il codice si compili. Prove vere con i dati si fanno in locale.

## Regole del progetto

- I testi dell'interfaccia stanno in `web/src/i18n`: ogni testo nuovo va scritto in **italiano e in inglese** (il compilatore avvisa se ne manca uno). Per aggiungere una lingua, vedi il README.
- Il backend non scrive frasi per l'utente: manda codici (`token-invalid`…) che il frontend traduce.
- Mai committare token, `master.key` o `tokens.enc`.

## Release

Le release non sono ancora automatiche: sono un passo successivo (vedi la issue sugli eseguibili per Windows, Linux e macOS).

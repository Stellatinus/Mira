# Mira su Android (.NET MAUI): studio di fattibilità

Stato: **studio**, nessun codice di produzione. Le prove stanno in [`spikes/android/`](../spikes/android), fuori dalla soluzione principale.

Domanda: il server di Mira può girare dentro un'app MAUI Android (modalità *standalone*: token e dati sul telefono)?

## Risposta breve

**Sì.** Raccomandazione: **opzione (b)**, cioè `HybridWebView` di MAUI con le chiamate `/api/*` smistate nel codice .NET dentro il processo, senza socket. Le classi di `Services`, `GitHub`, `Storage` e `Models` si riusano **senza modifiche**, e il frontend resta invariato grazie a un piccolo shim di `fetch` iniettato dalla shell Android (non da `web/src`).

Kestrel **non è distribuito per Android** (nessun runtime pack ASP.NET), ma con un trucco *funziona* davvero sul telefono: è il piano B se lo smistamento in-process dovesse rivelarsi troppo oneroso. Non lo consiglio come strada principale per i motivi sotto.

## Cosa è stato provato (e cosa no)

Ambiente: SDK 10.0.102, workload `maui` 10.0.100, Android SDK 36, build per `android-arm64` in Release. Prove a runtime su un telefono reale (Samsung SM-S937B, arm64) via debug wireless.

| Prova | Esito | Come |
|---|---|---|
| Kestrel con `FrameworkReference Microsoft.AspNetCore.App` su `android-arm64` | **Non compila** | `NETSDK1082`: non esiste un runtime pack di `Microsoft.AspNetCore.App` per `android-arm64`. Non esiste nemmeno per `linux-bionic-*` (il pacchetto NuGet dà 404). Per Android Microsoft non distribuisce il framework condiviso ASP.NET. |
| Kestrel aggirando il framework: DLL gestite prese dal pacchetto `Microsoft.AspNetCore.App.Runtime.linux-arm64` e referenziate a mano | **Compila e gira sul telefono** (non supportato ufficialmente) | Le DLL sono solo IL, senza librerie native. `spikes/android/kestrel/`: sul dispositivo `GET http://127.0.0.1:5080/api/version` risponde `ok` (serve il permesso `INTERNET`, anche per il bind del socket). |
| MAUI + `HybridWebView` + classi del server compilate così come sono | **Compila e gira sul telefono** | `spikes/android/maui/`: `Services`, `GitHub`, `Models`, `Storage` (compresa `EncryptedFileSecretStore`) inclusi con `<Compile Include>`; servono solo `Microsoft.Extensions.Http` e un `using Microsoft.Extensions.Logging` (nel Web SDK è implicito). |
| `HybridWebView.WebResourceRequested` (intercettazione di `fetch('/api/…')`) | **Funziona per GET/PUT/DELETE/POST, ma NON espone il body della richiesta** | Metodo e URL arrivano correttamente a .NET, il body no: è un limite di `shouldInterceptRequest` di Android. Quindi PUT/POST con JSON (aggiunta account, `seen`, `follow`) non sono instradabili così. |
| Shim `fetch` → `HybridWebView.InvokeDotNet('Api', [metodo, percorso, body])` iniettato nell'`index.html` della shell | **Funziona, body compreso** | Verificato con GET (anche con query), PUT e POST con JSON, DELETE: .NET riceve metodo, percorso e body (56 e 15 caratteri nelle prove) e il frontend riceve un `Response` normale. |
| `SecureStorage` (Keystore) | **Funziona** | Scrittura e rilettura sul telefono. |
| `HttpClient` verso `api.github.com` | **Funziona** | `GET /rate_limit` risponde 200 dal telefono. |
| `EncryptedFileSecretStore` e `DataDirectory.Resolve()` | **Funzionano** | La cartella dati è la privata dell'app: `/data/user/0/<id>/files/.config/Mira`. |
| Piccolo host HTTP alternativo (`HttpListener`/socket) | **Non prototipato** | Solo valutazione sulla carta (vedi sotto). |

**Non provato:** il frontend vero (`web/dist`) dentro la `HybridWebView`, i servizi reali di Mira dietro un dispatcher (le risposte degli spike sono finte), un APK con più ABI, il comportamento in background e dopo la sospensione del processo, e `HttpListener`. L'emulatore non c'era: tutto il resto è stato provato su un solo dispositivo (Android recente, arm64).

## Confronto delle tre opzioni

APK `android-arm64`, `dotnet publish -c Release` (trimming di default), un solo ABI. Un APK con più ABI pesa di più.

| | (a) Kestrel in-process | (b) HybridWebView + dispatch in-process | (c) host HTTP minimale |
|---|---|---|---|
| Funziona? | **Sì sul telefono** (spike); non supportato da Microsoft | **Sì sul telefono** (spike); API supportate e documentate | Non provato |
| APK | ≈ 25,4 MB (MAUI + DLL ASP.NET) | ≈ **18,4 MB** | ≈ 18,5 MB (stima: come (b)) |
| Riuso del codice server | Massimo: anche `Program.cs` e `Endpoints.cs` quasi invariati | `Services`/`GitHub`/`Storage`/`Models` (~2.300 righe su ~2.600) invariati; da riscrivere solo lo smistamento delle ~20 route (~200 righe) | Come (b), più un parser di route e di static file da scrivere |
| Frontend | Invariato (`fetch('/api')` su `127.0.0.1`; da verificare il traffico in chiaro verso localhost nella WebView) | Invariato nei sorgenti; la shell inietta lo shim di `fetch` | Invariato |
| Porta TCP aperta sul telefono | Sì: ogni app locale può raggiungere l'API | **No** | Sì |
| Manutenzione | Fragile: aggiornare ogni release di ASP.NET a mano | Bassa | Media: codice HTTP proprio |

### (a) Kestrel in-process: possibile, ma non consigliata (piano B)

Va detto onestamente: è l'opzione che riusa di più (anche `Program.cs` ed `Endpoints.cs`, parità garantita con il server desktop) e **funziona**. La sconsiglio per i costi seguenti, non perché non giri.

- Funziona solo grazie a un trucco: DLL del runtime Linux referenziate a mano. Nessun supporto, nessuna garanzia, e ogni aggiornamento di .NET richiede di rifare il giro.
- +7 MB di APK solo per ospitare un server che il telefono non ha motivo di avere.
- Apre una porta su `127.0.0.1` raggiungibile da qualunque altra app installata: l'API tiene i token GitHub, e il `LocalOnly` pensato per il DNS rebinding del browser non protegge da questo.
- In background Android sospende o termina il processo: un server socket sempre in ascolto non è un buon modello.
- Resterebbe come piano B solo se in futuro servisse davvero HTTP vero sul dispositivo.

### (b) HybridWebView con smistamento in-process: raccomandata

- API pubblica e supportata di MAUI, nessun trucco di build.
- I servizi non dipendono da ASP.NET (solo `IHttpClientFactory` e `ILogger`): l'unico codice «web» è `Endpoints.cs` (181 righe), `LocalOnly.cs` e `Program.cs`.
- Nessuna porta aperta, nessun problema di firewall o di ciclo di vita del processo.
- Evoluzione naturale: lo stesso smistamento può avere due implementazioni, «in-process» (standalone) e «`HttpClient` verso un server esterno» (modalità futura), con lo stesso contratto `/api/*`.
- Costo: bisogna riscrivere il routing a mano (con tabelle metodo + percorso → delegato) e tenerlo allineato a `Endpoints.cs`. Si mitiga estraendo i servizi in un progetto condiviso e testando il router con gli stessi test.
- **Il meccanismo da usare è lo shim di `fetch` con `InvokeDotNet`, non `WebResourceRequested`**: quest'ultimo non espone il body delle richieste (provato sul telefono), quindi non serve per PUT/POST con JSON. Lo shim (una decina di righe di JS, nell'`index.html` che la shell serve) sostituisce `window.fetch` solo per `/api/*` e restituisce un `Response` standard; il frontend non se ne accorge. Se in futuro si preferisse chiamare il bridge direttamente da `web/src/api.ts`, è una modifica di una riga, ma oggi non serve.
- Limiti da tenere a mente: i corpi e le risposte passano come stringhe attraverso il bridge (JSON, nessun problema per Mira); lo shim va coperto da test (anche solo uno scenario con body assente, `204 No Content` e errori `4xx/5xx`).

### (c) Host HTTP alternativo: scartata

Per esempio `HttpListener` o socket grezzi. Stesso problema di sicurezza della porta aperta, in più un server HTTP, un router e il serving dei file statici da scrivere e mantenere noi. Nessun vantaggio rispetto a (b).

## Segreti e dati su Android

- **Segreti**: `EncryptedFileSecretStore` compila su Android ma tiene la chiave `master.key` accanto al file cifrato nella stessa cartella: su un telefono non protegge quasi nulla. Serve un `ISecretStore` basato sul Keystore: la via più semplice è `SecureStorage` di MAUI Essentials (su Android usa il Keystore). Un'unica classe di ~40 righe; `LoadAllAsync` può tenere l'elenco dei nomi in un valore dedicato, perché `SecureStorage` non sa elencare le chiavi.
- **Dati** (`FollowStore`, `SeenStore`, `config.json`): `DataDirectory.Resolve()` su Android risolve già nella cartella privata dell'app (`/data/user/0/<id>/files/.config/Mira`, verificato sul telefono). In alternativa si imposta `MIRA_DATA_DIR` a `FileSystem.AppDataDirectory` prima di costruire i servizi.
- **Rete**: `HttpClient` verso `api.github.com` richiede solo il permesso `INTERNET` (già nel template).

## Cosa resta da fare (con stime)

1. ~~Verifica a runtime dello spike~~: **fatta** sul telefono (vedi sopra). Resta da provare il frontend vero dentro la `HybridWebView` e il comportamento in background. *0,5 giorno.*
2. **Progetto condiviso `Mira.Core`** (net10.0) con `Services`, `GitHub`, `Storage`, `Models`, `SemVer`; `Mira.Server` e `Mira.Android` lo referenziano; i test restano invariati. *1 giorno.*
3. **Dispatcher `/api/*`** in-process (tabella di route, deserializzazione JSON, codici di stato, `MiraException`) con test che confrontano le risposte con quelle degli `Endpoints`. *1,5–2 giorni.*
4. **Gusci MAUI**: `HybridWebView` che serve `web/dist` come asset (copia nella build di MAUI), shim di `fetch`, `SecureStorage`, icona e firma dell'APK. *1–1,5 giorni.*
5. **Pubblicazione**: APK firmato nelle release, con firma Sigstore come per il resto dei pacchetti (#92). Non toccata qui: la CI è fuori perimetro. *0,5–1 giorno.*
6. **Dopo** (non stimato qui): notifiche mobile (WorkManager/foreground service) e collegamento a un server esterno (seconda implementazione del dispatcher via `HttpClient` + autenticazione).

Stima complessiva per un APK standalone utilizzabile: **circa 5–7 giorni di lavoro**.

## Come riprodurre le prove

```bash
# (a) Kestrel: scarica prima le DLL (percorso atteso: %TEMP%\aspnetrt\runtimes\linux-arm64\lib\net10.0)
curl -L -o aspnetrt.zip https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.app.runtime.linux-arm64/10.0.2/microsoft.aspnetcore.app.runtime.linux-arm64.10.0.2.nupkg
#   poi estrarre in $TEMP/aspnetrt e:
dotnet publish spikes/android/kestrel -r android-arm64 -c Release -p:AndroidPackageFormat=apk

# (b) MAUI + HybridWebView
dotnet publish spikes/android/maui -f net10.0-android -r android-arm64 -c Release -p:AndroidPackageFormat=apk
# MAUI + Kestrel (per il confronto di dimensione)
dotnet publish spikes/android/maui -f net10.0-android -r android-arm64 -c Release -p:AndroidPackageFormat=apk -p:WithKestrel=true -p:DefineConstants=KESTREL
```

Gli spike sono volutamente fuori da `src` e `tests`, e la CI compila solo quelli (`dotnet build src/Mira.Server`).

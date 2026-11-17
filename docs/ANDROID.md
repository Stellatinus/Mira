# Mira su Android (.NET MAUI): studio di fattibilità

Stato: **studio**, nessun codice di produzione. Le prove stanno in [`spikes/android/`](../spikes/android), fuori dalla soluzione principale.

Domanda: il server di Mira può girare dentro un'app MAUI Android (modalità *standalone*: token e dati sul telefono)?

## Risposta breve

**Sì, ma non con Kestrel.** Raccomandazione: **opzione (b)**, cioè `HybridWebView` di MAUI con le chiamate `/api/*` intercettate e smistate nel codice .NET dentro il processo, senza socket. Il frontend resta quello attuale e le classi di `Services`, `GitHub`, `Storage` e `Models` si riusano **senza modificarle**.

## Cosa è stato provato (e cosa no)

Ambiente: SDK 10.0.102, workload `maui` 10.0.100, Android SDK 36, build per `android-arm64` in Release.

| Prova | Esito | Come |
|---|---|---|
| Kestrel con `FrameworkReference Microsoft.AspNetCore.App` su `android-arm64` | **Non compila** | `NETSDK1082`: non esiste un runtime pack di `Microsoft.AspNetCore.App` per `android-arm64`. Non esiste nemmeno per `linux-bionic-*` (il pacchetto NuGet dà 404). Per Android Microsoft non distribuisce il framework condiviso ASP.NET. |
| Kestrel aggirando il framework: DLL gestite prese dal pacchetto `Microsoft.AspNetCore.App.Runtime.linux-arm64` e referenziate a mano | **Compila e produce un APK** (non supportato ufficialmente) | Le DLL sono solo IL, senza librerie native, perciò il pacchetto si può impacchettare. `spikes/android/kestrel/` |
| MAUI + `HybridWebView` + classi del server compilate così come sono | **Compila e produce un APK** | `spikes/android/maui/`: `Services`, `GitHub`, `Models`, `Storage` (compresa `EncryptedFileSecretStore`) inclusi con `<Compile Include>`; servono solo `Microsoft.Extensions.Http` e un `using Microsoft.Extensions.Logging` (nel Web SDK è implicito). |
| `HybridWebView.WebResourceRequested` (intercettazione in-process di `fetch('/api/…')`) | **Il metodo esiste e compila** | `e.Handled = true; e.SetResponse(200, "OK", "application/json", stream)` |
| Piccolo host HTTP alternativo (`HttpListener`/socket) | **Non prototipato** | Solo valutazione sulla carta (vedi sotto). |

**Non ho potuto eseguire nulla su Android.** `adb devices` è vuoto, non c'è alcun AVD e non sono installate system image dell'emulatore. Quindi *nessuna* delle tre strade è stata avviata: so che compilano e quanto pesano, **non** che girino. In particolare restano da verificare a runtime: l'avvio di Kestrel su Bionic, la risposta di `WebResourceRequested` (corpo, header, metodi `PUT`/`DELETE`, `fetch` con body JSON), `HttpClient` verso `api.github.com` e `SecureStorage`.

## Confronto delle tre opzioni

APK `android-arm64`, `dotnet publish -c Release` (trimming di default), un solo ABI. Un APK con più ABI pesa di più.

| | (a) Kestrel in-process | (b) HybridWebView + dispatch in-process | (c) host HTTP minimale |
|---|---|---|---|
| Funziona? | Compila; **mai eseguito**; non supportato da Microsoft | Compila; **mai eseguito**; API supportate e documentate | Non provato |
| APK | ≈ 25,4 MB (MAUI + DLL ASP.NET) | ≈ **18,4 MB** | ≈ 18,5 MB (stima: come (b)) |
| Riuso del codice server | Massimo: anche `Program.cs` e `Endpoints.cs` quasi invariati | `Services`/`GitHub`/`Storage`/`Models` (~2.300 righe su ~2.600) invariati; da riscrivere solo lo smistamento delle ~20 route (~200 righe) | Come (b), più un parser di route e di static file da scrivere |
| Frontend | Invariato (`fetch('/api')` su `127.0.0.1`) | Invariato (intercettazione) | Invariato |
| Porta TCP aperta sul telefono | Sì: ogni app locale può raggiungere l'API | **No** | Sì |
| Manutenzione | Fragile: aggiornare ogni release di ASP.NET a mano | Bassa | Media: codice HTTP proprio |

### (a) Kestrel in-process: scartata

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
- Rischio da verificare per primo: che `WebResourceRequested` copra davvero tutti i metodi e i body del frontend (in particolare `PUT`/`DELETE`/`POST` con JSON); se avesse limiti, il piano B è il ponte `InvokeDotNet` con un piccolo shim JS al posto di `fetch` (una modifica a `web/src/api.ts`, oggi fuori perimetro).

### (c) Host HTTP alternativo: scartata

Per esempio `HttpListener` o socket grezzi. Stesso problema di sicurezza della porta aperta, in più un server HTTP, un router e il serving dei file statici da scrivere e mantenere noi. Nessun vantaggio rispetto a (b).

## Segreti e dati su Android

- **Segreti**: `EncryptedFileSecretStore` compila su Android ma tiene la chiave `master.key` accanto al file cifrato nella stessa cartella: su un telefono non protegge quasi nulla. Serve un `ISecretStore` basato sul Keystore: la via più semplice è `SecureStorage` di MAUI Essentials (su Android usa il Keystore). Un'unica classe di ~40 righe; `LoadAllAsync` può tenere l'elenco dei nomi in un valore dedicato, perché `SecureStorage` non sa elencare le chiavi.
- **Dati** (`FollowStore`, `SeenStore`, `config.json`): `DataDirectory.Resolve()` usa `ApplicationData`, che su Android punta già alla cartella privata dell'app (verificare). In alternativa si imposta `MIRA_DATA_DIR` a `FileSystem.AppDataDirectory` prima di costruire i servizi.
- **Rete**: `HttpClient` verso `api.github.com` richiede solo il permesso `INTERNET` (già nel template).

## Cosa resta da fare (con stime)

1. **Verifica a runtime** su emulatore (o telefono) dello spike (b): intercettazione di `GET`/`PUT`/`DELETE`/`POST` con JSON, `SecureStorage`, chiamata a GitHub. *0,5–1 giorno, serve una system image o un dispositivo.* È il passo che può cambiare la decisione: farlo prima di tutto.
2. **Progetto condiviso `Mira.Core`** (net10.0) con `Services`, `GitHub`, `Storage`, `Models`, `SemVer`; `Mira.Server` e `Mira.Android` lo referenziano; i test restano invariati. *1 giorno.*
3. **Dispatcher `/api/*`** in-process (tabella di route, deserializzazione JSON, codici di stato, `MiraException`) con test che confrontano le risposte con quelle degli `Endpoints`. *1,5–2 giorni.*
4. **Gusci MAUI**: `HybridWebView` che serve `web/dist` come asset (copia nella build di MAUI), `SecureStorage`, icona e firma dell'APK. *1–1,5 giorni.*
5. **Pubblicazione**: APK firmato nelle release, con firma Sigstore come per il resto dei pacchetti (#92). Non toccata qui: la CI è fuori perimetro. *0,5–1 giorno.*
6. **Dopo** (non stimato qui): notifiche mobile (WorkManager/foreground service) e collegamento a un server esterno (seconda implementazione del dispatcher via `HttpClient` + autenticazione).

Stima complessiva per un APK standalone utilizzabile: **circa 5–7 giorni di lavoro**, più il tempo per il passo 1.

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

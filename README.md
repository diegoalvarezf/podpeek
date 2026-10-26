# PodPeek

La batería y los gestos de tus AirPods en Windows, con el aspecto de macOS.

- **Batería** de cada auricular y del estuche, en una ventanita como la del iPhone al abrir el estuche.
- **Pausa la música al quitarte un auricular** y la reanuda al volver a ponértelo (solo si la pausó PodPeek).
- **Conectar con un clic**, como el botón de Configuración de Windows.
- **Los pone para todo al conectarlos**: salida y micrófono predeterminados, también para llamadas, para que
  Teams y compañía los usen enseguida.
- Aviso de **batería baja**, icono en la bandeja, modo claro y oscuro, inicio con Windows.

Un solo `PodPeek.exe` de 64 bits que lleva .NET dentro: no se instala ni pide permisos de administrador.
Windows 10 (1809+) y Windows 11. El fondo translúcido es de Windows 11 22H2 en adelante.

## Descargar

En [Releases](../../releases), `PodPeek.exe`. La primera vez Windows puede avisar de que es una app sin firmar
("Windows protegió su PC" › Más información › Ejecutar de todas formas).

## Versión script (sin .exe)

Para equipos donde no se puede descargar un `.exe`. Hace lo mismo, con Windows PowerShell 5.1 y Node.js:

```powershell
npm install -g https://github.com/diegoalvarezf/podpeek/archive/refs/heads/main.tar.gz
podpeek
```

El registro está en `%LOCALAPPDATA%\PodPeek\podpeek-script.log`.

## Cómo funciona

- **Batería y gestos.** Los AirPods emiten cada poco un anuncio Bluetooth LE de Apple (fabricante `0x004C`,
  mensaje tipo `0x07`) con la batería de cada pieza, si se están cargando y si están puestos. PodPeek lo escucha
  con la API Bluetooth de Windows y lo descifra ([AppleProximity.cs](src/PodPeek.Core/AppleProximity.cs)).
  Como en una oficina puede haber más AirPods, sigue a los que llegan con más señal.
- **Conectar.** Windows no tiene una API pública para conectar auriculares Bluetooth; su botón "Conectar" pide
  al driver de audio `KSPROPERTY_ONESHOT_RECONNECT` a través de `IKsControl`, y PodPeek hace lo mismo.
- **Predeterminados.** Con `IPolicyConfig`, la interfaz (no documentada) que usan SoundSwitch o EarTrumpet.
- **Pausa.** Con la API de medios de Windows, que sabe si algo está sonando: nunca pone música que estaba parada.

Nada sale del equipo: no hay conexión a internet ni telemetría. El registro está en `%LOCALAPPDATA%\PodPeek`.

## Compilar

```bash
dotnet test tests/PodPeek.Core.Tests
dotnet publish src/PodPeek/PodPeek.csproj -c Release -o out
```

GitHub Actions compila `PodPeek.exe` en cada push y lo publica al crear una etiqueta `v*`.

## Licencia

MIT. Escrito desde cero; inspirado por [AirPodsDesktop](https://github.com/SpriteOvO/AirPodsDesktop),
[OpenPods](https://github.com/adolfintel/OpenPods) y [ToothTray](https://github.com/m2jean/ToothTray).

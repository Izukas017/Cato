<p align="center">
  <img src="Assets/banner.jpg" alt="Cato" width="100%">
</p>

<p align="center">Un notch dinámico para Windows: música, Pomodoro, tiempo, tareas y más, en la parte de arriba de tu pantalla.</p>

---

## Instalación

1. Ve a [**Releases**](https://github.com/Izukas017/Cato/releases/latest) y descarga `CatoSetup-0.2.1.exe`.
2. Ábrelo. Como el instalador todavía no está firmado, Windows puede mostrar *«Windows protegió tu PC»*: pulsa **Más información → Ejecutar de todas formas**.
3. Sigue el asistente. Puedes elegir crear un acceso directo en el escritorio e **iniciar Cato con Windows**.

No hace falta instalar nada más: el instalador incluye todo lo necesario (.NET va dentro).
Se instala solo para tu usuario, sin permisos de administrador, en `%LOCALAPPDATA%\Programs\Cato`.

**Requisitos:** Windows 10 (versión 2004 o posterior) u 11, de 64 bits.

### Linux

En [**Releases**](https://github.com/Izukas017/Cato/releases/latest) hay dos opciones (64 bits, x86_64):

**Flatpak** (recomendado):

```bash
flatpak install --user ./Cato-0.2.1-x86_64.flatpak
flatpak run io.github.Izukas017.Cato
```

Si no tienes Flathub configurado, Flatpak lo añade para descargar el runtime `org.freedesktop.Platform 25.08`.

**Instalación normal** (sin Flatpak, solo para tu usuario, sin `sudo`):

```bash
tar -xzf Cato-0.2.1-linux-x64.tar.gz
cd cato-0.2.1
./install.sh        # para quitarlo: ./uninstall.sh
```

Queda en `~/.local/share/cato`, con entrada en el menú y el comando `cato`.

Funciona en sesiones **X11** y **Wayland** (a través de XWayland, que GNOME y KDE traen por defecto).
En Wayland los atajos se registran con el portal de atajos globales del escritorio (GNOME 48+, KDE Plasma 6), que puede pedirte confirmarlos la primera vez.
Si tu escritorio no lo admite, crea los atajos tú con `cato --toggle` y `cato --game` (o `flatpak run io.github.Izukas017.Cato --toggle` / `--game`).

### Actualizar

Descarga el instalador de la nueva versión y ejecútalo: cierra Cato si está abierto y lo sustituye. Tus ajustes se conservan.

### Desinstalar

**Configuración de Windows → Aplicaciones → Aplicaciones instaladas → Cato → Desinstalar.**
Tus ajustes se quedan en `%APPDATA%\Cato`; borra esa carpeta si quieres eliminarlos también.

## Uso

- **Pasa el ratón** por encima del notch para abrirlo; muévete con las flechas entre páginas.
- **Clic derecho** en el notch → *Configuración* o *Salir de Cato*. También tienes el engranaje del panel abierto.

| Atajo | Acción |
|---|---|
| `Ctrl` + `Alt izquierdo` + `N` | Ocultar / mostrar el notch |
| `Ctrl` + `Alt izquierdo` + `G` | Modo juego: barra fina con hora, FPS, CPU, GPU, RAM y VRAM |

`AltGr` (Alt derecho) no activa los atajos, así que puedes seguir escribiendo con él (por ejemplo, la ñ).

## Qué incluye

- **Música:** carátula, barra de progreso y controles de lo que suene en el PC (Spotify, YouTube…), con **letras sincronizadas**.
- **Pomodoro:** la ronda completa de un vistazo, con descansos cortos y largo configurables.
- **Día y tiempo:** temperatura y estado del cielo, y el arco del sol con alba, amanecer, mediodía, atardecer y anochecer.
- **Agua:** contador diario, gráfica por horas y recordatorios.
- **Bandeja:** suelta archivos en el notch y sácalos arrastrando cuando los necesites.
- **Portapapeles:** los últimos textos copiados, a un clic.
- **Tareas:** lista del día con recordatorios (`Llamar a Ana @18:30`).
- **Widgets:** la semana y la batería (o la memoria en un PC de sobremesa).
- **Terminal:** una sesión de PowerShell dentro del notch (Intro ejecuta, Esc cancela, ↑↓ historial). Se ejecuta con los permisos de Cato: como administrador si Cato lo está, y como usuario normal si no. No admite programas interactivos.
- **Avisos:** volumen y brillo, cargador, auriculares, agua, tareas y Pomodoro.
- **WhatsApp:** los mensajes de WhatsApp Desktop salen en el notch, que se ensancha un poco y crece hasta 4 líneas de texto. Haz clic en el aviso para abrir WhatsApp; cuánto tarda en cerrarse se elige en *Configuración → Comportamiento*.
- **Modo juego** con métricas del sistema.
- **Personalización:** estilo fusionado con el borde o *Dynamic Island*, animaciones, color de acento, fundido de color, zona de gracia, orden de las páginas…

### FPS en el modo juego

Windows solo deja medir los FPS de otro programa si Cato se ejecuta como **administrador**. En *Configuración → Atajos y modo juego* tienes el botón **Reiniciar como administrador**. Sin eso, el modo juego muestra el resto de datos. Se miden los juegos DirectX.

En Linux el modo juego muestra CPU y RAM, y GPU y VRAM en gráficas AMD; los FPS no están disponibles.

### Aviso cuando Claude Code termina

`Cato.exe --notify "Tarea terminada"` muestra un aviso con el logo de Claude en el notch de la instancia abierta (no abre otro); al hacer clic en él se abre Claude. Para que Claude Code lo lance al acabar, añade un hook `Stop` en `~/.claude/settings.json`:

```json
{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "\"%LOCALAPPDATA%\\Programs\\Cato\\Cato.exe\" --notify \"Claude terminó\"" } ] } ] } }
```

(Solo Windows por ahora.)
### Ver qué hace Claude Code (página «Claude»)

La página **Claude** del notch muestra en vivo qué está haciendo Claude Code: si trabaja, espera tu permiso o ha terminado, lo que hace ahora (`Edita Notify.cs`, `Ejecuta dotnet build…`) y las últimas acciones. Para alimentarla, añade a `~/.claude/settings.json` un hook que ejecute `Cato.exe --activity` en `UserPromptSubmit`, `PreToolUse` (con `"matcher": "*"`), `Notification` y `Stop`, con `"async": true`:

```json
{ "type": "command", "command": "\"%LOCALAPPDATA%\\Programs\\Cato\\Cato.exe\" --activity", "async": true }
```

En el notch solo se muestra el nombre de la herramienta y un resumen corto (archivo, comando o búsqueda). No se guarda nada.
## Privacidad

Cato funciona en tu PC y no envía datos de uso. Solo se conecta a internet para:

- **Letras:** envía el título y el artista de la canción a [lrclib.net](https://lrclib.net) (se puede desactivar en Configuración).
- **Tiempo:** envía las coordenadas de tu ciudad a [open-meteo.com](https://open-meteo.com).

Para los avisos de WhatsApp, Windows te pide permiso de notificaciones (*Ajustes → Privacidad → Notificaciones*). Cato lee solo los avisos de WhatsApp, y el texto no sale de tu PC.

El portapapeles y la bandeja solo se guardan en memoria mientras Cato está abierto.

## Compilar desde el código

La versión de Windows está en la raíz (WPF) y la de Linux en `linux/Cato.Linux` (Avalonia); comparten la lógica (`Logic.cs`, `Store.cs`).

Necesitas el [SDK de .NET 10](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/Izukas017/Cato.git
cd Cato
dotnet run -c Release
```

Para generar el instalador también necesitas [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts\publish
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer\Cato.iss
```

El instalador queda en `artifacts\CatoSetup-<versión>.exe`.

En Linux (con el SDK de .NET 10, y `flatpak-builder` para el Flatpak):

```bash
dotnet run --project linux/Cato.Linux                       # ejecutar
sh linux/install/make-tarball.sh                            # artifacts/Cato-<versión>-linux-x64.tar.gz
flatpak-builder --force-clean --repo=repo build linux/flatpak/io.github.Izukas017.Cato.yml
flatpak build-bundle repo Cato-0.2.1-x86_64.flatpak io.github.Izukas017.Cato --runtime-repo=https://dl.flathub.org/repo/flathub.flatpakrepo
```

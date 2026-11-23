<p align="center">
  <img src="Assets/cato.png" width="140" alt="Cato">
</p>

<h1 align="center">Cato</h1>

<p align="center">Un notch dinámico para Windows: música, Pomodoro, tiempo, tareas y más, en la parte de arriba de tu pantalla.</p>

---

## Instalación

1. Ve a [**Releases**](https://github.com/Izukas017/Cato/releases/latest) y descarga `CatoSetup-0.1.0.exe`.
2. Ábrelo. Como el instalador todavía no está firmado, Windows puede mostrar *«Windows protegió tu PC»*: pulsa **Más información → Ejecutar de todas formas**.
3. Sigue el asistente. Puedes elegir crear un acceso directo en el escritorio e **iniciar Cato con Windows**.

No hace falta instalar nada más: el instalador incluye todo lo necesario (.NET va dentro).
Se instala solo para tu usuario, sin permisos de administrador, en `%LOCALAPPDATA%\Programs\Cato`.

**Requisitos:** Windows 10 (versión 2004 o posterior) u 11, de 64 bits.

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
- **Avisos:** volumen y brillo, cargador, auriculares, agua, tareas y Pomodoro.
- **Modo juego** con métricas del sistema.
- **Personalización:** estilo fusionado con el borde o *Dynamic Island*, animaciones, color de acento, fundido de color, zona de gracia, orden de las páginas…

### FPS en el modo juego

Windows solo deja medir los FPS de otro programa si Cato se ejecuta como **administrador**. En *Configuración → Atajos y modo juego* tienes el botón **Reiniciar como administrador**. Sin eso, el modo juego muestra el resto de datos. Se miden los juegos DirectX.

## Privacidad

Cato funciona en tu PC y no envía datos de uso. Solo se conecta a internet para:

- **Letras:** envía el título y el artista de la canción a [lrclib.net](https://lrclib.net) (se puede desactivar en Configuración).
- **Tiempo:** envía las coordenadas de tu ciudad a [open-meteo.com](https://open-meteo.com).

El portapapeles y la bandeja solo se guardan en memoria mientras Cato está abierto.

## Compilar desde el código

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

# Grabador de Pantalla

Grabador de pantalla para Windows, ligero y rápido, **acelerado por GPU**.

Captura la pantalla por GPU y codifica el video también con la GPU, así gasta
poca CPU. Si no hay GPU compatible, usa la CPU. Graba cualquier monitor a MP4
(H.264) con audio del sistema, en una interfaz moderna de Windows 11.

![.NET](https://img.shields.io/badge/.NET-11-512BD4)
![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4)
![Windows](https://img.shields.io/badge/Windows-10%2F11-0078D4)

---

## Características

- **Grabación por GPU**: captura con DXGI Desktop Duplication (la copia vive en
  la GPU, sin pasar por la CPU).
- **Codificación por GPU automática**: elige el encoder según tu tarjeta.
  - AMD → `h264_amf`
  - NVIDIA → `h264_nvenc`
  - Intel → `h264_qsv`
  - sin GPU compatible → CPU (`libx264`)
  La regla es simple: **si hay GPU, siempre GPU; si no, CPU.**
- **Audio del sistema**: graba lo que suena por los altavoces (WASAPI loopback → AAC).
- **Multi-monitor**: elige cuál pantalla grabar.
- **FPS configurable**: 24, 30 o 60.
- **Carpeta de salida** elegible.
- **Recuerda** la posición/tamaño de la ventana y las últimas opciones.
- **Interfaz Windows 11**: WinUI 3 con fondo Mica y barra de título moderna.
- **Icono propio** (botón REC).

## Cómo funciona

```
Pantalla
   │  DXGI Desktop Duplication (GPU)
   ▼
Textura D3D11 ──► copia a memoria ──► stdin de FFmpeg ──► encoder GPU (AMF/NVENC/QSV) ─┐
                                                                                        ├─► MP4
Audio del sistema ──► WASAPI loopback ──► named pipe ──► FFmpeg (AAC) ───────────────── ┘
```

El video se guarda como `Grabacion_AAAAMMDD_HHMMSS.mp4` en la carpeta elegida
(por defecto, la carpeta **Vídeos** del usuario).

## Requisitos

- **Windows 10 2004 (build 19041)** o superior.
- **.NET 11 SDK** (para compilar).
- **FFmpeg** en el PATH o instalado por winget:
  ```
  winget install Gyan.FFmpeg
  ```
  La app también lo busca en la carpeta de paquetes de winget si no está en el PATH.

## Compilar y ejecutar

```
cd GravadorTela.WinUI
dotnet build -c Debug
dotnet run -c Debug
```

Es *unpackaged*: corre como un `.exe` normal, sin empaquetado MSIX.

## Uso

1. Elige el **monitor**, los **FPS** y la **carpeta** de salida.
2. Marca **Grabar audio del sistema** si quieres sonido.
3. Pulsa **● Grabar**. Haz lo que quieras en pantalla.
4. Pulsa **■ Parar**. El archivo queda en la carpeta elegida.

Las opciones y la posición de la ventana se recuerdan para la próxima vez.

## Configuración guardada

Se guarda al cerrar la app en:

```
%LOCALAPPDATA%\GravadorTela\settings.json
```

Incluye: posición y tamaño de la ventana, monitor, FPS, audio y carpeta. Si el
monitor guardado ya no existe, la app cae al primero automáticamente.

## Instalador (MSI)

Descarga el instalador desde la [página de releases](https://github.com/DarielVerdecia93/grabador-de-pantalla/releases) y ejecútalo. Instala la app (con .NET incluido, no necesitas instalar nada más) y crea accesos directos en el menú inicio y el escritorio.

Para generar el MSI tú mismo necesitas WiX v6:

```
dotnet tool install --global wix --version 6.0.1
wix extension add -g WixToolset.UI.wixext/6.0.1
pwsh installer\build-msi.ps1
```

El `.msi` queda en `dist\`. Es *self-contained*: un solo archivo con la app y el runtime.

## Estructura del proyecto

| Archivo | Responsabilidad |
|---------|-----------------|
| `ScreenRecorder.cs` | Captura por DXGI y canaliza video (y audio) a FFmpeg. |
| `AudioLoopback.cs` | Captura el audio del sistema por WASAPI loopback (COM nativo). |
| `DisplayInfo.cs` | Enumera los monitores conectados. |
| `FfmpegLocator.cs` | Encuentra `ffmpeg.exe` y elige el encoder (GPU o CPU). |
| `Settings.cs` | Guarda y lee la posición de la ventana y las opciones. |
| `installer\Product.wxs` | Definición del instalador MSI (WiX v6). |
| `installer\build-msi.ps1` | Script que publica y genera el MSI. |
| `MainWindow.xaml(.cs)` | Interfaz: Mica, monitor, FPS, audio, carpeta, Grabar/Parar. |
| `App.xaml(.cs)` | Arranque de la app. |
| `Assets\app.ico` | Icono de la app (generado por `Assets\make_icon.ps1`). |

## Tecnología

- **UI**: WinUI 3 (Windows App SDK), fondo Mica.
- **Lenguaje/plataforma**: C# sobre .NET 11.
- **Captura de pantalla**: DXGI Desktop Duplication vía [Vortice](https://github.com/amerkoleci/Vortice.Windows) (Direct3D 11 / DXGI).
- **Codificación de video**: FFmpeg con encoder de hardware (AMF / NVENC / QSV) y fallback a `libx264`.
- **Audio**: WASAPI loopback (Windows Core Audio) por interop COM.

## Notas

- La codificación va a un proceso de FFmpeg aparte, lo que mantiene la app
  estable y la UI fluida durante la grabación.
- El audio del sistema no se captura por FFmpeg directo en Windows; esta app usa
  WASAPI loopback nativo y lo mezcla con el video en el MP4.

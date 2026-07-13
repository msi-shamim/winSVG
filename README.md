# winSVG

<p align="center">
  <img src="assets/app-icon.svg" width="128" alt="winSVG logo">
</p>

**winSVG makes Windows treat SVG files like real images** — thumbnails in
Explorer, and a fast image viewer on double-click. Free and open-source (MIT).

## The problem

Windows has never treated SVG as a first-class image format:

- **No thumbnails.** Open a folder full of `.svg` icons and Explorer shows a
  wall of identical generic icons. Finding the right file means opening them
  one by one.
- **No previewer.** Double-clicking an SVG either does nothing, opens a text
  editor, or dumps the file into a browser tab with no zoom/pan/navigation.
- **Why?** Windows decodes images through the Windows Imaging Component
  (WIC), and Windows ships no SVG codec — so Photos, Explorer thumbnails, and
  the preview pane are all blind to SVG.

## The solution

winSVG fills the gap with two small components:

1. **Explorer thumbnail provider** — a COM shell extension that rasterizes
   SVG (and gzipped `.svgz`) files with the Skia graphics engine, so folders
   show real, transparency-aware thumbnails at every icon size.
2. **winSVG viewer** — a lightweight Chromium-fidelity image viewer that
   opens on double-click, with pan/zoom, folder navigation, and a
   transparency-checkerboard background.

Everything installs **per-user, with no administrator rights**, and
uninstalls cleanly from *Settings → Apps → Installed apps*.

## What winSVG can do

| Feature | Details |
| --- | --- |
| Explorer thumbnails | Real rendered previews for `.svg` and `.svgz`, with alpha transparency |
| Double-click viewer | Chromium-quality SVG rendering via WebView2 |
| Zoom & pan | Mouse-wheel zoom around the cursor, drag to pan, `+`/`-`, double-click toggles fit ↔ 100% |
| Folder navigation | `←`/`→` flips through every SVG in the folder |
| Background toggle | `B` cycles white → checkerboard → black; preview opens with a background by default |
| Export to image | `Ctrl+S` exports PNG / JPG / WebP at ×1, ×2, or ×4 scale, with white, black, or transparent background |
| Crop | `C` opens a crop overlay with aspect presets — Free, 1:1, 16:9, 21:9, 9:16, 4:3, 3:4 — drag to move/resize, then export the cropped region razor-sharp at any scale |
| Keyboard-first | `0` fit, `1` actual size, `Ctrl+O` open, `Esc` close |
| File info | Name, pixel dimensions, and position in folder shown in the toolbar |
| Open With / Default Apps | Registers properly so you can make it the default in one click |
| Clean uninstall | Restores your previous file association and removes every registry entry |

## Installation (release version)

1. Download **`winSVG-Setup-x.y.z.exe`** from the
   [latest release](https://github.com/msi-shamim/winSVG/releases/latest).
2. Run it. No admin prompt — winSVG installs only for your user account.
3. Open any folder with SVG files: **thumbnails just work** (press `F5` if
   the folder was already open).
4. To make double-click open winSVG: right-click any `.svg` → **Open with →
   Choose another app → winSVG → Always**. Windows requires this single
   manual confirmation for default apps — no installer can legitimately skip
   it.

### Requirements

- Windows 10 / 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
  (free; the installer checks and points you to it if missing)
- WebView2 Runtime (preinstalled on Windows 11 and most Windows 10 systems)

### Uninstall

*Settings → Apps → Installed apps → winSVG → Uninstall*, or re-run the setup
and choose remove. Your previous `.svg` association is restored.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0);
[Inno Setup 6](https://jrsoftware.org/isinfo.php) only if you want to build
the installer.

```powershell
git clone https://github.com/msi-shamim/winSVG.git
cd winSVG

# Developer install (publish + copy + register, no installer needed)
powershell -ExecutionPolicy Bypass -File scripts\install.ps1

# Or build the release installer
dotnet publish src\SvgViewer -c Release -o dist\viewer
dotnet publish src\SvgThumbnailProvider -c Release -o dist\thumbnail
iscc installer\winSVG.iss   # -> dist\installer\winSVG-Setup-x.y.z.exe
```

## Project layout

```
src/SvgViewer/              WPF host + WebView2 viewer (Assets/viewer.html is the UI)
src/SvgThumbnailProvider/   COM thumbnail handler (IThumbnailProvider + IInitializeWithStream)
installer/winSVG.iss        Inno Setup script for the release installer
tools/IconGenerator/        Renders assets/app-icon.svg into the multi-size .ico
assets/app-icon.svg         App logo (source of truth for the icon)
scripts/install.ps1         Developer install: build, deploy, register (HKCU)
scripts/uninstall.ps1       Developer uninstall
scripts/set-default-viewer.ps1  Clear a stale .svg association claimed by a browser
samples/                    Test SVGs
```

## How it works

**Thumbnails:** Explorer hands the file to the extension as a COM stream
(`IInitializeWithStream`) and asks for a bitmap (`IThumbnailProvider`). The
handler parses the SVG with [Svg.Skia](https://github.com/wieslawsoltes/Svg.Skia),
renders a premultiplied BGRA bitmap scaled to the requested square, copies it
into a top-down 32-bit GDI DIB section, and returns the `HBITMAP` with
`WTSAT_ARGB` so transparency survives. The class is exposed through .NET 8's
built-in COM hosting and registered per-user — no `regsvr32`, no admin.

**Viewer:** a WPF window hosting WebView2. The C# side owns file access and
folder navigation; the HTML/JS side renders the SVG with the Chromium engine
and implements pan/zoom. If the primary URL-based load fails, the host
re-sends the file as a base64 data URI, so display can't silently break.

**Why can't the installer set the double-click default?** Windows 10/11
protect the final association choice with a cryptographically hashed
`UserChoice` registry key that only user action through Windows UI can write
— an anti-hijacking measure. winSVG registers everywhere it legitimately can
(ProgId, Open With, Default Apps), leaving you exactly one click.

## License

[MIT](LICENSE) © MSI Shamim

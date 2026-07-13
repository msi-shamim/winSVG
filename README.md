# SVG Preview for Windows

Windows Explorer does not show thumbnails for `.svg` files, and double-clicking
one either does nothing useful or dumps you into a browser tab. **SVG Preview**
fixes both problems for the current user, without requiring administrator
rights:

| Problem | Solution in this repo |
| --- | --- |
| No thumbnails in Explorer folders | `SvgThumbnailProvider` — a COM shell extension that rasterizes SVGs (via Svg.Skia/SkiaSharp) into real image thumbnails, including transparency |
| No sensible double-click behavior | `SvgViewer` — a lightweight WPF + WebView2 viewer with pan/zoom, prev/next folder navigation, and background toggle |

## Requirements

- Windows 10 / 11 (x64)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (to build; the
  installed app only needs the .NET 8 Desktop Runtime)
- WebView2 Runtime (preinstalled on Windows 11 and most Windows 10 machines)

## Install

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1
```

The script publishes both projects, copies them to `%LOCALAPPDATA%\SvgPreview`,
and registers everything under `HKCU` (per-user, no admin prompt):

- the thumbnail provider CLSID + the `.svg` / `.svgz` shell extension hookup
- the `SvgPreview.svg` ProgId with an `open` verb pointing at the viewer
- an "Open with" entry for `SvgViewer.exe`

Then open the `samples\` folder in Explorer — you should see rendered
thumbnails. If the folder was already open, press **F5**.

> **Default-app note:** Windows protects double-click defaults with a hashed
> `UserChoice` key that programs cannot legitimately write. If `.svg` was
> previously claimed by a browser, right-click an SVG once → **Open with →
> Choose another app → SVG Preview → Always**. After that, double-click works
> forever.

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1
```

Removes all registry entries (restoring the previous `.svg` association when
one existed) and deletes `%LOCALAPPDATA%\SvgPreview`.

## Viewer shortcuts

| Key / action | Effect |
| --- | --- |
| `←` / `→` | Previous / next SVG in the same folder |
| Mouse wheel | Zoom around the cursor |
| Drag | Pan |
| Double-click | Toggle fit ↔ 100% |
| `+` / `-` | Zoom in / out |
| `0` / `1` | Fit to window / actual size |
| `B` | Cycle background: checkerboard → white → black |
| `Ctrl+O` | Open file dialog |
| `Esc` | Close the viewer |

## Project layout

```
src/SvgViewer/              WPF host + WebView2 viewer (Assets/viewer.html is the UI)
src/SvgThumbnailProvider/   COM thumbnail handler (IThumbnailProvider + IInitializeWithStream)
tools/IconGenerator/        Renders assets/app-icon.svg into the multi-size .ico
assets/app-icon.svg         App logo (source of truth for the icon)
scripts/install.ps1         Build, deploy to %LOCALAPPDATA%, register (HKCU)
scripts/uninstall.ps1       Unregister and remove
scripts/set-default-viewer.ps1  Clear a stale .svg association claimed by a browser
samples/                    Test SVGs
```

To regenerate the app icon after editing the logo:

```powershell
dotnet run --project tools\IconGenerator -c Release -- assets\app-icon.svg src\SvgViewer\Assets\app-icon.ico
```

## How the thumbnail handler works

Explorer hands the file content to the extension as a COM stream
(`IInitializeWithStream`), then asks for a bitmap of a given square size
(`IThumbnailProvider.GetThumbnail`). The handler parses the SVG with
**Svg.Skia**, renders it into a premultiplied BGRA Skia bitmap scaled to fit
the requested square, copies the pixels into a top-down 32-bit GDI DIB
section, and returns the `HBITMAP` with `WTSAT_ARGB` so transparency is
preserved. The class is exposed to COM through .NET 8's built-in COM hosting
(`SvgThumbnailProvider.comhost.dll`), registered per-user — no `regsvr32`, no
admin.

## Troubleshooting

- **Thumbnails don't appear:** press F5 in the folder; if still missing, sign
  out/in or run `taskkill /f /im explorer.exe` then `start explorer` to reload
  the shell. Ensure folder view is set to Medium icons or larger.
- **Stale/old thumbnails:** Windows caches thumbnails aggressively. Run Disk
  Cleanup → Thumbnails, or delete `%LOCALAPPDATA%\Microsoft\Windows\Explorer\thumbcache_*.db`
  while Explorer is stopped.
- **Reinstall fails copying files:** the DLL is loaded by Explorer's COM
  surrogate. Run `taskkill /f /im dllhost.exe` and retry the install script.

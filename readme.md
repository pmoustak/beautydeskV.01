# beautydesk

Hidden launcher panels for the Windows desktop. Push the mouse against a screen edge and a panel slides out with your apps, folders and projects. Move away and it slides back. Pinned rows stay on screen.

## Install

1. Download `beautydesk-win-x64.zip` and unzip it anywhere (for example `%LOCALAPPDATA%\Programs\beautydesk`).
2. Run `beautydesk.exe`. It is self-contained: no .NET or anything else to install. It lives in the system tray.
3. Tray menu → **Start with Windows** to keep it running.

## Use

- **Open a panel:** push the mouse against its edge (the first panel is at the top centre of the main screen).
- **Add items:** drag icons from the desktop (shortcuts, files, folders) onto a row, or click a row's **Add** card.
- **Remove an item:** hover it and click the small ✕.
- **Rows:** the **+** button adds a row (a new column on side panels). Each row has its own title and **pin**; pinned rows stay visible when the panel closes.
- **Panels:** Settings → **Add panel**. Pin a row, then drag the panel by its move button (four arrows) to any edge of any screen; it slides in from the nearest edge. A panel already on that edge moves clockwise to the next free edge.
- **Settings** (gear button or tray): transparency, colour, a background image for all panels or a different one per panel (more rows reveal more of it), panels, rows, start with Windows. Changes apply live and save on their own.

Settings are stored in `%APPDATA%\beautydesk\config.json` (tray → **Edit config file…**). Each item can also take `"action"` (`open`, `vscode`, `explorer`, `terminal`), `"args"`, `"icon"` and `"color"`; the file's header explains them.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet publish beautydesk.csproj -c Release -o app                     # folder build, for development
dotnet publish beautydesk.csproj -c Release -o release -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none   # single exe for the zip
```

Exit the app from the tray before rebuilding. Testing switches: `BEAUTYDESK_DIR` uses another config folder, `BEAUTYDESK_DEMO=1` opens Settings at start.

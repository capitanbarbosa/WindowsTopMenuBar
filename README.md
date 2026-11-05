<p align="center">
  <img src="Assets/mark.png" width="96" alt="Windows Top Menu Bar">
</p>

# Windows Top Menu Bar

A Kali Linux XFCE-style **top panel** and **application menu** for Windows. It reserves a strip of the desktop so maximized windows sit against the bar instead of covering it.

This is an independent project. It is not affiliated with Kali Linux or Offensive Security.

## What you get

- A top AppBar with Kali-like chrome: height, color, accent, opacity, and lock
- An application menu with search, categories, favorites, recently used, and session actions
- Reorderable panel items
- Virtual desktops: switch, rename, add, and remove
- Profiles, plus backup and restore from Panel settings

### Panel items

| Item | Role |
| --- | --- |
| Applications menu | Opens the application menu |
| Launcher | Icon shortcut to a pinned app |
| Workspace switcher | Virtual desktop chips |
| Clock | Date and time |
| Settings | Opens Panel settings |
| Separator / spacer | Layout, including an expanding gap that pushes the clock right |
| Clipboard | Windows clipboard history (`Win+V`) |
| Window buttons | Open windows |
| Show desktop | Minimize everything |

Default layout: menu, Terminal, Files, browser, workspace switcher, expanding separator, clock, settings.

## Download

64-bit Windows 10 or 11. No .NET SDK needed.

The latest build is on **[Releases](https://github.com/capitanbarbosa/WindowsTopMenuBar/releases/latest)**. Download `WindowsTopMenuBar-1.0.0-win-x64.zip`, unzip, and run `WindowsTopMenuBar.exe`.

Windows SmartScreen may warn on the first run because the exe is not code-signed. Choose **More info**, then **Run anyway**.

## Build from source

- Windows 10 or 11, 64-bit
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

From the repo root:

```bash
dotnet run
```

Or build and start the exe:

```bash
dotnet build -c Debug
start bin/Debug/net10.0-windows/WindowsTopMenuBar.exe
```

Only one instance can run. If `dotnet run` does nothing or the build fails with a file lock, the bar is already open. Right-click it and choose **Quit**, or:

```powershell
Stop-Process -Name WindowsTopMenuBar
```

Then run again.

## Use

- **Left-click** the menu icon for applications
- **Right-click** the bar for Panel settings, lock/unlock, add element, crash log, and Quit
- Unlock the panel to drag items and resize the bar from its inner edge
- Drag an app out of the menu onto the bar to pin it
- Workspace chips: click to switch, scroll to move, right-click to rename / add / remove

Settings live under **Panel**, **Items**, and Appearance.

## Files

Windows Top Menu Bar is an independent project and is not affiliated with Kali Linux or Offensive Security.

[Privacy policy](PRIVACY.md) — config and logs stay in `%AppData%\WindowsTopMenuBar\`:

| File | Purpose |
| --- | --- |
| `config.json` | Panel layout, colors, menu options, profiles |
| `log.txt` | Everyday errors |
| `crash.txt` | Unhandled crashes |

Right-click the bar → **Open crash log** to view the latest crash.

## Stack

WPF on .NET 10 (`net10.0-windows`), x64, Per-Monitor DPI V2. The bar uses the Win32 AppBar API so Windows keeps that strip clear of maximized windows.

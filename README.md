<p align="center">
  <img src="Assets/whisker-mark.png" width="96" alt="Windows Whisker">
</p>

# Windows Whisker

A Kali Linux XFCE-style **top panel** and **Whisker Menu** for Windows. It reserves a strip of the desktop so maximized windows sit against the bar instead of covering it.

This is an independent project. It is not affiliated with Kali Linux or Offensive Security.

## What you get

- A top AppBar with Kali-like chrome: height, color, accent, opacity, and lock
- A Whisker-style application menu with search, categories, favorites, recently used, and session actions
- Reorderable panel items
- Virtual desktops: switch, rename, add, and remove
- Profiles, plus backup and restore from Panel settings

### Panel items

| Item | Role |
| --- | --- |
| Applications menu | Opens the Whisker-style menu (`Alt+F1`) |
| Launcher | Icon shortcut to a pinned app |
| Workspace switcher | Virtual desktop chips |
| Clock | Date and time |
| Settings | Opens Panel settings |
| Separator / spacer | Layout, including an expanding gap that pushes the clock right |
| Clipboard | Windows clipboard history (`Win+V`) |
| Window buttons | Open windows |
| Show desktop | Minimize everything |

Default layout: menu, Terminal, Files, browser, workspace switcher, expanding separator, clock, settings.

## Requirements

- Windows 10 or 11, 64-bit
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Run

From the repo root:

```bash
dotnet run
```

Or build and start the exe:

```bash
dotnet build -c Debug
start bin/Debug/net10.0-windows/WindowsWhisker.exe
```

Only one instance can run. If `dotnet run` does nothing or the build fails with a file lock, the bar is already open. Right-click it and choose **Quit**, or:

```powershell
Stop-Process -Name WindowsWhisker
```

Then run again.

## Use

- **Left-click** the menu icon (or press `Alt+F1`) for applications
- **Right-click** the bar for Panel settings, lock/unlock, add element, crash log, and Quit
- Unlock the panel to drag items and resize the bar from its inner edge
- Drag an app out of the menu onto the bar to pin it
- Workspace chips: click to switch, scroll to move, right-click to rename / add / remove

Settings live under **Panel** (display, appearance, items), plus General, Appearance, Behavior, Commands, and Search Actions for the menu.

## Files

Config and logs are in `%AppData%\WindowsWhisker\`:

| File | Purpose |
| --- | --- |
| `config.json` | Panel layout, colors, menu options, profiles |
| `log.txt` | Everyday errors |
| `crash.txt` | Unhandled crashes |

Right-click the bar → **Open crash log** to view the latest crash.

## Stack

WPF on .NET 10 (`net10.0-windows`), x64, Per-Monitor DPI V2. The bar uses the Win32 AppBar API so Windows keeps that strip clear of maximized windows.

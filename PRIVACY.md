# Privacy policy — Windows Top Menu Bar

Windows Top Menu Bar is a local desktop panel for Windows. It does not require an account, does not include ads, and does not send telemetry to the author.

This app is an independent project. It is not affiliated with Kali Linux or Offensive Security.

## Data the app stores on this PC

Settings, layout, and recent-app history are written to:

`%AppData%\WindowsTopMenuBar\`

| File | What it contains |
| --- | --- |
| `config.json` | Panel position, colors, pinned items, application menu layout, recently used app keys, desktop-switcher names |
| `log.txt` | Local error messages |
| `crash.txt` | Local crash details |

Nothing in that folder is uploaded.

## Data the app reads on this PC

To draw the panel and menu, Windows Top Menu Bar reads:

- Start Menu shortcuts and installed-app identities, so the application menu can list programs
- Open window titles, process names, and executable paths, so the task list can group windows
- Virtual desktop names and order, so the workspace switcher can label desktops
- Icon files from those programs, so launchers and tasks can show their icons

That information stays on the device. It is used only to render the bar and menu.

## Network

The app does not phone home. Opening a program from the menu may start that program’s own network activity; that is outside Windows Top Menu Bar.

## Uninstall

Remove the app, then delete `%AppData%\WindowsTopMenuBar` if you also want the local config and logs gone.

## Contact

Questions about this policy: open an issue on [github.com/capitanbarbosa/WindowsTopMenuBar](https://github.com/capitanbarbosa/WindowsTopMenuBar).

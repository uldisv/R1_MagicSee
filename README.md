# R1 Keeper

A small Windows background program that keeps a **Magicsee R1** Bluetooth remote usable.

## Why it is needed

The Magicsee R1 is made for Android and iOS devices. On Windows 11 it only works right after
pairing. When the remote falls asleep or is switched off and then reconnects, it sends its
pairing keys again, Windows treats that as an error and drops the connection — and this
repeats forever until the remote is removed from Bluetooth devices by hand and paired again.

R1 Keeper does that removal and re-pairing itself, without any clicks, every time the remote
shows up. No drivers or administrator rights are needed.

## Usage

1. Install with `R1KeeperSetup-<version>.exe` (see [Installer](#installer)), or build without
   the installer: `dotnet publish src/R1Keeper -c Release -o publish` and run
   `publish\R1Keeper.exe`.
2. A round icon appears in the notification area.
3. Switch the remote on. The first time, the program remembers the first `Magicsee R1` it
   sees and from then on acts only on that one.
4. After every power-on of the remote, press **M + B** (game mode). After power-on the remote
   is always in music mode, where the stick and buttons change the volume; the program
   notices this and shows a reminder.

Icon colour: grey — remote not detected, orange — pairing, green — remote connected, grey
with a pause sign — paused.

Menu (right-click the icon). The default language is English; switch to Latvian with
**Language → Latviešu**. The choice is remembered.

| Item (EN / LV)                               | What it does                                                                                                       |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| Pause / Resume — Pauzēt / Turpināt           | Removes the remote's pairing from Windows and stops the automation, so the remote can be used with another device. |
| Forget remote — Aizmirst pulti               | Forgets the remembered remote; the next `Magicsee R1` seen becomes the program's remote.                           |
| Start with Windows — Startēt kopā ar Windows | Turns automatic start on or off.                                                                                   |
| Language — Valoda                            | Switches the language of the menu and notifications (English / Latviešu).                                          |
| Open log — Atvērt žurnālu                    | Opens the log file.                                                                                                |
| Exit — Iziet                                 | Closes the program. The remote keeps working until it next disconnects.                                            |

## Game mode buttons

| Remote      | Windows game controller          |
| ----------- | -------------------------------- |
| Stick       | X and Y axis, end positions only |
| A           | buttons 5 and 10 together        |
| B           | button 1                         |
| C           | buttons 4 and 9 together         |
| D           | button 2                         |
| Front upper | button 8                         |
| Front lower | button 7                         |

## Known from tests with the real remote

Tested 2026-11-16 on Windows 11 Insider build 26300 with one remote.

- After the remote is switched on or wakes up, it is usable in about 2 seconds. If the
  remote is switched off and on within a few seconds, it can take up to 15 seconds.
- The remote falls asleep by itself after about 5 minutes without button presses; any
  button wakes it.
- The remote does not remember game mode after being switched off or falling asleep. M + B
  has to be pressed after every wake-up. The mode cannot be switched from the computer.
- The first press in music mode changes the volume before the reminder appears.
- In game mode some buttons press two controller buttons at once. This is a quirk of the
  remote; the program does not remap buttons.
- About a minute after every pairing, Windows delivers an advertisement from the remote even
  though it is connected. The program ignores it; in the log it shows as
  `advertisement ignored: the link is up`.
- Every pairing plays the Windows device-connected sound.
- Not tested: computer sleep with the program running, turning Bluetooth off and on, other
  Windows builds.

## Installer

Requires [Inno Setup 7](https://jrsoftware.org/isinfo.php). Build with:

```
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

The script publishes the program together with .NET (the target machine does not need .NET)
to `dist\app` and creates `dist\R1KeeperSetup-<version>.exe`. The version is taken from
`src/R1Keeper/R1Keeper.csproj`.

The installer:

- installs for the current user only, without administrator rights; the default folder is
  `%LOCALAPPDATA%\Programs\R1 Keeper` and can be changed;
- offers "Start R1 Keeper with Windows" (on by default). It is the same setting the menu item
  changes, so on an upgrade the checkbox starts from its current state;
- asks for a language (English preselected), which also becomes the program's starting
  language. An upgrade does not change the program's language;
- closes a running R1 Keeper by itself on upgrade and uninstall, and starts it again after an
  upgrade;
- on uninstall removes the automatic start and the language choice, but leaves
  `%LOCALAPPDATA%\R1Keeper` (the remembered remote and the log).

## Files

- Log: `%LOCALAPPDATA%\R1Keeper\r1keeper.log`
- Settings: `%LOCALAPPDATA%\R1Keeper\settings.json`
- Language: in the registry under `HKCU\Software\R1Keeper`, value `Language` (`en` or `lv`)

`R1Keeper.exe --probe` changes nothing: for 15 seconds it logs what Windows knows about the
remote and which of the remote's advertisements are on the air.

## Development

- `dotnet test` — tests of the pairing logic (no remote needed).
- Design: `docs/superpowers/specs/2026-11-16-r1-keeper-design.md`

# StopCut

Cut manager for TigerStop saw stops: a Windows desktop app (C# WPF, .NET 8) that drives a TigerStop over RS-232
to set cut positions, keeps a preset cut list for each material, and tracks leftovers.

Requires TigerSet enabled on your TigerStop; StopCut uses the official SDK serial protocol.

> StopCut is an independent product and is not affiliated with, sponsored, or endorsed by TigerStop, LLC.
> "TigerStop" is a trademark of TigerStop, LLC and is used here only to say which hardware the app works with.

> **Safety.** This software moves machinery. It is provided "as is", without warranty of any kind (see
> [LICENSE](LICENSE)). You are responsible for safe setup and operation of your saw and stop: keep the
> machine's own guards, interlocks and hardwired emergency stops in place, and never rely on this app for safety.
> Use is subject to the [End User License Agreement](EULA.md), which you accept in the app on first start
> (and can read again under Help > License agreement).

## Run it

Download `StopCut.exe` from the [Releases](../../releases) page and double-click it. It is self-contained: no Visual Studio and no .NET install needed.
Windows SmartScreen may warn the first time because the exe is unsigned; choose "More info", then "Run anyway".

## Rebuild without Visual Studio

Install the .NET 8 SDK, then from this folder run:

```
dotnet publish StopCut/StopCut.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o release
```

Data is saved to `%AppData%\StopCut\data.json` (data from older versions in `%AppData%\TigerStop` is copied over on first start).

## How it works

- **Units**: Settings > Units switches between inches and millimeters. Switching converts every saved
  length. The amp's own units (its MMEnable setting) are read when you connect, and positions are converted both ways.
- **Dashboard**: touch-screen page with the current position, a big number pad, GO, HOME and STOP.
- **Cut lists**: add a material (stock length, kerf, end trim, minimum leftover length to keep),
  then add the parts it needs: label, length and quantity. Each material keeps its own list.
- **Jobs**: every cut line belongs to a job. Pick the job at the top of the Cut lists and Cutting tabs to see,
  plan and cut just that job; "All jobs" shows everything. Quantities are never merged across jobs.
- **Import CSV** (cut lists tab): reads a parts list CSV export (no header; dimensions "L X W X T" in mm,
  material code and description, quantity in the last column). Parts are grouped by material, missing materials are
  created (stock length taken from descriptions like "19x38x2438mm"), and a summary is shown before anything is added. The job name is the file name, e.g. "12345 EXAMPLE.csv" becomes job "12345 EXAMPLE".
- **Jobs folder** (Settings > Jobs folder...): every time the app starts it loads each CSV in that folder that is new
  or has changed since it was last loaded, with no questions asked. A changed file replaces that job's cut lines (done
  counts are kept for parts still in the file). "Update from folder" (Settings menu and Cut lists tab) rescans now.
- **Material library** (Settings menu): add, edit and delete materials (code, name, stock length, kerf, end trim,
  minimum leftover). Deleting a material that cut lines or leftovers still use asks first and lists what goes with it.
- **Now cutting**: the Cutting tab and the dashboard show the length and part being cut in very large text, the last
  cut below it, and a to-scale drawing of the current stick (trim, pieces, kerfs, leftover) with the piece being cut
  outlined in red and finished pieces greyed out with a check mark.
- **Leftover IDs**: every leftover gets an ID made of its material's letter and a number (A1, A2 for one material, B1 for the next; letters are set in the Material library, and numbers of used-up leftovers are reused) to write on the physical piece. When cutting
  creates a leftover, its ID is shown in large green text on the Cutting tab and the dashboard. The plan says
  "Use leftover A12" when it uses one.
- **Cutting**: pick a material and press **Build plan**. Leftovers of that material are used first,
  then new stock, longest parts first. Select a cut and press **Send position** to move the stop,
  then **Cut done** after cutting. With "Send next position automatically" on, the next position
  goes out as soon as you mark a cut done.
- **Leftovers**: when every cut on a stick is finished, the source leftover is used up and the
  remainder is saved as a new leftover if it is at least the material's minimum. You can also
  add or delete leftovers by hand.
- **Terminal** (Settings menu): connect to a COM port and watch everything the TigerStop sends, live, as text or hex
  with timestamps. Type raw commands and press Enter or **Send**; the line ending box sets what is added.
- **TigerStop connection** (Settings menu; Connect and Disconnect stay at the top of the main window): pick the COM port and serial settings, connect, and use **Manual move**
  to test. The serial log shows everything sent (TX) and received (RX).

## TigerStop protocol

The app uses the TigerStop Amp 5.x RS-232 command set (Comm Spec 4.0,
https://github.com/TigerStop/TigerStopSDK/wiki/RS-232:-TigerStop-Serial). Tested target: Amp III firmware 5.23.
Commands end with a carriage return (CR).

| Action | Sent | Amp replies |
|---|---|---|
| Move to position | `MG<pos>` | `MGS <target>` when it starts, `MGF` when it arrives |
| Home | `MH` | `MGF` for each of its 3 moves |
| Stop / E-stop | `MS` / `ME` | `MSS` / `MES` |
| Read position | `P` | the position |
| Version (used to detect the amp) | `V` | e.g. `TigerStop Amp III ver = 5.23` |
| Help | `?` | command list |
| Any error | | `X EC=<code> <text>` |

**Find TigerStop** tries every COM port at 57600 (the amp default), 9600, 19200, 38400, 115200 and 230400 baud
(8-N-1, DTR and RTS on) until the amp answers `V`. Positions are in the amp's units (inches unless MMEnable, D44, is on).
**Position offset** in Settings > TigerStop connection is added to every part length for calibration.

## Project layout

```
StopCut/
  Models/        Material, CutItem, Leftover, SerialSettings, AppData
  Services/      TigerStopController (serial port), CutPlanner, DataStore (JSON)
  ViewModels/    MainViewModel, PlannedCut / PlannedBar
  Mvvm/          ObservableObject, RelayCommand
  MainWindow.xaml
```

## License

[MIT](LICENSE), plus the [End User License Agreement and Terms of Use](EULA.md) for using the app with machinery.

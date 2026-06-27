# Chill More Todo Text

A [BepInEx](https://github.com/BepInEx/BepInEx) plugin for **Chill With You: Lo-Fi Story** that lets the
to-do list hold **much more text per entry** — and optionally widens the entire panel so text wraps less
and more content fits per row.

Vanilla caps each box to a couple of lines and cuts long entries off with a trailing `…`; this raises the
cap, shows the whole thing, grows rows to fit, and can scale the panel width/height to your liking.

> Personal mod for a game I own. The plugin contains **no game code** — it patches the game in memory at
> runtime and never modifies or redistributes any of the game's files.

## How it works

Unlike a hard numeric cap, the to-do text limit isn't an inlined constant you can rewrite. Each text box is
a `TMP_InputField`, and what limits it is:

- the field's **`lineLimit`** (a serialized prefab value), which stops accepting text after a couple of
  lines, and
- the display text component's **`overflowMode = Ellipsis`**, which is what renders the `…` once the text no
  longer fits.

Every multi-line text box in the game — to-do items, to-do list titles, habit names — is wired up through one
shared helper:

```csharp
InputFieldExtensions.SetupMultiLineSubmit(this TMP_InputField inputField)
```

So instead of chasing each UI class (`TodoUI`, `TodoTaskListItemView`, `SelectTodoListUI`, …), this plugin
uses a **Harmony postfix on that single shared chokepoint**. After the game finishes setting a field up, the
postfix:

| Change | Effect |
|---|---|
| raise `lineLimit` (upward only) | the box accepts many more lines of text |
| set `characterLimit` | optional hard character cap (default **0 = unlimited**) |
| `textComponent.overflowMode` → `Overflow` | removes the `…` truncation |
| `textComponent.enableWordWrapping` → `true` | long text wraps instead of running off the side |

Because `SetupMultiLineSubmit` reads `lineLimit` *live on every keystroke*, changing it from the postfix is
respected for all later typing — and because the postfix fires every time a cell is built, it also covers
to-do rows created later as you scroll or switch lists. Clean, reversible, and survives game updates (no
patched DLLs on disk).

**Rows that grow to fit.** Removing the truncation alone just lets long text spill out of the fixed-height
cell. So a second patch hooks each to-do cell's `Setup` (`TodoUI`, `TodoTaskListItemView`) and attaches a tiny
`TodoCellAutoSizer` component. Mirroring the game's own `AutoSizingHeightInputFieldView` pattern (resize a
`RectTransform` to the input field's `preferredHeight`), it grows the row to fit its text and reflows the rows
below it. It adapts to whatever the cell lives in: a layout group that controls child height → it writes a
`LayoutElement.preferredHeight`; one that doesn't → it sets the height directly; no layout group at all → it
falls back to TMP font auto-size so nothing ever overlaps.

**Panel widening.** A third patch hooks the to-do list panel's `Setup` (`TodoListUI`,
`Bulbul.Mobile.TodoListUIViewMobile`) and attaches a `TodoListUIScaler` component. This scales the panel's
width and/or height by the configured factors (default 1.5× wider). The panel repositions on spawn to stay
centered on screen, then releases after ~1 second so you can freely drag it around. The main scroll view, the
Completed list (including its open/close animation), and all inner elements scale proportionally — the text
itself is never stretched.

**Paste scroll fix.** When pasting text into a to-do cell, TMP's `ScrollToCaret` can shift the text out of
position. With `DisableInputScroll` enabled (default true), the plugin continuously resets the text viewport
scroll position so pasted text stays put.

The game targets the **Mono** scripting backend (Unity `2022.3.62f2`), which is why BepInEx 5 + Harmony is the
right tool here — no IL2CPP, and the gameplay assembly isn't name-obfuscated.

## Build

Requires the .NET SDK.

```bash
dotnet build src/ChillMoreTodoText/ChillMoreTodoText.csproj -c Release
```

The build references the game's own Unity assemblies. If your game isn't at the default Steam path, override it:

```bash
dotnet build src/ChillMoreTodoText/ChillMoreTodoText.csproj -c Release \
  -p:GameManagedDir="X:\path\to\Chill With You_Data\Managed" \
  -p:GameRootDir="X:\path\to\Chill with You Lo-Fi Story"
```

The build refreshes `dist/ChillMoreTodoText.dll` (used by the installer), and if `…\BepInEx\plugins` exists it
also auto-copies the plugin there.

## Install on Windows (easy — recommended)

1. Download **`ChillMoreTodoText-vX.Y.Z.zip`** from the
   [latest release](https://github.com/flamfoof/chill-with-you-more-todo-text/releases/latest) and unzip it.
2. Double-click **`Install.bat`**.
3. The installer auto-detects the game via Steam (or click **Browse** to pick the folder containing
   `Chill With You.exe`), then click **Install / Update Mod**. It downloads BepInEx if it isn't already
   installed and copies the plugin.
4. Launch the game normally.

> Windows may show a SmartScreen / "Windows protected your PC" prompt for the `.bat` — click
> **More info → Run anyway**. The script only edits this game's folder (installs BepInEx + the plugin)
> and downloads BepInEx from its official GitHub release. You can read `Install-ChillMoreTodoText.ps1`
> first if you want to verify it.

The installer also runs headless:

```powershell
# auto-detect the game via Steam
powershell -ExecutionPolicy Bypass -File installer\Install-ChillMoreTodoText.ps1 -NoGui
# or point it at a specific install
powershell -ExecutionPolicy Bypass -File installer\Install-ChillMoreTodoText.ps1 -NoGui -GamePath "X:\...\Chill with You Lo-Fi Story"
```

## Install manually

1. Install **BepInEx 5.x (x64, Mono)** into the game folder
   (`…\steamapps\common\Chill with You Lo-Fi Story`) — download from the
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases) (`BepInEx_win_x64_5.4.x`).
2. Run the game once so BepInEx generates its folders, then close it.
3. Build this plugin (above) or copy `ChillMoreTodoText.dll` into `…\BepInEx\plugins\`.
4. Launch the game. Check `…\BepInEx\LogOutput.log` for:
   `Chill More Todo Text loaded — to-do text boxes raised to 20 line(s), unlimited characters, …`

## Configure

After the first run with the plugin, edit:

```
…\BepInEx\config\com.flamfoof.chillmoretodotext.cfg
```

```ini
[General]
## How many lines a single to-do / list title can hold. 0 = leave the vanilla limit alone.
MaxLines = 20
## Maximum characters per entry. 0 = unlimited.
MaxCharacters = 0

[Display]
## Stop cutting long text off with a "..." ellipsis.
RemoveEllipsis = true
## Wrap long lines instead of running off the side of the box.
EnableWordWrap = true
## Stop each to-do text box from scrolling its own contents / hijacking the mouse wheel.
## Also prevents pasted text from shifting out of position.
DisableInputScroll = true

[Layout]
## Grow each to-do row to fit its text (and push the rows below it down).
GrowCellsToFitText = true
## Extra vertical padding (px) added when a row grows.
CellPadding = 24
## Scale the width of the to-do list panel. 1.0 = vanilla. 1.5 = 50% wider (default).
UIWidthScale = 1.5
## Scale the height of the to-do list panel. 1.0 = vanilla (default).
UIHeightScale = 1.0
## Padding (px) from the left edge of each cell to the first element.
CellPaddingLeft = 20
## Padding (px) from the right edge of each cell to the last element.
CellPaddingRight = 20
## Padding (px) from the top edge of each cell. Added to vertical growth.
CellPaddingTop = 20
## Padding (px) from the bottom edge of each cell. Added to vertical growth.
CellPaddingBottom = 20

[Debug]
## Log the live geometry of the to-do panel once after it opens. Turn off for normal play.
DiagnosticDump = false
```

Restart the game (or reopen the to-do panel) to apply.

### Panel widening details

- **`UIWidthScale`** scales the entire to-do panel horizontally. The main list, the Completed list, and all
  inner elements (buttons, input fields, backgrounds) scale proportionally. The Completed list's open/close
  animation is preserved — it stays closed when closed and scales to the wider width when opened.
- The panel repositions on spawn to stay centered, then releases after ~1 second so you can drag it freely.
- **`UIHeightScale`** scales the panel vertically so more rows are visible without scrolling.
- Text is never stretched — only the container sizes change.

## Caveats

- If a future game update renames `SetupMultiLineSubmit` or the cell `Setup` methods, the plugin logs a
  warning (`Could not find …` / `Could not hook any to-do cell Setup method …`) and falls back to vanilla
  behavior for that part.
- Very long entries make their row tall, so the list fills up faster — that's expected. Tune `CellPadding` if
  rows feel cramped/roomy, or set `GrowCellsToFitText = false` to keep the vanilla fixed-height rows (text
  then shrinks to fit instead).
- Existing tasks may need the to-do panel reopened once so they re-measure at the new height.
- When `UIWidthScale > 1.0`, the panel shifts left on spawn to stay centered, then locks in place after ~1
  second so it can be dragged freely. If you close and reopen the panel, it re-centers again.
- The Completed list scales proportionally with `UIWidthScale` — it stays closed when closed and opens to the
  wider width when clicked. The open/close animation is preserved.

## Repo layout

```
src/ChillMoreTodoText/   Plugin source (Plugin.cs) + csproj
installer/               One-click Windows installer (GUI + headless)
dist/                    Built plugin DLL the installer ships
STEAM_GUIDE.md           Copy/paste Steam Community guide (BBCode)
Directory.Build.props    Default game paths (override per-machine)
nuget.config             Adds the BepInEx NuGet feed
decompiled/              Local reference only — gitignored, never committed
```

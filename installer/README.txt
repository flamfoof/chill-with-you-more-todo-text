Chill More Todo Text — mod for "Chill With You: Lo-Fi Story"
===========================================================

What it does
------------
  * Lets each to-do entry (and to-do list title / habit name) hold a lot more
    text. Vanilla caps every box to a couple of lines and cuts long entries off
    with a "..." ellipsis; this raises that cap and shows the whole entry.
  * All configurable: max lines, max characters, whether to remove the "..."
    and whether to word-wrap long lines.

Install (easy)
--------------
  1. Double-click  Install.bat
  2. It auto-detects the game via Steam (or click Browse to pick the folder
     that contains "Chill With You.exe").
  3. Click "Install / Update Mod". It downloads BepInEx if needed and copies
     the plugin. Done.
  4. Launch the game normally.

  Note: Windows may warn about running a script — it only edits this game's
  folder (installs BepInEx + the plugin) and downloads BepInEx from its
  official GitHub release.

Configure
---------
  After the first launch with the mod, edit:
    <game>\BepInEx\config\com.flamfoof.chillmoretodotext.cfg

    [General]
    MaxLines = 20            ; lines a single to-do / title can hold (0 = leave vanilla)
    MaxCharacters = 0        ; 0 = unlimited characters

    [Display]
    RemoveEllipsis = true    ; stop cutting long text off with "..."
    EnableWordWrap = true    ; wrap long lines instead of running off the side

    [Layout]
    GrowCellsToFitText = true ; rows grow to fit the text (false = vanilla fixed height)
    CellPadding = 24          ; extra spacing when a row grows

Uninstall
---------
  Delete  <game>\BepInEx\plugins\ChillMoreTodoText.dll
  (or remove BepInEx entirely: delete winhttp.dll, doorstop_config.ini,
   .doorstop_version and the BepInEx folder from the game directory.)

Manual install (no installer)
-----------------------------
  1. Install BepInEx 5.x (x64) into the game folder.
  2. Run the game once, then close it.
  3. Copy ChillMoreTodoText.dll into <game>\BepInEx\plugins\.

Source: https://github.com/flamfoof/chill-with-you-more-todo-text

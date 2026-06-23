Steam guide — copy/paste into the Steam guide editor.
Steam uses BBCode (not markdown), so the tags below ([h1], [b], [list], etc.)
will render correctly when pasted into a Steam Community guide. Set the guide's
Title field to "Increasing the To-Do List Text Limit".

=====================================================================

[h1]Increasing the To-Do List Text Limit[/h1]

This was made because the to-do list only lets you type a tiny bit of text per task, and once you go past a certain point it just chops everything off with a "..." at the end. I wanted to actually write out longer tasks and notes without it cutting me off.

[h1]Challenges and known issues[/h1]

[h2]Resolved[/h2]
There were really two things going on here. The first was the "..." itself — the text box was set to truncate, so anything long just got hidden behind the dots. That part is gone now, it shows the whole thing. The second was that once the text got longer than the box, it would spill out the bottom of the row instead of the row getting any bigger. So I made the rows grow to fit the text, and the tasks below shift down out of the way like you'd expect them to.

[h2]Small issues[/h2]
If you write a really long task the row obviously gets pretty tall, so the list can fill up fast — that's just what happens when you let it hold more. Everything is in the config if you want to dial it in (max lines, max characters, padding, and so on). If a row ever feels too cramped or too roomy, bump the CellPadding value up or down. You may also need to reopen the to-do panel once so older tasks re-measure themselves.

[h1]Resizing the to-do box UI[/h1]

The to-do boxes now grow to fit whatever you type and show the entire entry from the top, so there's no more scrolling inside a single box and nothing gets clipped off the bottom. Because the whole task is always on screen, the box no longer scrolls its own contents or grabs the mouse wheel — the to-do list itself scrolls normally instead.

If you want the [b]entire to-do list panel[/b] to be bigger — not just the text, but the whole window including rows, buttons, and everything else — use the [b]UIWidthScale[/b] and [b]UIHeightScale[/b] options. They both default to [b]1.0[/b] (vanilla size). Set [b]UIWidthScale[/b] higher to make the panel wider (so text wraps less and more fits per row), and [b]UIHeightScale[/b] higher to make it taller (so more rows are visible without scrolling). The text itself is not stretched — only the panel's dimensions change. For example, 1.25 makes it 25% wider or taller.

[h1]How to install[/h1]

It uses BepinEx, and you just have to download the latest release and unzip it anywhere. Run the .bat or .ps1 file by double clicking it and direct it to the game's .exe file folder location and run Install/Update Mod.

[b]Releases:[/b] [url=https://github.com/flamfoof/chill-with-you-more-todo-text/releases]https://github.com/flamfoof/chill-with-you-more-todo-text/releases[/url]

[h1]Configuring it (optional)[/h1]

After you run the game once with the mod, you can tweak it here:

[code]<game folder>\BepInEx\config\com.flamfoof.chillmoretodotext.cfg[/code]

[list]
[*][b]MaxLines[/b] — how many lines one task/title can hold (default 20).
[*][b]MaxCharacters[/b] — character cap per entry, 0 = unlimited (default 0).
[*][b]RemoveEllipsis[/b] — kills the "..." cut-off (default true).
[*][b]EnableWordWrap[/b] — wraps long text instead of running off the side (default true).
[*][b]GrowCellsToFitText[/b] — rows grow to fit the text (default true). Turn off for the old fixed height.
[*][b]CellPadding[/b] — extra spacing when a row grows; raise it if rows feel tight (default 24).
[*][b]DisableInputScroll[/b] — stops a box from scrolling its own contents / grabbing the mouse wheel so the list scrolls normally (default true).
[*][b]UIWidthScale[/b] — make the to-do list panel wider (default 1.0 = vanilla width). Try 1.25 for 25% wider; text is not stretched.
[*][b]UIHeightScale[/b] — make the to-do list panel taller (default 1.0 = vanilla height). Try 1.25 for 25% taller; text is not stretched.
[/list]

Restart the game (or just reopen the to-do panel) after changing anything.

[h1]Notes[/h1]

It's all done in memory at runtime — it doesn't touch or replace any of the game's files, so it's easy to remove (just delete the plugin) and should keep working across small game updates. If a bigger update ever moves things around I'll patch it. This also plays nice alongside my "more music" mod — they don't touch the same parts of the game, so you can run both.

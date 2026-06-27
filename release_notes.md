## What's new

### Panel widening (UIWidthScale)
- Scale the to-do list panel width with `UIWidthScale` config (default 1.5x)
- Panel repositions on spawn to stay centered, then releases for free dragging
- Main Scroll View and all inner elements widen to match

### CompleteList scaling
- CompleteList container scales multiplicatively with `UIWidthScale` — stays closed when closed, doubles when open
- CompleteList Scroll View scales to match
- CompleteList repositions to sit at the new right edge of the widened panel

### Paste-induced text scroll fix
- Pasting text into to-do cells no longer shifts the text out of position
- Continuously resets text viewport scroll position when `DisableInputScroll` is enabled

### Other improvements
- Per-edge cell padding config (CellPaddingLeft/Right/Top/Bottom)
- Center-anchor drift correction for panel-level elements
- Diagnostic dump mode for troubleshooting layout issues
- Text vertically centered in cells

### Installation
1. Install BepInEx for Chill With You: Lo-Fi Story
2. Copy `ChillMoreTodoText.dll` into `BepInEx/plugins/`
3. Launch the game — config will be generated at `BepInEx/config/com.flamfoof.chillmoretodotext.cfg`

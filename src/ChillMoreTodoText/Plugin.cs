using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChillMoreTodoText
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.flamfoof.chillmoretodotext";
        public const string Name = "Chill More Todo Text";
        public const string Version = "1.2.0";

        internal static ManualLogSource Log;

        // Config — resolved once in Awake and read by the patches.
        internal static int MaxLines;
        internal static int MaxCharacters;
        internal static bool DisableEllipsis;
        internal static bool EnableWordWrap;
        internal static bool GrowCells;
        internal static float CellPadding;
        internal static bool DisableInputScroll;
        internal static float UIWidthScale;
        internal static float UIHeightScale;
        internal static bool DiagnosticDump;

        private void Awake()
        {
            Log = Logger;

            ConfigEntry<int> maxLinesCfg = Config.Bind(
                "General",
                "MaxLines",
                20,
                "How many lines a single to-do / list title can hold. The vanilla game caps each text box " +
                "to just a couple of lines (the prefab's lineLimit), after which it stops accepting text and " +
                "shows \"...\". This raises that cap. Only ever raises it — never lowers a box that already " +
                "allows more. Set 0 to leave the vanilla per-box line limit untouched.");

            ConfigEntry<int> maxCharsCfg = Config.Bind(
                "General",
                "MaxCharacters",
                0,
                "Maximum number of characters a single to-do / list title can hold. 0 = unlimited (recommended). " +
                "Set a positive number if you'd rather keep a hard character cap.");

            ConfigEntry<bool> ellipsisCfg = Config.Bind(
                "Display",
                "RemoveEllipsis",
                true,
                "Stop the text box from cutting long text off with a \"...\" ellipsis. When true, the full text " +
                "is shown (wrapping over the extra lines unlocked by MaxLines) instead of being truncated.");

            ConfigEntry<bool> wrapCfg = Config.Bind(
                "Display",
                "EnableWordWrap",
                true,
                "Wrap long to-do text onto multiple lines so it stays inside the box width instead of running " +
                "off the side. Pairs with MaxLines / RemoveEllipsis to show the whole entry.");

            ConfigEntry<bool> growCfg = Config.Bind(
                "Layout",
                "GrowCellsToFitText",
                true,
                "Grow each to-do row vertically so the whole entry is visible, and push the rows below it down " +
                "(instead of the text spilling out of a fixed-height box). Turn off to keep the vanilla fixed " +
                "row height.");

            ConfigEntry<float> padCfg = Config.Bind(
                "Layout",
                "CellPadding",
                24f,
                "Extra vertical padding (pixels) added on top of the text height when a row grows. Bump this up " +
                "if grown rows feel too tight, or down if they feel too roomy.");

            ConfigEntry<bool> noScrollCfg = Config.Bind(
                "Display",
                "DisableInputScroll",
                true,
                "Since rows now grow to show the whole entry, stop each to-do text box from scrolling its own " +
                "contents / hijacking the mouse wheel. This lets the to-do list itself scroll normally instead " +
                "of getting stuck scrolling inside a single box.");

            ConfigEntry<float> uiWidthScaleCfg = Config.Bind(
                "Layout",
                "UIWidthScale",
                1.0f,
                "Scale the width of the to-do list panel. 1.0 = vanilla width (the default). Values above 1.0 " +
                "make the panel wider so text wraps less and more content fits per row; e.g. 1.25 = 25% wider. " +
                "The text itself is not stretched — only the panel's width changes.");

            ConfigEntry<float> uiHeightScaleCfg = Config.Bind(
                "Layout",
                "UIHeightScale",
                1.0f,
                "Scale the height of the to-do list panel. 1.0 = vanilla height (the default). Values above 1.0 " +
                "make the panel taller so more rows are visible without scrolling; e.g. 1.25 = 25% taller. " +
                "The text itself is not stretched — only the panel's height changes.");

            ConfigEntry<bool> diagCfg = Config.Bind(
                "Debug",
                "DiagnosticDump",
                true,
                "Log the live geometry of the to-do panel (UI_FacilityTodo) once, a moment after it opens. " +
                "Used to diagnose resizing issues. Safe to leave off for normal play.");

            MaxLines = maxLinesCfg.Value;
            MaxCharacters = maxCharsCfg.Value;
            DisableEllipsis = ellipsisCfg.Value;
            EnableWordWrap = wrapCfg.Value;
            GrowCells = growCfg.Value;
            CellPadding = padCfg.Value;
            DisableInputScroll = noScrollCfg.Value;
            UIWidthScale = uiWidthScaleCfg.Value;
            UIHeightScale = uiHeightScaleCfg.Value;
            DiagnosticDump = diagCfg.Value;

            if (MaxLines < 0)
            {
                Log.LogWarning($"MaxLines {MaxLines} is negative; treating as 0 (leave vanilla line limit alone).");
                MaxLines = 0;
            }
            if (MaxCharacters < 0)
            {
                Log.LogWarning($"MaxCharacters {MaxCharacters} is negative; treating as 0 (unlimited).");
                MaxCharacters = 0;
            }
            if (CellPadding < 0f)
                CellPadding = 0f;
            if (UIWidthScale <= 0f)
            {
                Log.LogWarning($"UIWidthScale {UIWidthScale} is not positive; resetting to 1.0 (vanilla width).");
                UIWidthScale = 1.0f;
            }
            if (UIHeightScale <= 0f)
            {
                Log.LogWarning($"UIHeightScale {UIHeightScale} is not positive; resetting to 1.0 (vanilla height).");
                UIHeightScale = 1.0f;
            }

            var harmony = new Harmony(Guid);

            // (1) Text capacity + display. Every multi-line text box in the game (to-do items, to-do list
            // titles, habit names) is set up through the one shared helper SetupMultiLineSubmit(TMP_InputField).
            // Patching that single chokepoint reaches all of them, including cells created later as you scroll.
            MethodBase setup = ResolveSetupMultiLineSubmit();
            if (setup == null)
            {
                Log.LogWarning("Could not find InputFieldExtensions.SetupMultiLineSubmit — the game may have " +
                               "changed. To-do text limits left at vanilla.");
            }
            else
            {
                harmony.Patch(setup, postfix: new HarmonyMethod(
                    typeof(SetupMultiLineSubmit_Patch), nameof(SetupMultiLineSubmit_Patch.Postfix)));
            }

            // (2) Per-row sizing. Attach a TodoCellAutoSizer to each to-do cell as it's built.
            // Needed when rows grow to fit text (GrowCells) AND when the panel is widened
            // (UIWidthScale), since each row has to stretch to the wider list. Hook if either is on.
            if (GrowCells || !Mathf.Approximately(UIWidthScale, 1f))
            {
                int hooked = 0;
                hooked += PatchSetupMethod(harmony, "TodoUI", typeof(CellSetup_Patch));
                hooked += PatchSetupMethod(harmony, "TodoTaskListItemView", typeof(CellSetup_Patch));
                if (hooked == 0)
                    Log.LogWarning("Could not hook any to-do cell Setup method — rows won't auto-grow " +
                                   "or widen. (Text limits are unaffected.)");
            }

            // (3) Panel resizing. Widen/lengthen the to-do list panel via sizeDelta (not scale, so text
            // stays crisp). A TodoListUIScaler re-applies the size each frame in case the game resets it.
            if (!Mathf.Approximately(UIWidthScale, 1f) || !Mathf.Approximately(UIHeightScale, 1f) || DiagnosticDump)
            {
                int panelsHooked = 0;
                panelsHooked += PatchSetupMethod(harmony, "TodoListUI", typeof(PanelSetup_Patch));
                panelsHooked += PatchSetupMethod(harmony, "Bulbul.Mobile.TodoListUIViewMobile", typeof(PanelSetup_Patch));
                if (panelsHooked == 0)
                    Log.LogWarning("Could not hook any to-do list panel Setup method — UIWidthScale/UIHeightScale " +
                                   "won't apply. (Other features are unaffected.)");
            }

            Log.LogInfo($"Chill More Todo Text loaded — to-do text boxes raised to {MaxLines} line(s)" +
                        (MaxCharacters == 0 ? ", unlimited characters" : $", {MaxCharacters} characters") +
                        (DisableEllipsis ? ", ellipsis removed" : "") +
                        (EnableWordWrap ? ", word-wrap on" : "") +
                        (GrowCells ? ", rows auto-grow" : "") +
                        (!Mathf.Approximately(UIWidthScale, 1f) || !Mathf.Approximately(UIHeightScale, 1f)
                            ? $", panel {UIWidthScale:F2}x{UIHeightScale:F2}" : "") + ".");
        }

        // Patches every non-generic method named "Setup" on the given type. Returns how many it patched.
        private static int PatchSetupMethod(Harmony harmony, string typeName, Type patchType)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                Log.LogWarning($"Type '{typeName}' not found.");
                return 0;
            }

            var postfix = new HarmonyMethod(patchType, "Postfix");
            int patched = 0;
            foreach (var m in AccessTools.GetDeclaredMethods(type))
            {
                if (m.Name != "Setup" || m.IsGenericMethodDefinition || m.IsAbstract)
                    continue;
                try
                {
                    harmony.Patch(m, postfix: postfix);
                    patched++;
                }
                catch (Exception e)
                {
                    Log.LogWarning($"Failed to patch {typeName}.Setup: {e.Message}");
                }
            }
            return patched;
        }

        private static MethodBase ResolveSetupMultiLineSubmit()
        {
            var type = AccessTools.TypeByName("InputFieldExtensions");
            if (type == null)
                return null;
            return AccessTools.Method(type, "SetupMultiLineSubmit");
        }
    }

    /// <summary>
    /// Postfix for InputFieldExtensions.SetupMultiLineSubmit. The original only wires up the Enter-to-submit
    /// behaviour; it reads <c>lineLimit</c> live on every keystroke, so raising the limit here (after the
    /// original runs) is respected for all later input. We also widen the on-screen display so long entries
    /// are shown in full instead of being truncated with "...".
    /// </summary>
    internal static class SetupMultiLineSubmit_Patch
    {
        // Logged once so we confirm the patch is live without spamming for every cell.
        private static bool _loggedFirstApply;

        internal static void Postfix(TMP_InputField inputField)
        {
            if (inputField == null)
                return;

            try
            {
                // Raise the per-box line cap, but only ever upward. lineLimit == 0 already means "unlimited",
                // so leave those alone.
                if (Plugin.MaxLines > 0 && inputField.lineLimit > 0 && inputField.lineLimit < Plugin.MaxLines)
                    inputField.lineLimit = Plugin.MaxLines;

                // 0 = unlimited characters (TMP convention). Otherwise apply the configured hard cap.
                inputField.characterLimit = Plugin.MaxCharacters;

                // The whole entry is shown (the row grows to fit), so the box never needs to scroll its own
                // contents. Zeroing scrollSensitivity stops the field from eating the mouse wheel, so the
                // to-do list scrolls normally instead of getting stuck inside one box.
                if (Plugin.DisableInputScroll)
                    inputField.scrollSensitivity = 0f;

                TMP_Text text = inputField.textComponent;
                if (text != null)
                {
                    // The "..." is the text component overflowing in Ellipsis mode. Switch to Overflow so the
                    // full entry renders (the extra lines from MaxLines + word-wrap give it room).
                    if (Plugin.DisableEllipsis && text.overflowMode == TextOverflowModes.Ellipsis)
                        text.overflowMode = TextOverflowModes.Overflow;

                    if (Plugin.EnableWordWrap)
                        text.enableWordWrapping = true;
                }

                if (!_loggedFirstApply)
                {
                    _loggedFirstApply = true;
                    Plugin.Log.LogInfo("Applied expanded text settings to the first to-do/title text box " +
                                       "(applies to every box from here on).");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to expand a to-do text box: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Postfix for a to-do cell's Setup. Attaches one <see cref="TodoCellAutoSizer"/> per cell so the row
    /// grows to fit its text. Idempotent — re-running Setup (e.g. when a cell is reused) is harmless.
    /// </summary>
    internal static class CellSetup_Patch
    {
        // __instance is the cell MonoBehaviour (TodoUI / TodoTaskListItemView). Declaring it as the base
        // MonoBehaviour type lets one postfix serve both without referencing the game assembly.
        internal static void Postfix(MonoBehaviour __instance)
        {
            if (__instance == null)
                return;
            try
            {
                if (__instance.GetComponent<TodoCellAutoSizer>() == null)
                    __instance.gameObject.AddComponent<TodoCellAutoSizer>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to attach row auto-sizer: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Sits on a to-do cell and keeps the row's height matched to its text, and widens the cell
    /// horizontally to match <c>Plugin.UIWidthScale</c>.
    ///
    /// Vertical growth: the cell root is resized so the layout opens a taller slot. Fixed-height
    /// children (background box, input field) are also grown by the same delta so they fill the slot.
    /// Stretch-anchored children follow automatically.
    ///
    /// Horizontal widening: the VerticalLayoutGroup on Content is forced to control child widths
    /// (childControlWidth + childForceExpandWidth) and a LayoutElement with preferredWidth =
    /// Content width is added so the group sizes the cell to fill Content. Inner point-anchored
    /// elements (CellUIParent, Buttons, InputField) are widened manually to match.
    /// </summary>
    internal sealed class TodoCellAutoSizer : MonoBehaviour
    {
        private RectTransform _rt;
        private TMP_InputField _input;
        private TMP_Text _text;
        private LayoutElement _layoutElement;
        private HorizontalOrVerticalLayoutGroup _group;
        private Transform _lastParent;
        private bool _groupControlsHeight;
        private bool _noGroupFallbackApplied;
        private float _minHeight;
        private bool _haveMinHeight;

        // Fixed-height inner pieces (background box, input field viewport, etc.) that need to grow
        // vertically with the cell. Captured once at vanilla heights.
        private RectTransform[] _innerChain;
        private float[] _innerChainMin;

        // Vanilla cell width and input field width/position for horizontal scaling.
        private float _baseCellWidth;
        private RectTransform _inputRt;
        private float _origInputPosY;
        private float _origInputWidth;
        private float _origInputPosX;
        private RectTransform _cellUiParent;
        private float _origCellUiParentW;
        private RectTransform _buttonsRt;
        private float _origButtonsW;

        private bool _hookedCanvas;

        private void Awake()
        {
            _rt = transform as RectTransform;
            _input = GetComponentInChildren<TMP_InputField>(true);
            if (_input != null)
                _text = _input.textComponent;
        }

        private void OnEnable()
        {
            if (!_hookedCanvas)
            {
                Canvas.willRenderCanvases += ApplyWidthAfterLayout;
                _hookedCanvas = true;
            }
        }

        private void OnDisable()
        {
            if (_hookedCanvas)
            {
                Canvas.willRenderCanvases -= ApplyWidthAfterLayout;
                _hookedCanvas = false;
            }
        }

        private void OnDestroy()
        {
            if (_hookedCanvas)
            {
                Canvas.willRenderCanvases -= ApplyWidthAfterLayout;
                _hookedCanvas = false;
            }
        }

        // Called after all layout passes but before rendering — the last chance to set
        // sizeDelta without the layout system resetting it.
        private void ApplyWidthAfterLayout()
        {
            if (_rt == null || Mathf.Approximately(Plugin.UIWidthScale, 1f) || _baseCellWidth <= 1f)
                return;
            ApplyHorizontalWidening();
        }

        // Captures the chain of fixed-height RectTransforms from the text component up to (but not
        // including) the cell root, plus the input field's base position/size for horizontal scaling.
        // Also pins text to top alignment. Called once on the first laid-out frame.
        private void CaptureInnerChain()
        {
            if (_text != null)
                _text.verticalAlignment = VerticalAlignmentOptions.Top;

            _inputRt = _input != null ? _input.transform as RectTransform : null;
            if (_inputRt != null)
            {
                _origInputPosY = _inputRt.anchoredPosition.y;
                _origInputWidth = _inputRt.rect.width;
                _origInputPosX = _inputRt.anchoredPosition.x;
            }

            // Capture CellUIParent and Buttons for horizontal widening.
            _cellUiParent = FindChild(_rt, "CellUIParent");
            if (_cellUiParent != null)
                _origCellUiParentW = _cellUiParent.sizeDelta.x;
            if (_cellUiParent != null)
                _buttonsRt = FindChild(_cellUiParent, "Buttons");
            if (_buttonsRt != null)
                _origButtonsW = _buttonsRt.sizeDelta.x;

            var chain = new List<RectTransform>();
            var mins = new List<float>();
            Transform start = _text != null ? _text.transform : (_input != null ? _input.transform : null);
            Transform cur = start;
            while (cur != null && cur != _rt && cur != transform.parent)
            {
                if (cur is RectTransform crt && crt != _rt)
                {
                    chain.Add(crt);
                    mins.Add(crt.rect.height);
                }
                cur = cur.parent;
            }
            _innerChain = chain.ToArray();
            _innerChainMin = mins.ToArray();

            // Capture the cell's vanilla width for horizontal scaling.
            _baseCellWidth = _rt.rect.width;
        }

        // TMP's preferredHeight can under-report when the rect width isn't settled, so measure
        // explicitly at the live text width to fit every line.
        private float MeasureTextHeight()
        {
            if (_text == null)
                return 0f;
            float width = _text.rectTransform.rect.width;
            if (width > 1f)
                return _text.GetPreferredValues(_text.text, width, 0f).y;
            return _text.preferredHeight;
        }

        private void EnsureGroup()
        {
            if (transform.parent == _lastParent)
                return;
            _lastParent = transform.parent;
            _group = GetComponentInParent<HorizontalOrVerticalLayoutGroup>();

            if (Plugin.DiagnosticDump)
                Plugin.Log.LogInfo($"[EnsureGroup] cell='{_rt.name}' parent='{(transform.parent as RectTransform)?.name}' " +
                                   $"group={(_group != null ? _group.GetType().Name : "null")} " +
                                   $"childControlW={(_group != null && _group.childControlWidth)} " +
                                   $"childForceExpandW={(_group != null && _group.childForceExpandWidth)} " +
                                   $"childControlH={(_group != null && _group.childControlHeight)}");

            // Enable childControlWidth so the layout group respects our LayoutElement.preferredWidth.
            // Without this, the group ignores preferredWidth and keeps the vanilla 336px width.
            if (_group != null && !Mathf.Approximately(Plugin.UIWidthScale, 1f))
            {
                bool groupChanged = false;
                if (!_group.childControlWidth) { _group.childControlWidth = true; groupChanged = true; }
                if (!_group.childForceExpandWidth) { _group.childForceExpandWidth = true; groupChanged = true; }
                if (groupChanged && _rt.parent is RectTransform parentRt)
                    LayoutRebuilder.MarkLayoutForRebuild(parentRt);
            }

            _groupControlsHeight = _group != null && _group.childControlHeight;
            if (_layoutElement == null)
            {
                _layoutElement = GetComponent<LayoutElement>();
                if (_layoutElement == null)
                    _layoutElement = gameObject.AddComponent<LayoutElement>();
            }
            // flexibleWidth > 0 tells the layout group to give this child extra space when
            // childForceExpandWidth is true. Without this, the group keeps the prefab's 336px.
            if (_layoutElement != null && !Mathf.Approximately(Plugin.UIWidthScale, 1f))
                _layoutElement.flexibleWidth = 1f;
        }

        private void LateUpdate()
        {
            if (_rt == null || _input == null || _text == null)
                return;

            EnsureGroup();

            // No layout group → can't safely grow without overlapping neighbours.
            // Shrink the font to fit the existing box instead (applied once).
            if (_group == null)
            {
                if (!_noGroupFallbackApplied)
                {
                    _noGroupFallbackApplied = true;
                    _text.enableAutoSizing = true;
                }
                return;
            }

            if (!_haveMinHeight)
            {
                _minHeight = _rt.rect.height;
                if (_minHeight <= 1f)
                    return; // not laid out yet
                _haveMinHeight = true;
                CaptureInnerChain();
            }

            bool vertChanged = ApplyVerticalGrowth();

            // Apply horizontal widening in LateUpdate too (before layout pass), in addition to
            // the willRenderCanvases callback (after layout pass). Setting it in both places
            // maximizes the chance the width sticks despite the layout system resetting it.
            ApplyHorizontalWidening();

            // Only rebuild layout for vertical changes. LayoutRebuilder resets sizeDelta.x,
            // undoing our horizontal widening, so we must not trigger it for width-only changes.
            if (vertChanged && _rt.parent is RectTransform parent)
                LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        // Grows the cell root and its fixed-height inner pieces vertically to fit the text.
        // Returns true if any RectTransform was modified.
        private bool ApplyVerticalGrowth()
        {
            // The sizer is also attached for width-only scaling; skip vertical growth if disabled.
            if (!Plugin.GrowCells)
                return false;

            float desired = Mathf.Max(_minHeight, MeasureTextHeight() + Plugin.CellPadding);
            float delta = desired - _minHeight;
            bool changed = false;

            // Grow the cell root. Compare against the live value we control so we detect when
            // the game's layout system resets it back to vanilla each frame.
            float current = (_groupControlsHeight && _layoutElement != null)
                ? _layoutElement.preferredHeight
                : _rt.sizeDelta.y;

            if (Mathf.Abs(desired - current) >= 0.5f)
            {
                if (_groupControlsHeight && _layoutElement != null)
                {
                    _layoutElement.minHeight = desired;
                    _layoutElement.preferredHeight = desired;
                }
                else
                {
                    Vector2 sd = _rt.sizeDelta;
                    sd.y = desired;
                    _rt.sizeDelta = sd;
                }
                changed = true;
            }

            // Grow fixed-height inner pieces by the same delta. Stretch-anchored ones follow automatically.
            if (_innerChain != null)
            {
                for (int i = 0; i < _innerChain.Length; i++)
                {
                    RectTransform crt = _innerChain[i];
                    if (crt == null)
                        continue;
                    if (Mathf.Abs(crt.anchorMax.y - crt.anchorMin.y) > 0.0001f)
                        continue;
                    float target = _innerChainMin[i] + delta;
                    if (Mathf.Abs(crt.sizeDelta.y - target) < 0.5f)
                        continue;
                    Vector2 csd = crt.sizeDelta;
                    csd.y = target;
                    crt.sizeDelta = csd;
                    changed = true;
                }
            }

            // Counter center-anchor drift: the input field slides down by half the growth.
            // Nudge it back up so its top stays aligned with the cell's top.
            if (_inputRt != null)
            {
                float targetY = _origInputPosY + delta * 0.5f;
                Vector2 ap = _inputRt.anchoredPosition;
                if (Mathf.Abs(ap.y - targetY) >= 0.5f)
                {
                    ap.y = targetY;
                    _inputRt.anchoredPosition = ap;
                    changed = true;
                }
            }

            return changed;
        }

        // Widens the cell root and its inner elements to match the Content width.
        // The cell root uses stretch anchors (anchorMin.x=0, anchorMax.x=1) so it fills Content
        // regardless of whether the VerticalLayoutGroup has childControlWidth on or off.
        // Inner point-anchored children (CellUIParent, Buttons, InputField) are widened manually.
        // Returns true if anything changed.
        private bool ApplyHorizontalWidening()
        {
            if (Mathf.Approximately(Plugin.UIWidthScale, 1f) || _baseCellWidth <= 1f)
                return false;

            RectTransform parentRt = _rt.parent as RectTransform;
            float targetW = parentRt != null ? parentRt.rect.width : _baseCellWidth + (_baseCellWidth * (Plugin.UIWidthScale - 1f));
            float widthDelta = targetW - _baseCellWidth;
            if (widthDelta <= 0.5f)
                return false;
            bool changed = false;

            // Switch the cell to horizontal stretch anchors so it fills Content regardless
            // of the layout group's childControlWidth setting. The game keeps toggling
            // childControlWidth between true and false; with stretch anchors, the cell
            // width = parent width either way. Keep pivot at 0.5 so children stay centered.
            if (Mathf.Abs(_rt.anchorMin.x) > 0.001f || Mathf.Abs(_rt.anchorMax.x - 1f) > 0.001f)
            {
                Vector2 aMin = _rt.anchorMin; aMin.x = 0f; _rt.anchorMin = aMin;
                Vector2 aMax = _rt.anchorMax; aMax.x = 1f; _rt.anchorMax = aMax;
                changed = true;
            }
            // With stretch anchors, sizeDelta.x is the offset from the anchor rect.
            // Set to 0 so the cell exactly fills Content. Also zero anchoredPosition.x
            // since stretch anchors mean position is determined by anchors + sizeDelta.
            if (Mathf.Abs(_rt.sizeDelta.x) > 0.5f)
            {
                Vector2 sd = _rt.sizeDelta; sd.x = 0f; _rt.sizeDelta = sd;
                changed = true;
            }
            if (parentRt != null && Mathf.Abs(_rt.anchoredPosition.x) > 0.5f)
            {
                Vector2 ap2 = _rt.anchoredPosition; ap2.x = 0f; _rt.anchoredPosition = ap2;
                changed = true;
            }

            // Also set preferredWidth so when childControlWidth=true, the layout group
            // sizes the cell to targetW instead of the prefab's 336px.
            if (_group != null)
            {
                if (!_group.childControlWidth) _group.childControlWidth = true;
                if (!_group.childForceExpandWidth) _group.childForceExpandWidth = true;
            }
            if (_layoutElement != null)
            {
                if (Mathf.Abs(_layoutElement.preferredWidth - targetW) > 0.5f)
                    _layoutElement.preferredWidth = targetW;
                if (_layoutElement.flexibleWidth < 0f)
                    _layoutElement.flexibleWidth = 1f;
            }

            // Widen CellUIParent so stretch-anchored children (BackImage, etc.) fill the cell.
            // CellUIParent is point-anchored at center (0.5,0.5) of the cell, so increasing its
            // sizeDelta.x widens it in place. BackImage has stretch anchors within CellUIParent,
            // so it auto-widens. Buttons is point-anchored, so widen it separately below.
            if (_cellUiParent != null)
            {
                float cupTargetW = _origCellUiParentW + widthDelta;
                Vector2 cupSd = _cellUiParent.sizeDelta;
                if (Mathf.Abs(cupSd.x - cupTargetW) > 0.5f)
                {
                    cupSd.x = cupTargetW;
                    _cellUiParent.sizeDelta = cupSd;
                    changed = true;
                }
            }

            // Widen Buttons (point-anchored at left of CellUIParent, won't auto-stretch).
            if (_buttonsRt != null)
            {
                float btnTargetW = _origButtonsW + widthDelta;
                Vector2 btnSd = _buttonsRt.sizeDelta;
                if (Mathf.Abs(btnSd.x - btnTargetW) > 0.5f)
                {
                    btnSd.x = btnTargetW;
                    _buttonsRt.sizeDelta = btnSd;
                    changed = true;
                }
            }

            // Widen the input field by the full widthDelta.
            if (_inputRt != null && Mathf.Abs(_inputRt.anchorMax.x - _inputRt.anchorMin.x) < 0.0001f)
            {
                float inputTargetW = _origInputWidth + widthDelta;
                Vector2 isd = _inputRt.sizeDelta;
                if (Mathf.Abs(isd.x - inputTargetW) > 0.5f)
                {
                    isd.x = inputTargetW;
                    _inputRt.sizeDelta = isd;
                    changed = true;
                }
                Vector2 iap = _inputRt.anchoredPosition;
                if (Mathf.Abs(iap.x - _origInputPosX) > 0.5f)
                {
                    iap.x = _origInputPosX;
                    _inputRt.anchoredPosition = iap;
                    changed = true;
                }
            }

            return changed;
        }

        private static RectTransform FindChild(RectTransform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i) as RectTransform;
                if (child != null && child.name == name)
                    return child;
            }
            return null;
        }
    }

    /// <summary>
    /// Postfix for the to-do list panel's Setup. Attaches one <see cref="TodoListUIScaler"/>.
    /// Idempotent — re-running Setup is harmless.
    /// </summary>
    internal static class PanelSetup_Patch
    {
        internal static void Postfix(MonoBehaviour __instance)
        {
            if (__instance == null)
                return;
            try
            {
                if (__instance.GetComponent<TodoListUIScaler>() == null)
                    __instance.gameObject.AddComponent<TodoListUIScaler>();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Failed to attach panel scaler: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Sits on the to-do list panel root and enforces <c>Plugin.UIWidthScale</c> / <c>Plugin.UIHeightScale</c>
    /// by resizing the panel's <c>sizeDelta</c> (not <c>localScale</c>, so text stays crisp).
    ///
    /// The panel is stretch-anchored, so sizeDelta is an offset relative to the anchors, not the actual
    /// size. We use: targetSizeDelta = baseSizeDelta + (scale - 1) * baseSize. The game may reset sizes
    /// during animations/tab changes, so we re-apply in LateUpdate. Also grows the Scroll View width and
    /// shifts the CompleteList right to match the widened panel.
    /// </summary>
    internal sealed class TodoListUIScaler : MonoBehaviour
    {
        private RectTransform _rt;
        private Vector2 _baseSize;
        private Vector2 _baseSizeDelta;
        private bool _haveBase;

        // Inner elements with fixed width/position that don't follow the panel widening.
        private RectTransform _scrollView;
        private RectTransform _completeList;
        private float _completeListBaseX;

        // Center-anchored elements that drift right when the panel widens. Shift them left
        // by widthDelta/2 to keep them at their vanilla absolute positions.
        private RectTransform _dragButton;
        private float _dragButtonBaseX;
        private RectTransform _completeCountText;
        private float _completeCountTextBaseX;
        private RectTransform _addTodoUI;
        private float _addTodoUIBaseX;
        private RectTransform _compleateTitle;
        private float _compleateTitleBaseX;

        // One-time diagnostic dump bookkeeping.
        private int _frames;
        private bool _dumped;

        private void Awake()
        {
            _rt = transform as RectTransform;
        }

        private void LateUpdate()
        {
            if (_rt == null)
                return;

            if (!_haveBase)
            {
                Rect r = _rt.rect;
                if (r.width <= 1f || r.height <= 1f)
                    return;
                _baseSize = new Vector2(r.width, r.height);
                _baseSizeDelta = _rt.sizeDelta;
                _haveBase = true;

                // Find panel-level elements by name among direct children.
                for (int i = 0; i < _rt.childCount; i++)
                {
                    var child = _rt.GetChild(i) as RectTransform;
                    if (child == null) continue;
                    if (child.name == "CompleteList" && _completeList == null)
                    {
                        _completeList = child;
                        _completeListBaseX = child.anchoredPosition.x;
                    }
                    else if (child.name == "DragButton" && _dragButton == null)
                    {
                        _dragButton = child;
                        _dragButtonBaseX = child.anchoredPosition.x;
                    }
                    else if (child.name == "CompleteCountText (TMP)" && _completeCountText == null)
                    {
                        _completeCountText = child;
                        _completeCountTextBaseX = child.anchoredPosition.x;
                    }
                    else if (child.name == "AddTodoUI" && _addTodoUI == null)
                    {
                        _addTodoUI = child;
                        _addTodoUIBaseX = child.anchoredPosition.x;
                    }
                    else if (child.name == "CompleateTitle" && _compleateTitle == null)
                    {
                        _compleateTitle = child;
                        _compleateTitleBaseX = child.anchoredPosition.x;
                    }
                }
            }

            // Resolve the task-list Scroll View lazily: the list can be empty on the first frame,
            // so keep trying until we find the ScrollRect that actually holds the TodoCell rows
            // (falling back to a direct child literally named "Scroll View").
            if (_scrollView == null)
                ResolveScrollView();

            float widthDelta = (Plugin.UIWidthScale - 1f) * _baseSize.x;
            float targetX = _baseSizeDelta.x + widthDelta;
            float targetY = _baseSizeDelta.y + (Plugin.UIHeightScale - 1f) * _baseSize.y;
            Vector2 sd = _rt.sizeDelta;
            bool changed = false;

            if (Mathf.Abs(sd.x - targetX) > 0.5f)
            {
                sd.x = targetX;
                changed = true;
            }
            if (Mathf.Abs(sd.y - targetY) > 0.5f)
            {
                sd.y = targetY;
                changed = true;
            }

            if (changed)
                _rt.sizeDelta = sd;

            // Grow the task-list Scroll View to fill the widened panel with small padding on each side.
            // Use the panel's live rect.width (just updated above) so the Scroll View tracks the
            // actual rendered panel size, not a cached base that may have been captured post-scale.
            if (_scrollView != null && !Mathf.Approximately(Plugin.UIWidthScale, 1f))
            {
                float svTarget = _rt.rect.width;
                Vector2 svSd = _scrollView.sizeDelta;
                if (Mathf.Abs(svSd.x - svTarget) > 0.5f)
                {
                    svSd.x = svTarget;
                    _scrollView.sizeDelta = svSd;
                }
            }

            // Shift the CompleteList right so it sits at the new right edge.
            if (_completeList != null && !Mathf.Approximately(Plugin.UIWidthScale, 1f))
            {
                float clTargetX = _completeListBaseX + widthDelta;
                Vector2 clAp = _completeList.anchoredPosition;
                if (Mathf.Abs(clAp.x - clTargetX) > 0.5f)
                {
                    clAp.x = clTargetX;
                    _completeList.anchoredPosition = clAp;
                }
            }

            // Counter center-anchor drift for panel-level elements. The panel grows rightward
            // (pivot at left edge), so center-anchored children drift right by widthDelta/2.
            // Shift them left to keep their vanilla absolute positions.
            if (!Mathf.Approximately(Plugin.UIWidthScale, 1f))
            {
                float halfDelta = widthDelta * 0.5f;
                ShiftLeft(_dragButton, _dragButtonBaseX, halfDelta);
                ShiftLeft(_completeCountText, _completeCountTextBaseX, halfDelta);
                ShiftLeft(_addTodoUI, _addTodoUIBaseX, halfDelta);
                ShiftLeft(_compleateTitle, _compleateTitleBaseX, halfDelta);
            }

            // One-time diagnostic dump, a few frames after the panel settles.
            if (Plugin.DiagnosticDump && !_dumped && _haveBase && ++_frames > 30)
            {
                _dumped = true;
                DumpDiagnostics();
            }
        }

        private static void ShiftLeft(RectTransform rt, float baseX, float halfDelta)
        {
            if (rt == null) return;
            float targetX = baseX - halfDelta;
            Vector2 ap = rt.anchoredPosition;
            if (Mathf.Abs(ap.x - targetX) > 0.5f)
            {
                ap.x = targetX;
                rt.anchoredPosition = ap;
            }
        }

        private static string Fmt(RectTransform rt)
        {
            if (rt == null) return "NULL";
            return $"rect={rt.rect.width:F1}x{rt.rect.height:F1} sizeDelta={rt.sizeDelta} pos={rt.anchoredPosition} " +
                   $"aMin={rt.anchorMin} aMax={rt.anchorMax} pivot={rt.pivot}";
        }

        private void DumpDiagnostics()
        {
            try
            {
                var log = Plugin.Log;
                log.LogInfo($"===== ChillMoreTodoText panel hierarchy dump =====");
                log.LogInfo($"scale W={Plugin.UIWidthScale:F2} H={Plugin.UIHeightScale:F2} baseSize={_baseSize}");
                DumpNode(_rt, 0, log);
                log.LogInfo("===== end panel hierarchy dump =====");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Diagnostic dump failed: {e.Message}");
            }
        }

        private static void DumpNode(RectTransform rt, int depth, ManualLogSource log)
        {
            if (rt == null) return;
            string indent = new string(' ', depth * 2);
            float w = rt.rect.width;
            float h = rt.rect.height;
            log.LogInfo($"{indent}'{rt.name}' active={rt.gameObject.activeSelf} size=({w:F0}x{h:F0}) " +
                        $"aMin=({rt.anchorMin.x:F2},{rt.anchorMin.y:F2}) aMax=({rt.anchorMax.x:F2},{rt.anchorMax.y:F2}) " +
                        $"pivot=({rt.pivot.x:F2},{rt.pivot.y:F2}) sizeDelta=({rt.sizeDelta.x:F2},{rt.sizeDelta.y:F2}) " +
                        $"anchoredPos=({rt.anchoredPosition.x:F2},{rt.anchoredPosition.y:F2})");
            for (int i = 0; i < rt.childCount; i++)
            {
                var child = rt.GetChild(i) as RectTransform;
                if (child != null)
                    DumpNode(child, depth + 1, log);
            }
        }

        // Finds the task-list scroll area so it can be widened with the panel. Prefers the
        // ScrollRect whose content actually holds TodoCell rows; otherwise the first descendant
        // ScrollRect; otherwise a direct child literally named "Scroll View".
        private void ResolveScrollView()
        {
            ScrollRect fallback = null;
            foreach (var sr in GetComponentsInChildren<ScrollRect>(true))
            {
                if (sr == null) continue;
                if (fallback == null) fallback = sr;
                if (sr.content == null) continue;
                for (int i = 0; i < sr.content.childCount; i++)
                {
                    var n = sr.content.GetChild(i).name;
                    if (n != null && n.StartsWith("TodoCell"))
                    {
                        CaptureScrollView(sr.transform as RectTransform);
                        return;
                    }
                }
            }

            if (fallback != null)
            {
                CaptureScrollView(fallback.transform as RectTransform);
                return;
            }

            for (int i = 0; i < _rt.childCount; i++)
            {
                var child = _rt.GetChild(i) as RectTransform;
                if (child != null && child.name == "Scroll View")
                {
                    CaptureScrollView(child);
                    return;
                }
            }
        }

        private void CaptureScrollView(RectTransform rt)
        {
            if (rt == null) return;
            // Only capture once the rect is laid out, so we don't grab a zero-width placeholder.
            if (rt.rect.width <= 1f) return;
            _scrollView = rt;
        }
    }
}

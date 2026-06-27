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
        internal static float CellPaddingLeft;
        internal static float CellPaddingRight;
        internal static float CellPaddingTop;
        internal static float CellPaddingBottom;

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
                1.5f,
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
                false,
                "Log the live geometry of the to-do panel (UI_FacilityTodo) once, a moment after it opens. " +
                "Used to diagnose resizing issues. Turn off for normal play.");

            ConfigEntry<float> padLeftCfg = Config.Bind(
                "Layout",
                "CellPaddingLeft",
                20f,
                "Padding (pixels) from the left edge of each to-do cell to the first element (DragButton). " +
                "Also used as the gap between left-side elements.");

            ConfigEntry<float> padRightCfg = Config.Bind(
                "Layout",
                "CellPaddingRight",
                20f,
                "Padding (pixels) from the right edge of each to-do cell to the last element (TodoRemoveButton). " +
                "Also used as the gap between right-side elements.");

            ConfigEntry<float> padTopCfg = Config.Bind(
                "Layout",
                "CellPaddingTop",
                20f,
                "Padding (pixels) from the top edge of each to-do cell. Added to the cell's vertical growth " +
                "so text looks more centered.");

            ConfigEntry<float> padBottomCfg = Config.Bind(
                "Layout",
                "CellPaddingBottom",
                20f,
                "Padding (pixels) from the bottom edge of each to-do cell. Added to the cell's vertical growth " +
                "so text looks more centered.");

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
            CellPaddingLeft = padLeftCfg.Value;
            CellPaddingRight = padRightCfg.Value;
            CellPaddingTop = padTopCfg.Value;
            CellPaddingBottom = padBottomCfg.Value;

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
            if (CellPaddingLeft < 0f)
                CellPaddingLeft = 0f;
            if (CellPaddingRight < 0f)
                CellPaddingRight = 0f;
            if (CellPaddingTop < 0f)
                CellPaddingTop = 0f;
            if (CellPaddingBottom < 0f)
                CellPaddingBottom = 0f;
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
    /// Sits on a to-do cell and handles two concerns:
    ///
    /// Vertical growth: grows the cell root so the layout opens a taller slot, and grows fixed-height
    /// inner pieces by the same delta. Stretch-anchored children follow automatically.
    ///
    /// Horizontal widening: sets the cell's sizeDelta.x to the Content width using center anchors
    /// (width = sizeDelta.x directly). Forces childControlWidth OFF so the layout group doesn't
    /// override sizeDelta.x. Also widens CellUIParent to match so its children fill the cell.
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

        // Vanilla cell width for horizontal scaling; input field Y pos for vertical growth.
        private float _baseCellWidth;
        private RectTransform _inputRt;
        private float _origInputPosY;
        private RectTransform _cellUiParent;

        // Inner elements with fixed widths or center anchors that don't auto-stretch
        // when CellUIParent widens. Captured once at vanilla geometry.
        private RectTransform _buttonsRt;
        private CanvasGroup _buttonsCanvasGroup;
        private float _buttonsBaseWidth;
        private float _buttonsBaseX;
        private RectTransform _checkButtonRt;
        private float _checkButtonWidth;
        private RectTransform _inputFieldRt;
        private float _inputFieldBaseWidth;
        private RectTransform _backImageRt;
        private RectTransform _completeAnimImageRt;
        private RectTransform _dragButtonInnerRt;
        private float _dragButtonInnerWidth;
        private RectTransform _todoRemoveButtonRt;
        private float _todoRemoveButtonWidth;
        private RectTransform _deadLineCalenderButtonRt;
        private float _deadLineCalenderButtonWidth;
        private RectTransform _dateTimeRt;
        private float _dateTimeWidth;

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

        // Called after layout passes but before rendering — re-asserts stretch anchors and
        // sizeDelta.x=0 in case the layout system reset them during its pass.
        private void ApplyWidthAfterLayout()
        {
            if (_rt == null || Mathf.Approximately(Plugin.UIWidthScale, 1f) || _baseCellWidth <= 1f)
                return;
            ApplyHorizontalWidening();
        }

        // Captures the chain of fixed-height RectTransforms from the text component up to (but not
        // including) the cell root, plus the input field's base Y position for vertical drift
        // correction. Also centers text vertically and records the vanilla cell width.
        // Called once on the first laid-out frame.
        private void CaptureInnerChain()
        {
            if (_text != null)
                _text.verticalAlignment = VerticalAlignmentOptions.Middle;

            _inputRt = _input != null ? _input.transform as RectTransform : null;
            if (_inputRt != null)
                _origInputPosY = _inputRt.anchoredPosition.y;

            // Capture CellUIParent so we can switch it to stretch anchors.
            _cellUiParent = FindChild(_rt, "CellUIParent");

            // Capture inner elements that need adjustment when the cell widens:
            // - Buttons: left-anchored, fixed width, offset -118px from CellUIParent left
            // - CheckButton: center-anchored, drifts right when CellUIParent widens
            // - InputField: right-anchored, fixed 203px width, doesn't fill wider cell
            // - BackImage / CompleteAnimImage: stretch-anchored with sizeDelta.x=236,
            //   making them 236px wider than CellUIParent (965px at 2x scale — way oversized)
            if (_cellUiParent != null)
            {
                _buttonsRt = FindChild(_cellUiParent, "Buttons");
                if (_buttonsRt != null)
                {
                    _buttonsBaseWidth = _buttonsRt.rect.width;
                    _buttonsBaseX = _buttonsRt.anchoredPosition.x;
                    _buttonsCanvasGroup = _buttonsRt.GetComponent<CanvasGroup>();

                    _dragButtonInnerRt = FindChild(_buttonsRt, "DragButton");
                    if (_dragButtonInnerRt != null)
                        _dragButtonInnerWidth = _dragButtonInnerRt.rect.width;
                    _todoRemoveButtonRt = FindChild(_buttonsRt, "TodoRemoveButton");
                    if (_todoRemoveButtonRt != null)
                        _todoRemoveButtonWidth = _todoRemoveButtonRt.rect.width;
                    _deadLineCalenderButtonRt = FindChild(_buttonsRt, "DeadLineCalenderButton");
                    if (_deadLineCalenderButtonRt != null)
                        _deadLineCalenderButtonWidth = _deadLineCalenderButtonRt.rect.width;
                }

                _dateTimeRt = FindChild(_cellUiParent, "DateTime");
                if (_dateTimeRt != null)
                    _dateTimeWidth = _dateTimeRt.rect.width;

                _checkButtonRt = FindChild(_cellUiParent, "CheckButton");
                if (_checkButtonRt != null)
                    _checkButtonWidth = _checkButtonRt.rect.width;

                _inputFieldRt = FindChild(_cellUiParent, "InputField (TMP)");
                if (_inputFieldRt != null)
                    _inputFieldBaseWidth = _inputFieldRt.rect.width;

                _backImageRt = FindChild(_cellUiParent, "BackImage");
                _completeAnimImageRt = FindChild(_cellUiParent, "CompleteAnimImage");
            }

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

            // When widening, force childControlWidth OFF so the layout group doesn't
            // override our sizeDelta.x. We set sizeDelta.x directly in ApplyHorizontalWidening.
            if (_group != null && !Mathf.Approximately(Plugin.UIWidthScale, 1f))
            {
                if (_group.childControlWidth) _group.childControlWidth = false;
            }

            _groupControlsHeight = _group != null && _group.childControlHeight;
            if (_layoutElement == null)
            {
                _layoutElement = GetComponent<LayoutElement>();
                if (_layoutElement == null)
                    _layoutElement = gameObject.AddComponent<LayoutElement>();
            }
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
            ApplyHorizontalWidening();

            // Only rebuild layout for vertical changes — LayoutRebuilder can reset sizeDelta,
            // undoing horizontal widening.
            if (vertChanged && _rt.parent is RectTransform parent)
                LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        // Grows the cell root and its fixed-height inner pieces vertically to fit the text.
        // Returns true if any RectTransform was modified (used to decide whether to rebuild layout).
        private bool ApplyVerticalGrowth()
        {
            // The sizer is also attached for width-only scaling; skip vertical growth if disabled.
            if (!Plugin.GrowCells)
                return false;

            float desired = Mathf.Max(_minHeight, MeasureTextHeight() + Plugin.CellPadding + Plugin.CellPaddingTop + Plugin.CellPaddingBottom);
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

        // Sets the cell width to targetW using point anchors (center) + sizeDelta.x = targetW.
        // With point anchors, width = sizeDelta.x directly. Forces childControlWidth OFF so the
        // VerticalLayoutGroup doesn't override sizeDelta.x. Called from both LateUpdate and
        // willRenderCanvases. Also widens CellUIParent to match so its children position relative
        // to the full cell width.
        private void ApplyHorizontalWidening()
        {
            if (Mathf.Approximately(Plugin.UIWidthScale, 1f) || _baseCellWidth <= 1f)
                return;

            RectTransform parentRt = _rt.parent as RectTransform;
            float targetW = parentRt != null ? parentRt.rect.width : _baseCellWidth + (_baseCellWidth * (Plugin.UIWidthScale - 1f));
            if (targetW <= _baseCellWidth + 0.5f)
                return;

            // Use center anchors (0.5,0.5)→(0.5,0.5) so width = sizeDelta.x directly.
            // The prefab originally uses center anchors, so this is a no-op if unchanged.
            if (Mathf.Abs(_rt.anchorMin.x - 0.5f) > 0.001f || Mathf.Abs(_rt.anchorMax.x - 0.5f) > 0.001f)
            {
                Vector2 aMin = _rt.anchorMin; aMin.x = 0.5f; _rt.anchorMin = aMin;
                Vector2 aMax = _rt.anchorMax; aMax.x = 0.5f; _rt.anchorMax = aMax;
            }
            // Set sizeDelta.x = targetW so the cell is exactly targetW wide.
            if (Mathf.Abs(_rt.sizeDelta.x - targetW) > 0.5f)
            {
                Vector2 sd = _rt.sizeDelta; sd.x = targetW; _rt.sizeDelta = sd;
            }
            // Center the cell in Content.
            if (Mathf.Abs(_rt.anchoredPosition.x) > 0.5f)
            {
                Vector2 ap = _rt.anchoredPosition; ap.x = 0f; _rt.anchoredPosition = ap;
            }

            // Force childControlWidth OFF every frame — when it's true, the layout group
            // overrides sizeDelta.x and resets the cell to the prefab's 336px.
            if (_group != null && _group.childControlWidth)
                _group.childControlWidth = false;

            // Widen CellUIParent to match the cell so its children position/scale relative
            // to the full cell width. CellUIParent uses center anchors; set sizeDelta.x = targetW.
            if (_cellUiParent != null)
            {
                if (Mathf.Abs(_cellUiParent.anchorMin.x - 0.5f) > 0.001f || Mathf.Abs(_cellUiParent.anchorMax.x - 0.5f) > 0.001f)
                {
                    Vector2 aMin = _cellUiParent.anchorMin; aMin.x = 0.5f; _cellUiParent.anchorMin = aMin;
                    Vector2 aMax = _cellUiParent.anchorMax; aMax.x = 0.5f; _cellUiParent.anchorMax = aMax;
                }
                if (Mathf.Abs(_cellUiParent.sizeDelta.x - targetW) > 0.5f)
                {
                    Vector2 csd = _cellUiParent.sizeDelta; csd.x = targetW; _cellUiParent.sizeDelta = csd;
                }
            }

            // Widen Buttons (left-anchored, fixed width) to match the cell and align it
            // to CellUIParent's left edge (vanilla has -118px offset). This ensures the
            // right-anchored buttons inside (TodoRemoveButton, DragButton, etc.) sit at
            // the cell's right edge instead of 118px short of it.
            if (_buttonsRt != null && _buttonsBaseWidth > 1f)
            {
                if (Mathf.Abs(_buttonsRt.sizeDelta.x - targetW) > 0.5f)
                {
                    Vector2 bsd = _buttonsRt.sizeDelta; bsd.x = targetW; _buttonsRt.sizeDelta = bsd;
                }
                if (Mathf.Abs(_buttonsRt.anchoredPosition.x) > 0.5f)
                {
                    Vector2 bap = _buttonsRt.anchoredPosition; bap.x = 0f; _buttonsRt.anchoredPosition = bap;
                }
            }

            // Position the cell's inner buttons explicitly within the widened Buttons container.
            // Layout (left to right): DragButton → CheckButton ... DeadLineCalenderButton → TodoRemoveButton
            // Gaps use CellPaddingLeft for left-side elements, CellPaddingRight for right-side.
            //
            // DragButton (right-anchored, pivot 0.0): left edge at CellPaddingLeft from Buttons' left.
            if (_dragButtonInnerRt != null)
            {
                float targetDragX = Plugin.CellPaddingLeft - targetW;
                if (Mathf.Abs(_dragButtonInnerRt.anchoredPosition.x - targetDragX) > 0.5f)
                {
                    Vector2 dap = _dragButtonInnerRt.anchoredPosition; dap.x = targetDragX;
                    _dragButtonInnerRt.anchoredPosition = dap;
                }
            }

            // TodoRemoveButton (right-anchored, pivot 0.5): right edge at CellPaddingRight from Buttons' right.
            if (_todoRemoveButtonRt != null)
            {
                float targetRemoveX = -(Plugin.CellPaddingRight + _todoRemoveButtonWidth * 0.5f);
                if (Mathf.Abs(_todoRemoveButtonRt.anchoredPosition.x - targetRemoveX) > 0.5f)
                {
                    Vector2 tap = _todoRemoveButtonRt.anchoredPosition; tap.x = targetRemoveX;
                    _todoRemoveButtonRt.anchoredPosition = tap;
                }
            }

            // DeadLineCalenderButton (right-anchored, pivot 1.0): right edge CellPaddingRight left of TodoRemoveButton.
            // Activation and height fix are done once during CaptureInnerChain to avoid per-frame
            // SetActive toggling that breaks click handling.
            if (_deadLineCalenderButtonRt != null)
            {
                float targetCalX = -(Plugin.CellPaddingRight + _todoRemoveButtonWidth + Plugin.CellPaddingRight);
                if (Mathf.Abs(_deadLineCalenderButtonRt.anchoredPosition.x - targetCalX) > 0.5f)
                {
                    Vector2 dap = _deadLineCalenderButtonRt.anchoredPosition; dap.x = targetCalX;
                    _deadLineCalenderButtonRt.anchoredPosition = dap;
                }
            }

            // Fix BackImage and CompleteAnimImage: vanilla sizeDelta.x=236 + stretch anchors
            // makes them 236px wider than CellUIParent. At 2x scale that's 965px — way oversized.
            // Zero out sizeDelta.x and anchoredPos.x so they match CellUIParent exactly.
            if (_backImageRt != null)
            {
                if (Mathf.Abs(_backImageRt.sizeDelta.x) > 0.5f)
                {
                    Vector2 bsd = _backImageRt.sizeDelta; bsd.x = 0f; _backImageRt.sizeDelta = bsd;
                }
                if (Mathf.Abs(_backImageRt.anchoredPosition.x) > 0.5f)
                {
                    Vector2 bap = _backImageRt.anchoredPosition; bap.x = 0f; _backImageRt.anchoredPosition = bap;
                }
            }
            if (_completeAnimImageRt != null)
            {
                if (Mathf.Abs(_completeAnimImageRt.sizeDelta.x) > 0.5f)
                {
                    Vector2 csd = _completeAnimImageRt.sizeDelta; csd.x = 0f; _completeAnimImageRt.sizeDelta = csd;
                }
                if (Mathf.Abs(_completeAnimImageRt.anchoredPosition.x) > 0.5f)
                {
                    Vector2 cap = _completeAnimImageRt.anchoredPosition; cap.x = 0f; _completeAnimImageRt.anchoredPosition = cap;
                }
            }

            // CheckButton (center-anchored in CellUIParent, pivot 0.0): left edge at
            // CellPaddingLeft + DragButtonWidth + CellPaddingLeft from CellUIParent's left.
            if (_checkButtonRt != null)
            {
                float checkLeft = Plugin.CellPaddingLeft + _dragButtonInnerWidth + Plugin.CellPaddingLeft;
                float targetCheckX = checkLeft - targetW * 0.5f;
                if (Mathf.Abs(_checkButtonRt.anchoredPosition.x - targetCheckX) > 0.5f)
                {
                    Vector2 cap = _checkButtonRt.anchoredPosition; cap.x = targetCheckX;
                    _checkButtonRt.anchoredPosition = cap;
                }
            }

            // Reposition DateTime (right-anchored, pivot 1.0) to sit CellPaddingRight left
            // of TodoRemoveButton. Vanilla had it at 89.60px from the right; we move it to
            // CellPaddingRight + TodoRemoveButtonWidth + CellPaddingRight from the right.
            // Also widen DateTime so ExpireButton's Text (TMP) doesn't get clipped on the left.
            // Vanilla DateTime=55px but ExpireButton right edge is at 77.50px — overflow.
            if (_dateTimeRt != null)
            {
                float targetDateTimeX = -(Plugin.CellPaddingRight + _todoRemoveButtonWidth + Plugin.CellPaddingRight);
                if (Mathf.Abs(_dateTimeRt.anchoredPosition.x - targetDateTimeX) > 0.5f)
                {
                    Vector2 dap = _dateTimeRt.anchoredPosition; dap.x = targetDateTimeX;
                    _dateTimeRt.anchoredPosition = dap;
                }
                // Widen DateTime to 80px so ExpireButton (50px at offset 27.50) fits without clipping
                float targetDateTimeW = 80f;
                if (Mathf.Abs(_dateTimeRt.sizeDelta.x - targetDateTimeW) > 0.5f)
                {
                    Vector2 dsd = _dateTimeRt.sizeDelta; dsd.x = targetDateTimeW; _dateTimeRt.sizeDelta = dsd;
                }
            }

            // Widen InputField (right-anchored, fixed 203px width) to fill the space between
            // CheckButton and DateTime. Pivot.x = 1.0 (right) so it grows leftward.
            // Left edge: CellPaddingLeft right of CheckButton's right edge.
            // Right edge: CellPaddingRight left of DateTime's left edge.
            if (_inputFieldRt != null && _inputFieldBaseWidth > 1f)
            {
                float checkLeft = Plugin.CellPaddingLeft + _dragButtonInnerWidth + Plugin.CellPaddingLeft;
                float checkRight = checkLeft + _checkButtonWidth;
                float inputLeft = checkRight + Plugin.CellPaddingLeft;
                // DateTime right edge offset from CellUIParent right = CellPaddingRight + TodoRemoveButtonWidth + CellPaddingRight
                float dateTimeRightOffset = Plugin.CellPaddingRight + _todoRemoveButtonWidth + Plugin.CellPaddingRight;
                // InputField right edge = dateTimeRightOffset + 80px (widened DateTime) + CellPaddingRight
                float inputRightOffset = dateTimeRightOffset + 80f + Plugin.CellPaddingRight;
                float targetInputW = targetW - inputLeft - inputRightOffset;
                if (Mathf.Abs(_inputFieldRt.sizeDelta.x - targetInputW) > 0.5f)
                {
                    Vector2 isd = _inputFieldRt.sizeDelta; isd.x = targetInputW; _inputFieldRt.sizeDelta = isd;
                }
                if (Mathf.Abs(_inputFieldRt.pivot.x - 1f) > 0.001f)
                {
                    Vector2 piv = _inputFieldRt.pivot; piv.x = 1f; _inputFieldRt.pivot = piv;
                }
                float targetInputX = -inputRightOffset;
                if (Mathf.Abs(_inputFieldRt.anchoredPosition.x - targetInputX) > 0.5f)
                {
                    Vector2 iap = _inputFieldRt.anchoredPosition; iap.x = targetInputX; _inputFieldRt.anchoredPosition = iap;
                }
            }
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
    /// during animations/tab changes, so we re-apply in LateUpdate. Also shifts center-anchored
    /// panel-level elements left to counter drift from the widened panel.
    /// </summary>
    internal sealed class TodoListUIScaler : MonoBehaviour
    {
        private RectTransform _rt;
        private Vector2 _baseSize;
        private Vector2 _baseSizeDelta;
        private float _baseAnchoredPosX;
        private bool _haveBase;

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

        // Scroll View is left-anchored (not stretch), so it doesn't auto-widen with the panel.
        private RectTransform _scrollView;

        // CompleteList is center-anchored, so it needs to be shifted right by widthDelta
        // to sit at the new right edge of the widened panel.
        private RectTransform _completeList;
        private float _completeListBaseX;

        // CompleteList's own Scroll View (left-anchored, doesn't auto-widen).
        private RectTransform _completeListScrollView;
        private float _completeListScrollViewBaseW;

        // CompleteList container width tracking for multiplicative scaling.
        // The game animates the container width to open/close; we scale it proportionally.
        private float _gameClSdX;
        private float _lastSetClSdX;
        private bool _clSdInitialized;

        // One-time diagnostic dump bookkeeping.
        private int _frames;
        private bool _dumped;

        // Frame counter for one-time position shifts.
        private int _posApplyFrames;

        private void Awake()
        {
            _rt = transform as RectTransform;
        }

        private void OnEnable()
        {
            _posApplyFrames = 0;
            _clSdInitialized = false;
            Canvas.willRenderCanvases += ApplyPositionShifts;
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= ApplyPositionShifts;
        }

        private void ApplyPositionShifts()
        {
            if (_rt == null || !_haveBase)
                return;

            if (Mathf.Approximately(Plugin.UIWidthScale, 1f))
            {
                Canvas.willRenderCanvases -= ApplyPositionShifts;
                return;
            }

            // LateUpdate handles the frame counter and unsubscribing at 60 frames.
            // This callback just re-applies position shifts to counter game resets.
            if (_posApplyFrames > 60)
                return;

            float widthDelta = (Plugin.UIWidthScale - 1f) * _baseSize.x;
            float halfDelta = widthDelta * 0.5f;

            // Shift the panel left by widthDelta to stay centered.
            float targetPosX = _baseAnchoredPosX - widthDelta;
            Vector2 ap = _rt.anchoredPosition;
            if (Mathf.Abs(ap.x - targetPosX) > 0.5f)
            {
                ap.x = targetPosX;
                _rt.anchoredPosition = ap;
            }

            // Shift CompleteList right by widthDelta.
            if (_completeList != null)
            {
                float clTargetX = _completeListBaseX + widthDelta;
                Vector2 clAp = _completeList.anchoredPosition;
                if (Mathf.Abs(clAp.x - clTargetX) > 0.5f)
                {
                    clAp.x = clTargetX;
                    _completeList.anchoredPosition = clAp;
                }
            }

            // Counter center-anchor drift for panel-level elements.
            ShiftLeft(_dragButton, _dragButtonBaseX, halfDelta);
            ShiftLeft(_completeCountText, _completeCountTextBaseX, halfDelta);
            ShiftLeft(_addTodoUI, _addTodoUIBaseX, halfDelta);
            ShiftLeft(_compleateTitle, _compleateTitleBaseX, halfDelta);
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
                _baseAnchoredPosX = _rt.anchoredPosition.x;
                _haveBase = true;

                // Find panel-level elements by name among direct children.
                for (int i = 0; i < _rt.childCount; i++)
                {
                    var child = _rt.GetChild(i) as RectTransform;
                    if (child == null) continue;
                    if (child.name == "DragButton" && _dragButton == null)
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
                    else if (child.name == "CompleteList" && _completeList == null)
                    {
                        _completeList = child;
                        _completeListBaseX = child.anchoredPosition.x;
                    }
                }
            }

            // Resolve the Scroll View lazily (list may be empty on the first frame).
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

            // Widen the main Scroll View to match the panel's live width.
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

            // Apply position shifts for the first N frames, then stop so user can drag.
            if (!Mathf.Approximately(Plugin.UIWidthScale, 1f) && _posApplyFrames <= 60)
            {
                _posApplyFrames++;
                if (_posApplyFrames == 1)
                    Plugin.Log.LogInfo($"[TodoListUIScaler] Applying position shifts for 60 frames: widthDelta={widthDelta:F1}, panelTargetX={_baseAnchoredPosX - widthDelta:F1}, clTargetX={(_completeList != null ? _completeListBaseX + widthDelta : 0):F1}");
                if (_posApplyFrames == 60)
                {
                    Plugin.Log.LogInfo("[TodoListUIScaler] Position shifts complete, unsubscribing from willRenderCanvases.");
                    Canvas.willRenderCanvases -= ApplyPositionShifts;
                }
                float halfDelta = widthDelta * 0.5f;

                // Shift the panel left by widthDelta to stay centered.
                // Both the main panel and CompleteList each grow by widthDelta,
                // so total extra width is 2*widthDelta; shift by half of that = widthDelta.
                float targetPosX = _baseAnchoredPosX - widthDelta;
                Vector2 ap = _rt.anchoredPosition;
                if (Mathf.Abs(ap.x - targetPosX) > 0.5f)
                {
                    ap.x = targetPosX;
                    _rt.anchoredPosition = ap;
                }

                // Shift CompleteList right by widthDelta.
                if (_completeList != null)
                {
                    float clTargetX = _completeListBaseX + widthDelta;
                    Vector2 clAp = _completeList.anchoredPosition;
                    if (Mathf.Abs(clAp.x - clTargetX) > 0.5f)
                    {
                        clAp.x = clTargetX;
                        _completeList.anchoredPosition = clAp;
                    }
                }

                // Counter center-anchor drift.
                ShiftLeft(_dragButton, _dragButtonBaseX, halfDelta);
                ShiftLeft(_completeCountText, _completeCountTextBaseX, halfDelta);
                ShiftLeft(_addTodoUI, _addTodoUIBaseX, halfDelta);
                ShiftLeft(_compleateTitle, _compleateTitleBaseX, halfDelta);
            }

            // Continuously scale the CompleteList container and Scroll View multiplicatively.
            // The game animates the container width to open/close; we scale it proportionally
            // so closed stays closed (-3 * 2 = -6) and open doubles (367 * 2 = 734).
            if (!Mathf.Approximately(Plugin.UIWidthScale, 1f) && _completeList != null)
            {
                // Scale the CompleteList container width.
                float curClSdX = _completeList.sizeDelta.x;
                if (!_clSdInitialized)
                {
                    _gameClSdX = curClSdX;
                    _lastSetClSdX = curClSdX;
                    _clSdInitialized = true;
                }
                else if (Mathf.Abs(curClSdX - _lastSetClSdX) > 0.5f)
                {
                    // Game changed the width (open/close animation) — record new target.
                    _gameClSdX = curClSdX;
                }
                float clSdTarget = _gameClSdX * Plugin.UIWidthScale;
                if (Mathf.Abs(curClSdX - clSdTarget) > 0.5f)
                {
                    Vector2 clSd = _completeList.sizeDelta;
                    clSd.x = clSdTarget;
                    _completeList.sizeDelta = clSd;
                    _lastSetClSdX = clSdTarget;
                }
            }

            // Scale the CompleteList's Scroll View width multiplicatively.
            if (!Mathf.Approximately(Plugin.UIWidthScale, 1f) && _completeListScrollView != null && _completeListScrollViewBaseW > 1f)
            {
                float clSvTarget = _completeListScrollViewBaseW * Plugin.UIWidthScale;
                Vector2 clSvSd = _completeListScrollView.sizeDelta;
                if (Mathf.Abs(clSvSd.x - clSvTarget) > 0.5f)
                {
                    clSvSd.x = clSvTarget;
                    _completeListScrollView.sizeDelta = clSvSd;
                }
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

        private void ResolveScrollView()
        {
            ScrollRect fallback = null;
            foreach (var sr in GetComponentsInChildren<ScrollRect>(true))
            {
                if (sr == null) continue;
                if (fallback == null) fallback = sr;
                if (sr.content == null) continue;
                bool hasTodoCell = false;
                for (int i = 0; i < sr.content.childCount; i++)
                {
                    if (sr.content.GetChild(i).name != null && sr.content.GetChild(i).name.StartsWith("TodoCell"))
                    {
                        hasTodoCell = true;
                        break;
                    }
                }
                if (hasTodoCell)
                {
                    RectTransform srRt = sr.transform as RectTransform;
                    if (_scrollView == null)
                    {
                        _scrollView = srRt;
                    }
                    else if (_completeListScrollView == null && srRt != _scrollView)
                    {
                        _completeListScrollView = srRt;
                        _completeListScrollViewBaseW = srRt.rect.width;
                    }
                }
            }
            if (_scrollView == null && fallback != null)
                _scrollView = fallback.transform as RectTransform;
        }
    }
}

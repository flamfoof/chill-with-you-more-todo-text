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
        public const string Version = "1.1.0";

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

            MaxLines = maxLinesCfg.Value;
            MaxCharacters = maxCharsCfg.Value;
            DisableEllipsis = ellipsisCfg.Value;
            EnableWordWrap = wrapCfg.Value;
            GrowCells = growCfg.Value;
            CellPadding = padCfg.Value;
            DisableInputScroll = noScrollCfg.Value;
            UIWidthScale = uiWidthScaleCfg.Value;
            UIHeightScale = uiHeightScaleCfg.Value;

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

            // (2) Row growth. Attach a TodoCellAutoSizer to each to-do cell as it's built.
            if (GrowCells)
            {
                int hooked = 0;
                hooked += PatchSetupMethod(harmony, "TodoUI", typeof(CellSetup_Patch));
                hooked += PatchSetupMethod(harmony, "TodoTaskListItemView", typeof(CellSetup_Patch));
                if (hooked == 0)
                    Log.LogWarning("Could not hook any to-do cell Setup method — rows won't auto-grow. " +
                                   "(Text limits are unaffected.)");
            }

            // (3) Panel resizing. Widen/lengthen the to-do list panel via sizeDelta (not scale, so text
            // stays crisp). A TodoListUIScaler re-applies the size each frame in case the game resets it.
            if (!Mathf.Approximately(UIWidthScale, 1f) || !Mathf.Approximately(UIHeightScale, 1f))
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
    /// Horizontal widening: the cell root and input field are widened by the panel's width delta.
    /// The cell root has center-x pivot, so its anchoredPosition is shifted to keep the left edge
    /// in place. The input field is widened and shifted left to keep its right edge (buttons) in place.
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
        private bool _groupControlsWidth;
        private bool _noGroupFallbackApplied;
        private float _minHeight;
        private bool _haveMinHeight;

        // Fixed-height inner pieces (background box, input field viewport, etc.) that need to grow
        // vertically with the cell. Captured once at vanilla heights.
        private RectTransform[] _innerChain;
        private float[] _innerChainMin;

        // Vanilla cell width/position and input field width/position for horizontal scaling.
        private float _baseCellWidth;
        private float _baseCellPosX;
        private RectTransform _inputRt;
        private float _origInputPosY;
        private float _origInputWidth;
        private float _origInputPosX;

        private void Awake()
        {
            _rt = transform as RectTransform;
            _input = GetComponentInChildren<TMP_InputField>(true);
            if (_input != null)
                _text = _input.textComponent;
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

            // Capture the cell's vanilla width and position for horizontal scaling.
            _baseCellWidth = _rt.rect.width;
            _baseCellPosX = _rt.anchoredPosition.x;
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
            _groupControlsHeight = _group != null && _group.childControlHeight;
            _groupControlsWidth = _group != null && _group.childControlWidth;
            if ((_groupControlsHeight || _groupControlsWidth) && _layoutElement == null)
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

            bool changed = ApplyVerticalGrowth();
            changed |= ApplyHorizontalWidening();

            if (changed && _rt.parent is RectTransform parent)
                LayoutRebuilder.MarkLayoutForRebuild(parent);
        }

        // Grows the cell root and its fixed-height inner pieces vertically to fit the text.
        // Returns true if any RectTransform was modified.
        private bool ApplyVerticalGrowth()
        {
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

        // Widens the cell root and input field horizontally to match UIWidthScale.
        // The cell root (center-x pivot) is shifted right by widthDelta/2 to keep its left edge in place.
        // The input field (center-x pivot) is shifted left by widthDelta/2 to keep its right edge in place.
        // Returns true if any RectTransform was modified.
        private bool ApplyHorizontalWidening()
        {
            if (Mathf.Approximately(Plugin.UIWidthScale, 1f) || _baseCellWidth <= 1f)
                return false;

            float targetW = _baseCellWidth * Plugin.UIWidthScale;
            float widthDelta = targetW - _baseCellWidth;
            bool changed = false;

            // Cell root: drive via LayoutElement if the group controls width, otherwise set sizeDelta directly.
            if (_groupControlsWidth && _layoutElement != null)
            {
                if (Mathf.Abs(_layoutElement.preferredWidth - targetW) > 0.5f)
                {
                    _layoutElement.minWidth = targetW;
                    _layoutElement.preferredWidth = targetW;
                    changed = true;
                }
            }
            else if (Mathf.Abs(_rt.anchorMax.x - _rt.anchorMin.x) < 0.0001f)
            {
                if (Mathf.Abs(_rt.sizeDelta.x - targetW) > 0.5f)
                {
                    Vector2 sd = _rt.sizeDelta;
                    sd.x = targetW;
                    _rt.sizeDelta = sd;
                    changed = true;
                }
                // Shift right by half the delta so the left edge stays at its vanilla position.
                float targetPosX = _baseCellPosX + widthDelta * 0.5f;
                Vector2 ap = _rt.anchoredPosition;
                if (Mathf.Abs(ap.x - targetPosX) > 0.5f)
                {
                    ap.x = targetPosX;
                    _rt.anchoredPosition = ap;
                    changed = true;
                }
            }

            // Widen ONLY the input field (not CellUIParent, which holds buttons).
            // Shift left by widthDelta/2 so the right edge stays put and it extends leftward.
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
                float inputTargetPosX = _origInputPosX - widthDelta * 0.5f;
                Vector2 iap = _inputRt.anchoredPosition;
                if (Mathf.Abs(iap.x - inputTargetPosX) > 0.5f)
                {
                    iap.x = inputTargetPosX;
                    _inputRt.anchoredPosition = iap;
                    changed = true;
                }
            }

            return changed;
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
        private float _scrollViewBaseWidth;
        private RectTransform _completeList;
        private float _completeListBaseX;

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

                // Find the task-list Scroll View and CompleteList by name among direct children.
                for (int i = 0; i < _rt.childCount; i++)
                {
                    var child = _rt.GetChild(i) as RectTransform;
                    if (child == null) continue;
                    if (child.name == "Scroll View" && _scrollView == null)
                    {
                        _scrollView = child;
                        _scrollViewBaseWidth = child.rect.width;
                    }
                    if (child.name == "CompleteList" && _completeList == null)
                    {
                        _completeList = child;
                        _completeListBaseX = child.anchoredPosition.x;
                    }
                }
            }

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

            // Grow the task-list Scroll View width so task bars fill the widened panel.
            if (_scrollView != null && !Mathf.Approximately(Plugin.UIWidthScale, 1f))
            {
                float svTarget = _scrollViewBaseWidth + widthDelta;
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
        }
    }
}

using System;
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

            MaxLines = maxLinesCfg.Value;
            MaxCharacters = maxCharsCfg.Value;
            DisableEllipsis = ellipsisCfg.Value;
            EnableWordWrap = wrapCfg.Value;
            GrowCells = growCfg.Value;
            CellPadding = padCfg.Value;

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

            // (2) Row growth. Attach a small auto-sizer to each to-do cell as it's built, so the row resizes
            // to its text. We hook the cell Setup methods (desktop TodoUI + mobile TodoTaskListItemView) —
            // their `this` IS the cell root, which is what needs to grow.
            if (GrowCells)
            {
                int hooked = 0;
                hooked += PatchCellSetup(harmony, "TodoUI");
                hooked += PatchCellSetup(harmony, "TodoTaskListItemView");
                if (hooked == 0)
                    Log.LogWarning("Could not hook any to-do cell Setup method — rows won't auto-grow. " +
                                   "(Text limits are unaffected.)");
            }

            Log.LogInfo($"Chill More Todo Text loaded — to-do text boxes raised to {MaxLines} line(s)" +
                        (MaxCharacters == 0 ? ", unlimited characters" : $", {MaxCharacters} characters") +
                        (DisableEllipsis ? ", ellipsis removed" : "") +
                        (EnableWordWrap ? ", word-wrap on" : "") +
                        (GrowCells ? ", rows auto-grow" : "") + ".");
        }

        private static MethodBase ResolveSetupMultiLineSubmit()
        {
            var type = AccessTools.TypeByName("InputFieldExtensions");
            if (type == null)
                return null;
            // Extension method: static void SetupMultiLineSubmit(this TMP_InputField inputField)
            return AccessTools.Method(type, "SetupMultiLineSubmit");
        }

        // Patches every (non-generic) method literally named "Setup" on the given cell type, so we catch the
        // right overload regardless of its argument list. Returns how many it patched.
        private static int PatchCellSetup(Harmony harmony, string typeName)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
            {
                Log.LogWarning($"Cell type '{typeName}' not found — its rows won't auto-grow.");
                return 0;
            }

            var postfix = new HarmonyMethod(typeof(CellSetup_Patch), nameof(CellSetup_Patch.Postfix));
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
    /// Sits on a to-do cell and keeps the row's height matched to its text. Mirrors the game's own
    /// <c>AutoSizingHeightInputFieldView</c> pattern (resize a RectTransform to the input field's preferred
    /// height) but drives it through the cell's layout so the rows below reflow.
    ///
    /// Strategy, picked from what the cell actually lives in:
    ///   • parent layout group that controls child height → write a LayoutElement.preferredHeight
    ///   • parent layout group that doesn't control height → set the RectTransform height directly
    ///   • no layout group found → fall back to TMP font auto-size (shrink to fit) so nothing overlaps
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
        private float _lastApplied = -1f;
        private bool _haveMinHeight;

        private void Awake()
        {
            _rt = transform as RectTransform;
            _input = GetComponentInChildren<TMP_InputField>(true);
            if (_input != null)
                _text = _input.textComponent;
        }

        private void EnsureGroup()
        {
            if (transform.parent == _lastParent)
                return;
            _lastParent = transform.parent;
            _group = GetComponentInParent<HorizontalOrVerticalLayoutGroup>();
            _groupControlsHeight = _group != null && _group.childControlHeight;
            if (_groupControlsHeight && _layoutElement == null)
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

            // No layout group to reflow into → can't safely grow without overlapping neighbours.
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
                    return; // not laid out yet — try again next frame
                _haveMinHeight = true;
            }

            float desired = Mathf.Max(_minHeight, _text.preferredHeight + Plugin.CellPadding);
            if (Mathf.Abs(desired - _lastApplied) < 0.5f)
                return;
            _lastApplied = desired;

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

            if (_rt.parent is RectTransform parent)
                LayoutRebuilder.MarkLayoutForRebuild(parent);
        }
    }
}

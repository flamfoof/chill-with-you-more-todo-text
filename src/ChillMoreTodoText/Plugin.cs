using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;

namespace ChillMoreTodoText
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.flamfoof.chillmoretodotext";
        public const string Name = "Chill More Todo Text";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        // Config — resolved once in Awake and read by the patch.
        internal static int MaxLines;
        internal static int MaxCharacters;
        internal static bool DisableEllipsis;
        internal static bool EnableWordWrap;

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

            MaxLines = maxLinesCfg.Value;
            MaxCharacters = maxCharsCfg.Value;
            DisableEllipsis = ellipsisCfg.Value;
            EnableWordWrap = wrapCfg.Value;

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

            var harmony = new Harmony(Guid);

            // Every multi-line text box in the game (to-do items, to-do list titles, habit names) is set up
            // through the one shared helper InputFieldExtensions.SetupMultiLineSubmit(TMP_InputField). We patch
            // that single chokepoint so the fix reaches all of them, including cells created later as you scroll.
            MethodBase target = ResolveSetupMultiLineSubmit();
            if (target == null)
            {
                Log.LogWarning("Could not find InputFieldExtensions.SetupMultiLineSubmit — the game may have " +
                               "changed. To-do text limits left at vanilla.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(
                typeof(SetupMultiLineSubmit_Patch), nameof(SetupMultiLineSubmit_Patch.Postfix)));

            Log.LogInfo($"Chill More Todo Text loaded — to-do text boxes raised to {MaxLines} line(s)" +
                        (MaxCharacters == 0 ? ", unlimited characters" : $", {MaxCharacters} characters") +
                        (DisableEllipsis ? ", ellipsis removed" : "") +
                        (EnableWordWrap ? ", word-wrap on" : "") + ".");
        }

        private static MethodBase ResolveSetupMultiLineSubmit()
        {
            var type = AccessTools.TypeByName("InputFieldExtensions");
            if (type == null)
                return null;
            // Extension method: static void SetupMultiLineSubmit(this TMP_InputField inputField)
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
}

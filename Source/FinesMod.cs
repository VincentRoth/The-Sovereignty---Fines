using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SEX_Machinists;
using UnityEngine;
using Verse;

namespace SovereigntyFines
{
    public class FinesMod : Mod
    {
        public static FinesSettings Settings { get; private set; }

        public FinesMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<FinesSettings>();
            ApplyPatches();
        }

        public override string SettingsCategory()
        {
            return "SEXF_SettingsTitle".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            SettingsWindow.Draw(inRect);
        }

        private static void ApplyPatches()
        {
            Harmony harmony = new Harmony("Local.SovereigntyFines");

            // Inspection on/off switch (does not depend on reflection).
            TryPatch(harmony, AccessTools.Method(typeof(SEX_PolicyManager), "SpawnInspection"),
                "SEX_PolicyManager.SpawnInspection", nameof(Patches.SpawnInspectionPrefix), null);

            // Fine system: needs the original's private sets and ConsequencesOfMyActions.
            if (Sets.Validate())
            {
                TryPatch(harmony, AccessTools.Method(typeof(SEX_PolicyManager), "MachinistInspection"),
                    "SEX_PolicyManager.MachinistInspection", nameof(Patches.MachinistInspectionPrefix), null);
            }

            // Comms console: redemption option.
            MethodInfo dialog = typeof(FactionDialogMaker)
                .GetMethods(AccessTools.all)
                .FirstOrDefault(m => m.Name == "FactionDialogFor" && m.ReturnType == typeof(DiaNode));
            TryPatch(harmony, dialog, "FactionDialogMaker.FactionDialogFor", null, nameof(Patches.FactionDialogPostfix));

            Log.Message("[SovereigntyFines] Loaded.");
        }

        private static void TryPatch(Harmony harmony, MethodBase original, string label, string prefix, string postfix)
        {
            try
            {
                if (original == null)
                {
                    Log.Error("[SovereigntyFines] Method not found, patch skipped: " + label);
                    return;
                }
                harmony.Patch(
                    original,
                    prefix: prefix != null ? new HarmonyMethod(typeof(Patches), prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(typeof(Patches), postfix) : null);
            }
            catch (Exception ex)
            {
                Log.Error("[SovereigntyFines] Patch failed for " + label + ": " + ex);
            }
        }
    }
}

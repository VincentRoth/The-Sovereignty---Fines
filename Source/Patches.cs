using System;
using System.Collections.Generic;
using RimWorld;
using SEX_Machinists;
using UnityEngine;
using Verse;

namespace SovereigntyFines
{
    internal static class Patches
    {
        /// <summary>Replaces SEX_PolicyManager.MachinistInspection(): same letters, but offers a fine before the warning.</summary>
        public static bool MachinistInspectionPrefix(SEX_PolicyManager __instance)
        {
            if (!FinesMod.Settings.inspectionEnabled)
            {
                return false; // inspections disabled: no infraction, no warning
            }
            try
            {
                InspectionFlow.Run(__instance);
                return false; // original skipped
            }
            catch (Exception ex)
            {
                Log.Error("[SovereigntyFines] Inspection flow failed, falling back to the original mod: " + ex);
                return true;
            }
        }

        /// <summary>Stops the inspection incident from being spawned when the option is off.</summary>
        public static bool SpawnInspectionPrefix()
        {
            return FinesMod.Settings.inspectionEnabled;
        }

        /// <summary>Adds the "pay the exceptional fine" option to the comms console dialog of the Sovereignty.</summary>
        public static void FactionDialogPostfix(DiaNode __result, object[] __args)
        {
            try
            {
                Pawn negotiator = __args.Length > 0 ? __args[0] as Pawn : null;
                Faction faction = __args.Length > 1 ? __args[1] as Faction : null;
                if (__result == null || faction == null || faction.def.defName != "SEX_Machinists")
                {
                    return;
                }

                SEX_PolicyManager policy = Find.World.GetComponent<SEX_PolicyManager>();
                if (policy == null || !(policy.MachinistsUpset == 1 || faction.def.permanentEnemy))
                {
                    return;
                }

                Map map = negotiator?.Map ?? Find.AnyPlayerHomeMap;
                int fine = FinesMod.Settings.redeemFine;
                int available = SilverUtility.Count(map);

                DiaOption option = new DiaOption("SEXF_RedeemOption".Translate(fine))
                {
                    action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "SEXF_RedeemConfirm".Translate(fine, faction.Name),
                        () => Redeem.Execute(map, faction))),
                    resolveTree = true
                };
                if (available < fine)
                {
                    option.Disable("SEXF_NotEnoughSilver".Translate(available, fine));
                }

                // Insert before the last option (the "Disconnect" entry).
                int index = Math.Max(0, __result.options.Count - 1);
                __result.options.Insert(index, option);
            }
            catch (Exception ex)
            {
                Log.Error("[SovereigntyFines] Comms dialog patch failed: " + ex);
            }
        }
    }

    internal static class InspectionFlow
    {
        // Mirrors the cooldown in SEX_PolicyManager.ConsequencesOfMyActions (no warning counted before it).
        private const int WarningCooldownTicks = 7600;

        public static void Run(SEX_PolicyManager policy)
        {
            Map map = Find.CurrentMap; // same map the original mod inspects
            if (map == null)
            {
                return;
            }

            List<FineLine> lines = FineCalculator.Inspect(map);
            if (lines.Count == 0)
            {
                return;
            }

            // Same letters as the original mod, one per category found.
            foreach (FineLine line in lines)
            {
                string key = line.Category.LetterKey;
                Find.LetterStack.ReceiveLetter(key.Translate(), (key + "Description").Translate(), LetterDefOf.NeutralEvent);
            }

            // Relation loss is applied right away, whatever the player decides about the fine.
            Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named("SEX_Machinists"));
            int requestedLoss = FineCalculator.TotalRelationLoss(lines);
            int baseLoss = RelationUtility.Lose(faction, requestedLoss);
            if (faction != null && faction.HostileTo(Faction.OfPlayer))
            {
                // The game rules made the faction hostile: no fine can change that.
                return;
            }

            FineGameComponent comp = FineGameComponent.Instance;
            if (comp == null)
            {
                FineGameComponent.ApplyWarning(false);
                return;
            }

            comp.SettlePreviousAsRefused();

            // The original mod would not count a warning during its cooldown: nothing to pay for.
            if (policy.LastWarningTicks <= WarningCooldownTicks)
            {
                return;
            }

            int total = FineCalculator.Total(lines);
            if (total <= 0)
            {
                Messages.Message("SEXF_FineWaived".Translate(), MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            string breakdown = string.Join("\n", lines.ConvertAll(l => l.Format()));
            // Extra loss if unpaid, computed on the requested loss and limited by the floor.
            int extraLoss = RelationUtility.Clamp(faction,
                Mathf.RoundToInt(requestedLoss * (FinesMod.Settings.unpaidMultiplier - 1f)));
            comp.Offer(map, breakdown, total, baseLoss, extraLoss);
        }
    }

    internal static class Redeem
    {
        private const int ResetGoodwill = -50;

        /// <summary>Pays the exceptional fine and lifts the permanent hostility.</summary>
        public static void Execute(Map map, Faction faction)
        {
            SEX_PolicyManager policy = Find.World.GetComponent<SEX_PolicyManager>();
            if (policy == null || faction == null)
            {
                return;
            }

            int fine = FinesMod.Settings.redeemFine;
            if (!SilverUtility.TryPay(map, fine))
            {
                Messages.Message("SEXF_NotEnoughSilverMessage".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Faction player = Faction.OfPlayer;

            // Order matters:
            // 1. permanentEnemy must be off or the goodwill cannot change;
            // 2. relation first, then MachinistsUpset, otherwise WorldComponentTick re-triggers the permanent enemy.
            faction.def.permanentEnemy = false;
            int before = faction.PlayerGoodwill;
            bool changed = faction.TryAffectGoodwillWith(player, ResetGoodwill - before, false, false);
            if (faction.PlayerGoodwill != ResetGoodwill)
            {
                // The game refused or ignored the change: write the base goodwill on both relations directly.
                Log.Warning("[SovereigntyFines] TryAffectGoodwillWith returned " + changed + ", goodwill " + before
                    + " -> " + faction.PlayerGoodwill + ". Forcing " + ResetGoodwill + ".");
                RelationUtility.SetBaseGoodwill(faction, player, ResetGoodwill);
                RelationUtility.SetBaseGoodwill(player, faction, ResetGoodwill);
            }
            SetNeutral(faction, player);
            SetNeutral(player, faction);
            if (faction.PlayerGoodwill != ResetGoodwill)
            {
                Log.Warning("[SovereigntyFines] Goodwill is " + faction.PlayerGoodwill + " after redemption (expected " + ResetGoodwill + ").");
            }

            policy.MachinistWarnings = 0;
            policy.MachinistsUpset = 0;
            FineGameComponent.Instance?.Clear();

            Messages.Message("SEXF_RedeemDone".Translate(faction.Name), MessageTypeDefOf.PositiveEvent, false);
        }

        // Going from -100 to -50 does not leave Hostile by itself (the game switches back to Neutral at >= 0).
        private static void SetNeutral(Faction a, Faction b)
        {
            FactionRelation relation = a.RelationWith(b, true);
            if (relation != null)
            {
                relation.kind = FactionRelationKind.Neutral;
            }
        }
    }
}

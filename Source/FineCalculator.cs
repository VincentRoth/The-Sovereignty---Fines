using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SovereigntyFines
{
    public class FineLine
    {
        public FineCategory Category;
        public int Count;      // elements found
        public int Billed;     // elements actually billed (after cap); 0 in fixed mode
        public bool FixedMode;
        public int Amount;     // final amount in silver (multiplier applied)
        public int RelationLoss; // goodwill points lost for this category (0 if disabled)

        public string Format()
        {
            string name = Category.NameKey.Translate().ToString();
            if (FixedMode)
            {
                return "SEXF_LineFixed".Translate(name, Amount).ToString();
            }
            if (Count > Billed)
            {
                return "SEXF_LineCountCapped".Translate(name, Count, Billed, Amount).ToString();
            }
            return "SEXF_LineCount".Translate(name, Count, Amount).ToString();
        }
    }

    public static class FineCalculator
    {
        /// <summary>Runs every enabled category check on the map and prices what it finds.</summary>
        public static List<FineLine> Inspect(Map map)
        {
            FinesSettings settings = FinesMod.Settings;
            List<FineLine> lines = new List<FineLine>();

            foreach (FineCategory cat in FineCategories.All)
            {
                CategorySetting cs = settings.Get(cat);
                if (!cs.included || !cat.IsAvailable)
                {
                    continue;
                }

                int count = cat.Counter(map);
                if (Prefs.DevMode)
                {
                    Log.Message("[SovereigntyFines] Inspection count - " + cat.Id + ": " + count);
                }
                if (count <= 0)
                {
                    continue;
                }

                bool perElement = cat.SupportsPerElement && cs.perElement;
                int billed = 0;
                int baseAmount;
                if (perElement)
                {
                    billed = Mathf.Min(count, settings.maxBilledElements);
                    baseAmount = cs.perAmount * billed;
                }
                else
                {
                    baseAmount = cs.fixedAmount;
                }

                lines.Add(new FineLine
                {
                    Category = cat,
                    Count = count,
                    Billed = billed,
                    FixedMode = !perElement,
                    Amount = Mathf.RoundToInt(baseAmount * settings.globalMultiplier),
                    RelationLoss = settings.relationLossEnabled ? cs.relationLoss : 0
                });
            }

            return lines;
        }

        public static int Total(List<FineLine> lines)
        {
            return lines.Sum(l => l.Amount);
        }

        public static int TotalRelationLoss(List<FineLine> lines)
        {
            return lines.Sum(l => l.RelationLoss);
        }
    }

    /// <summary>Goodwill loss with the Sovereignty, through the game's own API so normal rules apply
    /// (messages, hostility threshold...). No floor: if the faction turns hostile it is by the game rules.</summary>
    internal static class RelationUtility
    {
        public static void Lose(Faction faction, int points)
        {
            if (faction == null || points <= 0)
            {
                return;
            }
            int before = faction.PlayerGoodwill;
            faction.TryAffectGoodwillWith(Faction.OfPlayer, -points, true, true);
            if (Prefs.DevMode)
            {
                Log.Message("[SovereigntyFines] Relation loss requested: -" + points + " (goodwill " + before + " -> " + faction.PlayerGoodwill + ")");
            }
        }
    }

    /// <summary>
    /// Silver handling, same logic as caravan trading: silver in storage (or in the home area),
    /// reachable, not fogged, not forbidden.
    /// </summary>
    internal static class SilverUtility
    {
        public static List<Thing> Collect(Map map)
        {
            List<Thing> result = new List<Thing>();
            if (map == null)
            {
                return result;
            }
            foreach (Thing t in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                if (!t.Spawned || t.Position.Fogged(map) || t.IsForbidden(Faction.OfPlayer))
                {
                    continue;
                }
                if (map.areaManager.Home[t.Position] || t.IsInAnyStorage())
                {
                    result.Add(t);
                }
            }
            return result;
        }

        public static int Count(Map map)
        {
            return Collect(map).Sum(t => t.stackCount);
        }

        public static bool TryPay(Map map, int amount)
        {
            List<Thing> things = Collect(map);
            if (things.Sum(t => t.stackCount) < amount)
            {
                return false;
            }
            int remaining = amount;
            foreach (Thing t in things)
            {
                if (remaining <= 0)
                {
                    break;
                }
                int take = Math.Min(remaining, t.stackCount);
                t.SplitOff(take).Destroy();
                remaining -= take;
            }
            return true;
        }
    }
}

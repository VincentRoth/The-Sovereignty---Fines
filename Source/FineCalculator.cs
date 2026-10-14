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

    /// <summary>
    /// Goodwill changes with the Sovereignty. Losses never take the relation below GoodwillFloor:
    /// at -75 the game turns the faction hostile and the original mod then makes it a permanent enemy.
    /// Only the warnings are meant to lead to that. Values are written exactly (the game's own API
    /// may adjust amounts), and no hostility check is triggered since the floor is above the threshold.
    /// </summary>
    internal static class RelationUtility
    {
        public const int GoodwillFloor = -74;

        /// <summary>How many points can actually be lost without going below the floor.</summary>
        public static int Clamp(Faction faction, int points)
        {
            if (faction == null || points <= 0)
            {
                return 0;
            }
            return Mathf.Clamp(points, 0, Mathf.Max(0, faction.PlayerGoodwill - GoodwillFloor));
        }

        /// <summary>Applies the loss (clamped) and returns the number of points actually lost.</summary>
        public static int Lose(Faction faction, int points)
        {
            int applied = Clamp(faction, points);
            if (applied <= 0)
            {
                return 0;
            }
            int before = faction.PlayerGoodwill;
            int target = before - applied;
            Faction player = Faction.OfPlayer;
            if (!SetBaseGoodwill(faction, player, target) || !SetBaseGoodwill(player, faction, target))
            {
                // Field not reachable: fall back on the game's API (amount already clamped).
                faction.TryAffectGoodwillWith(player, -applied, false, false);
            }
            if (Prefs.DevMode)
            {
                Log.Message("[SovereigntyFines] Relation loss: -" + applied + " (requested " + points + ", goodwill " + before + " -> " + faction.PlayerGoodwill + ")");
            }
            Messages.Message("SEXF_RelationLost".Translate(faction.Name, applied, faction.PlayerGoodwill), MessageTypeDefOf.NegativeEvent, false);
            return applied;
        }

        /// <summary>Writes FactionRelation.baseGoodwill directly (reflection).</summary>
        public static bool SetBaseGoodwill(Faction a, Faction b, int value)
        {
            FactionRelation relation = a.RelationWith(b, true);
            System.Reflection.FieldInfo field = HarmonyLib.AccessTools.Field(typeof(FactionRelation), "baseGoodwill");
            if (relation == null || field == null)
            {
                Log.Error("[SovereigntyFines] Cannot write FactionRelation.baseGoodwill.");
                return false;
            }
            field.SetValue(relation, value);
            return true;
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

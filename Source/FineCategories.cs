using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SEX_Machinists;
using Verse;

namespace SovereigntyFines
{
    /// <summary>
    /// One category of infraction checked by the Sovereignty inspectors.
    /// Mirrors the Has*() checks of SEX_PolicyManager, but counts instead of stopping at the first hit.
    /// </summary>
    public class FineCategory
    {
        public readonly string Id;
        /// <summary>Translation key of the original mod's letter (title); description is key + "Description".</summary>
        public readonly string LetterKey;
        public readonly bool SupportsPerElement;
        public readonly int DefaultPerElement;
        public readonly int DefaultFixed;
        public readonly string RequiredModId;
        public readonly string RequiredModLabel;
        public readonly Func<Map, int> Counter;

        public FineCategory(string id, string letterKey, bool supportsPerElement, int defaultPer, int defaultFixed,
            Func<Map, int> counter, string requiredModId = null, string requiredModLabel = null)
        {
            Id = id;
            LetterKey = letterKey;
            SupportsPerElement = supportsPerElement;
            DefaultPerElement = defaultPer;
            DefaultFixed = defaultFixed;
            Counter = counter;
            RequiredModId = requiredModId;
            RequiredModLabel = requiredModLabel;
        }

        public string NameKey => "SEXF_Cat_" + Id;
        public string UnitKey => "SEXF_Unit_" + Id;
        public bool IsAvailable => RequiredModId == null || ModsConfig.IsActive(RequiredModId);
    }

    /// <summary>Reads the (private static) forbidden-defName sets of the original mod by reflection.</summary>
    internal static class Sets
    {
        private static readonly Dictionary<string, HashSet<string>> cache = new Dictionary<string, HashSet<string>>();

        private static readonly string[] Required =
        {
            "ForbiddenPlants", "ForbiddenAnimals", "ForbiddenGenes", "BloodfeederGenes",
            "CannibalItems", "Drugs", "Antigrain", "CombustiblePlants"
        };

        public static HashSet<string> Get(string field)
        {
            if (cache.TryGetValue(field, out HashSet<string> set))
            {
                return set;
            }
            FieldInfo fi = AccessTools.Field(typeof(SEX_PolicyManager), field);
            set = fi?.GetValue(null) as HashSet<string>;
            if (set == null)
            {
                throw new InvalidOperationException("Cannot read SEX_PolicyManager." + field);
            }
            cache[field] = set;
            return set;
        }

        /// <summary>True if everything we need from the original mod is reachable.</summary>
        public static bool Validate()
        {
            try
            {
                foreach (string name in Required)
                {
                    Get(name);
                }
                if (AccessTools.Method(typeof(SEX_PolicyManager), "ConsequencesOfMyActions") == null)
                {
                    throw new InvalidOperationException("SEX_PolicyManager.ConsequencesOfMyActions not found");
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[SovereigntyFines] Incompatible Sovereignty version, fine system disabled: " + ex.Message);
                return false;
            }
        }
    }

    public static class FineCategories
    {
        // Same order as SEX_PolicyManager.MachinistInspection().
        public static readonly List<FineCategory> All = new List<FineCategory>
        {
            new FineCategory("Plants", "SEX_ForbiddenPlants", false, 0, 300,
                map => CountThings(map, ThingRequestGroup.Plant, Sets.Get("ForbiddenPlants"), false)),

            new FineCategory("Slaves", "SEX_ForbiddenSlaves", true, 200, 600,
                map => CountPawns(map, p => p.IsSlave)),

            new FineCategory("Animals", "SEX_ForbiddenAnimals", true, 75, 250,
                map =>
                {
                    HashSet<string> set = Sets.Get("ForbiddenAnimals");
                    return CountPawns(map, p => p.RaceProps != null && p.RaceProps.Animal && set.Contains(p.def.defName));
                }),

            new FineCategory("Mechs", "SEX_ForbiddenMechs", true, 100, 400,
                map => CountPawns(map, p => p.RaceProps.IsMechanoid)),

            new FineCategory("Androids", "SEX_ForbiddenAndroids", true, 400, 1000,
                map => CountColonistsWithGene(map, Sets.Get("ForbiddenGenes")),
                "vanillaracesexpanded.android", "Vanilla Races Expanded - Android"),

            new FineCategory("Cannibal", "SEX_CannibalItems", true, 25, 150,
                map => CountThings(map, ThingRequestGroup.HaulableAlways, Sets.Get("CannibalItems"), true)),

            new FineCategory("Drugs", "SEX_Drugs", true, 100, 300,
                map => CountThings(map, ThingRequestGroup.HaulableAlways, Sets.Get("Drugs"), true)),

            new FineCategory("Antigrain", "SEX_Anti", true, 400, 800,
                map => CountThings(map, ThingRequestGroup.HaulableAlways, Sets.Get("Antigrain"), true)),

            new FineCategory("Blazebulb", "SEX_Blazebulb", false, 0, 250,
                map => CountThings(map, ThingRequestGroup.Plant, Sets.Get("CombustiblePlants"), false),
                "CETeam.CombatExtended", "Combat Extended"),

            new FineCategory("Bloodfeeders", "SEX_Bloodfeeders", true, 400, 1000,
                map => CountColonistsWithGene(map, Sets.Get("BloodfeederGenes")))
        };

        // Counts Things (one per stack) whose defName is in the set.
        private static int CountThings(Map map, ThingRequestGroup group, HashSet<string> set, bool skipFogged)
        {
            int n = 0;
            foreach (Thing t in map.listerThings.ThingsInGroup(group))
            {
                if (!set.Contains(t.def.defName))
                {
                    continue;
                }
                if (skipFogged && t.Position.Fogged(map))
                {
                    continue;
                }
                n++;
            }
            return n;
        }

        private static int CountPawns(Map map, Predicate<Pawn> predicate)
        {
            int n = 0;
            foreach (Pawn p in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
            {
                if (predicate(p))
                {
                    n++;
                }
            }
            return n;
        }

        private static int CountColonistsWithGene(Map map, HashSet<string> geneDefNames)
        {
            int n = 0;
            foreach (Pawn p in map.mapPawns.FreeColonists)
            {
                if (p.genes == null)
                {
                    continue;
                }
                foreach (Gene g in p.genes.GenesListForReading)
                {
                    if (geneDefNames.Contains(g.def.defName))
                    {
                        n++;
                        break;
                    }
                }
            }
            return n;
        }
    }
}

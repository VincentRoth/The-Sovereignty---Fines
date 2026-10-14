using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SovereigntyFines
{
    public class CategorySetting
    {
        public bool included = true;
        public bool perElement = true;
        public int perAmount;
        public int fixedAmount;
    }

    public class FinesSettings : ModSettings
    {
        public const int DefaultRedeemFine = 8000;
        public const int DefaultMaxBilled = 10;

        public bool inspectionEnabled = true;
        public float globalMultiplier = 1f;
        public int maxBilledElements = DefaultMaxBilled;
        public int redeemFine = DefaultRedeemFine;

        private readonly Dictionary<string, CategorySetting> categories = new Dictionary<string, CategorySetting>();

        public CategorySetting Get(FineCategory cat)
        {
            if (!categories.TryGetValue(cat.Id, out CategorySetting s))
            {
                s = new CategorySetting
                {
                    included = true,
                    perElement = true,
                    perAmount = cat.DefaultPerElement,
                    fixedAmount = cat.DefaultFixed
                };
                categories[cat.Id] = s;
            }
            return s;
        }

        public void ResetToDefaults()
        {
            inspectionEnabled = true;
            globalMultiplier = 1f;
            maxBilledElements = DefaultMaxBilled;
            redeemFine = DefaultRedeemFine;
            categories.Clear(); // Get() recreates defaults on demand
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inspectionEnabled, "inspectionEnabled", true);
            Scribe_Values.Look(ref globalMultiplier, "globalMultiplier", 1f);
            Scribe_Values.Look(ref maxBilledElements, "maxBilledElements", DefaultMaxBilled);
            Scribe_Values.Look(ref redeemFine, "redeemFine", DefaultRedeemFine);

            foreach (FineCategory cat in FineCategories.All)
            {
                CategorySetting s = Get(cat);
                Scribe_Values.Look(ref s.included, cat.Id + "_included", true);
                Scribe_Values.Look(ref s.perElement, cat.Id + "_perElement", true);
                Scribe_Values.Look(ref s.perAmount, cat.Id + "_perAmount", cat.DefaultPerElement);
                Scribe_Values.Look(ref s.fixedAmount, cat.Id + "_fixedAmount", cat.DefaultFixed);
            }
        }
    }

    internal static class SettingsWindow
    {
        private static Vector2 scroll;
        private static float viewHeight = 2000f;

        private static int Snap(float value, int step)
        {
            return Mathf.RoundToInt(value / step) * step;
        }

        public static void Draw(Rect inRect)
        {
            FinesSettings s = FinesMod.Settings;

            Rect view = new Rect(0f, 0f, inRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(inRect, ref scroll, view);
            Listing_Standard l = new Listing_Standard();
            l.Begin(view);

            l.CheckboxLabeled("SEXF_InspectionEnabled".Translate(), ref s.inspectionEnabled,
                "SEXF_InspectionEnabledTip".Translate());
            l.Gap(6f);

            l.Label("SEXF_GlobalMultiplier".Translate(s.globalMultiplier.ToString("0.00")));
            s.globalMultiplier = Snap(l.Slider(s.globalMultiplier * 100f, 25f, 300f), 5) / 100f;

            l.Label("SEXF_MaxBilled".Translate(s.maxBilledElements));
            s.maxBilledElements = Mathf.RoundToInt(l.Slider(s.maxBilledElements, 1f, 50f));

            l.Label("SEXF_RedeemFine".Translate(s.redeemFine));
            s.redeemFine = Snap(l.Slider(s.redeemFine, 1000f, 50000f), 250);

            l.Gap(4f);
            if (l.ButtonText("SEXF_ResetDefaults".Translate()))
            {
                s.ResetToDefaults();
            }

            foreach (FineCategory cat in FineCategories.All)
            {
                CategorySetting cs = s.Get(cat);

                l.GapLine();
                string title = cat.NameKey.Translate().ToString();
                if (!cat.IsAvailable)
                {
                    title += " " + "SEXF_RequiresMod".Translate(cat.RequiredModLabel).ToString();
                }
                l.CheckboxLabeled(title, ref cs.included, "SEXF_IncludedTip".Translate());

                if (!cs.included)
                {
                    continue;
                }

                l.Indent();

                if (cat.SupportsPerElement)
                {
                    l.CheckboxLabeled(
                        "SEXF_PerElement".Translate(cat.UnitKey.Translate()),
                        ref cs.perElement);

                    GUI.color = cs.perElement ? Color.white : Color.gray;
                    l.Label("SEXF_AmountPerElement".Translate(cs.perAmount));
                    cs.perAmount = Snap(l.Slider(cs.perAmount, 0f, 1000f), 5);
                    GUI.color = Color.white;
                }
                else
                {
                    l.Label("SEXF_FixedOnly".Translate());
                }

                bool fixedActive = !cat.SupportsPerElement || !cs.perElement;
                GUI.color = fixedActive ? Color.white : Color.gray;
                l.Label("SEXF_AmountFixed".Translate(cs.fixedAmount));
                cs.fixedAmount = Snap(l.Slider(cs.fixedAmount, 0f, 5000f), 25);
                GUI.color = Color.white;

                l.Outdent();
            }

            viewHeight = l.CurHeight + 10f;
            l.End();
            Widgets.EndScrollView();
        }
    }
}

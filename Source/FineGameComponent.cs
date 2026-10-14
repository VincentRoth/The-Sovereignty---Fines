using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using SEX_Machinists;
using Verse;

namespace SovereigntyFines
{
    /// <summary>A fine offered after an inspection, waiting for the player's decision (saved with the game).</summary>
    public class PendingFine : IExposable
    {
        public int total;
        public int expiryTick;
        public int mapId = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref total, "total");
            Scribe_Values.Look(ref expiryTick, "expiryTick");
            Scribe_Values.Look(ref mapId, "mapId", -1);
        }

        public Map GetMap()
        {
            Map map = Find.Maps.Find(m => m.uniqueID == mapId);
            return map ?? Find.AnyPlayerHomeMap;
        }
    }

    public class FineGameComponent : GameComponent
    {
        public PendingFine pending;

        public FineGameComponent(Game game)
        {
        }

        public static FineGameComponent Instance => Current.Game?.GetComponent<FineGameComponent>();

        public override void GameComponentTick()
        {
            if (pending == null)
            {
                return;
            }

            // The faction already turned permanently hostile (e.g. through another path): the offer is moot.
            SEX_PolicyManager policy = Find.World?.GetComponent<SEX_PolicyManager>();
            if (policy != null && policy.MachinistsUpset == 1)
            {
                Clear();
                return;
            }

            if (Find.TickManager.TicksGame >= pending.expiryTick)
            {
                Refuse(true);
            }
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref pending, "pendingFine");
        }

        /// <summary>Creates the pending fine and the choice letter (one day to pay).</summary>
        public void Offer(Map map, string breakdown, int total)
        {
            SEX_PolicyManager policy = Find.World.GetComponent<SEX_PolicyManager>();
            Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named("SEX_Machinists"));

            pending = new PendingFine
            {
                total = total,
                expiryTick = Find.TickManager.TicksGame + GenDate.TicksPerDay,
                mapId = map.uniqueID
            };

            // LetterDef SEXF_FineOffer (1.6/Defs/LetterDefs) maps to ChoiceLetter_FineOffer.
            LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("SEXF_FineOffer");
            ChoiceLetter letter = LetterMaker.MakeLetter(
                "SEXF_FineOfferTitle".Translate(),
                "SEXF_FineOfferText".Translate(faction.NameColored, breakdown, total, policy.MachinistWarnings),
                letterDef,
                faction);
            letter.StartTimeout(GenDate.TicksPerDay);
            Find.LetterStack.ReceiveLetter(letter);
        }

        /// <summary>Settles a fine that is still pending when a new inspection ends (should not happen in practice).</summary>
        public void SettlePreviousAsRefused()
        {
            if (pending != null)
            {
                Refuse(false);
            }
        }

        public void Pay()
        {
            if (pending == null)
            {
                return;
            }
            Map map = pending.GetMap();
            int total = pending.total;
            if (!SilverUtility.TryPay(map, total))
            {
                Messages.Message("SEXF_NotEnoughSilverMessage".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Clear();
            Messages.Message("SEXF_FinePaid".Translate(total), MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>Player refused (expired = false) or the one-day deadline elapsed (expired = true).</summary>
        public void Refuse(bool expired)
        {
            if (pending == null)
            {
                return;
            }
            Clear();
            ApplyWarning(expired);
        }

        public void Clear()
        {
            pending = null;
            List<Letter> letters = Find.LetterStack.LettersListForReading;
            for (int i = letters.Count - 1; i >= 0; i--)
            {
                if (letters[i] is ChoiceLetter_FineOffer)
                {
                    Find.LetterStack.RemoveLetter(letters[i]);
                }
            }
        }

        /// <summary>
        /// Counts the warning exactly like the original mod (private ConsequencesOfMyActions: message,
        /// warning counter, permanent hostility at 3). If the deadline forced it, a letter announces it.
        /// </summary>
        public static void ApplyWarning(bool notifyByLetter)
        {
            SEX_PolicyManager policy = Find.World.GetComponent<SEX_PolicyManager>();
            if (policy == null)
            {
                return;
            }
            int before = policy.MachinistWarnings;
            AccessTools.Method(typeof(SEX_PolicyManager), "ConsequencesOfMyActions").Invoke(policy, null);

            if (notifyByLetter && policy.MachinistWarnings > before)
            {
                Find.LetterStack.ReceiveLetter(
                    "SEXF_FineExpiredTitle".Translate(),
                    "SEXF_FineExpiredText".Translate(policy.MachinistWarnings),
                    LetterDefOf.NegativeEvent);
            }
        }
    }

    public class ChoiceLetter_FineOffer : ChoiceLetter
    {
        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                FineGameComponent comp = FineGameComponent.Instance;
                if (comp == null || comp.pending == null)
                {
                    // Stale letter (should not happen): just let the player dismiss it.
                    yield return new DiaOption("Close".Translate())
                    {
                        action = () => Find.LetterStack.RemoveLetter(this),
                        resolveTree = true
                    };
                    yield break;
                }

                int total = comp.pending.total;
                int available = SilverUtility.Count(comp.pending.GetMap());

                DiaOption pay = new DiaOption("SEXF_PayFineOption".Translate(total))
                {
                    action = () => comp.Pay(),
                    resolveTree = true
                };
                if (available < total)
                {
                    pay.Disable("SEXF_NotEnoughSilver".Translate(available, total));
                }
                yield return pay;

                yield return new DiaOption("SEXF_RefuseFineOption".Translate())
                {
                    action = () => comp.Refuse(false),
                    resolveTree = true
                };

                // Closes the window; the letter stays and the deadline keeps running.
                yield return new DiaOption("SEXF_DecideLater".Translate())
                {
                    resolveTree = true
                };
            }
        }
    }
}

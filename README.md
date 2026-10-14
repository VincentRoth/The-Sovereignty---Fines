# The Sovereignty - Fines & Inspection Options (RimWorld 1.6)

Companion mod for "The Sovereignty - A Faction Expansion" (Workshop 3488373703).

## Build (Visual Studio)
1. Open `Source/SovereigntyFines.csproj`.
2. Check the two paths at the top (`RimWorldDir`, `WorkshopDir`). The project references:
   RimWorld's `Assembly-CSharp`, Unity modules, Harmony (Workshop 2009463077)
   and `SEX_Machinists.dll` (Workshop 3488373703).
3. Build. The DLL is written to `1.6/Assemblies/SovereigntyFines.dll`.
4. Copy the whole `SovereigntyFines` folder into RimWorld's `Mods` folder.
   Load order: Harmony, The Sovereignty, then this mod.

## What it does
- Inspection ends -> same letters as the original mod, then ONE fine offer (sum of all categories found).
  The original mod counts at most one warning per inspection, so paying a partial amount would change nothing.
- Choice letter, one day to decide:
  - Pay: silver is taken from storage / home area (same rule as caravan trading), no warning.
  - Refuse or deadline reached: the original warning logic runs (message, counter, hostility at 3).
    If it is the deadline, an extra letter announces the warning.
  - "Decide later" only closes the window; the deadline keeps running.
- Settings (Options > Mod settings): inspections on/off, global multiplier, max billed elements,
  exceptional fine amount, and per category: inspected on/off, per element vs fixed, both amounts.
- Comms console: when the faction is a permanent enemy, a new option pays the exceptional fine:
  warnings = 0, MachinistsUpset = 0, permanentEnemy = false, relations Neutral at -50.

## Behaviours worth knowing
- A total of 0 silver (all amounts at 0) settles the inspection without warning.
- Forbidden silver is not usable for payment.
- The original bug (permanentEnemy flag lost on reload) is NOT fixed, on purpose.

## Quick test (dev mode)
1. Spawn a slave (or a drug stack) in the colony.
2. Dev menu > Execute incident > `SEX_Inspection`.
3. Wait for the inspectors to leave (8000-22000 ticks): letters + fine offer appear.
4. To test redemption: set `MachinistsUpset` to 1 and the faction hostile, then open the comms console.

## Relation loss (added)
- After each inspection, the faction's goodwill drops for every category found (flat per category, configurable, 0 = none).
- If the fine is refused or the deadline passes, an extra loss is applied: total = base x "unpaid multiplier" (x2 by default).
- Applied through the game's own goodwill API, no floor: if relations fall low enough the game turns the faction
  hostile, and the original mod then makes it a permanent enemy (its own behaviour, not forced by this mod).
- If the faction turns hostile at inspection time, no fine is offered (paying cannot change it); use the comms console redemption.

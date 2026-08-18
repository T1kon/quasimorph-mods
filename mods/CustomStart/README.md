# CustomStart

CustomStart creates a new Quasimorph campaign that looks as though the world has already been
running for a while. It ships with four profiles: `Early`, `EarlyMid`, `Mid`, and `Late`.

The mod changes only newly created games. Loading an existing save never arms the generator.
CustomStart stores no custom save component, so a generated campaign remains loadable after the
mod is disabled or removed; the generated date, items, unlocks, and faction state simply remain in
that save.

## What a profile generates

| Profile | Elapsed days | Faction tech | Total clones / classes | Magnum nodes | Weapons / armor sets | Ammo types (common + specialist) | Augments / implants | Learned recipes | Material types |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `Early` | 180 | 2–3, spread 1 | 5 / 5 | 8 | 4 / 1 | 5 + 1 | 3 / 0 | 8 | 12 |
| `EarlyMid` | 420 | 3–5, spread 2 | 8 / 7 | 32 | 10 / 3 | 6 + 3 | 6 / 3 | 24 | 20 |
| `Mid` | 800 | 5–7, spread 2 | 14 / 12 | 90 | 20 / 5 | 7 + 6 | 10 / 12 | 55 | 28 |
| `Late` | 1960 | 9–10, spread 1 | all / all | all | 30 / 8 | 8 + 12 | 18 / 25 | 120 | 40 |

Clone and class counts are targets for the total starting roster, including the vanilla grants.
CustomStart never removes a vanilla starting clone or class if the selected difficulty already
provides more than a profile requests.

Each preset's elapsed time is treated as a campaign-age prior when calibrating its bundled budgets,
not merely as a calendar label. It is not a live formula: changing `ElapsedDays` alone does not
recalculate the other settings. The Mid profile is an established 2202 hard-difficulty campaign:
most Magnum progression is complete and the stash is large, but it is biased toward accumulated
common ammunition, medicine, repair supplies, ordinary weapons, and crafting materials rather
than an implausible pile of rare components. `EarlyMid` fills the former progression gap with a
developing 420-day campaign; Late remains the effectively completed-campaign option.

Early always includes the Monitoring, Conveyor, and Capsule department roots. EarlyMid adds the
Classes and Genome roots; Mid additionally guarantees the weapon-station and armor-station roots.
`TargetUpgradeCount` still controls the total connected Magnum graph, including the guaranteed
roots and any required path nodes.

The generator performs these operations in order:

1. Move the fresh campaign clock forward before dated items, expiry timers, and clone state are
   created.
2. Select helped and rival factions and generate safe station ownership changes.
3. Estimate each active faction's research economy, then generate correlated faction levels and
   partial tech experience.
4. Add station power and a small amount of pending station research so the world does not look
   freshly initialized.
5. Grant additional clones and classes up to the configured targets.
6. Generate a connected Magnum upgrade graph. Guaranteed nodes include the shortest valid path
   back to a root node.
7. Build a stash from faction rewards, crafted equipment, retained mission loot, bulk everyday
   supplies, augmentation access, and a deliberately limited material stockpile.
8. Add production recipes learned from historical blueprint chips. Crafted armor-set recipes and
   firearm ammunition are expanded the same way the normal unlock flow expands them.
9. Write the exact result to `last-start-report.json`.

## Faction progression model

Faction levels are deliberately not rolled independently.

CustomStart first calculates final station ownership, then estimates weekly research throughput
from each station's faction-specific production recipes, population production multiplier, tech
output, and the selected difficulty's faction growth speed. All factions share a configured world
progress level. Their economy changes that common value by a bounded offset, a small random offset
is added, and the final result is constrained by `MaxActiveFactionSpread`.

This reproduces the important shape of the normal campaign: established factions stay close
together, while factions with stronger station portfolios—often AnCom—tend to lead. Helping a
faction does not grant a flat arbitrary tech level. Its benefits are represented by reputation,
trade history, and eligible station captures, which also affect the economy estimate.

CustomStart is a coherent snapshot generator, not a week-by-week simulation. It does not replay
historical missions, shipping, item consumption, treaties, faction strategies, news, or story
questlines. Story progress remains fresh so the normal opening and quest sequence still run at the
generated date. The plan/apply split is intentionally reusable so a fuller simulation can replace
the snapshot planner later without changing save integration.

## Configuration

### In-game configuration

CustomStart supports Crynano's [Mod Configuration Menu](https://steamcommunity.com/sharedfiles/filedetails/?id=3469678797).
When MCM is installed, open **Mods** from Quasimorph's main menu and select **CustomStart**. MCM is
optional; without it, CustomStart continues to use JSON normally.

The in-game screen exposes the settings most players are likely to change:

- Enable/disable, active profile, deterministic seed, special-faction reputation history, and
  diagnostic reporting.
- Elapsed time, helped/rival faction counts, and station-history counts for the active profile.
- Clone, class, and Magnum progression targets.
- Faction reward counts; weapons, complete armor sets, common/specialist ammunition, medicine,
  repair kits, augmentations, implants, and learned-recipe targets for the active profile.
- Common-ammunition and repair stack sizes, material-stockpile size, rare-material cap, common
  material stacks, upgrade-material units, and upgrade-material tier.

The current MCM build does not render its advertised free-form text-box control, so CustomStart
uses a supported numeric seed field plus a **Random seed** toggle. The in-game numeric field covers
`-10000000` through `10000000`; the JSON setting still accepts any signed 32-bit integer.

Only the active preset's detailed World, Progression, and Stash sections are displayed. Selecting a
different active profile and saving rebuilds the CustomStart page with that profile's controls;
settings belonging to the previously displayed profile are saved before the switch.

MCM writes the canonical JSON file. Changes apply to the next genuinely new campaign; they do not
rewrite an existing save. Exact faction IDs, roster allow/exclude lists, guaranteed IDs, economy
model parameters, and detailed weighting remain advanced JSON settings.

### JSON configuration

The persistent configuration is stored at:

```text
%USERPROFILE%\AppData\LocalLow\Magnum Scriptum LTD\Quasimorph_ModConfigs\CustomStart\config.json
```

That file is the canonical configuration and is reloaded whenever a new game is created. Editing
the installed copy inside `LocalUserPresets\CustomStart` is also supported: if it is newer and
differs from the packaged defaults, CustomStart uses it for the next new game and imports it into
the persistent location. An unchanged installed default never overwrites a persistent setup after
a reinstall or Workshop update. The selected path is logged and written as `ConfigSource` in the
start report.

Changing profiles does not require rebuilding the DLL. Restarting the game is still recommended
after replacing the mod assembly.

Top-level settings:

| Setting | Meaning |
| --- | --- |
| `Enabled` | When false, new games use the vanilla start. |
| `ActiveProfile` | Name of the profile used for the next new game. |
| `Seed` | `null` creates a new random seed. An integer makes the generated plan reproducible. |
| `WriteReport` | Writes the exact generated outcome after each new game. |
| `DisableStationTransfersOnUnknownBuild` | On an unvalidated game assembly, suppress only station transfers while keeping the rest of the profile active. |
| `AllowCivilResistanceAndTezctlanReputationChanges` | When true, Civil Resistance and Tezctlan may be selected as helped or rival factions. Defaults to false. |

Faction selectors support two modes. An explicit selection uses `Ids`:

```json
"HelpedFactions": {
  "Mode": "Explicit",
  "Count": 0,
  "Ids": ["AnCom", "RealWare"],
  "AllowedIds": [],
  "ExcludedIds": []
}
```

A random selection uses `Count`; `AllowedIds` limits its pool and `ExcludedIds` removes choices.
The helped and rival sets are always disjoint. Civil Resistance (`CResistance`) and Tezctlan are
excluded from reputation history by default, although they still receive normal faction tech and
world progression. Set `AllowCivilResistanceAndTezctlanReputationChanges` to `true` to include both
in explicit and random helped/rival selections.

Roster restrictions apply to additional grants. Guaranteed IDs are attempted first, allowed lists
limit the random grant pool, and excluded lists remove entries:

```json
"Roster": {
  "TargetCloneCount": 8,
  "TargetClassCount": 7,
  "GuaranteedCloneIds": ["mercenary_profile_internal_id"],
  "GuaranteedClassIds": [],
  "AllowedCloneIds": [],
  "AllowedClassIds": [],
  "ExcludedCloneIds": [],
  "ExcludedClassIds": []
}
```

`TargetCloneCount`, `TargetClassCount`, and `TargetUpgradeCount` use `-1` to mean every eligible
record. `GuaranteedItems` maps an internal item ID to a number of individual units:

```json
"GuaranteedItems": {
  "item_internal_id": 3
}
```

Stash generation uses four complementary histories. Their budgets are separate so recipe demand
cannot consume the slots intended for weapons, armor, medicine, or ammunition.

**Notable faction rewards** come from the helped faction's actual reward table at its generated
tech and player reputation. `RewardSelection` estimates which rewards a player would plausibly
keep:

- The vanilla starting cargo and guaranteed items count as already owned.
- Missing weapon, armor, carrying, medical, maintenance, ammunition, provision, utility,
  augmentation, and knowledge roles receive a bonus.
- Repeated item IDs receive a strong penalty; repeated role groups receive a smaller penalty.
- Faction reward weight remains the base likelihood, with small tech-level and price modifiers.
- Random selection is limited to the strongest fraction of the eligible pool.
- Equipment and chips default to one copy per item. Consumable limits are two in `Early`, three
  in `EarlyMid` and `Mid`, and four in `Late`.

**Practical inventory history** models what a player keeps rather than another series of reward
rolls:

- The arsenal mixes ranged and melee weapons and favors different weapon/ammunition roles. Half of
  the selected equipment is treated as Magnum-produced where a recipe exists; the rest is retained
  loot or faction rewards.
- Armor is selected as complete helmet/body/legs/boots sets instead of unrelated individual pieces.
- Common ammunition is ranked by how many stage-appropriate weapons use it, how often it appears in
  container tables, its tech, and its price. It arrives in bulk. Specialist and faction ammunition
  is matched to selected weapons and helped-faction availability, then granted in smaller reserves.
- Medical stock strongly favors low-cost, frequently looted Medical-category items such as sorbent,
  bandages, and splints. Specialist medicine receives fewer stacks.
- Repair kits are selected across different repair roles. Augmentations and implants use separate
  stage caps; Early has basic cybernetics and no implants, EarlyMid introduces basic implants and
  improved cybernetics, Mid includes recreational/military cybernetics and common implants, and
  Late may include quasi items.

**Recipe history** directly records production recipes already learned from past blueprint chips.
The Mid default targets at least 55 learned recipes. Armor chips expand to the whole armor set and
weapon recipes include their default ammunition when those recipes exist, matching the normal game
unlock behavior. Physical unopened chip rewards can still appear separately.

**Accumulated stockpile materials** model a loot-oriented player instead of making more reward
rolls. CustomStart derives the candidates directly from the current game's crafting recipes and
Magnum project price tables:

- Frequently required inputs such as plates, plastic, rubber, electronics, weapon parts, powder,
  and repair components rank highly.
- Class, clone, weapon, armor, and other development currencies enter at their configured Magnum
  price grade. High-grade currencies are unavailable in earlier profiles.
- Expiring ingredients are excluded using the game's actual expiry table.
- Candidate tech cannot exceed the generated world's current faction tech.
- Items present in helped-faction reward pools receive an availability bonus, but retained mission
  loot is also represented.
- Container-table frequency separates common loot from uncommon components. A configurable rare-ID
  list also covers advanced components whose raw recipe demand would otherwise make them dominate.
- Only a stage-specific minority of the selected material types may be rare. Common crafting
  materials arrive as multiple full stacks; rare crafting parts and development currencies arrive
  as individual units.

The behavior can be tuned per profile:

| Setting | Meaning |
| --- | --- |
| `Enabled` | `false` restores the original raw faction-weighted rolls. |
| `MaxEquipmentCopiesPerItem` | Equipment copy cap; `-1` disables the cap. |
| `MaxConsumableCopiesPerItem` | Consumable copy cap; `-1` disables the cap. |
| `MaxChipCopiesPerItem` | Chip copy cap; `-1` disables the cap. |
| `DuplicateItemWeight` | Multiplier for each already-owned copy of the exact item. |
| `DuplicateGroupWeight` | Multiplier for each existing item in the same practical role. |
| `MissingGroupWeight` | Bonus when the practical role is absent. |
| `FactionWeightExponent` | Controls how strongly the faction's original reward weight matters. |
| `TechLevelWeight` | Small preference for higher-tech items available at the generated stage. |
| `PriceWeight` | Small logarithmic preference for more valuable rewards. |
| `TopCandidateFraction` | Fraction of best-scoring candidates kept for the random draw. |
| `MinimumCandidatePoolSize` | Minimum number of top candidates retained when available. |

If every authentic candidate has reached its copy cap, the generator grants fewer items instead
of filling the stash with implausible duplicates. The report records `SelectionGroup` and
`SelectionScore` for each reward so the defaults can be tuned from actual runs.

Role-stockpile settings:

| Setting | Meaning |
| --- | --- |
| `WeaponItems` / `ArmorSets` | Historical spare-weapon count and complete armor-set count. |
| `CommonAmmoTypes` / `SpecialAmmoTypes` | Distinct bulk and specialist ammunition families. |
| `CommonAmmoStacks` / `SpecialAmmoStacks` | Full stacks granted for each selected ammunition family. |
| `MedicalItemTypes` | Distinct medical supplies; 70% of the target is reserved for basic medicine when possible. |
| `BasicMedicineStacks` / `PremiumMedicineStacks` | Full stacks for cheap/common and specialist medicine. |
| `RepairKitTypes` / `RepairKitStacks` | Distinct repair roles and full stacks per selected kit. |
| `AugmentationItems` / `ImplantItems` | Uninstalled stage-appropriate spares. |
| `MaximumAugmentationTech` / `MaximumImplantTech` | Independent access ceilings for the two systems. |
| `ProductionRecipeUnlocks` | Minimum learned production-recipe target. |
| `AllowQuasiItems` | Permit quasi equipment, augments, and implants in this profile. |

Material-stockpile settings:

| Setting | Meaning |
| --- | --- |
| `Enabled` | Enable accumulated crafting and upgrade materials. |
| `TargetDistinctItems` | Target number of different retained materials. |
| `MinimumRecipeUses` | Minimum number of current recipes that must use a normal crafting input. |
| `MaximumUpgradeGrade` | Highest Magnum price grade permitted; `-1` allows every grade. |
| `MinimumCraftingStacks` / `MaximumCraftingStacks` | Full-stack range for common recipe materials. |
| `MinimumUpgradeUnits` / `MaximumUpgradeUnits` | Individual-unit range for rarer development currencies. |
| `FactionAvailabilityWeight` | Bonus when a helped faction can formally reward the item. |
| `DemandWeight` | Strength of recipe-frequency and required-quantity weighting. |
| `TopCandidateFraction` | Demand-ranked candidate fraction available to the variable part of the stockpile. |
| `MaximumRareItems` | Maximum distinct rare crafting/upgrade entries. |
| `MinimumCommonLootOccurrences` | Minimum container-table appearances before an unlisted material is considered common. |
| `RareItemIds` | Advanced components always counted against the rare budget; editable in JSON. |

Missing IDs, implicit weapons, and implicit ammunition records are skipped during planning and
recorded as warnings. A complete plan is validated before faction, station, roster, Magnum, recipe,
or stash mutations begin.

## Start report

The most recent report is written beside the persistent config:

```text
%USERPROFILE%\AppData\LocalLow\Magnum Scriptum LTD\Quasimorph_ModConfigs\CustomStart\last-start-report.json
```

It contains the config source, resolved seed, date, helped/rival choices, every faction level and
research rate, station captures, station economy values, clone/class grants, Magnum nodes, learned
production recipes, item grants with acquisition basis, selection groups and scores, warnings, and
any application error. A successful run has:

```json
"Applied": true,
"Error": ""
```

The save also receives harmless vanilla story-trigger markers beginning with `customstart.`. They
identify the profile and seed without introducing a custom serialized type.

## Build

The project expects Quasimorph at `C:\Games\Steam\steamapps\common\Quasimorph` and MCM Workshop
item `3469678797` in the same Steam library. Override `GameManagedDir` and `McmAssemblyPath` when
building against other locations:

```powershell
dotnet build .\src\CustomStart.csproj -c Release `
  -p:GameManagedDir="D:\SteamLibrary\steamapps\common\Quasimorph\Quasimorph_Data\Managed" `
  -p:McmAssemblyPath="D:\SteamLibrary\steamapps\workshop\content\2059170\3469678797\MCM.dll"
```

The resulting DLL and PDB are copied into `package`.

## Local installation

Copy the contents of `package` into:

```text
%USERPROFILE%\AppData\LocalLow\Magnum Scriptum LTD\Quasimorph\LocalUserPresets\CustomStart
```

Enable `CustomStart` in the game's mod list and restart the game after replacing the DLL. Subscribe
to MCM if in-game configuration is desired; it is not required for JSON-only use.

## User-run smoke test

Repository automation does not launch or drive Quasimorph. To check a new build:

1. Back up saves and install the current `package` directory.
2. In the main-menu **Mods** screen, select `CustomStart`, choose a profile, and set a fixed integer
   seed. If MCM is not installed, make the same changes in JSON.
3. Create a genuinely new campaign; do not load an existing campaign for this test.
4. Reach the orbital view, then close the game normally.
5. Open `last-start-report.json` and verify `ConfigSource`, `Profile`, `Applied`, and `Error`; faction
   tech should remain inside the selected profile's configured range.
6. Check `Player.log` for lines beginning with `[CustomStart]` and for exceptions occurring after
   `Applied`.
7. Confirm the campaign date, faction screen, roster/classes, Magnum tree, and stash agree with the
   report. For Mid, check that common ammo and basic medicine are visibly bulkier than specialist
   supplies, armor appears in complete sets, repair/augmentation/implant reserves are non-empty,
   `ProductionRecipeUnlocks` contains at least 55 entries, and no item ID contains `implicted_`.
   Review `AcquisitionBasis`, `SelectionGroup`, and `SelectionScore` when a category looks wrong.

For diagnosis, provide `last-start-report.json` and the `[CustomStart]` section plus any following
exception from:

```text
%USERPROFILE%\AppData\LocalLow\Magnum Scriptum LTD\Quasimorph\Player.log
```

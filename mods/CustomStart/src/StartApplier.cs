using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using UnityEngine;

namespace CustomStart;

internal sealed class StartApplier
{
    private readonly State _state;

    public StartApplier(State state)
    {
        _state = state;
    }

    public void Apply(StartPlan plan)
    {
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(plan.Seed);
        try
        {
            ApplyFactions(plan);
            ApplyStations(plan);
            RebuildFactionCachesAndPower(plan);
            ApplyMagnumUpgrades(plan);
            ApplyRoster(plan);
            ApplyProductionRecipeUnlocks(plan);
            ApplyItems(plan);
            AddSaveMarkers(plan);
            plan.Applied = true;
        }
        finally
        {
            UnityEngine.Random.state = randomState;
        }
    }

    private void ApplyFactions(StartPlan plan)
    {
        Factions factions = Require<Factions>();
        foreach (FactionPlan factionPlan in plan.Factions)
        {
            Faction faction = factions.Get(factionPlan.Id, logMissing: false);
            if (faction == null)
            {
                plan.Warnings.Add($"Faction '{factionPlan.Id}' disappeared before the start plan was applied.");
                continue;
            }

            faction.CurrentTechLevel = factionPlan.TechLevel;
            faction.TechExp = factionPlan.TechExp;
            faction.PlayerReputation = factionPlan.PlayerReputation;
            faction.PlayerTradePoints = factionPlan.PlayerTradePoints;
            faction.AllTimeTradingPoints = Math.Max(faction.AllTimeTradingPoints, factionPlan.PlayerTradePoints);
        }
    }

    private void ApplyStations(StartPlan plan)
    {
        Stations stations = Require<Stations>();
        SpaceTime spaceTime = Require<SpaceTime>();

        foreach (StationTransferPlan transfer in plan.StationTransfers)
        {
            Station station = stations.Get(transfer.StationId, logMissing: false);
            if (station == null)
            {
                plan.Warnings.Add($"Station '{transfer.StationId}' disappeared before its transfer was applied.");
                continue;
            }

            station.OwnerFactionId = transfer.NewOwnerId;
            station.OwnerChangedDirty = true;
            station.LastCapturedTime = spaceTime.Time.AddDays(-transfer.DaysAgo);
            station.ImmuneToAttackHours = 0;
            station.ImmuneToAttack = false;
        }

        foreach (KeyValuePair<string, int> entry in plan.StationPower)
        {
            Station station = stations.Get(entry.Key, logMissing: false);
            if (station != null)
            {
                station.GainedPower = entry.Value;
            }
        }

        foreach (KeyValuePair<string, float> entry in plan.StationPendingTech)
        {
            Station station = stations.Get(entry.Key, logMissing: false);
            if (station != null)
            {
                station.GainedTechLevel = entry.Value;
            }
        }
    }

    private void RebuildFactionCachesAndPower(StartPlan plan)
    {
        Factions factions = Require<Factions>();
        Stations stations = Require<Stations>();
        FactionCachedData cache = Require<FactionCachedData>();

        CacheSystem.CacheFactionRelations(cache, factions);
        CacheSystem.CacheStationNeighbours(cache, stations);
        CacheSystem.CacheStationOwners(cache, stations);
        Dictionary<string, FactionPlan> factionPlans = plan.Factions.ToDictionary(
            faction => faction.Id,
            faction => faction,
            StringComparer.OrdinalIgnoreCase);

        foreach (Faction faction in factions.Values)
        {
            int stationPower = stations.Values
                .Where(station => station.OwnerFactionId.Equals(faction.Id, StringComparison.Ordinal))
                .Sum(station => station.Record.Power + station.GainedPower);
            int techIndex = Math.Max(0, Math.Min(faction.CurrentTechLevel, Data.Global.TechLevelToPower.Length - 1));
            faction.Power = faction.BasePower + stationPower + Data.Global.TechLevelToPower[techIndex];
            faction.PowerGainNewsIndex = GetReachedIndex(Data.Global.FactionPowerGainNews, faction.Power);
            faction.TechLevelGainNewsIndex = GetReachedIndex(Data.Global.FactionTechLevelGainNews, faction.CurrentTechLevel);
            if (factionPlans.TryGetValue(faction.Id, out FactionPlan factionPlan))
            {
                factionPlan.Power = faction.Power;
            }
        }
    }

    private void ApplyMagnumUpgrades(StartPlan plan)
    {
        MagnumProgression progression = Require<MagnumProgression>();
        foreach (string upgradeId in plan.MagnumUpgrades)
        {
            progression.AddPerk(upgradeId, silent: true);
        }

        foreach (MagnumDepartment department in progression.Departments)
        {
            department.OnPerksUpdated();
        }
    }

    private void ApplyRoster(StartPlan plan)
    {
        Mercenaries mercenaries = Require<Mercenaries>();
        MagnumProgression progression = Require<MagnumProgression>();
        MagnumProjects projects = Require<MagnumProjects>();
        SpaceTime spaceTime = Require<SpaceTime>();
        Difficulty difficulty = Require<Difficulty>();
        PerkFactory perkFactory = Require<PerkFactory>();

        foreach (string classId in plan.Classes)
        {
            if (!mercenaries.UnlockedClasses.Contains(classId))
            {
                mercenaries.UnlockedClasses.Add(classId);
            }
        }

        foreach (MercenaryGrant grant in plan.Mercenaries)
        {
            if (!mercenaries.UnlockedMercenaries.Contains(grant.ProfileId))
            {
                mercenaries.UnlockedMercenaries.Add(grant.ProfileId);
            }

            if (!mercenaries.IsMercenaryExist(grant.ProfileId))
            {
                MercenarySystem.CloneMercenary(
                    spaceTime,
                    projects,
                    progression,
                    mercenaries,
                    grant.ProfileId,
                    cloneInstant: true,
                    difficulty,
                    perkFactory);
            }

            Mercenary mercenary = mercenaries.Get(grant.ProfileId);
            if (mercenary != null && !string.IsNullOrEmpty(grant.AgentName))
            {
                mercenary.AgentName = grant.AgentName;
            }
        }

        foreach (Mercenary mercenary in mercenaries.Values)
        {
            MercenarySystem.SyncWithMagnumProgression(mercenary, progression);
        }
    }

    private void ApplyItems(StartPlan plan)
    {
        MagnumCargo cargo = Require<MagnumCargo>();
        SpaceTime spaceTime = Require<SpaceTime>();
        ItemFactory itemFactory = SingletonMonoBehaviour<ItemFactory>.Instance;
        if (itemFactory == null)
        {
            throw new InvalidOperationException("ItemFactory is unavailable while applying the generated stash.");
        }

        foreach (ItemGrant grant in plan.Items)
        {
            if (grant.FullStacks)
            {
                for (int stack = 0; stack < grant.Count; stack++)
                {
                    BasePickupItem? item = CreateItem(itemFactory, grant.ItemId, plan);
                    if (item == null)
                    {
                        break;
                    }

                    SetStackCount(item, item.MaxStack);
                    MagnumCargoSystem.AddCargo(cargo, spaceTime, item, null, splittedItem: false, tabFilter: true);
                }

                continue;
            }

            int remaining = grant.Count;
            while (remaining > 0)
            {
                BasePickupItem? item = CreateItem(itemFactory, grant.ItemId, plan);
                if (item == null)
                {
                    break;
                }

                short stackCount = (short)Math.Min(remaining, Math.Max(1, (int)item.MaxStack));
                SetStackCount(item, stackCount);
                MagnumCargoSystem.AddCargo(cargo, spaceTime, item, null, splittedItem: false, tabFilter: true);
                remaining -= stackCount;
            }
        }

        foreach (ItemStorage storage in MagnumCargoSystem.GetAvailableCargoStorages(cargo))
        {
            storage.SortWithExpandByTypeAndName(spaceTime);
        }
    }

    private void ApplyProductionRecipeUnlocks(StartPlan plan)
    {
        MagnumCargo cargo = Require<MagnumCargo>();
        foreach (string itemId in plan.ProductionRecipeUnlocks)
        {
            if (!cargo.UnlockedProductionItems.Contains(itemId))
            {
                cargo.UnlockedProductionItems.Add(itemId);
            }
        }
    }

    private static BasePickupItem? CreateItem(ItemFactory itemFactory, string itemId, StartPlan plan)
    {
        BasePickupItem item = itemFactory.CreateForInventory(itemId);
        if (item == null)
        {
            plan.Warnings.Add($"Item '{itemId}' could not be created and was skipped.");
        }

        return item;
    }

    private static void SetStackCount(BasePickupItem item, short count)
    {
        item.StackCount = count;
        UsableItemComponent usable = item.Comp<UsableItemComponent>();
        usable?.FillAllCapacity(count);
    }

    private void AddSaveMarkers(StartPlan plan)
    {
        StoryTriggers triggers = Require<StoryTriggers>();
        triggers.Pass("customstart.applied.v1");
        triggers.Pass("customstart.profile." + SanitizeMarker(plan.Profile));
        triggers.Pass("customstart.seed." + plan.Seed);
    }

    private static string SanitizeMarker(string value)
    {
        char[] chars = value.ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) || character == '-' || character == '_'
                ? character
                : '-').ToArray();
        return new string(chars);
    }

    private static int GetReachedIndex(IReadOnlyList<int> thresholds, int value)
    {
        int result = -1;
        for (int index = 0; index < thresholds.Count; index++)
        {
            if (value >= thresholds[index])
            {
                result = index;
            }
        }

        return result;
    }

    private static int GetReachedIndex(IReadOnlyList<float> thresholds, float value)
    {
        int result = -1;
        for (int index = 0; index < thresholds.Count; index++)
        {
            if (value >= thresholds[index])
            {
                result = index;
            }
        }

        return result;
    }

    private T Require<T>() where T : class
    {
        return _state.Get<T>() ?? throw new InvalidOperationException($"Game state component {typeof(T).Name} is unavailable.");
    }
}

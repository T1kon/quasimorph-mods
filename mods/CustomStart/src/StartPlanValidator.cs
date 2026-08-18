using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;

namespace CustomStart;

internal sealed class StartPlanValidator
{
    private readonly State _state;

    public StartPlanValidator(State state)
    {
        _state = state;
    }

    public void Validate(StartPlan plan)
    {
        if (plan.TargetDate != Require<SpaceTime>().Time)
        {
            throw new InvalidOperationException("The generated target date no longer matches the fresh game state.");
        }

        ValidateFactionSelections(plan);
        ValidateFactions(plan);
        ValidateStations(plan);
        ValidateRoster(plan);
        ValidateMagnumUpgrades(plan);
        ValidateProductionRecipeUnlocks(plan);
        ValidateItems(plan);
    }

    private static void ValidateFactionSelections(StartPlan plan)
    {
        HashSet<string> helped = new(plan.HelpedFactions, StringComparer.OrdinalIgnoreCase);
        if (helped.Count != plan.HelpedFactions.Count)
        {
            throw new InvalidOperationException("The helped-faction selection contains duplicates.");
        }

        if (plan.RivalFactions.Any(helped.Contains))
        {
            throw new InvalidOperationException("A faction cannot be both helped and rivalled in one start plan.");
        }

        if (new HashSet<string>(plan.RivalFactions, StringComparer.OrdinalIgnoreCase).Count != plan.RivalFactions.Count)
        {
            throw new InvalidOperationException("The rival-faction selection contains duplicates.");
        }
    }

    private void ValidateFactions(StartPlan plan)
    {
        Factions factions = Require<Factions>();
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (FactionPlan factionPlan in plan.Factions)
        {
            if (!ids.Add(factionPlan.Id))
            {
                throw new InvalidOperationException($"Faction '{factionPlan.Id}' has more than one generated state.");
            }

            if (factions.Get(factionPlan.Id, logMissing: false) == null)
            {
                throw new InvalidOperationException($"Generated faction '{factionPlan.Id}' does not exist in the fresh game state.");
            }

            if (factionPlan.TechLevel < 1 || factionPlan.TechLevel > Data.Global.MaxTechLevel)
            {
                throw new InvalidOperationException($"Faction '{factionPlan.Id}' has invalid tech level {factionPlan.TechLevel}.");
            }

            if (factionPlan.TechExp < 0f)
            {
                throw new InvalidOperationException($"Faction '{factionPlan.Id}' has negative tech experience.");
            }

            if (factionPlan.TechLevel >= Data.Global.MaxTechLevel && factionPlan.TechExp != 0f)
            {
                throw new InvalidOperationException($"Max-tech faction '{factionPlan.Id}' has residual tech experience.");
            }

            if (factionPlan.TechLevel < Data.Global.MaxTechLevel
                && Data.TechLevels.TryGetValue(factionPlan.TechLevel, out TechLevelRecord levelRecord)
                && factionPlan.TechExp >= levelRecord.ExperienceToLevelUp)
            {
                throw new InvalidOperationException($"Faction '{factionPlan.Id}' has enough experience to exceed its planned tech level.");
            }

            if (factionPlan.PlayerReputation < -100f || factionPlan.PlayerReputation > 100f)
            {
                throw new InvalidOperationException($"Faction '{factionPlan.Id}' has out-of-range player reputation.");
            }

            if (factionPlan.PlayerTradePoints < 0)
            {
                throw new InvalidOperationException($"Faction '{factionPlan.Id}' has negative trade points.");
            }
        }
    }

    private void ValidateStations(StartPlan plan)
    {
        Stations stations = Require<Stations>();
        Factions factions = Require<Factions>();
        HashSet<string> transferred = new(StringComparer.OrdinalIgnoreCase);
        foreach (StationTransferPlan transfer in plan.StationTransfers)
        {
            Station station = stations.Get(transfer.StationId, logMissing: false)
                              ?? throw new InvalidOperationException($"Generated station '{transfer.StationId}' does not exist.");
            if (!transferred.Add(transfer.StationId))
            {
                throw new InvalidOperationException($"Station '{transfer.StationId}' is transferred more than once.");
            }

            if (!station.OwnerFactionId.Equals(transfer.PreviousOwnerId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Station '{transfer.StationId}' no longer has its expected original owner.");
            }

            if (factions.Get(transfer.NewOwnerId, logMissing: false) == null)
            {
                throw new InvalidOperationException($"Station '{transfer.StationId}' has unknown new owner '{transfer.NewOwnerId}'.");
            }

            if (transfer.DaysAgo < 0 || transfer.DaysAgo > plan.ElapsedDays)
            {
                throw new InvalidOperationException($"Station '{transfer.StationId}' has an invalid capture age.");
            }
        }

        foreach (KeyValuePair<string, int> entry in plan.StationPower)
        {
            if (stations.Get(entry.Key, logMissing: false) == null || entry.Value < 0)
            {
                throw new InvalidOperationException($"Station power entry '{entry.Key}' is invalid.");
            }
        }

        foreach (KeyValuePair<string, float> entry in plan.StationPendingTech)
        {
            if (stations.Get(entry.Key, logMissing: false) == null || entry.Value < 0f)
            {
                throw new InvalidOperationException($"Station pending-tech entry '{entry.Key}' is invalid.");
            }
        }
    }

    private void ValidateRoster(StartPlan plan)
    {
        Mercenaries mercenaries = Require<Mercenaries>();
        HashSet<string> mercenaryIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (MercenaryGrant grant in plan.Mercenaries)
        {
            if (!mercenaryIds.Add(grant.ProfileId)
                || Data.MercenaryProfiles.GetRecord(grant.ProfileId) == null
                || mercenaries.IsMercenaryExist(grant.ProfileId))
            {
                throw new InvalidOperationException($"Generated mercenary grant '{grant.ProfileId}' is invalid or redundant.");
            }
        }

        HashSet<string> classIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (string classId in plan.Classes)
        {
            if (!classIds.Add(classId)
                || Data.MercenaryClasses.GetRecord(classId) == null
                || mercenaries.UnlockedClasses.Contains(classId))
            {
                throw new InvalidOperationException($"Generated class grant '{classId}' is invalid or redundant.");
            }
        }
    }

    private void ValidateMagnumUpgrades(StartPlan plan)
    {
        MagnumProgression progression = Require<MagnumProgression>();
        HashSet<string> selected = new(
            Data.MagnumPerks.Records
                .Where(record => progression.IsPerkPurchased(record.Id))
                .Select(record => record.Id),
            StringComparer.OrdinalIgnoreCase);

        foreach (string upgradeId in plan.MagnumUpgrades)
        {
            MagnumPerkRecord record = Data.MagnumPerks.GetRecord(upgradeId)
                                      ?? throw new InvalidOperationException($"Generated Magnum upgrade '{upgradeId}' does not exist.");
            if (!record.Enabled || selected.Contains(upgradeId))
            {
                throw new InvalidOperationException($"Generated Magnum upgrade '{upgradeId}' is disabled or duplicated.");
            }

            bool connected = record.Parents.Count == 0
                             || record.Parents.Any(selected.Contains)
                             || record.Childs.Any(selected.Contains);
            if (!connected)
            {
                throw new InvalidOperationException($"Generated Magnum upgrade '{upgradeId}' is disconnected from the purchased graph.");
            }

            selected.Add(upgradeId);
        }
    }

    private static void ValidateItems(StartPlan plan)
    {
        foreach (ItemGrant grant in plan.Items)
        {
            if (grant.Count <= 0 || !StartPlanner.IsPlayerFacingItem(grant.ItemId))
            {
                throw new InvalidOperationException($"Generated item grant '{grant.ItemId}' is invalid.");
            }
        }
    }

    private void ValidateProductionRecipeUnlocks(StartPlan plan)
    {
        MagnumCargo cargo = Require<MagnumCargo>();
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (string itemId in plan.ProductionRecipeUnlocks)
        {
            if (!ids.Add(itemId)
                || cargo.UnlockedProductionItems.Contains(itemId)
                || !StartPlanner.IsPlayerFacingItem(itemId)
                || !Data.ProduceReceipts.Any(receipt =>
                    receipt.OutputItem.Equals(itemId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Generated production-recipe unlock '{itemId}' is invalid or redundant.");
            }
        }
    }

    private T Require<T>() where T : class
    {
        return _state.Get<T>() ?? throw new InvalidOperationException($"Game state component {typeof(T).Name} is unavailable.");
    }
}

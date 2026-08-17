using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;

namespace CustomStart;

internal sealed class StartPlanner
{
    private static readonly HashSet<string> KnownStoryStationIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Carcosa",
        "Carmel",
        "Clotho",
        "FeatheredTemple",
        "FlatObsidian",
        "FrescoPlatforms",
        "FullersCore",
        "Hargeysa",
        "HeritageStation",
        "HISAres",
        "HISUnveiled",
        "HISWoodpecker",
        "HumankindsHope",
        "IntercityTradeCenter",
        "ISSStation",
        "Jamame",
        "LouhiCity",
        "Oddibjord",
        "Orphan",
        "Photosphere",
        "PilgrimsRift",
        "RebusSkyCity",
        "RedThrone",
        "RogueCity",
        "SinkholeOasis",
        "Sundog",
        "Vasa"
    };

    private const string NameCharacters = "abcdefghijklmnopqrstuvwxyz1234567890";

    private readonly State _state;
    private readonly StartProfile _profile;
    private readonly string _profileName;
    private readonly int _seed;
    private readonly bool _allowStationTransfers;
    private readonly Random _random;

    public StartPlanner(
        State state,
        StartProfile profile,
        string profileName,
        int seed,
        bool allowStationTransfers)
    {
        _state = state;
        _profile = profile;
        _profileName = profileName;
        _seed = seed;
        _allowStationTransfers = allowStationTransfers;
        _random = new Random(seed);
    }

    public StartPlan Build()
    {
        SpaceTime spaceTime = Require<SpaceTime>();
        List<string> eligibleFactionIds = GetEligibleFactionIds();
        StartPlan plan = new StartPlan
        {
            Profile = _profileName,
            Seed = _seed,
            ElapsedDays = _profile.ElapsedDays,
            TargetDate = spaceTime.Time
        };

        plan.HelpedFactions.AddRange(
            SelectFactions(_profile.Factions.HelpedFactions, eligibleFactionIds, new HashSet<string>(), plan));
        plan.RivalFactions.AddRange(
            SelectFactions(
                _profile.Factions.RivalFactions,
                eligibleFactionIds,
                new HashSet<string>(plan.HelpedFactions, StringComparer.OrdinalIgnoreCase),
                plan));

        Dictionary<string, string> finalOwners = BuildFinalStationOwners(plan, eligibleFactionIds);
        Dictionary<string, double> researchRates = CalculateResearchRates(eligibleFactionIds, finalOwners);
        BuildFactionPlans(plan, eligibleFactionIds, researchRates);
        BuildStationEconomy(plan, finalOwners);
        BuildRosterPlan(plan);
        BuildMagnumPlan(plan);
        BuildStashPlan(plan, eligibleFactionIds);
        return plan;
    }

    private Dictionary<string, string> BuildFinalStationOwners(StartPlan plan, List<string> eligibleFactionIds)
    {
        Stations stations = Require<Stations>();
        Dictionary<string, string> owners = stations.Values.ToDictionary(
            station => station.Id,
            station => station.OwnerFactionId,
            StringComparer.OrdinalIgnoreCase);

        if (!_allowStationTransfers)
        {
            if (_profile.Factions.PlayerStationTransfers + _profile.Factions.BackgroundStationTransfers > 0)
            {
                plan.Warnings.Add("Station transfers were disabled because this game build is not recognized.");
            }

            return owners;
        }

        HashSet<string> protectedIds = new(
            _profile.Factions.AdditionalProtectedStationIds,
            StringComparer.OrdinalIgnoreCase);
        if (_profile.Factions.ProtectKnownStoryStations)
        {
            protectedIds.UnionWith(KnownStoryStationIds);
        }

        List<Station> transferable = stations.Values
            .Where(station =>
                !string.IsNullOrEmpty(station.SpaceObjectId)
                && station.Record.SpawnOnStart
                && !station.UncapturableByDefault
                && !protectedIds.Contains(station.Id)
                && eligibleFactionIds.Contains(station.OwnerFactionId))
            .OrderBy(station => station.Id, StringComparer.Ordinal)
            .ToList();

        Dictionary<string, int> ownerCounts = stations.Values
            .Where(station => !string.IsNullOrEmpty(station.SpaceObjectId))
            .GroupBy(station => station.OwnerFactionId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        HashSet<string> transferredStations = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < _profile.Factions.PlayerStationTransfers; index++)
        {
            if (!TryPlanTransfer(
                    plan,
                    owners,
                    ownerCounts,
                    transferable,
                    transferredStations,
                    plan.HelpedFactions,
                    plan.RivalFactions,
                    eligibleFactionIds,
                    playerInfluenced: true))
            {
                plan.Warnings.Add($"Only {index} of {_profile.Factions.PlayerStationTransfers} player-influenced station transfers could be generated safely.");
                break;
            }
        }

        for (int index = 0; index < _profile.Factions.BackgroundStationTransfers; index++)
        {
            if (!TryPlanTransfer(
                    plan,
                    owners,
                    ownerCounts,
                    transferable,
                    transferredStations,
                    eligibleFactionIds,
                    eligibleFactionIds,
                    eligibleFactionIds,
                    playerInfluenced: false))
            {
                plan.Warnings.Add($"Only {index} of {_profile.Factions.BackgroundStationTransfers} background station transfers could be generated safely.");
                break;
            }
        }

        return owners;
    }

    private bool TryPlanTransfer(
        StartPlan plan,
        Dictionary<string, string> owners,
        Dictionary<string, int> ownerCounts,
        List<Station> transferable,
        HashSet<string> transferredStations,
        IReadOnlyList<string> capturerPool,
        IReadOnlyList<string> preferredVictimPool,
        IReadOnlyList<string> fallbackVictimPool,
        bool playerInfluenced)
    {
        if (capturerPool.Count == 0)
        {
            return false;
        }

        for (int attempt = 0; attempt < 300; attempt++)
        {
            string capturerId = capturerPool[_random.Next(capturerPool.Count)];
            string victimId = SelectVictim(capturerId, preferredVictimPool, fallbackVictimPool, ownerCounts);
            if (string.IsNullOrEmpty(victimId) || AreFriends(capturerId, victimId))
            {
                continue;
            }

            List<Station> candidates = transferable
                .Where(station =>
                    owners[station.Id].Equals(victimId, StringComparison.OrdinalIgnoreCase)
                    && !transferredStations.Contains(station.Id))
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            Station selected = candidates[_random.Next(candidates.Count)];
            int captureAgeLimit = Math.Min(_profile.ElapsedDays, _profile.Factions.MaximumCaptureAgeDays);
            int minimumAge = Math.Min(_profile.Factions.MinimumCaptureAgeDays, captureAgeLimit);
            int daysAgo = captureAgeLimit <= minimumAge
                ? minimumAge
                : _random.Next(minimumAge, captureAgeLimit + 1);

            plan.StationTransfers.Add(new StationTransferPlan
            {
                StationId = selected.Id,
                PreviousOwnerId = victimId,
                NewOwnerId = capturerId,
                DaysAgo = daysAgo,
                PlayerInfluenced = playerInfluenced
            });
            owners[selected.Id] = capturerId;
            ownerCounts[victimId]--;
            ownerCounts[capturerId] = ownerCounts.TryGetValue(capturerId, out int count) ? count + 1 : 1;
            transferredStations.Add(selected.Id);
            return true;
        }

        return false;
    }

    private string SelectVictim(
        string capturerId,
        IReadOnlyList<string> preferred,
        IReadOnlyList<string> fallback,
        IReadOnlyDictionary<string, int> ownerCounts)
    {
        List<string> candidates = preferred
            .Where(id => IsValidVictim(capturerId, id, ownerCounts))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = fallback
                .Where(id => IsValidVictim(capturerId, id, ownerCounts))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        return candidates.Count == 0 ? string.Empty : candidates[_random.Next(candidates.Count)];
    }

    private bool IsValidVictim(string capturerId, string victimId, IReadOnlyDictionary<string, int> ownerCounts)
    {
        return !capturerId.Equals(victimId, StringComparison.OrdinalIgnoreCase)
               && ownerCounts.TryGetValue(victimId, out int count)
               && count > _profile.Factions.MinimumStationsPerFaction
               && !AreFriends(capturerId, victimId);
    }

    private static bool AreFriends(string firstId, string secondId)
    {
        FactionRecord first = Data.Factions.GetRecord(firstId);
        FactionRecord second = Data.Factions.GetRecord(secondId);
        if (first == null || second == null || !first.AllianceType.Equals(second.AllianceType, StringComparison.Ordinal))
        {
            return false;
        }

        AllianceRecord alliance = Data.Alliances.GetRecord(first.AllianceType);
        return alliance != null && !alliance.AllowStrife;
    }

    private Dictionary<string, double> CalculateResearchRates(
        IEnumerable<string> eligibleFactionIds,
        IReadOnlyDictionary<string, string> finalOwners)
    {
        Dictionary<string, double> rates = eligibleFactionIds.ToDictionary(
            id => id,
            _ => 0.0,
            StringComparer.OrdinalIgnoreCase);
        Difficulty difficulty = Require<Difficulty>();

        foreach (Station station in Require<Stations>().Values)
        {
            if (string.IsNullOrEmpty(station.SpaceObjectId)
                || !finalOwners.TryGetValue(station.Id, out string ownerId)
                || !rates.ContainsKey(ownerId))
            {
                continue;
            }

            FactionRecord factionRecord = Data.Factions.GetRecord(ownerId);
            StationBarterRecord stationBarter = Data.StationBarter.GetRecord(station.Id);
            if (factionRecord == null || stationBarter == null)
            {
                continue;
            }

            double populationMultiplier = Data.Global.StationProducePopulationMult
                                          - (double)station.Population / Math.Max(1, station.Record.MaxPopulation);
            populationMultiplier = Math.Max(0.1, populationMultiplier);
            foreach (string receiptId in GetProductionReceipts(factionRecord.FactionType, stationBarter))
            {
                BarterReceipt receipt = Data.BarterReceipts.GetRecord(receiptId);
                if (receipt == null || receipt.OutputTechLevelGain <= 0f || receipt.ProduceTimeInHours <= 0f)
                {
                    continue;
                }

                rates[ownerId] += receipt.OutputTechLevelGain
                                  * station.Record.TechLevelGain
                                  / (receipt.ProduceTimeInHours * populationMultiplier)
                                  * 168.0
                                  * difficulty.Preset.FactionGrowthSpeed;
            }
        }

        return rates;
    }

    private void BuildFactionPlans(
        StartPlan plan,
        List<string> eligibleFactionIds,
        IReadOnlyDictionary<string, double> researchRates)
    {
        TechProgressionSettings tech = _profile.TechProgression;
        List<double> positiveRates = researchRates.Values.Where(rate => rate > 0.0).OrderBy(rate => rate).ToList();
        double medianRate = positiveRates.Count == 0
            ? 1.0
            : positiveRates[positiveRates.Count / 2];
        int cohesionMinimum = (int)Math.Round(
            tech.WorldProgressLevel - tech.MaxActiveFactionSpread / 2.0,
            MidpointRounding.AwayFromZero);
        cohesionMinimum = Clamp(cohesionMinimum, tech.MinimumLevel, tech.MaximumLevel);
        int cohesionMaximum = Clamp(
            cohesionMinimum + tech.MaxActiveFactionSpread,
            cohesionMinimum,
            tech.MaximumLevel);
        HashSet<string> helped = new(plan.HelpedFactions, StringComparer.OrdinalIgnoreCase);
        HashSet<string> rivals = new(plan.RivalFactions, StringComparer.OrdinalIgnoreCase);
        foreach (string configuredFactionId in tech.ExactLevels.Keys)
        {
            if (!eligibleFactionIds.Contains(configuredFactionId, StringComparer.OrdinalIgnoreCase))
            {
                plan.Warnings.Add($"Exact tech level faction '{configuredFactionId}' is not an eligible active faction.");
            }
        }

        foreach (string factionId in eligibleFactionIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            FactionRecord record = Data.Factions.GetRecord(factionId);
            double rate = researchRates.TryGetValue(factionId, out double value) ? value : 0.0;
            double economyOffset = 0.0;
            int level;
            double progressFraction;

            if (tech.ExactLevels.TryGetValue(factionId, out int exactLevel))
            {
                level = Clamp(exactLevel, 1, Data.Global.MaxTechLevel);
                progressFraction = NextDouble(tech.MinimumProgressFraction, tech.MaximumProgressFraction);
            }
            else
            {
                if (rate > 0.0 && medianRate > 0.0)
                {
                    economyOffset = Math.Log(rate / medianRate, 2.0) * tech.EconomyOffsetPerDoubling;
                    economyOffset = Clamp(economyOffset, -tech.MaximumEconomyOffset, tech.MaximumEconomyOffset);
                }

                double randomOffset = NextDouble(-tech.RandomOffset, tech.RandomOffset);
                double continuousLevel = tech.WorldProgressLevel + economyOffset + randomOffset;
                int rawLevel = (int)Math.Round(continuousLevel, MidpointRounding.AwayFromZero);
                level = Clamp(rawLevel, cohesionMinimum, cohesionMaximum);
                level = Math.Max(level, record.InitialTechLevel);
                progressFraction = rawLevel == level
                    ? 0.5 + continuousLevel - rawLevel
                    : rawLevel < level
                        ? tech.MinimumProgressFraction
                        : tech.MaximumProgressFraction;
                progressFraction = Clamp(
                    progressFraction,
                    tech.MinimumProgressFraction,
                    tech.MaximumProgressFraction);
            }

            float techExp = 0f;
            if (level < Data.Global.MaxTechLevel
                && Data.TechLevels.TryGetValue(level, out TechLevelRecord levelRecord))
            {
                techExp = (float)(levelRecord.ExperienceToLevelUp * progressFraction);
            }

            float reputation = record.InitialPlayerReputation;
            int tradePoints = 0;
            if (helped.Contains(factionId))
            {
                reputation = Math.Max(reputation, NextInt(_profile.Factions.HelpedReputation));
                tradePoints = NextInt(_profile.Factions.HelpedTradePoints);
            }
            else if (rivals.Contains(factionId))
            {
                reputation = Math.Min(reputation, NextInt(_profile.Factions.RivalReputation));
            }

            plan.Factions.Add(new FactionPlan
            {
                Id = factionId,
                TechLevel = level,
                TechExp = techExp,
                PlayerReputation = reputation,
                PlayerTradePoints = tradePoints,
                ResearchRate = Math.Round(rate, 3),
                EconomyOffset = Math.Round(economyOffset, 3)
            });
        }
    }

    private void BuildStationEconomy(StartPlan plan, IReadOnlyDictionary<string, string> finalOwners)
    {
        Dictionary<string, FactionPlan> factions = plan.Factions.ToDictionary(
            faction => faction.Id,
            faction => faction,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> stationCounts = finalOwners.Values
            .Where(factions.ContainsKey)
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (Station station in Require<Stations>().Values.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(station.SpaceObjectId)
                || !finalOwners.TryGetValue(station.Id, out string ownerId)
                || !factions.TryGetValue(ownerId, out FactionPlan faction))
            {
                continue;
            }

            plan.StationPower[station.Id] = NextInt(_profile.Factions.StationPower);
            float pendingTech = 0f;
            if (faction.TechLevel < Data.Global.MaxTechLevel
                && _profile.TechProgression.PendingTechFraction > 0.0
                && Data.TechLevels.TryGetValue(faction.TechLevel, out TechLevelRecord levelRecord))
            {
                double totalPending = levelRecord.ExperienceToLevelUp * _profile.TechProgression.PendingTechFraction;
                double average = totalPending / Math.Max(1, stationCounts[ownerId]);
                pendingTech = (float)(average * NextDouble(0.5, 1.5));
            }

            plan.StationPendingTech[station.Id] = pendingTech;
        }
    }

    private void BuildRosterPlan(StartPlan plan)
    {
        Mercenaries mercenaries = Require<Mercenaries>();
        RosterSettings settings = _profile.Roster;
        HashSet<string> existingMercenaries = new(
            mercenaries.UnlockedMercenaries,
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> existingClasses = new(
            mercenaries.UnlockedClasses,
            StringComparer.OrdinalIgnoreCase);

        List<string> mercenaryCandidates = Data.MercenaryProfiles.Ids
            .Where(id => !id.EndsWith("_boss", StringComparison.OrdinalIgnoreCase)
                         && !id.EndsWith("_custom", StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        FilterCandidates(
            mercenaryCandidates,
            settings.AllowedCloneIds,
            settings.ExcludedCloneIds);
        List<string> selectedMercenaries = SelectToTarget(
            existingMercenaries,
            mercenaryCandidates,
            settings.GuaranteedCloneIds,
            settings.TargetCloneCount,
            "mercenary",
            plan);
        foreach (string id in selectedMercenaries)
        {
            plan.Mercenaries.Add(new MercenaryGrant { ProfileId = id, AgentName = GenerateAgentName() });
        }

        List<string> classCandidates = Data.MercenaryClasses.Ids
            .Where(id => !id.EndsWith("_custom", StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        FilterCandidates(classCandidates, settings.AllowedClassIds, settings.ExcludedClassIds);
        plan.Classes.AddRange(
            SelectToTarget(
                existingClasses,
                classCandidates,
                settings.GuaranteedClassIds,
                settings.TargetClassCount,
                "class",
                plan));
    }

    private void BuildMagnumPlan(StartPlan plan)
    {
        MagnumSettings settings = _profile.Magnum;
        MagnumProgression progression = Require<MagnumProgression>();
        List<MagnumPerkRecord> enabled = Data.MagnumPerks.Records
            .Where(record => record.Enabled)
            .OrderBy(record => record.Id, StringComparer.Ordinal)
            .ToList();
        HashSet<string> selected = new(
            enabled.Where(record => progression.IsPerkPurchased(record.Id)).Select(record => record.Id),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> excluded = new(settings.ExcludedUpgradeIds, StringComparer.OrdinalIgnoreCase);

        foreach (string guaranteedId in settings.GuaranteedUpgradeIds)
        {
            MagnumPerkRecord record = Data.MagnumPerks.GetRecord(guaranteedId);
            if (record == null || !record.Enabled)
            {
                plan.Warnings.Add($"Guaranteed Magnum upgrade '{guaranteedId}' does not exist or is disabled.");
                continue;
            }

            List<MagnumPerkRecord>? path = FindShortestRootPath(record, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (path == null)
            {
                plan.Warnings.Add($"No valid root path was found for guaranteed Magnum upgrade '{guaranteedId}'.");
                continue;
            }

            foreach (MagnumPerkRecord pathRecord in path)
            {
                AddPlannedUpgrade(pathRecord.Id, selected, plan.MagnumUpgrades);
            }
        }

        int target = settings.TargetUpgradeCount < 0 ? enabled.Count : settings.TargetUpgradeCount;
        while (selected.Count < target)
        {
            List<MagnumPerkRecord> available = enabled
                .Where(record =>
                    !selected.Contains(record.Id)
                    && !excluded.Contains(record.Id)
                    && IsAllowedDepartment(record, settings.AllowedDepartments)
                    && IsConnected(record, selected))
                .ToList();
            if (available.Count == 0)
            {
                plan.Warnings.Add($"Only {selected.Count} of {target} requested Magnum upgrades form an allowed connected graph.");
                break;
            }

            MagnumPerkRecord selectedRecord = available[_random.Next(available.Count)];
            AddPlannedUpgrade(selectedRecord.Id, selected, plan.MagnumUpgrades);
        }
    }

    private void BuildStashPlan(StartPlan plan, List<string> eligibleFactionIds)
    {
        RewardSelectionState selectionState = CreateRewardSelectionState();
        foreach (KeyValuePair<string, int> guaranteed in _profile.Stash.GuaranteedItems.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (guaranteed.Value <= 0 || !IsPlayerFacingItem(guaranteed.Key))
            {
                plan.Warnings.Add($"Guaranteed item '{guaranteed.Key}' is invalid or has a non-positive count.");
                continue;
            }

            plan.Items.Add(new ItemGrant
            {
                ItemId = guaranteed.Key,
                Count = guaranteed.Value,
                Category = "Guaranteed",
                SelectionGroup = GetRewardGroup(guaranteed.Key)
            });
            selectionState.Add(guaranteed.Key, guaranteed.Value);
        }

        List<string> sources = plan.HelpedFactions.Count > 0
            ? new List<string>(plan.HelpedFactions)
            : eligibleFactionIds.Take(1).ToList();
        if (sources.Count == 0)
        {
            plan.Warnings.Add("No eligible faction exists for generated stash rewards.");
            return;
        }

        AddFactionDrops(plan, sources, FactionDropCollection.TradeItemsFor.Equipment, _profile.Stash.EquipmentRolls, selectionState);
        AddFactionDrops(plan, sources, FactionDropCollection.TradeItemsFor.Consumables, _profile.Stash.ConsumableRolls, selectionState);
        AddFactionDrops(plan, sources, FactionDropCollection.TradeItemsFor.Chips, _profile.Stash.ChipRolls, selectionState);
        AddRoleStockpile(plan, sources, selectionState);
        FillProductionRecipeUnlocks(plan);
        AddAccumulatedMaterialStockpile(plan, sources, selectionState);
    }

    private void AddRoleStockpile(
        StartPlan plan,
        IReadOnlyList<string> sourceFactionIds,
        RewardSelectionState selectionState)
    {
        RoleStockpileSettings settings = _profile.Stash.RoleStockpile;
        if (!settings.Enabled)
        {
            return;
        }

        int maximumWorldTech = plan.Factions.Count == 0
            ? _profile.TechProgression.MaximumLevel
            : plan.Factions.Max(faction => faction.TechLevel);
        Dictionary<string, FactionPlan> factionPlans = plan.Factions.ToDictionary(
            faction => faction.Id,
            faction => faction,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> factionAvailability = BuildFactionItemAvailability(
            sourceFactionIds,
            factionPlans);
        Dictionary<string, LootAvailability> lootAvailability = BuildLootAvailability();
        HashSet<string> craftableItems = new(
            Data.ProduceReceipts
                .Where(receipt => !string.IsNullOrWhiteSpace(receipt.OutputItem))
                .Select(receipt => receipt.OutputItem),
            StringComparer.OrdinalIgnoreCase);
        List<CompositeItemRecord> catalog = Data.Items.Records
            .OfType<CompositeItemRecord>()
            .Where(record => IsPlayerFacingItem(record.Id))
            .ToList();

        List<RoleCandidate> selectedWeapons = AddHistoricalArsenal(
            plan,
            catalog,
            maximumWorldTech,
            settings,
            factionAvailability,
            lootAvailability,
            craftableItems,
            selectionState);
        AddHistoricalArmor(
            plan,
            maximumWorldTech,
            settings,
            factionAvailability,
            lootAvailability,
            craftableItems,
            selectionState);
        AddAmmoReserve(
            plan,
            catalog,
            selectedWeapons,
            maximumWorldTech,
            settings,
            factionAvailability,
            lootAvailability,
            selectionState);
        AddMedicalReserve(
            plan,
            catalog,
            maximumWorldTech,
            settings,
            factionAvailability,
            lootAvailability,
            selectionState);
        AddRepairReserve(
            plan,
            catalog,
            maximumWorldTech,
            settings,
            factionAvailability,
            lootAvailability,
            selectionState);
        AddAugmentationReserve(
            plan,
            catalog,
            settings,
            factionAvailability,
            lootAvailability,
            selectionState);
    }

    private List<RoleCandidate> AddHistoricalArsenal(
        StartPlan plan,
        IReadOnlyList<CompositeItemRecord> catalog,
        int maximumWorldTech,
        RoleStockpileSettings settings,
        IReadOnlyDictionary<string, string> factionAvailability,
        IReadOnlyDictionary<string, LootAvailability> lootAvailability,
        ISet<string> craftableItems,
        RewardSelectionState selectionState)
    {
        List<RoleCandidate> candidates = catalog
            .Select(record =>
            {
                ItemRecord? item = record.GetRecord<ItemRecord>();
                WeaponRecord? weapon = record.GetRecord<WeaponRecord>();
                if (item == null
                    || weapon == null
                    || weapon.IsImplicit
                    || !IsAllowedAtStage(record, item, maximumWorldTech, settings.AllowQuasiItems))
                {
                    return null;
                }

                string group = weapon.IsMelee
                    ? "Melee." + weapon.WeaponSubClass
                    : "Ranged." + weapon.WeaponClass + "." + weapon.RequiredAmmo;
                return CreateRoleCandidate(
                    record,
                    item,
                    group,
                    factionAvailability,
                    lootAvailability,
                    craftableItems.Contains(record.Id));
            })
            .Where(candidate => candidate != null)
            .Cast<RoleCandidate>()
            .ToList();

        int meleeTarget = settings.WeaponItems >= 4 ? Math.Max(1, settings.WeaponItems / 4) : 0;
        List<RoleCandidate> selected = SelectRoleCandidates(
            candidates.Where(candidate => candidate.Record.GetRecord<WeaponRecord>()?.IsMelee == false).ToList(),
            settings.WeaponItems - meleeTarget,
            preferDistinctGroups: true);
        selected.AddRange(SelectRoleCandidates(
            candidates.Where(candidate => candidate.Record.GetRecord<WeaponRecord>()?.IsMelee == true).ToList(),
            meleeTarget,
            preferDistinctGroups: true));
        if (selected.Count < settings.WeaponItems)
        {
            HashSet<string> selectedIds = new(selected.Select(candidate => candidate.Item.Id), StringComparer.OrdinalIgnoreCase);
            selected.AddRange(SelectRoleCandidates(
                candidates.Where(candidate => !selectedIds.Contains(candidate.Item.Id)).ToList(),
                settings.WeaponItems - selected.Count,
                preferDistinctGroups: true));
        }

        int craftedTarget = (int)Math.Ceiling(selected.Count * 0.5);
        int crafted = 0;
        foreach (RoleCandidate candidate in selected)
        {
            bool produced = candidate.Craftable
                            && crafted < craftedTarget
                            && IsRecipeDiscoverableAtStage(candidate.Item.Id, maximumWorldTech);
            if (produced)
            {
                AddProductionRecipeUnlockWithDependencies(plan, candidate.Item.Id);
                crafted++;
            }

            plan.Items.Add(new ItemGrant
            {
                ItemId = candidate.Item.Id,
                Count = 1,
                SourceFactionId = candidate.SourceFactionId,
                Category = "HistoricalArsenal",
                AcquisitionBasis = produced
                    ? "Produced aboard Magnum from an unlocked recipe"
                    : string.IsNullOrEmpty(candidate.SourceFactionId)
                        ? "Useful mission loot retained as a spare weapon"
                        : "Helped-faction reward retained as a spare weapon",
                SelectionGroup = candidate.Record.GetRecord<WeaponRecord>()?.IsMelee == true
                    ? "Arsenal.Melee"
                    : "Arsenal.Ranged",
                SelectionScore = Math.Round(candidate.Score, 6)
            });
            selectionState.Add(candidate.Item.Id);
        }

        WarnRoleShortfall(plan, "weapons", selected.Count, settings.WeaponItems);
        return selected;
    }

    private void AddHistoricalArmor(
        StartPlan plan,
        int maximumWorldTech,
        RoleStockpileSettings settings,
        IReadOnlyDictionary<string, string> factionAvailability,
        IReadOnlyDictionary<string, LootAvailability> lootAvailability,
        ISet<string> craftableItems,
        RewardSelectionState selectionState)
    {
        List<ArmorSetCandidate> candidates = new();
        foreach (ArmorSetRecord armorSet in Data.ArmorSets.Records)
        {
            List<RoleCandidate> parts = new();
            foreach (string itemId in armorSet.Items ?? new List<string>())
            {
                CompositeItemRecord? record = GetCompositeRecord(itemId);
                ItemRecord? item = record?.GetRecord<ItemRecord>();
                if (record == null
                    || item == null
                    || !IsArmorItem(item.ItemClass)
                    || !IsPlayerFacingItem(itemId)
                    || !IsAllowedAtStage(record, item, maximumWorldTech, settings.AllowQuasiItems))
                {
                    parts.Clear();
                    break;
                }

                parts.Add(CreateRoleCandidate(
                    record,
                    item,
                    item.ItemClass.ToString(),
                    factionAvailability,
                    lootAvailability,
                    craftableItems.Contains(itemId)));
            }

            if (parts.Count >= 3)
            {
                candidates.Add(new ArmorSetCandidate(
                    armorSet.Id,
                    parts,
                    parts.Average(part => part.Score)));
            }
        }

        List<ArmorSetCandidate> selected = candidates
            .OrderByDescending(candidate => candidate.Score * NextDouble(0.9, 1.1))
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Take(settings.ArmorSets)
            .ToList();
        int craftedTarget = (int)Math.Ceiling(selected.Count * 0.5);
        for (int setIndex = 0; setIndex < selected.Count; setIndex++)
        {
            ArmorSetCandidate armorSet = selected[setIndex];
            bool produced = setIndex < craftedTarget
                            && armorSet.Parts.Any(part =>
                                part.Craftable && IsRecipeDiscoverableAtStage(part.Item.Id, maximumWorldTech));
            if (produced)
            {
                RoleCandidate recipePart = armorSet.Parts.First(part =>
                    part.Craftable && IsRecipeDiscoverableAtStage(part.Item.Id, maximumWorldTech));
                AddProductionRecipeUnlockWithDependencies(plan, recipePart.Item.Id);
            }

            foreach (RoleCandidate part in armorSet.Parts)
            {
                plan.Items.Add(new ItemGrant
                {
                    ItemId = part.Item.Id,
                    Count = 1,
                    SourceFactionId = part.SourceFactionId,
                    Category = "HistoricalArmor",
                    AcquisitionBasis = produced
                        ? "Produced aboard Magnum as part of a complete armor set"
                        : string.IsNullOrEmpty(part.SourceFactionId)
                            ? "Complete armor set assembled from retained mission loot"
                            : "Complete armor set assembled from faction rewards and mission loot",
                    SelectionGroup = "ArmorSet." + armorSet.Id + "." + part.Item.ItemClass,
                    SelectionScore = Math.Round(part.Score, 6)
                });
                selectionState.Add(part.Item.Id);
            }
        }

        WarnRoleShortfall(plan, "complete armor sets", selected.Count, settings.ArmorSets);
    }

    private void AddAmmoReserve(
        StartPlan plan,
        IReadOnlyList<CompositeItemRecord> catalog,
        IReadOnlyList<RoleCandidate> selectedWeapons,
        int maximumWorldTech,
        RoleStockpileSettings settings,
        IReadOnlyDictionary<string, string> factionAvailability,
        IReadOnlyDictionary<string, LootAvailability> lootAvailability,
        RewardSelectionState selectionState)
    {
        Dictionary<string, int> weaponUseCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach (CompositeItemRecord record in catalog)
        {
            ItemRecord? item = record.GetRecord<ItemRecord>();
            WeaponRecord? weapon = record.GetRecord<WeaponRecord>();
            if (item == null
                || weapon == null
                || weapon.IsMelee
                || weapon.IsImplicit
                || string.IsNullOrEmpty(weapon.DefaultAmmoId)
                || !IsAllowedAtStage(record, item, maximumWorldTech, settings.AllowQuasiItems)
                || !IsPlayerFacingItem(weapon.DefaultAmmoId))
            {
                continue;
            }

            weaponUseCounts[weapon.DefaultAmmoId] = weaponUseCounts.TryGetValue(weapon.DefaultAmmoId, out int count)
                ? count + 1
                : 1;
        }

        List<RoleCandidate> ammoCandidates = catalog
            .Select(record =>
            {
                ItemRecord? item = record.GetRecord<ItemRecord>();
                AmmoRecord? ammo = record.GetRecord<AmmoRecord>();
                if (item == null
                    || ammo == null
                    || ammo.IsImplictedAmmo
                    || ammo.MaxStack <= 0
                    || !IsAllowedAtStage(record, item, maximumWorldTech, settings.AllowQuasiItems))
                {
                    return null;
                }

                RoleCandidate candidate = CreateRoleCandidate(
                    record,
                    item,
                    ammo.AmmoType,
                    factionAvailability,
                    lootAvailability,
                    craftable: false);
                int uses = weaponUseCounts.TryGetValue(item.Id, out int count) ? count : 0;
                int occurrences = GetLootAvailability(lootAvailability, item.Id).Occurrences;
                candidate.Score = 1.0
                                  + uses * 8.0
                                  + occurrences * 2.0
                                  + (HasCategory(item, "Common") ? 15.0 : 0.0)
                                  + Math.Max(0, maximumWorldTech - item.TechLevel) * 2.0
                                  - Math.Min(10.0, item.Price * 0.02);
                return candidate;
            })
            .Where(candidate => candidate != null)
            .Cast<RoleCandidate>()
            .ToList();
        List<RoleCandidate> common = ammoCandidates
            .Where(candidate => IsEverydayAmmo(candidate.Record.GetRecord<AmmoRecord>()?.AmmoType))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
            .Take(settings.CommonAmmoTypes)
            .ToList();
        HashSet<string> commonIds = new(common.Select(candidate => candidate.Item.Id), StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedWeaponAmmo = new(
            selectedWeapons
                .Select(candidate => candidate.Record.GetRecord<WeaponRecord>()?.DefaultAmmoId)
                .Where(itemId => !string.IsNullOrEmpty(itemId))
                .Cast<string>(),
            StringComparer.OrdinalIgnoreCase);
        List<RoleCandidate> specialPool = ammoCandidates
            .Where(candidate => !commonIds.Contains(candidate.Item.Id))
            .ToList();
        foreach (RoleCandidate candidate in specialPool)
        {
            if (selectedWeaponAmmo.Contains(candidate.Item.Id))
            {
                candidate.Score *= 2.0;
            }

            if (!string.IsNullOrEmpty(candidate.SourceFactionId))
            {
                candidate.Score *= 1.5;
            }
        }

        List<RoleCandidate> special = SelectRoleCandidates(
            specialPool,
            settings.SpecialAmmoTypes,
            preferDistinctGroups: false);
        AddStackedRoleGrants(
            plan,
            common,
            settings.CommonAmmoStacks,
            "AmmoReserve",
            "Ammo.Common",
            "Common ammunition accumulated across routine missions",
            selectionState);
        AddStackedRoleGrants(
            plan,
            special,
            settings.SpecialAmmoStacks,
            "AmmoReserve",
            "Ammo.Specialist",
            "Smaller reserve matched to specialist and faction weapons",
            selectionState);
        WarnRoleShortfall(plan, "common ammunition families", common.Count, settings.CommonAmmoTypes);
        WarnRoleShortfall(plan, "specialist ammunition families", special.Count, settings.SpecialAmmoTypes);
    }

    private void AddMedicalReserve(
        StartPlan plan,
        IReadOnlyList<CompositeItemRecord> catalog,
        int maximumWorldTech,
        RoleStockpileSettings settings,
        IReadOnlyDictionary<string, string> factionAvailability,
        IReadOnlyDictionary<string, LootAvailability> lootAvailability,
        RewardSelectionState selectionState)
    {
        List<RoleCandidate> candidates = catalog
            .Select(record =>
            {
                ItemRecord? item = record.GetRecord<ItemRecord>();
                if (item == null
                    || !IsMedicalItem(item.ItemClass)
                    || !HasCategory(item, "Medical")
                    || !IsAllowedAtStage(record, item, maximumWorldTech, settings.AllowQuasiItems))
                {
                    return null;
                }

                RoleCandidate candidate = CreateRoleCandidate(
                    record,
                    item,
                    item.ItemClass.ToString(),
                    factionAvailability,
                    lootAvailability,
                    craftable: false);
                LootAvailability loot = GetLootAvailability(lootAvailability, item.Id);
                candidate.Score = 1.0
                                  + loot.Occurrences * 4.0
                                  + 30.0 / (1.0 + Math.Max(0.0, item.Price) / 20.0)
                                  + (item.Price <= 20f ? 60.0 : 0.0)
                                  + Math.Max(0, maximumWorldTech - item.TechLevel) * 2.0;
                return candidate;
            })
            .Where(candidate => candidate != null)
            .Cast<RoleCandidate>()
            .ToList();
        int basicTarget = Math.Min(settings.MedicalItemTypes, (int)Math.Ceiling(settings.MedicalItemTypes * 0.7));
        List<RoleCandidate> basic = candidates
            .Where(candidate => candidate.Item.TechLevel <= 2 && candidate.Item.Price <= 125f)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
            .Take(basicTarget)
            .ToList();
        HashSet<string> selectedIds = new(basic.Select(candidate => candidate.Item.Id), StringComparer.OrdinalIgnoreCase);
        List<RoleCandidate> premium = SelectRoleCandidates(
            candidates.Where(candidate => !selectedIds.Contains(candidate.Item.Id)).ToList(),
            settings.MedicalItemTypes - basic.Count,
            preferDistinctGroups: true);
        AddStackedRoleGrants(
            plan,
            basic,
            settings.BasicMedicineStacks,
            "MedicalReserve",
            "Medical.Basic",
            "Frequently looted low-cost medicine retained in bulk",
            selectionState);
        AddStackedRoleGrants(
            plan,
            premium,
            settings.PremiumMedicineStacks,
            "MedicalReserve",
            "Medical.Specialist",
            "Smaller reserve of specialist medicine",
            selectionState);
        WarnRoleShortfall(plan, "medical supply types", basic.Count + premium.Count, settings.MedicalItemTypes);
    }

    private void AddRepairReserve(
        StartPlan plan,
        IReadOnlyList<CompositeItemRecord> catalog,
        int maximumWorldTech,
        RoleStockpileSettings settings,
        IReadOnlyDictionary<string, string> factionAvailability,
        IReadOnlyDictionary<string, LootAvailability> lootAvailability,
        RewardSelectionState selectionState)
    {
        List<RoleCandidate> candidates = catalog
            .Select(record =>
            {
                ItemRecord? item = record.GetRecord<ItemRecord>();
                RepairRecord? repair = record.GetRecord<RepairRecord>();
                if (item == null
                    || repair == null
                    || item.ItemClass != ItemClass.RepairKit
                    || !IsAllowedAtStage(record, item, maximumWorldTech, settings.AllowQuasiItems))
                {
                    return null;
                }

                RoleCandidate candidate = CreateRoleCandidate(
                    record,
                    item,
                    repair.RepairSpecialRule.ToString(),
                    factionAvailability,
                    lootAvailability,
                    craftable: false);
                LootAvailability loot = GetLootAvailability(lootAvailability, item.Id);
                candidate.Score += loot.Occurrences * 2.0 + 20.0 / (1.0 + item.Price / 100.0);
                return candidate;
            })
            .Where(candidate => candidate != null)
            .Cast<RoleCandidate>()
            .ToList();
        List<RoleCandidate> selected = SelectRoleCandidates(
            candidates,
            settings.RepairKitTypes,
            preferDistinctGroups: true);
        AddStackedRoleGrants(
            plan,
            selected,
            settings.RepairKitStacks,
            "RepairReserve",
            "Repair.Kit",
            "Repair kits retained after routine equipment maintenance",
            selectionState);
        WarnRoleShortfall(plan, "repair-kit types", selected.Count, settings.RepairKitTypes);
    }

    private void AddAugmentationReserve(
        StartPlan plan,
        IReadOnlyList<CompositeItemRecord> catalog,
        RoleStockpileSettings settings,
        IReadOnlyDictionary<string, string> factionAvailability,
        IReadOnlyDictionary<string, LootAvailability> lootAvailability,
        RewardSelectionState selectionState)
    {
        List<RoleCandidate> augmentations = catalog
            .Select(record =>
            {
                ItemRecord? item = record.GetRecord<ItemRecord>();
                AugmentationRecord? augmentation = record.GetRecord<AugmentationRecord>();
                if (item == null
                    || augmentation == null
                    || record.GetRecord<ImplantRecord>() != null
                    || item.TechLevel <= 0
                    || item.TechLevel > settings.MaximumAugmentationTech
                    || item.Price <= 0f
                    || !HasUsefulCategories(item)
                    || (!settings.AllowQuasiItems && IsQuasiItem(record, item)))
                {
                    return null;
                }

                return CreateRoleCandidate(
                    record,
                    item,
                    augmentation.AugmentationClass.ToString(),
                    factionAvailability,
                    lootAvailability,
                    craftable: false);
            })
            .Where(candidate => candidate != null)
            .Cast<RoleCandidate>()
            .ToList();
        List<RoleCandidate> implants = catalog
            .Select(record =>
            {
                ItemRecord? item = record.GetRecord<ItemRecord>();
                ImplantRecord? implant = record.GetRecord<ImplantRecord>();
                if (item == null
                    || implant == null
                    || item.TechLevel <= 0
                    || item.TechLevel > settings.MaximumImplantTech
                    || item.Price <= 0f
                    || !HasUsefulCategories(item)
                    || (!settings.AllowQuasiItems && IsQuasiItem(record, item)))
                {
                    return null;
                }

                return CreateRoleCandidate(
                    record,
                    item,
                    implant.SlotType,
                    factionAvailability,
                    lootAvailability,
                    craftable: false);
            })
            .Where(candidate => candidate != null)
            .Cast<RoleCandidate>()
            .ToList();
        List<RoleCandidate> selectedAugmentations = SelectRoleCandidates(
            augmentations,
            settings.AugmentationItems,
            preferDistinctGroups: true);
        List<RoleCandidate> selectedImplants = SelectRoleCandidates(
            implants,
            settings.ImplantItems,
            preferDistinctGroups: true);
        AddIndividualRoleGrants(
            plan,
            selectedAugmentations,
            "AugmentationReserve",
            "Augmentation.Spare",
            "Spares acquired from augmentation clinics and recovered mission loot",
            selectionState);
        AddIndividualRoleGrants(
            plan,
            selectedImplants,
            "ImplantReserve",
            "Implant.Spare",
            "Implants acquired from clinics, factions, and recovered mission loot",
            selectionState);
        WarnRoleShortfall(plan, "augmentation spares", selectedAugmentations.Count, settings.AugmentationItems);
        WarnRoleShortfall(plan, "implant spares", selectedImplants.Count, settings.ImplantItems);
    }

    private void FillProductionRecipeUnlocks(StartPlan plan)
    {
        RoleStockpileSettings settings = _profile.Stash.RoleStockpile;
        if (!settings.Enabled || settings.ProductionRecipeUnlocks <= plan.ProductionRecipeUnlocks.Count)
        {
            return;
        }

        int maximumWorldTech = plan.Factions.Count == 0
            ? _profile.TechProgression.MaximumLevel
            : plan.Factions.Max(faction => faction.TechLevel);
        HashSet<string> unlocked = new(
            Require<MagnumCargo>().UnlockedProductionItems.Concat(plan.ProductionRecipeUnlocks),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> craftable = new(
            Data.ProduceReceipts.Select(receipt => receipt.OutputItem),
            StringComparer.OrdinalIgnoreCase);
        List<string> candidates = Data.Items.Records
            .OfType<CompositeItemRecord>()
            .Select(record => record.GetRecord<DatadiskRecord>())
            .Where(record => record != null
                             && record.UnlockType == DatadiskUnlockType.ProductionItem
                             && record.TechLevel <= maximumWorldTech)
            .SelectMany(record => record!.UnlockIds ?? new List<string>())
            .Where(itemId =>
            {
                CompositeItemRecord? itemRecord = GetCompositeRecord(itemId);
                ItemRecord? item = itemRecord?.GetRecord<ItemRecord>();
                return itemRecord != null
                       && item != null
                       && craftable.Contains(itemId)
                       && !unlocked.Contains(itemId)
                       && IsPlayerFacingItem(itemId)
                       && IsAllowedAtStage(itemRecord, item, maximumWorldTech, settings.AllowQuasiItems)
                       && IsAllowedAugmentationRecipe(itemRecord, item, settings);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(itemId => GetItemRecord(itemId)?.TechLevel ?? int.MaxValue)
            .ThenBy(itemId => itemId, StringComparer.Ordinal)
            .ToList();
        while (plan.ProductionRecipeUnlocks.Count < settings.ProductionRecipeUnlocks && candidates.Count > 0)
        {
            int topCount = Math.Min(12, candidates.Count);
            string selected = candidates[_random.Next(topCount)];
            candidates.Remove(selected);
            int before = plan.ProductionRecipeUnlocks.Count;
            AddProductionRecipeUnlockWithDependencies(plan, selected);
            if (plan.ProductionRecipeUnlocks.Count == before)
            {
                continue;
            }

            unlocked.UnionWith(plan.ProductionRecipeUnlocks);
            candidates.RemoveAll(unlocked.Contains);
        }

        WarnRoleShortfall(
            plan,
            "unlocked production recipes",
            plan.ProductionRecipeUnlocks.Count,
            settings.ProductionRecipeUnlocks);
    }

    private void AddProductionRecipeUnlockWithDependencies(StartPlan plan, string itemId)
    {
        MagnumCargo cargo = Require<MagnumCargo>();
        HashSet<string> existing = new(
            cargo.UnlockedProductionItems.Concat(plan.ProductionRecipeUnlocks),
            StringComparer.OrdinalIgnoreCase);
        List<string> additions = new() { itemId };
        CompositeItemRecord? record = GetCompositeRecord(itemId);
        if (record?.GetRecord<WeaponRecord>() is WeaponRecord weapon
            && !string.IsNullOrEmpty(weapon.DefaultAmmoId))
        {
            additions.Add(weapon.DefaultAmmoId);
        }

        ArmorSetRecord? armorSet = Data.ArmorSets.Records.FirstOrDefault(set =>
            set.Items != null && set.Items.Contains(itemId));
        if (armorSet?.Items != null)
        {
            additions.AddRange(armorSet.Items);
        }

        foreach (string addition in additions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!existing.Contains(addition)
                && IsPlayerFacingItem(addition)
                && Data.ProduceReceipts.Any(receipt =>
                    receipt.OutputItem.Equals(addition, StringComparison.OrdinalIgnoreCase)))
            {
                plan.ProductionRecipeUnlocks.Add(addition);
                existing.Add(addition);
            }
        }
    }

    private static bool IsRecipeDiscoverableAtStage(string itemId, int maximumWorldTech)
    {
        return Data.Items.Records
            .OfType<CompositeItemRecord>()
            .Select(record => record.GetRecord<DatadiskRecord>())
            .Any(record => record != null
                           && record.UnlockType == DatadiskUnlockType.ProductionItem
                           && record.TechLevel <= maximumWorldTech
                           && record.UnlockIds != null
                           && record.UnlockIds.Contains(itemId));
    }

    private RoleCandidate CreateRoleCandidate(
        CompositeItemRecord record,
        ItemRecord item,
        string group,
        IReadOnlyDictionary<string, string> factionAvailability,
        IReadOnlyDictionary<string, LootAvailability> lootAvailability,
        bool craftable)
    {
        LootAvailability loot = GetLootAvailability(lootAvailability, item.Id);
        string sourceFactionId = factionAvailability.TryGetValue(item.Id, out string factionId)
            ? factionId
            : string.Empty;
        double score = 1.0
                       + Math.Log(1.0 + loot.Occurrences) * 1.5
                       + Math.Log(1.0 + loot.Weight) * 0.25
                       + (HasCategory(item, "Common") ? 1.5 : 0.0)
                       + (craftable ? 1.25 : 0.0)
                       + (!string.IsNullOrEmpty(sourceFactionId) ? 1.0 : 0.0)
                       + Math.Max(0, _profile.TechProgression.MaximumLevel - item.TechLevel) * 0.1;
        return new RoleCandidate(record, item, group, sourceFactionId, craftable, score);
    }

    private List<RoleCandidate> SelectRoleCandidates(
        List<RoleCandidate> candidates,
        int target,
        bool preferDistinctGroups)
    {
        List<RoleCandidate> remaining = candidates
            .GroupBy(candidate => candidate.Item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).First())
            .ToList();
        List<RoleCandidate> selected = new();
        Dictionary<string, int> groupCounts = new(StringComparer.OrdinalIgnoreCase);
        while (selected.Count < target && remaining.Count > 0)
        {
            List<RoleCandidate> ranked = remaining
                .OrderByDescending(candidate =>
                    candidate.Score
                    / (preferDistinctGroups
                        ? 1.0 + (groupCounts.TryGetValue(candidate.Group, out int count) ? count * 2.0 : 0.0)
                        : 1.0))
                .ThenBy(candidate => candidate.Item.Id, StringComparer.Ordinal)
                .Take(Math.Min(10, remaining.Count))
                .ToList();
            double total = ranked.Sum(candidate => Math.Max(0.001, candidate.Score));
            double value = _random.NextDouble() * total;
            RoleCandidate chosen = ranked[ranked.Count - 1];
            foreach (RoleCandidate candidate in ranked)
            {
                value -= Math.Max(0.001, candidate.Score);
                if (value <= 0.0)
                {
                    chosen = candidate;
                    break;
                }
            }

            selected.Add(chosen);
            remaining.Remove(chosen);
            groupCounts[chosen.Group] = groupCounts.TryGetValue(chosen.Group, out int count) ? count + 1 : 1;
        }

        return selected;
    }

    private static Dictionary<string, LootAvailability> BuildLootAvailability()
    {
        Dictionary<string, LootAvailability> availability = new(StringComparer.OrdinalIgnoreCase);
        foreach (string containerId in Data.ContainerItemDrop.ContainerIds)
        {
            foreach (string biomeId in Data.ContainerItemDrop.GetDropBiomes(containerId))
            {
                List<Tuple<float, string>> drops = Data.ContainerItemDrop.GetDrop(containerId, biomeId);
                if (drops == null)
                {
                    continue;
                }

                foreach (Tuple<float, string> drop in drops)
                {
                    if (string.IsNullOrWhiteSpace(drop.Item2))
                    {
                        continue;
                    }

                    if (!availability.TryGetValue(drop.Item2, out LootAvailability loot))
                    {
                        loot = new LootAvailability();
                        availability.Add(drop.Item2, loot);
                    }

                    loot.Occurrences++;
                    loot.Weight += Math.Max(0.0, drop.Item1);
                }
            }
        }

        return availability;
    }

    private static LootAvailability GetLootAvailability(
        IReadOnlyDictionary<string, LootAvailability> availability,
        string itemId)
    {
        return availability.TryGetValue(itemId, out LootAvailability loot)
            ? loot
            : LootAvailability.Empty;
    }

    private static bool IsAllowedAtStage(
        CompositeItemRecord record,
        ItemRecord item,
        int maximumTech,
        bool allowQuasiItems)
    {
        return item.TechLevel > 0
               && item.TechLevel <= maximumTech
               && (allowQuasiItems || !IsQuasiItem(record, item));
    }

    private static bool IsAllowedAugmentationRecipe(
        CompositeItemRecord record,
        ItemRecord item,
        RoleStockpileSettings settings)
    {
        if (record.GetRecord<ImplantRecord>() != null)
        {
            return item.TechLevel <= settings.MaximumImplantTech;
        }

        return record.GetRecord<AugmentationRecord>() == null
               || item.TechLevel <= settings.MaximumAugmentationTech;
    }

    private static bool IsQuasiItem(CompositeItemRecord record, ItemRecord item)
    {
        return item.ItemClass == ItemClass.QuasiAug
               || item.ItemClass == ItemClass.QuasiArtefact
               || item.ItemClass == ItemClass.QuasiOrgan
               || item.ItemClass == ItemClass.QuasiPact
               || item.Id.StartsWith("quasi_", StringComparison.OrdinalIgnoreCase)
               || HasCategory(item, "Quasi")
               || HasCategory(item, "RitualItem");
    }

    private static bool IsArmorItem(ItemClass itemClass)
    {
        return itemClass == ItemClass.Helmet
               || itemClass == ItemClass.Armor
               || itemClass == ItemClass.Leggings
               || itemClass == ItemClass.Boots;
    }

    private static bool IsMedicalItem(ItemClass itemClass)
    {
        return itemClass == ItemClass.Pills
               || itemClass == ItemClass.Syringe
               || itemClass == ItemClass.Medpack
               || itemClass == ItemClass.Dressing;
    }

    private static bool IsEverydayAmmo(string? ammoType)
    {
        return ammoType != null
               && (ammoType.Equals("Bullets", StringComparison.OrdinalIgnoreCase)
                   || ammoType.Equals("Medium", StringComparison.OrdinalIgnoreCase)
                   || ammoType.Equals("Shells", StringComparison.OrdinalIgnoreCase)
                   || ammoType.Equals("Heavy", StringComparison.OrdinalIgnoreCase)
                   || ammoType.Equals("Bolts", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasCategory(ItemRecord item, string category)
    {
        return item.Categories != null && item.Categories.Contains(category);
    }

    private static bool HasUsefulCategories(ItemRecord item)
    {
        return item.Categories != null
               && item.Categories.Any(category =>
                   !string.IsNullOrWhiteSpace(category)
                   && !category.Equals("none", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddStackedRoleGrants(
        StartPlan plan,
        IEnumerable<RoleCandidate> candidates,
        int stacks,
        string category,
        string selectionGroup,
        string acquisitionBasis,
        RewardSelectionState selectionState)
    {
        foreach (RoleCandidate candidate in candidates)
        {
            plan.Items.Add(new ItemGrant
            {
                ItemId = candidate.Item.Id,
                Count = stacks,
                FullStacks = true,
                SourceFactionId = candidate.SourceFactionId,
                Category = category,
                AcquisitionBasis = acquisitionBasis,
                SelectionGroup = selectionGroup,
                SelectionScore = Math.Round(candidate.Score, 6)
            });
            selectionState.Add(candidate.Item.Id, stacks);
        }
    }

    private static void AddIndividualRoleGrants(
        StartPlan plan,
        IEnumerable<RoleCandidate> candidates,
        string category,
        string selectionGroup,
        string acquisitionBasis,
        RewardSelectionState selectionState)
    {
        foreach (RoleCandidate candidate in candidates)
        {
            plan.Items.Add(new ItemGrant
            {
                ItemId = candidate.Item.Id,
                Count = 1,
                SourceFactionId = candidate.SourceFactionId,
                Category = category,
                AcquisitionBasis = acquisitionBasis,
                SelectionGroup = selectionGroup + "." + candidate.Group,
                SelectionScore = Math.Round(candidate.Score, 6)
            });
            selectionState.Add(candidate.Item.Id);
        }
    }

    private static void WarnRoleShortfall(StartPlan plan, string role, int actual, int target)
    {
        if (actual < target)
        {
            plan.Warnings.Add($"Generated {actual} of {target} requested {role} after stage and player-facing filters.");
        }
    }

    private void AddAccumulatedMaterialStockpile(
        StartPlan plan,
        IReadOnlyList<string> sourceFactionIds,
        RewardSelectionState selectionState)
    {
        MaterialStockpileSettings settings = _profile.Stash.MaterialStockpile;
        if (!settings.Enabled || settings.TargetDistinctItems <= 0)
        {
            return;
        }

        Dictionary<string, MaterialCandidate> demand = BuildMaterialDemand();
        Dictionary<string, FactionPlan> factionPlans = plan.Factions.ToDictionary(
            faction => faction.Id,
            faction => faction,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> factionAvailability = BuildFactionItemAvailability(
            sourceFactionIds,
            factionPlans);
        Dictionary<string, LootAvailability> lootAvailability = BuildLootAvailability();
        HashSet<string> configuredRareIds = new(settings.RareItemIds, StringComparer.OrdinalIgnoreCase);
        int maximumWorldTech = plan.Factions.Count == 0
            ? _profile.TechProgression.MaximumLevel
            : plan.Factions.Max(faction => faction.TechLevel);

        List<MaterialCandidate> candidates = demand.Values
            .Where(candidate => IsEligibleStockpileMaterial(candidate, settings, maximumWorldTech))
            .Select(candidate =>
            {
                candidate.SourceFactionId = factionAvailability.TryGetValue(candidate.ItemId, out string factionId)
                    ? factionId
                    : string.Empty;
                candidate.LootOccurrences = GetLootAvailability(lootAvailability, candidate.ItemId).Occurrences;
                candidate.IsRare = candidate.UpgradeEligible
                                   || configuredRareIds.Contains(candidate.ItemId)
                                   || candidate.LootOccurrences < settings.MinimumCommonLootOccurrences;
                candidate.Score = CalculateMaterialScore(candidate, settings);
                return candidate;
            })
            .Where(candidate => candidate.Score > 0.0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.ItemId, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
        {
            plan.Warnings.Add("No eligible crafting or upgrade materials were available for the accumulated stockpile.");
            return;
        }

        int target = Math.Min(settings.TargetDistinctItems, candidates.Count);
        int candidatePoolSize = Math.Min(
            candidates.Count,
            Math.Max(target, (int)Math.Ceiling(candidates.Count * settings.TopCandidateFraction)));
        List<MaterialCandidate> eligible = candidates.Take(candidatePoolSize).ToList();
        List<MaterialCandidate> commonEligible = eligible.Where(candidate => !candidate.IsRare).ToList();
        List<MaterialCandidate> rareEligible = eligible.Where(candidate => candidate.IsRare).ToList();
        int rareTarget = Math.Min(
            Math.Min(settings.MaximumRareItems, rareEligible.Count),
            (int)Math.Ceiling(target * 0.2));
        int commonTarget = Math.Min(target - rareTarget, commonEligible.Count);
        rareTarget = Math.Min(settings.MaximumRareItems, Math.Min(rareEligible.Count, target - commonTarget));
        List<MaterialCandidate> selected = new();
        int reliableCommon = Math.Min(commonTarget, (int)Math.Ceiling(commonTarget * 0.7));
        selected.AddRange(commonEligible.Take(reliableCommon));
        commonEligible.RemoveRange(0, reliableCommon);
        while (selected.Count < commonTarget && commonEligible.Count > 0)
        {
            MaterialCandidate chosen = SelectWeightedMaterial(commonEligible);
            selected.Add(chosen);
            commonEligible.Remove(chosen);
        }

        int selectedRare = 0;
        int reliableRare = Math.Min(rareTarget, (int)Math.Ceiling(rareTarget * 0.5));
        selected.AddRange(rareEligible.Take(reliableRare));
        rareEligible.RemoveRange(0, reliableRare);
        selectedRare += reliableRare;
        while (selectedRare < rareTarget && rareEligible.Count > 0)
        {
            MaterialCandidate chosen = SelectWeightedMaterial(rareEligible);
            selected.Add(chosen);
            rareEligible.Remove(chosen);
            selectedRare++;
        }

        while (selected.Count < target && commonEligible.Count > 0)
        {
            MaterialCandidate chosen = SelectWeightedMaterial(commonEligible);
            selected.Add(chosen);
            commonEligible.Remove(chosen);
        }

        double maximumCraftDemand = Math.Max(
            1.0,
            selected.Where(candidate => candidate.CraftEligible)
                .Select(candidate => candidate.CraftingDemand)
                .DefaultIfEmpty(1.0)
                .Max());
        foreach (MaterialCandidate material in selected.OrderBy(candidate => candidate.ItemId, StringComparer.Ordinal))
        {
            bool fullStacks = material.CraftEligible && !material.IsRare;
            int count = fullStacks
                ? CalculateCraftingStackCount(material, maximumCraftDemand, settings)
                : CalculateUpgradeUnitCount(material, settings);
            if (count <= 0)
            {
                continue;
            }

            plan.Items.Add(new ItemGrant
            {
                ItemId = material.ItemId,
                Count = count,
                FullStacks = fullStacks,
                SourceFactionId = material.SourceFactionId,
                Category = "AccumulatedStockpile",
                AcquisitionBasis = string.IsNullOrEmpty(material.SourceFactionId)
                    ? "Recovered mission loot retained for future crafting"
                    : "Helped-faction rewards plus retained mission loot",
                SelectionGroup = fullStacks
                    ? "Stockpile.CommonCrafting"
                    : material.UpgradeEligible
                        ? "Stockpile.Upgrade"
                        : "Stockpile.RareCrafting",
                SelectionScore = Math.Round(material.Score, 6)
            });
            selectionState.Add(material.ItemId, count);
        }

        if (selected.Count < settings.TargetDistinctItems)
        {
            plan.Warnings.Add(
                $"Generated {selected.Count} of {settings.TargetDistinctItems} requested distinct stockpile materials "
                + "after recipe, upgrade-grade, durability, and world-tech filters.");
        }
    }

    private static Dictionary<string, MaterialCandidate> BuildMaterialDemand()
    {
        Dictionary<string, MaterialCandidate> demand = new(StringComparer.OrdinalIgnoreCase);
        foreach (ItemProduceReceipt receipt in Data.ProduceReceipts)
        {
            AddRecipeDemand(demand, receipt.RequiredItems);
        }

        foreach (WorkbenchReceiptRecord receipt in Data.WorkbenchReceipts)
        {
            AddRecipeDemand(demand, receipt.RequiredItems);
        }

        foreach (MagnumProjectPrice price in Data.MagnumProjectPrices)
        {
            if (price.ItemsGrades == null)
            {
                continue;
            }

            foreach (KeyValuePair<string, int> item in price.ItemsGrades)
            {
                MaterialCandidate candidate = GetMaterialCandidate(demand, item.Key);
                candidate.MinimumUpgradeGrade = Math.Min(candidate.MinimumUpgradeGrade, Math.Max(1, item.Value));
                candidate.UpgradeProjectUses++;
            }
        }

        return demand;
    }

    private static void AddRecipeDemand(
        IDictionary<string, MaterialCandidate> demand,
        IEnumerable<ItemQuantity>? requiredItems)
    {
        if (requiredItems == null)
        {
            return;
        }

        foreach (ItemQuantity required in requiredItems)
        {
            if (string.IsNullOrWhiteSpace(required.ItemId) || required.Count <= 0)
            {
                continue;
            }

            MaterialCandidate candidate = GetMaterialCandidate(demand, required.ItemId);
            candidate.RecipeUses++;
            candidate.RequiredUnits += required.Count;
        }
    }

    private static MaterialCandidate GetMaterialCandidate(
        IDictionary<string, MaterialCandidate> demand,
        string itemId)
    {
        if (!demand.TryGetValue(itemId, out MaterialCandidate candidate))
        {
            candidate = new MaterialCandidate(itemId);
            demand.Add(itemId, candidate);
        }

        return candidate;
    }

    private Dictionary<string, string> BuildFactionItemAvailability(
        IReadOnlyList<string> sourceFactionIds,
        IReadOnlyDictionary<string, FactionPlan> factionPlans)
    {
        Dictionary<string, string> availability = new(StringComparer.OrdinalIgnoreCase);
        FactionDropCollection.TradeItemsFor[] categories =
        {
            FactionDropCollection.TradeItemsFor.Equipment,
            FactionDropCollection.TradeItemsFor.Consumables,
            FactionDropCollection.TradeItemsFor.Chips
        };
        foreach (string factionId in sourceFactionIds)
        {
            foreach (FactionDropCollection.TradeItemsFor category in categories)
            {
                foreach (ContentDropRecord reward in GetFactionRewardPool(factionId, factionPlans[factionId], category))
                {
                    if (reward.ContentIds == null)
                    {
                        continue;
                    }

                    foreach (string itemId in reward.ContentIds)
                    {
                        if (!availability.ContainsKey(itemId))
                        {
                            availability.Add(itemId, factionId);
                        }
                    }
                }
            }
        }

        return availability;
    }

    private static bool IsEligibleStockpileMaterial(
        MaterialCandidate candidate,
        MaterialStockpileSettings settings,
        int maximumWorldTech)
    {
        BasePickupItemRecord? record = Data.Items.GetRecord(candidate.ItemId);
        ItemRecord? item = GetItemRecord(candidate.ItemId);
        if (record == null || item == null || !IsPlayerFacingItem(candidate.ItemId))
        {
            return false;
        }

        if (item.TechLevel > maximumWorldTech)
        {
            return false;
        }

        if (Data.ItemExpire.GetRecord(candidate.ItemId) != null)
        {
            return false;
        }

        bool upgradeEligible = candidate.MinimumUpgradeGrade != int.MaxValue
                               && (settings.MaximumUpgradeGrade < 0
                                   || candidate.MinimumUpgradeGrade <= settings.MaximumUpgradeGrade);
        candidate.CraftEligible = candidate.RecipeUses >= settings.MinimumRecipeUses;
        candidate.UpgradeEligible = upgradeEligible;
        if (!candidate.CraftEligible && !candidate.UpgradeEligible)
        {
            return false;
        }

        switch (item.ItemClass)
        {
            case ItemClass.Weapon:
            case ItemClass.ThrowableWeapon:
            case ItemClass.Helmet:
            case ItemClass.Armor:
            case ItemClass.Leggings:
            case ItemClass.Boots:
            case ItemClass.Backpack:
            case ItemClass.Vest:
            case ItemClass.Turret:
            case ItemClass.Mine:
            case ItemClass.Grenade:
            case ItemClass.QuasiPact:
            case ItemClass.QuestItem:
            case ItemClass.BioAug:
            case ItemClass.CyberneticAug:
            case ItemClass.QuasiAug:
            case ItemClass.PlaceableObstacle:
            case ItemClass.Key:
                return candidate.UpgradeEligible;
            default:
                return true;
        }
    }

    private static double CalculateMaterialScore(
        MaterialCandidate candidate,
        MaterialStockpileSettings settings)
    {
        double craftingDemand = Math.Log(1.0 + candidate.RequiredUnits)
                                + 0.75 * Math.Log(1.0 + candidate.RecipeUses);
        candidate.CraftingDemand = craftingDemand;
        double upgradeDemand = candidate.UpgradeEligible
            ? 2.5 / Math.Sqrt(Math.Max(1, candidate.MinimumUpgradeGrade))
              + 0.25 * candidate.UpgradeProjectUses
            : 0.0;
        double lootAvailability = Math.Log(1.0 + candidate.LootOccurrences);
        double score = 1.0 + settings.DemandWeight * (craftingDemand + upgradeDemand) + lootAvailability;
        if (!string.IsNullOrEmpty(candidate.SourceFactionId))
        {
            score *= settings.FactionAvailabilityWeight;
        }

        return score;
    }

    private MaterialCandidate SelectWeightedMaterial(IReadOnlyList<MaterialCandidate> candidates)
    {
        double total = candidates.Sum(candidate => candidate.Score);
        double value = _random.NextDouble() * total;
        foreach (MaterialCandidate candidate in candidates)
        {
            value -= candidate.Score;
            if (value <= 0.0)
            {
                return candidate;
            }
        }

        return candidates[candidates.Count - 1];
    }

    private int CalculateCraftingStackCount(
        MaterialCandidate material,
        double maximumDemand,
        MaterialStockpileSettings settings)
    {
        double demandRatio = Clamp(material.CraftingDemand / maximumDemand, 0.0, 1.0);
        double position = Clamp(0.25 + demandRatio * 0.75 + NextDouble(-0.15, 0.15), 0.0, 1.0);
        return InterpolateInt(settings.MinimumCraftingStacks, settings.MaximumCraftingStacks, position);
    }

    private int CalculateUpgradeUnitCount(
        MaterialCandidate material,
        MaterialStockpileSettings settings)
    {
        int maximumGrade = settings.MaximumUpgradeGrade < 0 ? 21 : Math.Max(1, settings.MaximumUpgradeGrade);
        double gradeRatio = Clamp(
            (material.MinimumUpgradeGrade - 1.0) / Math.Max(1.0, maximumGrade - 1.0),
            0.0,
            1.0);
        double position = Clamp(0.85 - gradeRatio * 0.65 + NextDouble(-0.1, 0.1), 0.0, 1.0);
        return InterpolateInt(settings.MinimumUpgradeUnits, settings.MaximumUpgradeUnits, position);
    }

    private static int InterpolateInt(int minimum, int maximum, double position)
    {
        return minimum + (int)Math.Round((maximum - minimum) * position, MidpointRounding.AwayFromZero);
    }

    private void AddFactionDrops(
        StartPlan plan,
        IReadOnlyList<string> sourceFactionIds,
        FactionDropCollection.TradeItemsFor category,
        int rolls,
        RewardSelectionState selectionState)
    {
        Dictionary<string, FactionPlan> factionPlans = plan.Factions.ToDictionary(
            faction => faction.Id,
            faction => faction,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<ContentDropRecord>> factionPools = new(StringComparer.OrdinalIgnoreCase);
        foreach (string factionId in sourceFactionIds)
        {
            FactionPlan factionPlan = factionPlans[factionId];
            List<ContentDropRecord> pool = GetFactionRewardPool(factionId, factionPlan, category);
            factionPools[factionId] = pool;
            if (pool.Count == 0)
            {
                plan.Warnings.Add($"No {category} reward pool was available for {factionId} at tech {factionPlan.TechLevel} and reputation {factionPlan.PlayerReputation:0}.");
            }
        }

        int generated = 0;
        for (int roll = 0; roll < rolls; roll++)
        {
            SelectedReward? selected = null;
            string factionId = string.Empty;
            for (int sourceOffset = 0; sourceOffset < sourceFactionIds.Count; sourceOffset++)
            {
                factionId = sourceFactionIds[(roll + sourceOffset) % sourceFactionIds.Count];
                selected = SelectReward(
                    factionPools[factionId],
                    category,
                    factionPlans[factionId],
                    selectionState);
                if (selected != null)
                {
                    break;
                }
            }

            if (selected == null)
            {
                break;
            }

            foreach (string itemId in selected.Record.ContentIds)
            {
                if (!IsPlayerFacingItem(itemId))
                {
                    plan.Warnings.Add($"Faction reward pool referenced internal, implicit, or missing item '{itemId}'.");
                    continue;
                }

                plan.Items.Add(new ItemGrant
                {
                    ItemId = itemId,
                    Count = 1,
                    SourceFactionId = factionId,
                    Category = category.ToString(),
                    SelectionGroup = GetRewardGroup(itemId),
                    SelectionScore = Math.Round(selected.Score, 6)
                });
                selectionState.Add(itemId);
                if (!_profile.Stash.RoleStockpile.Enabled)
                {
                    AddWeaponAmmo(plan, itemId, factionId, selectionState);
                }
            }

            generated++;
        }

        if (generated < rolls && factionPools.Values.Any(pool => pool.Count > 0))
        {
            plan.Warnings.Add(
                $"Generated {generated} of {rolls} requested {category} rewards because the remaining "
                + "faction-authentic candidates exceeded configured duplicate limits.");
        }
    }

    private List<ContentDropRecord> GetFactionRewardPool(
        string factionId,
        FactionPlan factionPlan,
        FactionDropCollection.TradeItemsFor category)
    {
        Faction temporaryFaction = Faction.Create(factionId);
        temporaryFaction.CurrentTechLevel = factionPlan.TechLevel;
        temporaryFaction.PlayerReputation = factionPlan.PlayerReputation;
        List<ContentDropRecord> pool = Data.FactionDrop.GetTradeItems(temporaryFaction, category);
        if ((pool == null || pool.Count == 0)
            && temporaryFaction.Record.UseGeneralRewards
            && category != FactionDropCollection.TradeItemsFor.Chips)
        {
            FactionDropCollection.TradeItemsFor fallback = category == FactionDropCollection.TradeItemsFor.Equipment
                ? FactionDropCollection.TradeItemsFor.GeneralEquipment
                : FactionDropCollection.TradeItemsFor.GeneralConsumables;
            pool = Data.FactionDrop.GetTradeItems(temporaryFaction, fallback);
        }

        return pool ?? new List<ContentDropRecord>();
    }

    private SelectedReward? SelectReward(
        IReadOnlyList<ContentDropRecord> pool,
        FactionDropCollection.TradeItemsFor category,
        FactionPlan factionPlan,
        RewardSelectionState selectionState)
    {
        RewardSelectionSettings settings = _profile.Stash.RewardSelection;
        if (!settings.Enabled)
        {
            if (pool.Count == 0)
            {
                return null;
            }

            ContentDropRecord legacySelection = SelectWeighted(pool);
            return new SelectedReward(legacySelection, Math.Max(0.0, legacySelection.Weight));
        }

        int copyLimit = GetCopyLimit(category, settings);
        List<SelectedReward> candidates = pool
            .Where(record => IsEligibleReward(record, copyLimit, selectionState))
            .Select(record => new SelectedReward(
                record,
                CalculateRewardScore(record, factionPlan, settings, selectionState)))
            .Where(candidate => candidate.Score > 0.0)
            .OrderByDescending(candidate => candidate.Score)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        int topCount = Math.Min(
            candidates.Count,
            Math.Max(
                settings.MinimumCandidatePoolSize,
                (int)Math.Ceiling(candidates.Count * settings.TopCandidateFraction)));
        return SelectWeightedReward(candidates.Take(topCount).ToList());
    }

    private static int GetCopyLimit(
        FactionDropCollection.TradeItemsFor category,
        RewardSelectionSettings settings)
    {
        return category == FactionDropCollection.TradeItemsFor.Equipment
            ? settings.MaxEquipmentCopiesPerItem
            : category == FactionDropCollection.TradeItemsFor.Chips
                ? settings.MaxChipCopiesPerItem
                : settings.MaxConsumableCopiesPerItem;
    }

    private static bool IsEligibleReward(
        ContentDropRecord record,
        int copyLimit,
        RewardSelectionState selectionState)
    {
        if (record.ContentIds == null || record.ContentIds.Count == 0)
        {
            return false;
        }

        Dictionary<string, int> bundleCounts = new(StringComparer.OrdinalIgnoreCase);
        foreach (string itemId in record.ContentIds)
        {
            if (!IsPlayerFacingItem(itemId))
            {
                return false;
            }

            bundleCounts[itemId] = bundleCounts.TryGetValue(itemId, out int count) ? count + 1 : 1;
        }

        return copyLimit < 0 || bundleCounts.All(pair => selectionState.GetItemCount(pair.Key) + pair.Value <= copyLimit);
    }

    private static double CalculateRewardScore(
        ContentDropRecord record,
        FactionPlan factionPlan,
        RewardSelectionSettings settings,
        RewardSelectionState selectionState)
    {
        double factionWeight = Math.Max(0.0, record.Weight);
        if (factionWeight <= 0.0)
        {
            return 0.0;
        }

        factionWeight = settings.FactionWeightExponent == 0.0
            ? 1.0
            : Math.Pow(factionWeight, settings.FactionWeightExponent);
        double totalUtility = 0.0;
        int validItems = 0;
        foreach (string itemId in record.ContentIds)
        {
            ItemRecord? item = GetItemRecord(itemId);
            if (item == null)
            {
                continue;
            }

            string group = GetRewardGroup(itemId);
            int itemCopies = Math.Min(5, selectionState.GetItemCount(itemId));
            int groupCopies = Math.Min(5, selectionState.GetGroupCount(group));
            double utility = itemCopies > 0
                ? Math.Pow(settings.DuplicateItemWeight, itemCopies)
                : 1.0;
            utility *= groupCopies > 0
                ? Math.Pow(settings.DuplicateGroupWeight, groupCopies)
                : settings.MissingGroupWeight;
            utility *= 1.0 + settings.TechLevelWeight * item.TechLevel / Math.Max(1.0, factionPlan.TechLevel);
            utility *= 1.0 + settings.PriceWeight * Math.Log10(1.0 + Math.Max(0.0, item.Price));
            totalUtility += utility;
            validItems++;
        }

        return validItems == 0 ? 0.0 : factionWeight * totalUtility / validItems;
    }

    private SelectedReward SelectWeightedReward(IReadOnlyList<SelectedReward> candidates)
    {
        double total = candidates.Sum(candidate => candidate.Score);
        double value = _random.NextDouble() * total;
        foreach (SelectedReward candidate in candidates)
        {
            value -= candidate.Score;
            if (value <= 0.0)
            {
                return candidate;
            }
        }

        return candidates[candidates.Count - 1];
    }

    private void AddWeaponAmmo(
        StartPlan plan,
        string itemId,
        string factionId,
        RewardSelectionState selectionState)
    {
        if (_profile.Stash.AmmoStacksPerWeapon <= 0
            || Data.Items.GetRecord(itemId) is not CompositeItemRecord composite
            || composite.GetRecord<WeaponRecord>() is not WeaponRecord weapon
            || string.IsNullOrEmpty(weapon.DefaultAmmoId)
            || !IsPlayerFacingItem(weapon.DefaultAmmoId)
            || GetCompositeRecord(weapon.DefaultAmmoId)?.GetRecord<AmmoRecord>() is not AmmoRecord ammo
            || ammo.IsImplictedAmmo)
        {
            return;
        }

        plan.Items.Add(new ItemGrant
        {
            ItemId = weapon.DefaultAmmoId,
            Count = _profile.Stash.AmmoStacksPerWeapon,
            FullStacks = true,
            SourceFactionId = factionId,
            Category = "WeaponAmmo",
            SelectionGroup = "Supply.Ammo"
        });
        selectionState.Add(weapon.DefaultAmmoId, _profile.Stash.AmmoStacksPerWeapon);
    }

    private RewardSelectionState CreateRewardSelectionState()
    {
        RewardSelectionState state = new();
        MagnumCargo cargo = Require<MagnumCargo>();
        foreach (ItemStorage storage in MagnumCargoSystem.GetAvailableCargoStorages(cargo))
        {
            foreach (BasePickupItem item in storage.Items)
            {
                state.Add(item.Id, Math.Max(1, (int)item.StackCount));
            }
        }

        return state;
    }

    private static CompositeItemRecord? GetCompositeRecord(string itemId)
    {
        return Data.Items.GetRecord(itemId) as CompositeItemRecord;
    }

    internal static bool IsPlayerFacingItem(string itemId)
    {
        CompositeItemRecord? composite = GetCompositeRecord(itemId);
        ItemRecord? item = composite?.GetRecord<ItemRecord>();
        if (composite == null
            || item == null
            || string.IsNullOrWhiteSpace(item.Id)
            || item.Id.IndexOf("_custom", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        WeaponRecord? weapon = composite.GetRecord<WeaponRecord>();
        if (weapon?.IsImplicit == true)
        {
            return false;
        }

        AmmoRecord? ammo = composite.GetRecord<AmmoRecord>();
        return ammo?.IsImplictedAmmo != true && (ammo == null || ammo.MaxStack > 0);
    }

    private static ItemRecord? GetItemRecord(string itemId)
    {
        BasePickupItemRecord record = Data.Items.GetRecord(itemId);
        if (record is CompositeItemRecord composite)
        {
            return composite.Records.OfType<ItemRecord>().FirstOrDefault();
        }

        return record as ItemRecord;
    }

    private static string GetRewardGroup(string itemId)
    {
        BasePickupItemRecord record = Data.Items.GetRecord(itemId);
        CompositeItemRecord? composite = record as CompositeItemRecord;
        ItemRecord? item = GetItemRecord(itemId);
        if (item?.ItemClass == ItemClass.ThrowableWeapon)
        {
            return "Weapon.Throwable";
        }

        WeaponRecord? weapon = composite?.GetRecord<WeaponRecord>() ?? record as WeaponRecord;
        if (weapon != null)
        {
            return weapon.IsMelee ? "Weapon.Melee" : "Weapon.Ranged";
        }

        if (item == null)
        {
            return "Unknown";
        }

        switch (item.ItemClass)
        {
            case ItemClass.Helmet:
                return "Armor.Helmet";
            case ItemClass.Armor:
                return "Armor.Body";
            case ItemClass.Leggings:
                return "Armor.Legs";
            case ItemClass.Boots:
                return "Armor.Boots";
            case ItemClass.Backpack:
                return "Carry.Backpack";
            case ItemClass.Vest:
                return "Carry.Vest";
            case ItemClass.Ammo:
                return "Supply.Ammo";
            case ItemClass.Food:
            case ItemClass.Drink:
            case ItemClass.Alcohol:
                return "Supply.Provisions";
            case ItemClass.Pills:
            case ItemClass.Syringe:
            case ItemClass.Medpack:
            case ItemClass.Dressing:
                return "Supply.Medical";
            case ItemClass.Parts:
            case ItemClass.RepairKit:
                return "Supply.Maintenance";
            case ItemClass.Turret:
            case ItemClass.Mine:
            case ItemClass.Grenade:
            case ItemClass.PlaceableObstacle:
                return "Supply.CombatUtility";
            case ItemClass.BioAug:
            case ItemClass.CyberneticAug:
            case ItemClass.QuasiAug:
            case ItemClass.Cyborg:
                return "Augmentation";
            case ItemClass.Data:
            case ItemClass.Blueprint:
                return "Knowledge";
            case ItemClass.MilitaryBarter:
            case ItemClass.ScienceBarter:
            case ItemClass.IndustrialBarter:
            case ItemClass.ValuableBarter:
                return "Trade.Barter";
            default:
                return item.ItemClass.ToString();
        }
    }

    private ContentDropRecord SelectWeighted(IReadOnlyList<ContentDropRecord> pool)
    {
        double total = pool.Sum(record => Math.Max(0.0, record.Weight));
        if (total <= 0.0)
        {
            return pool[pool.Count - 1];
        }

        double value = _random.NextDouble() * total;
        foreach (ContentDropRecord record in pool)
        {
            value -= Math.Max(0.0, record.Weight);
            if (value <= 0.0)
            {
                return record;
            }
        }

        return pool[pool.Count - 1];
    }

    private sealed class SelectedReward
    {
        public SelectedReward(ContentDropRecord record, double score)
        {
            Record = record;
            Score = score;
        }

        public ContentDropRecord Record { get; }

        public double Score { get; }
    }

    private sealed class RoleCandidate
    {
        public RoleCandidate(
            CompositeItemRecord record,
            ItemRecord item,
            string group,
            string sourceFactionId,
            bool craftable,
            double score)
        {
            Record = record;
            Item = item;
            Group = group;
            SourceFactionId = sourceFactionId;
            Craftable = craftable;
            Score = score;
        }

        public CompositeItemRecord Record { get; }

        public ItemRecord Item { get; }

        public string Group { get; }

        public string SourceFactionId { get; }

        public bool Craftable { get; }

        public double Score { get; set; }
    }

    private sealed class ArmorSetCandidate
    {
        public ArmorSetCandidate(string id, List<RoleCandidate> parts, double score)
        {
            Id = id;
            Parts = parts;
            Score = score;
        }

        public string Id { get; }

        public List<RoleCandidate> Parts { get; }

        public double Score { get; }
    }

    private sealed class LootAvailability
    {
        public static readonly LootAvailability Empty = new();

        public int Occurrences { get; set; }

        public double Weight { get; set; }
    }

    private sealed class MaterialCandidate
    {
        public MaterialCandidate(string itemId)
        {
            ItemId = itemId;
        }

        public string ItemId { get; }

        public int RecipeUses { get; set; }

        public int RequiredUnits { get; set; }

        public int UpgradeProjectUses { get; set; }

        public int MinimumUpgradeGrade { get; set; } = int.MaxValue;

        public bool CraftEligible { get; set; }

        public bool UpgradeEligible { get; set; }

        public double CraftingDemand { get; set; }

        public string SourceFactionId { get; set; } = string.Empty;

        public double Score { get; set; }

        public int LootOccurrences { get; set; }

        public bool IsRare { get; set; }
    }

    private sealed class RewardSelectionState
    {
        private readonly Dictionary<string, int> _itemCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _groupCounts = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string itemId, int count = 1)
        {
            int normalizedCount = Math.Max(1, count);
            _itemCounts[itemId] = GetItemCount(itemId) + normalizedCount;
            string group = GetRewardGroup(itemId);
            _groupCounts[group] = GetGroupCount(group) + 1;
        }

        public int GetItemCount(string itemId)
        {
            return _itemCounts.TryGetValue(itemId, out int count) ? count : 0;
        }

        public int GetGroupCount(string group)
        {
            return _groupCounts.TryGetValue(group, out int count) ? count : 0;
        }
    }

    private List<string> SelectFactions(
        FactionSelectionSettings settings,
        IReadOnlyList<string> eligibleIds,
        HashSet<string> excludedByOtherSelection,
        StartPlan plan)
    {
        HashSet<string> eligible = new(eligibleIds, StringComparer.OrdinalIgnoreCase);
        HashSet<string> explicitExclusions = new(settings.ExcludedIds, StringComparer.OrdinalIgnoreCase);
        List<string> result = new();

        if (settings.Mode.Equals("Explicit", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string id in settings.Ids)
            {
                if (!eligible.Contains(id) || explicitExclusions.Contains(id) || excludedByOtherSelection.Contains(id))
                {
                    plan.Warnings.Add($"Explicit faction '{id}' is not eligible for this selection.");
                    continue;
                }

                if (!result.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        HashSet<string> allowed = new(settings.AllowedIds, StringComparer.OrdinalIgnoreCase);
        List<string> candidates = eligibleIds
            .Where(id =>
                (allowed.Count == 0 || allowed.Contains(id))
                && !explicitExclusions.Contains(id)
                && !excludedByOtherSelection.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        Shuffle(candidates);
        result.AddRange(candidates.Take(Math.Min(settings.Count, candidates.Count)));
        if (result.Count < settings.Count)
        {
            plan.Warnings.Add($"Only {result.Count} of {settings.Count} requested factions were eligible for a random selection.");
        }

        return result;
    }

    private static List<string> GetEligibleFactionIds()
    {
        return Data.Factions.Records
            .Where(record =>
                record.Enabled
                && record.CanBeTraded
                && record.FactionType != FactionType.None
                && !record.Id.Equals("Magnum", StringComparison.OrdinalIgnoreCase)
                && !record.Id.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                && !record.Id.EndsWith("Feral", StringComparison.OrdinalIgnoreCase))
            .Select(record => record.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> GetProductionReceipts(
        FactionType factionType,
        StationBarterRecord stationBarter)
    {
        return factionType switch
        {
            FactionType.Corp => stationBarter.CorpProduceItems,
            FactionType.CivilRes => stationBarter.CivResProduceItems,
            FactionType.Tezctlan or FactionType.Xiomara or FactionType.Shedu => stationBarter.QuasiProduceItems,
            FactionType.Pirates => stationBarter.PiratesProduceItems,
            _ => Array.Empty<string>()
        };
    }

    private List<string> SelectToTarget(
        HashSet<string> existing,
        List<string> candidates,
        IEnumerable<string> guaranteed,
        int configuredTarget,
        string entityName,
        StartPlan plan)
    {
        List<string> result = new();
        HashSet<string> available = new(candidates, StringComparer.OrdinalIgnoreCase);
        foreach (string id in guaranteed)
        {
            if (!available.Contains(id))
            {
                plan.Warnings.Add($"Guaranteed {entityName} '{id}' does not exist or is excluded.");
                continue;
            }

            if (!existing.Contains(id) && !result.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(id);
            }
        }

        int target = configuredTarget < 0 ? candidates.Count : configuredTarget;
        List<string> randomCandidates = candidates
            .Where(id => !existing.Contains(id) && !result.Contains(id, StringComparer.OrdinalIgnoreCase))
            .ToList();
        Shuffle(randomCandidates);
        int needed = Math.Max(0, target - existing.Count - result.Count);
        result.AddRange(randomCandidates.Take(needed));
        return result;
    }

    private static void FilterCandidates(
        List<string> candidates,
        IReadOnlyCollection<string> allowedIds,
        IReadOnlyCollection<string> excludedIds)
    {
        HashSet<string> allowed = new(allowedIds, StringComparer.OrdinalIgnoreCase);
        HashSet<string> excluded = new(excludedIds, StringComparer.OrdinalIgnoreCase);
        candidates.RemoveAll(id => (allowed.Count > 0 && !allowed.Contains(id)) || excluded.Contains(id));
    }

    private static bool IsConnected(MagnumPerkRecord record, HashSet<string> selected)
    {
        return record.Parents.Count == 0
               || record.Parents.Any(selected.Contains)
               || record.Childs.Any(selected.Contains);
    }

    private static bool IsAllowedDepartment(MagnumPerkRecord record, IReadOnlyCollection<string> allowedDepartments)
    {
        if (allowedDepartments.Count == 0)
        {
            return true;
        }

        return allowedDepartments.Any(value =>
            value.Equals(record.ModuleId, StringComparison.OrdinalIgnoreCase)
            || value.Equals(record.DepartmentId, StringComparison.OrdinalIgnoreCase)
            || value.Equals(record.Id, StringComparison.OrdinalIgnoreCase));
    }

    private static List<MagnumPerkRecord>? FindShortestRootPath(
        MagnumPerkRecord record,
        HashSet<string> visiting)
    {
        if (!visiting.Add(record.Id))
        {
            return null;
        }

        if (record.Parents.Count == 0)
        {
            visiting.Remove(record.Id);
            return new List<MagnumPerkRecord> { record };
        }

        List<MagnumPerkRecord>? best = null;
        foreach (string parentId in record.Parents.OrderBy(id => id, StringComparer.Ordinal))
        {
            MagnumPerkRecord parent = Data.MagnumPerks.GetRecord(parentId);
            if (parent == null || !parent.Enabled)
            {
                continue;
            }

            List<MagnumPerkRecord>? path = FindShortestRootPath(parent, visiting);
            if (path != null && (best == null || path.Count < best.Count))
            {
                best = path;
            }
        }

        visiting.Remove(record.Id);
        if (best != null)
        {
            best.Add(record);
        }

        return best;
    }

    private static void AddPlannedUpgrade(string id, HashSet<string> selected, List<string> plan)
    {
        if (selected.Add(id))
        {
            plan.Add(id);
        }
    }

    private string GenerateAgentName()
    {
        char first = NameCharacters[_random.Next(NameCharacters.Length)];
        int firstNumber = _random.Next(10);
        char second = NameCharacters[_random.Next(NameCharacters.Length)];
        int secondNumber = _random.Next(100, 1000);
        return $"{first}{firstNumber}-{second}{secondNumber}";
    }

    private int NextInt(IntRangeSettings range)
    {
        if (range.Maximum <= range.Minimum)
        {
            return range.Minimum;
        }

        long span = (long)range.Maximum - range.Minimum + 1L;
        long offset = (long)Math.Floor(_random.NextDouble() * span);
        return (int)(range.Minimum + offset);
    }

    private double NextDouble(double minimum, double maximum)
    {
        return maximum <= minimum ? minimum : minimum + _random.NextDouble() * (maximum - minimum);
    }

    private void Shuffle<T>(IList<T> values)
    {
        for (int index = values.Count - 1; index > 0; index--)
        {
            int swapIndex = _random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }

    private T Require<T>() where T : class
    {
        return _state.Get<T>() ?? throw new InvalidOperationException($"Game state component {typeof(T).Name} is unavailable.");
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }
}

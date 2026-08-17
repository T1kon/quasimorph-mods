using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomStart;

[Serializable]
public sealed class ModConfig
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool Enabled { get; set; } = true;

    public string ActiveProfile { get; set; } = "Early";

    public int? Seed { get; set; }

    public bool WriteReport { get; set; } = true;

    public bool DisableStationTransfersOnUnknownBuild { get; set; } = true;

    public Dictionary<string, StartProfile> Profiles { get; set; } = CreateDefaultProfiles();

    public static ModConfig Normalize(ModConfig? config, Action<string> warn)
    {
        config ??= new ModConfig();
        Dictionary<string, StartProfile> normalizedProfiles = new(StringComparer.OrdinalIgnoreCase);
        if (config.Profiles != null)
        {
            foreach (KeyValuePair<string, StartProfile> entry in config.Profiles)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value == null)
                {
                    warn("An unnamed or null start profile was ignored.");
                    continue;
                }

                normalizedProfiles[entry.Key.Trim()] = entry.Value;
            }
        }

        config.Profiles = normalizedProfiles;
        MigrateEarlyProfileName(config, warn);

        foreach (KeyValuePair<string, StartProfile> defaultProfile in CreateDefaultProfiles())
        {
            if (!config.Profiles.ContainsKey(defaultProfile.Key))
            {
                config.Profiles.Add(defaultProfile.Key, defaultProfile.Value);
            }
        }

        foreach (KeyValuePair<string, StartProfile> profile in config.Profiles)
        {
            profile.Value.Normalize(profile.Key, warn);
        }

        if (config.SchemaVersion > CurrentSchemaVersion)
        {
            warn($"Configuration schema {config.SchemaVersion} is newer than supported schema {CurrentSchemaVersion}; CustomStart is disabled.");
            config.Enabled = false;
        }
        else
        {
            if (config.SchemaVersion < 2)
            {
                MigrateLegacyStashDefaults(config);
                warn("Configuration schema 1 was upgraded with the new accumulated-stockpile defaults.");
            }

            if (config.SchemaVersion < 3)
            {
                MigrateRoleStockpileDefaults(config);
                warn("Configuration was upgraded with stage-specific arsenal, supply, augmentation, and recipe-history budgets.");
            }

            if (config.SchemaVersion < 4)
            {
                MigrateEstablishedCampaignDefaults(config);
                warn("Configuration was upgraded with guaranteed core Magnum departments and campaign-age-adjusted Mid/Late accumulation budgets.");
            }

            config.SchemaVersion = CurrentSchemaVersion;
        }

        if (string.IsNullOrWhiteSpace(config.ActiveProfile) || !config.Profiles.ContainsKey(config.ActiveProfile))
        {
            warn($"Active profile '{config.ActiveProfile}' does not exist; using Early.");
            config.ActiveProfile = "Early";
        }
        else
        {
            config.ActiveProfile = config.Profiles.Keys.First(key =>
                key.Equals(config.ActiveProfile, StringComparison.OrdinalIgnoreCase));
        }

        return config;
    }

    private static void MigrateEarlyProfileName(ModConfig config, Action<string> warn)
    {
        if (string.Equals(config.ActiveProfile, "EarlyPlus", StringComparison.OrdinalIgnoreCase))
        {
            config.ActiveProfile = "Early";
        }

        if (!config.Profiles.TryGetValue("EarlyPlus", out StartProfile legacyProfile))
        {
            return;
        }

        if (!config.Profiles.ContainsKey("Early"))
        {
            config.Profiles.Add("Early", legacyProfile);
        }

        config.Profiles.Remove("EarlyPlus");
        warn("Profile 'EarlyPlus' was renamed to 'Early'.");
    }

    private static void MigrateLegacyStashDefaults(ModConfig config)
    {
        MigrateLegacyStashProfile(config, "Early", 6, 12, 1, 4, 6, 1);
        MigrateLegacyStashProfile(config, "Mid", 20, 35, 5, 10, 12, 3);
        MigrateLegacyStashProfile(config, "Late", 40, 70, 12, 18, 20, 6);
    }

    private static void MigrateRoleStockpileDefaults(ModConfig config)
    {
        MigrateRoleStockpileProfile(config, "Early", 4, 6, 1, 18, 3, 4, 1, 12);
        MigrateRoleStockpileProfile(config, "Mid", 10, 12, 3, 32, 6, 6, 2, 16);
        MigrateRoleStockpileProfile(config, "Late", 18, 20, 6, 48, 10, 10, 4, 24);
    }

    private static void MigrateRoleStockpileProfile(
        ModConfig config,
        string profileName,
        int oldEquipment,
        int oldConsumables,
        int oldChips,
        int oldMaterialTypes,
        int newEquipment,
        int newConsumables,
        int newChips,
        int newMaterialTypes)
    {
        if (!config.Profiles.TryGetValue(profileName, out StartProfile profile))
        {
            return;
        }

        if (profile.Stash.EquipmentRolls == oldEquipment
            && profile.Stash.ConsumableRolls == oldConsumables
            && profile.Stash.ChipRolls == oldChips)
        {
            profile.Stash.EquipmentRolls = newEquipment;
            profile.Stash.ConsumableRolls = newConsumables;
            profile.Stash.ChipRolls = newChips;
        }

        if (profile.Stash.MaterialStockpile.TargetDistinctItems == oldMaterialTypes)
        {
            profile.Stash.MaterialStockpile.TargetDistinctItems = newMaterialTypes;
        }
    }

    private static void MigrateLegacyStashProfile(
        ModConfig config,
        string profileName,
        int oldEquipment,
        int oldConsumables,
        int oldChips,
        int newEquipment,
        int newConsumables,
        int newChips)
    {
        if (!config.Profiles.TryGetValue(profileName, out StartProfile profile)
            || profile.Stash.EquipmentRolls != oldEquipment
            || profile.Stash.ConsumableRolls != oldConsumables
            || profile.Stash.ChipRolls != oldChips)
        {
            return;
        }

        profile.Stash.EquipmentRolls = newEquipment;
        profile.Stash.ConsumableRolls = newConsumables;
        profile.Stash.ChipRolls = newChips;
    }

    private static void MigrateEstablishedCampaignDefaults(ModConfig config)
    {
        if (config.Profiles.TryGetValue("Early", out StartProfile early))
        {
            EnsureGuaranteedUpgradeIds(early.Magnum, StartProfile.CreateEarly().Magnum.GuaranteedUpgradeIds);
        }

        if (config.Profiles.TryGetValue("Mid", out StartProfile mid))
        {
            EnsureGuaranteedUpgradeIds(mid.Magnum, StartProfile.CreateMid().Magnum.GuaranteedUpgradeIds);
            mid.Roster.TargetCloneCount = MigrateDefaultValue(mid.Roster.TargetCloneCount, 10, 14);
            mid.Roster.TargetClassCount = MigrateDefaultValue(mid.Roster.TargetClassCount, 8, 12);
            mid.Magnum.TargetUpgradeCount = MigrateDefaultValue(mid.Magnum.TargetUpgradeCount, 40, 90);
            mid.Stash.EquipmentRolls = MigrateDefaultValue(mid.Stash.EquipmentRolls, 6, 8);
            mid.Stash.ConsumableRolls = MigrateDefaultValue(mid.Stash.ConsumableRolls, 6, 8);
            mid.Stash.ChipRolls = MigrateDefaultValue(mid.Stash.ChipRolls, 2, 3);
            MigrateMaterialStockpile(mid.Stash.MaterialStockpile, false);
            MigrateRoleStockpile(mid.Stash.RoleStockpile, false);
        }

        if (config.Profiles.TryGetValue("Late", out StartProfile late))
        {
            late.Stash.EquipmentRolls = MigrateDefaultValue(late.Stash.EquipmentRolls, 10, 14);
            late.Stash.ConsumableRolls = MigrateDefaultValue(late.Stash.ConsumableRolls, 10, 14);
            late.Stash.ChipRolls = MigrateDefaultValue(late.Stash.ChipRolls, 4, 6);
            MigrateMaterialStockpile(late.Stash.MaterialStockpile, true);
            MigrateRoleStockpile(late.Stash.RoleStockpile, true);
        }
    }

    private static void EnsureGuaranteedUpgradeIds(MagnumSettings settings, IEnumerable<string> requiredIds)
    {
        HashSet<string> existing = new(settings.GuaranteedUpgradeIds, StringComparer.OrdinalIgnoreCase);
        HashSet<string> excluded = new(settings.ExcludedUpgradeIds, StringComparer.OrdinalIgnoreCase);
        foreach (string id in requiredIds)
        {
            if (!excluded.Contains(id) && existing.Add(id))
            {
                settings.GuaranteedUpgradeIds.Add(id);
            }
        }
    }

    private static void MigrateMaterialStockpile(MaterialStockpileSettings settings, bool late)
    {
        settings.TargetDistinctItems = MigrateDefaultValue(settings.TargetDistinctItems, late ? 24 : 16, late ? 40 : 28);
        settings.MinimumCraftingStacks = MigrateDefaultValue(settings.MinimumCraftingStacks, late ? 2 : 1, late ? 3 : 2);
        settings.MaximumCraftingStacks = MigrateDefaultValue(settings.MaximumCraftingStacks, late ? 5 : 3, late ? 8 : 6);
        settings.MaximumUpgradeUnits = MigrateDefaultValue(settings.MaximumUpgradeUnits, late ? 8 : 4, late ? 12 : 6);
        settings.MaximumRareItems = MigrateDefaultValue(settings.MaximumRareItems, late ? 8 : 4, late ? 10 : 5);
    }

    private static void MigrateRoleStockpile(RoleStockpileSettings settings, bool late)
    {
        settings.WeaponItems = MigrateDefaultValue(settings.WeaponItems, late ? 18 : 10, late ? 30 : 20);
        settings.ArmorSets = MigrateDefaultValue(settings.ArmorSets, late ? 5 : 3, late ? 8 : 5);
        settings.CommonAmmoTypes = MigrateDefaultValue(settings.CommonAmmoTypes, late ? 7 : 6, late ? 8 : 7);
        settings.SpecialAmmoTypes = MigrateDefaultValue(settings.SpecialAmmoTypes, late ? 8 : 4, late ? 12 : 6);
        settings.CommonAmmoStacks = MigrateDefaultValue(settings.CommonAmmoStacks, late ? 10 : 6, late ? 14 : 10);
        settings.SpecialAmmoStacks = MigrateDefaultValue(settings.SpecialAmmoStacks, late ? 4 : 2, late ? 6 : 3);
        settings.MedicalItemTypes = MigrateDefaultValue(settings.MedicalItemTypes, late ? 10 : 7, late ? 14 : 10);
        settings.BasicMedicineStacks = MigrateDefaultValue(settings.BasicMedicineStacks, late ? 6 : 4, late ? 8 : 6);
        settings.PremiumMedicineStacks = MigrateDefaultValue(settings.PremiumMedicineStacks, late ? 4 : 2, late ? 5 : 3);
        settings.RepairKitTypes = MigrateDefaultValue(settings.RepairKitTypes, late ? 8 : 4, late ? 10 : 7);
        settings.RepairKitStacks = MigrateDefaultValue(settings.RepairKitStacks, late ? 5 : 3, late ? 7 : 5);
        settings.AugmentationItems = MigrateDefaultValue(settings.AugmentationItems, late ? 12 : 6, late ? 18 : 10);
        settings.ImplantItems = MigrateDefaultValue(settings.ImplantItems, late ? 15 : 6, late ? 25 : 12);
        settings.ProductionRecipeUnlocks = MigrateDefaultValue(settings.ProductionRecipeUnlocks, late ? 70 : 26, late ? 120 : 55);
    }

    private static int MigrateDefaultValue(int value, int oldDefault, int newDefault)
    {
        return value == oldDefault ? newDefault : value;
    }

    public StartProfile GetActiveProfile()
    {
        return Profiles[ActiveProfile];
    }

    public static Dictionary<string, StartProfile> CreateDefaultProfiles()
    {
        return new Dictionary<string, StartProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Early"] = StartProfile.CreateEarly(),
            ["Mid"] = StartProfile.CreateMid(),
            ["Late"] = StartProfile.CreateLate()
        };
    }
}

[Serializable]
public sealed class StartProfile
{
    public int ElapsedDays { get; set; } = 180;

    public TechProgressionSettings TechProgression { get; set; } = new();

    public FactionHistorySettings Factions { get; set; } = new();

    public RosterSettings Roster { get; set; } = new();

    public MagnumSettings Magnum { get; set; } = new();

    public StashSettings Stash { get; set; } = new();

    public void Normalize(string name, Action<string> warn)
    {
        int originalElapsedDays = ElapsedDays;
        ElapsedDays = Math.Max(0, Math.Min(1_000_000, ElapsedDays));
        if (ElapsedDays != originalElapsedDays)
        {
            warn($"Profile '{name}' ElapsedDays was clamped to {ElapsedDays}.");
        }
        TechProgression ??= new TechProgressionSettings();
        Factions ??= new FactionHistorySettings();
        Roster ??= new RosterSettings();
        Magnum ??= new MagnumSettings();
        Stash ??= new StashSettings();

        TechProgression.Normalize();
        Factions.Normalize();
        Roster.Normalize();
        Magnum.Normalize();
        Stash.Normalize(name);

        if (ElapsedDays == 0)
        {
            warn($"Profile '{name}' has ElapsedDays=0; its date remains vanilla.");
        }
    }

    public static StartProfile CreateEarly()
    {
        return new StartProfile
        {
            ElapsedDays = 180,
            TechProgression = new TechProgressionSettings
            {
                WorldProgressLevel = 2.4,
                MinimumLevel = 2,
                MaximumLevel = 3,
                MaxActiveFactionSpread = 1,
                PendingTechFraction = 0.04
            },
            Factions = new FactionHistorySettings
            {
                HelpedFactions = FactionSelectionSettings.Random(1),
                RivalFactions = FactionSelectionSettings.Random(1),
                HelpedReputation = new IntRangeSettings(25, 48),
                RivalReputation = new IntRangeSettings(-20, 0),
                HelpedTradePoints = new IntRangeSettings(250, 1000),
                PlayerStationTransfers = 1,
                BackgroundStationTransfers = 1,
                StationPower = new IntRangeSettings(100, 350),
                MaximumCaptureAgeDays = 120
            },
            Roster = new RosterSettings { TargetCloneCount = 5, TargetClassCount = 5 },
            Magnum = new MagnumSettings
            {
                TargetUpgradeCount = 8,
                GuaranteedUpgradeIds = new List<string>
                {
                    "news_department",
                    "prodline_department",
                    "autonomcapsule_department"
                }
            },
            Stash = new StashSettings
            {
                EquipmentRolls = 3,
                ConsumableRolls = 4,
                ChipRolls = 1,
                AmmoStacksPerWeapon = 1,
                RewardSelection = new RewardSelectionSettings { MaxConsumableCopiesPerItem = 2 },
                MaterialStockpile = MaterialStockpileSettings.CreateEarly(),
                RoleStockpile = RoleStockpileSettings.CreateEarly()
            }
        };
    }

    public static StartProfile CreateMid()
    {
        return new StartProfile
        {
            ElapsedDays = 800,
            TechProgression = new TechProgressionSettings
            {
                WorldProgressLevel = 6.0,
                MinimumLevel = 5,
                MaximumLevel = 7,
                MaxActiveFactionSpread = 2,
                PendingTechFraction = 0.05
            },
            Factions = new FactionHistorySettings
            {
                HelpedFactions = FactionSelectionSettings.Random(2),
                RivalFactions = FactionSelectionSettings.Random(2),
                HelpedReputation = new IntRangeSettings(49, 74),
                RivalReputation = new IntRangeSettings(-50, -10),
                HelpedTradePoints = new IntRangeSettings(2500, 8000),
                PlayerStationTransfers = 3,
                BackgroundStationTransfers = 5,
                StationPower = new IntRangeSettings(250, 900),
                MaximumCaptureAgeDays = 500
            },
            Roster = new RosterSettings { TargetCloneCount = 14, TargetClassCount = 12 },
            Magnum = new MagnumSettings
            {
                TargetUpgradeCount = 90,
                GuaranteedUpgradeIds = new List<string>
                {
                    "news_department",
                    "prodline_department",
                    "autonomcapsule_department",
                    "memdefrag_department",
                    "genomeeditor_department",
                    "weaponstation_department",
                    "armorstation_department"
                }
            },
            Stash = new StashSettings
            {
                EquipmentRolls = 8,
                ConsumableRolls = 8,
                ChipRolls = 3,
                AmmoStacksPerWeapon = 2,
                RewardSelection = new RewardSelectionSettings { MaxConsumableCopiesPerItem = 3 },
                MaterialStockpile = MaterialStockpileSettings.CreateMid(),
                RoleStockpile = RoleStockpileSettings.CreateMid()
            }
        };
    }

    public static StartProfile CreateLate()
    {
        return new StartProfile
        {
            ElapsedDays = 1960,
            TechProgression = new TechProgressionSettings
            {
                WorldProgressLevel = 9.7,
                MinimumLevel = 9,
                MaximumLevel = 10,
                MaxActiveFactionSpread = 1,
                PendingTechFraction = 0.0
            },
            Factions = new FactionHistorySettings
            {
                HelpedFactions = FactionSelectionSettings.Random(3),
                RivalFactions = FactionSelectionSettings.Random(3),
                HelpedReputation = new IntRangeSettings(75, 100),
                RivalReputation = new IntRangeSettings(-100, -40),
                HelpedTradePoints = new IntRangeSettings(12000, 30000),
                PlayerStationTransfers = 6,
                BackgroundStationTransfers = 10,
                StationPower = new IntRangeSettings(700, 2500),
                MaximumCaptureAgeDays = 1200
            },
            Roster = new RosterSettings { TargetCloneCount = -1, TargetClassCount = -1 },
            Magnum = new MagnumSettings { TargetUpgradeCount = -1 },
            Stash = new StashSettings
            {
                EquipmentRolls = 14,
                ConsumableRolls = 14,
                ChipRolls = 6,
                AmmoStacksPerWeapon = 3,
                RewardSelection = new RewardSelectionSettings { MaxConsumableCopiesPerItem = 4 },
                MaterialStockpile = MaterialStockpileSettings.CreateLate(),
                RoleStockpile = RoleStockpileSettings.CreateLate()
            }
        };
    }
}

[Serializable]
public sealed class TechProgressionSettings
{
    public double WorldProgressLevel { get; set; } = 2.4;

    public int MinimumLevel { get; set; } = 2;

    public int MaximumLevel { get; set; } = 3;

    public int MaxActiveFactionSpread { get; set; } = 1;

    public double MaximumEconomyOffset { get; set; } = 0.75;

    public double EconomyOffsetPerDoubling { get; set; } = 0.45;

    public double RandomOffset { get; set; } = 0.2;

    public double MinimumProgressFraction { get; set; } = 0.05;

    public double MaximumProgressFraction { get; set; } = 0.85;

    public double PendingTechFraction { get; set; } = 0.04;

    public Dictionary<string, int> ExactLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        MinimumLevel = Clamp(MinimumLevel, 1, 10);
        MaximumLevel = Clamp(MaximumLevel, MinimumLevel, 10);
        WorldProgressLevel = Clamp(WorldProgressLevel, MinimumLevel, MaximumLevel);
        MaxActiveFactionSpread = Clamp(MaxActiveFactionSpread, 0, 9);
        MaximumEconomyOffset = Clamp(MaximumEconomyOffset, 0.0, 3.0);
        EconomyOffsetPerDoubling = Clamp(EconomyOffsetPerDoubling, 0.0, 2.0);
        RandomOffset = Clamp(RandomOffset, 0.0, 2.0);
        MinimumProgressFraction = Clamp(MinimumProgressFraction, 0.0, 0.99);
        MaximumProgressFraction = Clamp(MaximumProgressFraction, MinimumProgressFraction, 0.99);
        PendingTechFraction = Clamp(PendingTechFraction, 0.0, 1.0);
        Dictionary<string, int> normalizedExactLevels = new(StringComparer.OrdinalIgnoreCase);
        if (ExactLevels != null)
        {
            foreach (KeyValuePair<string, int> entry in ExactLevels)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key))
                {
                    normalizedExactLevels[entry.Key.Trim()] = Clamp(entry.Value, 1, 10);
                }
            }
        }

        ExactLevels = normalizedExactLevels;
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    private static double Clamp(double value, double min, double max)
    {
        return double.IsNaN(value) || double.IsInfinity(value) ? min : Math.Max(min, Math.Min(max, value));
    }
}

[Serializable]
public sealed class FactionHistorySettings
{
    public FactionSelectionSettings HelpedFactions { get; set; } = FactionSelectionSettings.Random(1);

    public FactionSelectionSettings RivalFactions { get; set; } = FactionSelectionSettings.Random(1);

    public IntRangeSettings HelpedReputation { get; set; } = new(25, 48);

    public IntRangeSettings RivalReputation { get; set; } = new(-20, 0);

    public IntRangeSettings HelpedTradePoints { get; set; } = new(250, 1000);

    public int PlayerStationTransfers { get; set; } = 1;

    public int BackgroundStationTransfers { get; set; } = 1;

    public int MinimumStationsPerFaction { get; set; } = 1;

    public bool ProtectKnownStoryStations { get; set; } = true;

    public List<string> AdditionalProtectedStationIds { get; set; } = new();

    public IntRangeSettings StationPower { get; set; } = new(100, 350);

    public int MinimumCaptureAgeDays { get; set; } = 14;

    public int MaximumCaptureAgeDays { get; set; } = 120;

    public void Normalize()
    {
        HelpedFactions ??= FactionSelectionSettings.Random(1);
        RivalFactions ??= FactionSelectionSettings.Random(1);
        HelpedReputation ??= new IntRangeSettings(25, 48);
        RivalReputation ??= new IntRangeSettings(-20, 0);
        HelpedTradePoints ??= new IntRangeSettings(250, 1000);
        StationPower ??= new IntRangeSettings(100, 350);
        AdditionalProtectedStationIds = ConfigIdList.Normalize(AdditionalProtectedStationIds);

        HelpedFactions.Normalize();
        RivalFactions.Normalize();
        HelpedReputation.Normalize(-100, 100);
        RivalReputation.Normalize(-100, 100);
        HelpedTradePoints.Normalize(0, int.MaxValue);
        StationPower.Normalize(0, int.MaxValue);
        PlayerStationTransfers = Math.Max(0, PlayerStationTransfers);
        BackgroundStationTransfers = Math.Max(0, BackgroundStationTransfers);
        MinimumStationsPerFaction = Math.Max(1, MinimumStationsPerFaction);
        MinimumCaptureAgeDays = Math.Max(0, MinimumCaptureAgeDays);
        MaximumCaptureAgeDays = Math.Max(MinimumCaptureAgeDays, MaximumCaptureAgeDays);
    }
}

[Serializable]
public sealed class FactionSelectionSettings
{
    public string Mode { get; set; } = "Random";

    public int Count { get; set; } = 1;

    public List<string> Ids { get; set; } = new();

    public List<string> AllowedIds { get; set; } = new();

    public List<string> ExcludedIds { get; set; } = new();

    public static FactionSelectionSettings Random(int count) => new() { Count = count };

    public void Normalize()
    {
        Mode = string.Equals(Mode, "Explicit", StringComparison.OrdinalIgnoreCase) ? "Explicit" : "Random";
        Count = Math.Max(0, Count);
        Ids = ConfigIdList.Normalize(Ids);
        AllowedIds = ConfigIdList.Normalize(AllowedIds);
        ExcludedIds = ConfigIdList.Normalize(ExcludedIds);
    }
}

[Serializable]
public sealed class RosterSettings
{
    public int TargetCloneCount { get; set; } = 5;

    public int TargetClassCount { get; set; } = 5;

    public List<string> GuaranteedCloneIds { get; set; } = new();

    public List<string> GuaranteedClassIds { get; set; } = new();

    public List<string> AllowedCloneIds { get; set; } = new();

    public List<string> AllowedClassIds { get; set; } = new();

    public List<string> ExcludedCloneIds { get; set; } = new();

    public List<string> ExcludedClassIds { get; set; } = new();

    public void Normalize()
    {
        TargetCloneCount = Math.Max(-1, TargetCloneCount);
        TargetClassCount = Math.Max(-1, TargetClassCount);
        GuaranteedCloneIds = ConfigIdList.Normalize(GuaranteedCloneIds);
        GuaranteedClassIds = ConfigIdList.Normalize(GuaranteedClassIds);
        AllowedCloneIds = ConfigIdList.Normalize(AllowedCloneIds);
        AllowedClassIds = ConfigIdList.Normalize(AllowedClassIds);
        ExcludedCloneIds = ConfigIdList.Normalize(ExcludedCloneIds);
        ExcludedClassIds = ConfigIdList.Normalize(ExcludedClassIds);
    }
}

[Serializable]
public sealed class MagnumSettings
{
    public int TargetUpgradeCount { get; set; } = 8;

    public List<string> GuaranteedUpgradeIds { get; set; } = new();

    public List<string> AllowedDepartments { get; set; } = new();

    public List<string> ExcludedUpgradeIds { get; set; } = new();

    public void Normalize()
    {
        TargetUpgradeCount = Math.Max(-1, TargetUpgradeCount);
        GuaranteedUpgradeIds = ConfigIdList.Normalize(GuaranteedUpgradeIds);
        AllowedDepartments = ConfigIdList.Normalize(AllowedDepartments);
        ExcludedUpgradeIds = ConfigIdList.Normalize(ExcludedUpgradeIds);
    }
}

[Serializable]
public sealed class StashSettings
{
    public int EquipmentRolls { get; set; } = 6;

    public int ConsumableRolls { get; set; } = 12;

    public int ChipRolls { get; set; } = 1;

    public int AmmoStacksPerWeapon { get; set; } = 1;

    public Dictionary<string, int> GuaranteedItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public RewardSelectionSettings RewardSelection { get; set; } = null!;

    public MaterialStockpileSettings MaterialStockpile { get; set; } = null!;

    public RoleStockpileSettings RoleStockpile { get; set; } = null!;

    public void Normalize(string profileName)
    {
        EquipmentRolls = Math.Max(0, EquipmentRolls);
        ConsumableRolls = Math.Max(0, ConsumableRolls);
        ChipRolls = Math.Max(0, ChipRolls);
        AmmoStacksPerWeapon = Math.Max(0, AmmoStacksPerWeapon);
        RewardSelection ??= new RewardSelectionSettings
        {
            MaxConsumableCopiesPerItem = profileName.Equals("Mid", StringComparison.OrdinalIgnoreCase)
                ? 3
                : profileName.Equals("Late", StringComparison.OrdinalIgnoreCase)
                    ? 4
                    : 2
        };
        RewardSelection.Normalize();
        MaterialStockpile ??= MaterialStockpileSettings.CreateForProfile(profileName);
        MaterialStockpile.Normalize();
        RoleStockpile ??= RoleStockpileSettings.CreateForProfile(profileName);
        RoleStockpile.Normalize();
        Dictionary<string, int> normalizedItems = new(StringComparer.OrdinalIgnoreCase);
        if (GuaranteedItems != null)
        {
            foreach (KeyValuePair<string, int> entry in GuaranteedItems)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key))
                {
                    normalizedItems[entry.Key.Trim()] = entry.Value;
                }
            }
        }

        GuaranteedItems = normalizedItems;
    }
}

[Serializable]
public sealed class MaterialStockpileSettings
{
    public bool Enabled { get; set; } = true;

    public int TargetDistinctItems { get; set; } = 18;

    public int MinimumRecipeUses { get; set; } = 3;

    public int MaximumUpgradeGrade { get; set; } = 3;

    public int MinimumCraftingStacks { get; set; } = 1;

    public int MaximumCraftingStacks { get; set; } = 2;

    public int MinimumUpgradeUnits { get; set; } = 1;

    public int MaximumUpgradeUnits { get; set; } = 2;

    public double FactionAvailabilityWeight { get; set; } = 1.35;

    public double DemandWeight { get; set; } = 0.7;

    public double TopCandidateFraction { get; set; } = 0.8;

    public int MaximumRareItems { get; set; } = 2;

    public int MinimumCommonLootOccurrences { get; set; } = 4;

    public List<string> RareItemIds { get; set; } = new()
    {
        "lens",
        "glass",
        "microelectronics_parts",
        "circuit_board",
        "capacitor_parts",
        "transformer",
        "bulb",
        "electrical_parts_container",
        "engineering_parts_container"
    };

    public static MaterialStockpileSettings CreateForProfile(string profileName)
    {
        return profileName.Equals("Mid", StringComparison.OrdinalIgnoreCase)
            ? CreateMid()
            : profileName.Equals("Late", StringComparison.OrdinalIgnoreCase)
                ? CreateLate()
                : CreateEarly();
    }

    public static MaterialStockpileSettings CreateEarly()
    {
        return new MaterialStockpileSettings
        {
            TargetDistinctItems = 12
        };
    }

    public static MaterialStockpileSettings CreateMid()
    {
        return new MaterialStockpileSettings
        {
            TargetDistinctItems = 28,
            MinimumRecipeUses = 3,
            MaximumUpgradeGrade = 12,
            MinimumCraftingStacks = 2,
            MaximumCraftingStacks = 6,
            MinimumUpgradeUnits = 2,
            MaximumUpgradeUnits = 6,
            MaximumRareItems = 5
        };
    }

    public static MaterialStockpileSettings CreateLate()
    {
        return new MaterialStockpileSettings
        {
            TargetDistinctItems = 40,
            MinimumRecipeUses = 1,
            MaximumUpgradeGrade = -1,
            MinimumCraftingStacks = 3,
            MaximumCraftingStacks = 8,
            MinimumUpgradeUnits = 3,
            MaximumUpgradeUnits = 12,
            MaximumRareItems = 10
        };
    }

    public void Normalize()
    {
        TargetDistinctItems = Math.Max(0, Math.Min(200, TargetDistinctItems));
        MinimumRecipeUses = Math.Max(1, Math.Min(1000, MinimumRecipeUses));
        MaximumUpgradeGrade = Math.Max(-1, Math.Min(1000, MaximumUpgradeGrade));
        MinimumCraftingStacks = Math.Max(1, Math.Min(100, MinimumCraftingStacks));
        MaximumCraftingStacks = Math.Max(MinimumCraftingStacks, Math.Min(100, MaximumCraftingStacks));
        MinimumUpgradeUnits = Math.Max(1, Math.Min(1000, MinimumUpgradeUnits));
        MaximumUpgradeUnits = Math.Max(MinimumUpgradeUnits, Math.Min(1000, MaximumUpgradeUnits));
        FactionAvailabilityWeight = Clamp(FactionAvailabilityWeight, 1.0, 10.0);
        DemandWeight = Clamp(DemandWeight, 0.0, 5.0);
        TopCandidateFraction = Clamp(TopCandidateFraction, 0.1, 1.0);
        MaximumRareItems = Math.Max(0, Math.Min(TargetDistinctItems, MaximumRareItems));
        MinimumCommonLootOccurrences = Math.Max(0, Math.Min(1000, MinimumCommonLootOccurrences));
        RareItemIds = ConfigIdList.Normalize(RareItemIds);
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? minimum
            : Math.Max(minimum, Math.Min(maximum, value));
    }
}

[Serializable]
public sealed class RoleStockpileSettings
{
    public bool Enabled { get; set; } = true;

    public int WeaponItems { get; set; } = 4;

    public int ArmorSets { get; set; } = 1;

    public int CommonAmmoTypes { get; set; } = 5;

    public int SpecialAmmoTypes { get; set; } = 1;

    public int CommonAmmoStacks { get; set; } = 3;

    public int SpecialAmmoStacks { get; set; } = 1;

    public int MedicalItemTypes { get; set; } = 5;

    public int BasicMedicineStacks { get; set; } = 3;

    public int PremiumMedicineStacks { get; set; } = 1;

    public int RepairKitTypes { get; set; } = 2;

    public int RepairKitStacks { get; set; } = 2;

    public int AugmentationItems { get; set; } = 3;

    public int ImplantItems { get; set; }

    public int ProductionRecipeUnlocks { get; set; } = 8;

    public int MaximumAugmentationTech { get; set; } = 1;

    public int MaximumImplantTech { get; set; }

    public bool AllowQuasiItems { get; set; }

    public static RoleStockpileSettings CreateForProfile(string profileName)
    {
        return profileName.Equals("Mid", StringComparison.OrdinalIgnoreCase)
            ? CreateMid()
            : profileName.Equals("Late", StringComparison.OrdinalIgnoreCase)
                ? CreateLate()
                : CreateEarly();
    }

    public static RoleStockpileSettings CreateEarly() => new();

    public static RoleStockpileSettings CreateMid()
    {
        return new RoleStockpileSettings
        {
            WeaponItems = 20,
            ArmorSets = 5,
            CommonAmmoTypes = 7,
            SpecialAmmoTypes = 6,
            CommonAmmoStacks = 10,
            SpecialAmmoStacks = 3,
            MedicalItemTypes = 10,
            BasicMedicineStacks = 6,
            PremiumMedicineStacks = 3,
            RepairKitTypes = 7,
            RepairKitStacks = 5,
            AugmentationItems = 10,
            ImplantItems = 12,
            ProductionRecipeUnlocks = 55,
            MaximumAugmentationTech = 4,
            MaximumImplantTech = 5
        };
    }

    public static RoleStockpileSettings CreateLate()
    {
        return new RoleStockpileSettings
        {
            WeaponItems = 30,
            ArmorSets = 8,
            CommonAmmoTypes = 8,
            SpecialAmmoTypes = 12,
            CommonAmmoStacks = 14,
            SpecialAmmoStacks = 6,
            MedicalItemTypes = 14,
            BasicMedicineStacks = 8,
            PremiumMedicineStacks = 5,
            RepairKitTypes = 10,
            RepairKitStacks = 7,
            AugmentationItems = 18,
            ImplantItems = 25,
            ProductionRecipeUnlocks = 120,
            MaximumAugmentationTech = 10,
            MaximumImplantTech = 10,
            AllowQuasiItems = true
        };
    }

    public void Normalize()
    {
        WeaponItems = Clamp(WeaponItems, 0, 100);
        ArmorSets = Clamp(ArmorSets, 0, 30);
        CommonAmmoTypes = Clamp(CommonAmmoTypes, 0, 50);
        SpecialAmmoTypes = Clamp(SpecialAmmoTypes, 0, 50);
        CommonAmmoStacks = Clamp(CommonAmmoStacks, 1, 50);
        SpecialAmmoStacks = Clamp(SpecialAmmoStacks, 1, 50);
        MedicalItemTypes = Clamp(MedicalItemTypes, 0, 50);
        BasicMedicineStacks = Clamp(BasicMedicineStacks, 1, 50);
        PremiumMedicineStacks = Clamp(PremiumMedicineStacks, 1, 50);
        RepairKitTypes = Clamp(RepairKitTypes, 0, 50);
        RepairKitStacks = Clamp(RepairKitStacks, 1, 50);
        AugmentationItems = Clamp(AugmentationItems, 0, 100);
        ImplantItems = Clamp(ImplantItems, 0, 100);
        ProductionRecipeUnlocks = Clamp(ProductionRecipeUnlocks, 0, 500);
        MaximumAugmentationTech = Clamp(MaximumAugmentationTech, 0, 100);
        MaximumImplantTech = Clamp(MaximumImplantTech, 0, 100);
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, value));
    }
}

[Serializable]
public sealed class RewardSelectionSettings
{
    public bool Enabled { get; set; } = true;

    public int MaxEquipmentCopiesPerItem { get; set; } = 1;

    public int MaxConsumableCopiesPerItem { get; set; } = 2;

    public int MaxChipCopiesPerItem { get; set; } = 1;

    public double DuplicateItemWeight { get; set; } = 0.15;

    public double DuplicateGroupWeight { get; set; } = 0.6;

    public double MissingGroupWeight { get; set; } = 2.5;

    public double FactionWeightExponent { get; set; } = 1.0;

    public double TechLevelWeight { get; set; } = 0.35;

    public double PriceWeight { get; set; } = 0.08;

    public double TopCandidateFraction { get; set; } = 0.4;

    public int MinimumCandidatePoolSize { get; set; } = 3;

    public void Normalize()
    {
        MaxEquipmentCopiesPerItem = Math.Max(-1, MaxEquipmentCopiesPerItem);
        MaxConsumableCopiesPerItem = Math.Max(-1, MaxConsumableCopiesPerItem);
        MaxChipCopiesPerItem = Math.Max(-1, MaxChipCopiesPerItem);
        DuplicateItemWeight = Clamp(DuplicateItemWeight, 0.0, 1.0);
        DuplicateGroupWeight = Clamp(DuplicateGroupWeight, 0.0, 1.0);
        MissingGroupWeight = Clamp(MissingGroupWeight, 1.0, 10.0);
        FactionWeightExponent = Clamp(FactionWeightExponent, 0.0, 3.0);
        TechLevelWeight = Clamp(TechLevelWeight, 0.0, 3.0);
        PriceWeight = Clamp(PriceWeight, 0.0, 1.0);
        TopCandidateFraction = Clamp(TopCandidateFraction, 0.05, 1.0);
        MinimumCandidatePoolSize = Math.Max(1, Math.Min(100, MinimumCandidatePoolSize));
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? minimum
            : Math.Max(minimum, Math.Min(maximum, value));
    }
}

[Serializable]
public sealed class IntRangeSettings
{
    public IntRangeSettings()
    {
    }

    public IntRangeSettings(int minimum, int maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public int Minimum { get; set; }

    public int Maximum { get; set; }

    public void Normalize(int limitMinimum, int limitMaximum)
    {
        Minimum = Math.Max(limitMinimum, Math.Min(limitMaximum, Minimum));
        Maximum = Math.Max(Minimum, Math.Min(limitMaximum, Maximum));
    }
}

internal static class ConfigIdList
{
    public static List<string> Normalize(IEnumerable<string>? values)
    {
        if (values == null)
        {
            return new List<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

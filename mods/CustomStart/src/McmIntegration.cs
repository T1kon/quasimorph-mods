using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ModConfigMenu;
using ModConfigMenu.Contracts;
using ModConfigMenu.Implementations;
using ModConfigMenu.Objects;
using UnityEngine;

namespace CustomStart;

internal static class McmIntegration
{
    private const string LogPrefix = "[CustomStart] ";
    private const string RandomSeedKey = "__RandomSeed";

    private static readonly string[] ProfileNames = { "Early", "EarlyMid", "Mid", "Late" };

    private static ModConfig? _config;
    private static Action<ModConfig>? _save;
    private static bool _registered;

    public static bool TryRegister(ModConfig config, Action<ModConfig> save)
    {
        if (_registered)
        {
            return true;
        }

        _config = config;
        _save = save;
        try
        {
            Register();
            _registered = true;
            Debug.Log(LogPrefix + "Registered in Mod Configuration Menu.");
            return true;
        }
        catch (FileNotFoundException)
        {
            Debug.Log(LogPrefix + "Mod Configuration Menu is not installed; JSON configuration remains available.");
        }
        catch (TypeLoadException)
        {
            Debug.Log(LogPrefix + "Mod Configuration Menu is not loaded; JSON configuration remains available.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(LogPrefix + "Could not register Mod Configuration Menu controls. " + exception.Message);
        }

        return false;
    }

    private static void Register()
    {
        ModConfig config = _config ?? throw new InvalidOperationException("MCM configuration was not initialized.");
        List<IConfigValue> values = new()
        {
            new ConfigValue(
                nameof(ModConfig.Enabled),
                config.Enabled,
                "General",
                true,
                "Enable CustomStart for newly created campaigns.",
                "Enabled"),
            new DropdownConfig(
                nameof(ModConfig.ActiveProfile),
                config.ActiveProfile,
                "General",
                "Early",
                "Choose which campaign snapshot is used for the next new game.",
                "Active profile",
                new List<object> { "Early", "EarlyMid", "Mid", "Late" }),
            new ConfigValue(
                RandomSeedKey,
                !config.Seed.HasValue,
                "General",
                true,
                "Generate a fresh random seed for every new campaign. Disable this to use the seed value below.",
                "Random seed"),
            CreateRange(
                nameof(ModConfig.Seed),
                config.Seed ?? 12345,
                12345,
                -10_000_000,
                10_000_000,
                "General",
                "A reproducible signed seed. This is used only when Random seed is disabled; JSON accepts the full Int32 range.",
                "Seed value"),
            new ConfigValue(
                nameof(ModConfig.WriteReport),
                config.WriteReport,
                "General",
                true,
                "Write last-start-report.json after generation.",
                "Write diagnostic report"),
            new ConfigValue(
                nameof(ModConfig.AllowCivilResistanceAndTezctlanReputationChanges),
                config.AllowCivilResistanceAndTezctlanReputationChanges,
                "General",
                false,
                "Allow Civil Resistance and Tezctlan to be selected as helped or rival factions. Their normal world progression is unaffected.",
                "Civil Resistance/Tezctlan reputation"),
            new ConfigValue(
                "__ApplyNote",
                "Changes apply when a genuinely new campaign is created.",
                "General")
        };

        foreach (string profileName in ProfileNames)
        {
            AddProfileControls(values, profileName, config.Profiles[profileName]);
        }

        ModConfigMenuAPI.RegisterModConfig("CustomStart", values, OnSave);
    }

    private static void AddProfileControls(
        ICollection<IConfigValue> values,
        string profileName,
        StartProfile profile)
    {
        string profileLabel = profileName.Equals("EarlyMid", StringComparison.Ordinal)
            ? "Early-Mid"
            : profileName;
        string worldHeader = profileLabel + " - World";
        string progressionHeader = profileLabel + " - Progression";
        string stashHeader = profileLabel + " - Stash";

        values.Add(CreateRange(
            Key(profileName, "ElapsedDays"),
            profile.ElapsedDays,
            DefaultProfile(profileName).ElapsedDays,
            0,
            5000,
            worldHeader,
            "How many days have elapsed since the vanilla campaign start.",
            "Elapsed days"));
        values.Add(CreateRange(
            Key(profileName, "HelpedFactions"),
            profile.Factions.HelpedFactions.Count,
            DefaultProfile(profileName).Factions.HelpedFactions.Count,
            0,
            15,
            worldHeader,
            "Number of factions with positive player history when using random selection.",
            "Helped factions"));
        values.Add(CreateRange(
            Key(profileName, "RivalFactions"),
            profile.Factions.RivalFactions.Count,
            DefaultProfile(profileName).Factions.RivalFactions.Count,
            0,
            15,
            worldHeader,
            "Number of factions with negative player history when using random selection.",
            "Rival factions"));
        values.Add(CreateRange(
            Key(profileName, "PlayerStationTransfers"),
            profile.Factions.PlayerStationTransfers,
            DefaultProfile(profileName).Factions.PlayerStationTransfers,
            0,
            30,
            worldHeader,
            "Station captures attributed to the player's historical faction support.",
            "Player-influenced captures"));
        values.Add(CreateRange(
            Key(profileName, "BackgroundStationTransfers"),
            profile.Factions.BackgroundStationTransfers,
            DefaultProfile(profileName).Factions.BackgroundStationTransfers,
            0,
            50,
            worldHeader,
            "Additional station captures generated as background history.",
            "Background captures"));

        values.Add(CreateRange(
            Key(profileName, "TargetCloneCount"),
            profile.Roster.TargetCloneCount,
            DefaultProfile(profileName).Roster.TargetCloneCount,
            -1,
            50,
            progressionHeader,
            "Total starting clone target including vanilla grants. -1 unlocks every eligible clone.",
            "Clone target"));
        values.Add(CreateRange(
            Key(profileName, "TargetClassCount"),
            profile.Roster.TargetClassCount,
            DefaultProfile(profileName).Roster.TargetClassCount,
            -1,
            50,
            progressionHeader,
            "Total starting class target including vanilla grants. -1 unlocks every eligible class.",
            "Class target"));
        values.Add(CreateRange(
            Key(profileName, "TargetUpgradeCount"),
            profile.Magnum.TargetUpgradeCount,
            DefaultProfile(profileName).Magnum.TargetUpgradeCount,
            -1,
            200,
            progressionHeader,
            "Connected Magnum upgrade target. -1 purchases every eligible node.",
            "Magnum upgrade target"));

        values.Add(CreateRange(
            Key(profileName, "EquipmentRolls"),
            profile.Stash.EquipmentRolls,
            DefaultProfile(profileName).Stash.EquipmentRolls,
            0,
            100,
            stashHeader,
            "Distinct loadout-style rewards selected from helped factions' authentic equipment pools.",
            "Equipment rewards"));
        values.Add(CreateRange(
            Key(profileName, "ConsumableRolls"),
            profile.Stash.ConsumableRolls,
            DefaultProfile(profileName).Stash.ConsumableRolls,
            0,
            150,
            stashHeader,
            "Mission supplies selected from helped factions' authentic consumable pools.",
            "Supply rewards"));
        values.Add(CreateRange(
            Key(profileName, "ChipRolls"),
            profile.Stash.ChipRolls,
            DefaultProfile(profileName).Stash.ChipRolls,
            0,
            50,
            stashHeader,
            "Knowledge and chip rewards selected from helped factions' authentic pools.",
            "Chip rewards"));
        values.Add(CreateRange(
            Key(profileName, "WeaponItems"),
            profile.Stash.RoleStockpile.WeaponItems,
            DefaultProfile(profileName).Stash.RoleStockpile.WeaponItems,
            0,
            100,
            stashHeader,
            "Spare weapons accumulated from crafting, faction rewards, and mission loot.",
            "Historical weapons"));
        values.Add(CreateRange(
            Key(profileName, "ArmorSets"),
            profile.Stash.RoleStockpile.ArmorSets,
            DefaultProfile(profileName).Stash.RoleStockpile.ArmorSets,
            0,
            30,
            stashHeader,
            "Complete four-slot armor sets retained at campaign start.",
            "Complete armor sets"));
        values.Add(CreateRange(
            Key(profileName, "CommonAmmoTypes"),
            profile.Stash.RoleStockpile.CommonAmmoTypes,
            DefaultProfile(profileName).Stash.RoleStockpile.CommonAmmoTypes,
            0,
            50,
            stashHeader,
            "Number of common ammunition families retained in bulk.",
            "Common ammo types"));
        values.Add(CreateRange(
            Key(profileName, "CommonAmmoStacks"),
            profile.Stash.RoleStockpile.CommonAmmoStacks,
            DefaultProfile(profileName).Stash.RoleStockpile.CommonAmmoStacks,
            1,
            50,
            stashHeader,
            "Full stacks granted for every selected common ammunition family.",
            "Common ammo stacks"));
        values.Add(CreateRange(
            Key(profileName, "SpecialAmmoTypes"),
            profile.Stash.RoleStockpile.SpecialAmmoTypes,
            DefaultProfile(profileName).Stash.RoleStockpile.SpecialAmmoTypes,
            0,
            50,
            stashHeader,
            "Number of faction and specialist ammunition families retained in smaller quantities.",
            "Specialist ammo types"));
        values.Add(CreateRange(
            Key(profileName, "MedicalItemTypes"),
            profile.Stash.RoleStockpile.MedicalItemTypes,
            DefaultProfile(profileName).Stash.RoleStockpile.MedicalItemTypes,
            0,
            50,
            stashHeader,
            "Distinct medical supplies, biased toward cheap and commonly looted medicine.",
            "Medical supply types"));
        values.Add(CreateRange(
            Key(profileName, "RepairKitTypes"),
            profile.Stash.RoleStockpile.RepairKitTypes,
            DefaultProfile(profileName).Stash.RoleStockpile.RepairKitTypes,
            0,
            50,
            stashHeader,
            "Distinct repair-kit roles retained for the historical arsenal.",
            "Repair-kit types"));
        values.Add(CreateRange(
            Key(profileName, "RepairKitStacks"),
            profile.Stash.RoleStockpile.RepairKitStacks,
            DefaultProfile(profileName).Stash.RoleStockpile.RepairKitStacks,
            1,
            50,
            stashHeader,
            "Full stacks granted for every selected repair-kit type.",
            "Repair-kit stacks"));
        values.Add(CreateRange(
            Key(profileName, "AugmentationItems"),
            profile.Stash.RoleStockpile.AugmentationItems,
            DefaultProfile(profileName).Stash.RoleStockpile.AugmentationItems,
            0,
            100,
            stashHeader,
            "Uninstalled stage-appropriate augmentation spares.",
            "Augmentation spares"));
        values.Add(CreateRange(
            Key(profileName, "ImplantItems"),
            profile.Stash.RoleStockpile.ImplantItems,
            DefaultProfile(profileName).Stash.RoleStockpile.ImplantItems,
            0,
            100,
            stashHeader,
            "Uninstalled stage-appropriate implant spares.",
            "Implant spares"));
        values.Add(CreateRange(
            Key(profileName, "ProductionRecipeUnlocks"),
            profile.Stash.RoleStockpile.ProductionRecipeUnlocks,
            DefaultProfile(profileName).Stash.RoleStockpile.ProductionRecipeUnlocks,
            0,
            500,
            stashHeader,
            "Minimum production recipes already learned from past blueprint chips.",
            "Unlocked production recipes"));
        values.Add(CreateRange(
            Key(profileName, "TargetDistinctMaterials"),
            profile.Stash.MaterialStockpile.TargetDistinctItems,
            DefaultProfile(profileName).Stash.MaterialStockpile.TargetDistinctItems,
            0,
            100,
            stashHeader,
            "How many different durable crafting and upgrade materials the historical loot stockpile tries to retain.",
            "Distinct stockpile materials"));
        values.Add(CreateRange(
            Key(profileName, "MaximumCraftingStacks"),
            profile.Stash.MaterialStockpile.MaximumCraftingStacks,
            DefaultProfile(profileName).Stash.MaterialStockpile.MaximumCraftingStacks,
            1,
            20,
            stashHeader,
            "Maximum full stacks for common, frequently used crafting materials.",
            "Maximum crafting stacks"));
        values.Add(CreateRange(
            Key(profileName, "MaximumUpgradeUnits"),
            profile.Stash.MaterialStockpile.MaximumUpgradeUnits,
            DefaultProfile(profileName).Stash.MaterialStockpile.MaximumUpgradeUnits,
            1,
            100,
            stashHeader,
            "Maximum individual units for rarer Magnum, class, and clone upgrade materials.",
            "Maximum upgrade units"));
        values.Add(CreateRange(
            Key(profileName, "MaximumRareItems"),
            profile.Stash.MaterialStockpile.MaximumRareItems,
            DefaultProfile(profileName).Stash.MaterialStockpile.MaximumRareItems,
            0,
            50,
            stashHeader,
            "Maximum distinct advanced or uncommon crafting materials in the material stockpile.",
            "Rare material types"));
        values.Add(CreateRange(
            Key(profileName, "MaximumUpgradeGrade"),
            profile.Stash.MaterialStockpile.MaximumUpgradeGrade,
            DefaultProfile(profileName).Stash.MaterialStockpile.MaximumUpgradeGrade,
            -1,
            30,
            stashHeader,
            "Highest upgrade-price tier allowed in the stockpile. -1 permits every tier.",
            "Maximum upgrade material tier"));
    }

    private static ConfigValue CreateRange(
        string key,
        int value,
        int defaultValue,
        int minimum,
        int maximum,
        string header,
        string tooltip,
        string label)
    {
        return new ConfigValue(key, value, header, defaultValue, tooltip, label, minimum, maximum);
    }

    private static bool OnSave(Dictionary<string, object> values, out string feedbackMessage)
    {
        feedbackMessage = string.Empty;
        ModConfig config = _config ?? throw new InvalidOperationException("MCM configuration was not initialized.");
        try
        {
            bool useRandomSeed = Convert.ToBoolean(values[RandomSeedKey], CultureInfo.InvariantCulture);
            int? seed = useRandomSeed
                ? null
                : GetInt(values, nameof(ModConfig.Seed));

            string activeProfile = Convert.ToString(
                values[nameof(ModConfig.ActiveProfile)],
                CultureInfo.InvariantCulture) ?? string.Empty;
            if (!config.Profiles.ContainsKey(activeProfile))
            {
                feedbackMessage = $"Unknown profile '{activeProfile}'.";
                return false;
            }

            config.Enabled = Convert.ToBoolean(values[nameof(ModConfig.Enabled)], CultureInfo.InvariantCulture);
            config.ActiveProfile = activeProfile;
            config.Seed = seed;
            config.WriteReport = Convert.ToBoolean(values[nameof(ModConfig.WriteReport)], CultureInfo.InvariantCulture);
            config.AllowCivilResistanceAndTezctlanReputationChanges = Convert.ToBoolean(
                values[nameof(ModConfig.AllowCivilResistanceAndTezctlanReputationChanges)],
                CultureInfo.InvariantCulture);
            foreach (string profileName in ProfileNames)
            {
                ApplyProfileValues(config.Profiles[profileName], profileName, values);
            }

            List<string> warnings = new();
            ModConfig.Normalize(config, warnings.Add);
            _save?.Invoke(config);
            feedbackMessage = warnings.Count == 0
                ? "Saved. Changes apply to the next newly created campaign."
                : "Saved with normalization: " + string.Join(" ", warnings);
            return true;
        }
        catch (Exception exception)
        {
            feedbackMessage = "CustomStart could not save these settings: " + exception.Message;
            return false;
        }
    }

    private static void ApplyProfileValues(
        StartProfile profile,
        string profileName,
        IReadOnlyDictionary<string, object> values)
    {
        profile.ElapsedDays = GetInt(values, Key(profileName, "ElapsedDays"));
        profile.Factions.HelpedFactions.Count = GetInt(values, Key(profileName, "HelpedFactions"));
        profile.Factions.RivalFactions.Count = GetInt(values, Key(profileName, "RivalFactions"));
        profile.Factions.PlayerStationTransfers = GetInt(values, Key(profileName, "PlayerStationTransfers"));
        profile.Factions.BackgroundStationTransfers = GetInt(values, Key(profileName, "BackgroundStationTransfers"));
        profile.Roster.TargetCloneCount = GetInt(values, Key(profileName, "TargetCloneCount"));
        profile.Roster.TargetClassCount = GetInt(values, Key(profileName, "TargetClassCount"));
        profile.Magnum.TargetUpgradeCount = GetInt(values, Key(profileName, "TargetUpgradeCount"));
        profile.Stash.EquipmentRolls = GetInt(values, Key(profileName, "EquipmentRolls"));
        profile.Stash.ConsumableRolls = GetInt(values, Key(profileName, "ConsumableRolls"));
        profile.Stash.ChipRolls = GetInt(values, Key(profileName, "ChipRolls"));
        profile.Stash.RoleStockpile.WeaponItems = GetInt(values, Key(profileName, "WeaponItems"));
        profile.Stash.RoleStockpile.ArmorSets = GetInt(values, Key(profileName, "ArmorSets"));
        profile.Stash.RoleStockpile.CommonAmmoTypes = GetInt(values, Key(profileName, "CommonAmmoTypes"));
        profile.Stash.RoleStockpile.CommonAmmoStacks = GetInt(values, Key(profileName, "CommonAmmoStacks"));
        profile.Stash.RoleStockpile.SpecialAmmoTypes = GetInt(values, Key(profileName, "SpecialAmmoTypes"));
        profile.Stash.RoleStockpile.MedicalItemTypes = GetInt(values, Key(profileName, "MedicalItemTypes"));
        profile.Stash.RoleStockpile.RepairKitTypes = GetInt(values, Key(profileName, "RepairKitTypes"));
        profile.Stash.RoleStockpile.RepairKitStacks = GetInt(values, Key(profileName, "RepairKitStacks"));
        profile.Stash.RoleStockpile.AugmentationItems = GetInt(values, Key(profileName, "AugmentationItems"));
        profile.Stash.RoleStockpile.ImplantItems = GetInt(values, Key(profileName, "ImplantItems"));
        profile.Stash.RoleStockpile.ProductionRecipeUnlocks = GetInt(values, Key(profileName, "ProductionRecipeUnlocks"));
        profile.Stash.MaterialStockpile.TargetDistinctItems = GetInt(values, Key(profileName, "TargetDistinctMaterials"));
        profile.Stash.MaterialStockpile.MaximumCraftingStacks = GetInt(values, Key(profileName, "MaximumCraftingStacks"));
        profile.Stash.MaterialStockpile.MaximumUpgradeUnits = GetInt(values, Key(profileName, "MaximumUpgradeUnits"));
        profile.Stash.MaterialStockpile.MaximumRareItems = GetInt(values, Key(profileName, "MaximumRareItems"));
        profile.Stash.MaterialStockpile.MaximumUpgradeGrade = GetInt(values, Key(profileName, "MaximumUpgradeGrade"));
    }

    private static int GetInt(IReadOnlyDictionary<string, object> values, string key)
    {
        return Convert.ToInt32(values[key], CultureInfo.InvariantCulture);
    }

    private static string Key(string profileName, string setting)
    {
        return profileName + "." + setting;
    }

    private static StartProfile DefaultProfile(string profileName)
    {
        return profileName.Equals("EarlyMid", StringComparison.Ordinal)
            ? StartProfile.CreateEarlyMid()
            : profileName.Equals("Mid", StringComparison.Ordinal)
                ? StartProfile.CreateMid()
                : profileName.Equals("Late", StringComparison.Ordinal)
                    ? StartProfile.CreateLate()
                    : StartProfile.CreateEarly();
    }
}

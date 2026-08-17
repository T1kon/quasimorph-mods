using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using MGSC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CustomStart;

public static class Plugin
{
    public const string Version = "0.6.0";

    private const string HarmonyId = "quasimorph.custom_start";
    private const string LogPrefix = "[CustomStart] ";
    private const string ExpectedAssemblySha256 = "EE9214048DE649AA5C7E913F0CAFBCA44B8A1E164520D74DE72AEA11006C2729";
    private const string ConfigFileName = "config.json";
    private const string ReportFileName = "last-start-report.json";

    private static State? _state;
    private static string _packagePath = string.Empty;
    private static string _configDirectory = string.Empty;
    private static string _assemblySha256 = string.Empty;
    private static bool _recognizedBuild;
    private static bool _harmonyInstalled;
    private static PreparedStart? _preparedStart;

    [Hook(ModHookType.AfterBootstrap)]
    public static void OnAfterBootstrap(IModContext context)
    {
        _state = context.State;
        _packagePath = context.ModContentPath;
        _configDirectory = GetPersistentConfigDirectory();

        try
        {
            EnsurePersistentConfig();
            LoadedConfig menuConfig = LoadConfig();
            if (menuConfig.IsValid)
            {
                McmIntegration.TryRegister(menuConfig.Config, SaveMcmConfig);
            }

            _assemblySha256 = CalculateGameAssemblyHash();
            _recognizedBuild = _assemblySha256.Equals(ExpectedAssemblySha256, StringComparison.OrdinalIgnoreCase);
            InstallHarmonyOnce();

            if (_recognizedBuild)
            {
                Debug.Log(LogPrefix + $"Initialized version {Version} for the recognized game build.");
            }
            else
            {
                Debug.LogWarning(
                    LogPrefix
                    + "The game assembly does not match the build used to validate station transfers. "
                    + $"Detected SHA-256: {_assemblySha256}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError(LogPrefix + "Initialization failed; no custom start will be applied.");
            Debug.LogException(exception);
        }
    }

    internal static void PrepareGlobalComponents(bool initContent)
    {
        _preparedStart = null;
        if (!initContent || _state == null)
        {
            return;
        }

        LoadedConfig loadedConfig = LoadConfig();
        ModConfig config = loadedConfig.Config;
        if (!config.Enabled)
        {
            Debug.Log(LogPrefix + "Disabled by configuration; the new game will use the vanilla start.");
            return;
        }

        StartProfile profile = config.GetActiveProfile();
        int seed = config.Seed ?? CreateRandomSeed();
        bool allowTransfers = _recognizedBuild || !config.DisableStationTransfersOnUnknownBuild;
        _preparedStart = new PreparedStart(
            config,
            loadedConfig.SourcePath,
            config.ActiveProfile,
            profile,
            seed,
            allowTransfers);
        Debug.Log(
            LogPrefix
            + $"Prepared profile '{config.ActiveProfile}' with seed {seed}; target elapsed time is {profile.ElapsedDays} days. "
            + $"Config source: '{loadedConfig.SourcePath}'.");
    }

    internal static void AdjustFreshGameTime(SpaceTime spaceTime)
    {
        if (_preparedStart == null)
        {
            return;
        }

        spaceTime.Time = spaceTime.StartGameDate.AddDays(_preparedStart.Profile.ElapsedDays);
    }

    internal static void ApplyPreparedStart()
    {
        PreparedStart? prepared = _preparedStart;
        _preparedStart = null;
        if (prepared == null || _state == null)
        {
            return;
        }

        StartPlan? plan = null;
        try
        {
            plan = new StartPlanner(
                    _state,
                    prepared.Profile,
                    prepared.ProfileName,
                    prepared.Seed,
                    prepared.AllowStationTransfers)
                .Build();
            plan.GameAssemblySha256 = _assemblySha256;
            plan.StationTransfersEnabled = prepared.AllowStationTransfers;
            plan.ConfigSource = prepared.ConfigSource;

            new StartPlanValidator(_state).Validate(plan);
            new StartApplier(_state).Apply(plan);
            Debug.Log(
                LogPrefix
                + $"Applied '{plan.Profile}' (seed {plan.Seed}): "
                + $"{plan.Factions.Count} factions, {plan.StationTransfers.Count} station transfers, "
                + $"{plan.Mercenaries.Count} additional clones, {plan.Classes.Count} additional classes, "
                + $"{plan.MagnumUpgrades.Count} Magnum upgrades, {plan.ProductionRecipeUnlocks.Count} production recipes, "
                + $"{plan.Items.Count} item grants.");
        }
        catch (Exception exception)
        {
            plan ??= CreateFailurePlan(prepared, exception);
            plan.Error = exception.ToString();
            Debug.LogError(LogPrefix + "Failed to apply the custom start. The new game may contain a partial start state.");
            Debug.LogException(exception);
        }
        finally
        {
            if (prepared.Config.WriteReport && plan != null)
            {
                WriteReport(plan);
            }
        }
    }

    private static StartPlan CreateFailurePlan(PreparedStart prepared, Exception exception)
    {
        SpaceTime? spaceTime = _state?.Get<SpaceTime>();
        return new StartPlan
        {
            Profile = prepared.ProfileName,
            Seed = prepared.Seed,
            ElapsedDays = prepared.Profile.ElapsedDays,
            TargetDate = spaceTime?.Time ?? DateTime.MinValue,
            ConfigSource = prepared.ConfigSource,
            GameAssemblySha256 = _assemblySha256,
            StationTransfersEnabled = prepared.AllowStationTransfers,
            Error = exception.ToString()
        };
    }

    private static LoadedConfig LoadConfig()
    {
        string persistentPath = Path.Combine(_configDirectory, ConfigFileName);
        string installedPath = Path.Combine(_packagePath, ConfigFileName);
        string path = ResolveConfigPath(installedPath, persistentPath);
        try
        {
            string json = File.ReadAllText(path);
            ModConfig? config = JsonConvert.DeserializeObject<ModConfig>(json);
            int loadedSchemaVersion = config?.SchemaVersion ?? 0;
            ModConfig normalized = ModConfig.Normalize(config, message => Debug.LogWarning(LogPrefix + message));
            if (path.Equals(installedPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Copy(installedPath, persistentPath, overwrite: true);
                    Debug.Log(LogPrefix + $"Imported the newer installed config into '{persistentPath}'.");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        LogPrefix
                        + $"Using the installed config, but it could not be copied to '{persistentPath}'. "
                        + exception.Message);
                }
            }

            if (loadedSchemaVersion < ModConfig.CurrentSchemaVersion)
            {
                try
                {
                    File.WriteAllText(persistentPath, JsonConvert.SerializeObject(normalized, Formatting.Indented));
                    Debug.Log(LogPrefix + $"Persisted configuration schema {normalized.SchemaVersion} to '{persistentPath}'.");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        LogPrefix
                        + $"Using the migrated configuration, but it could not be persisted to '{persistentPath}'. "
                        + exception.Message);
                }
            }

            return new LoadedConfig(normalized, path, isValid: true);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                LogPrefix
                + $"Could not read '{path}'. CustomStart is disabled for this new game. "
                + exception.Message);
            return new LoadedConfig(new ModConfig { Enabled = false }, path, isValid: false);
        }
    }

    private static void SaveMcmConfig(ModConfig config)
    {
        Directory.CreateDirectory(_configDirectory);
        string path = Path.Combine(_configDirectory, ConfigFileName);
        File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
        Debug.Log(LogPrefix + $"Saved Mod Configuration Menu settings to '{path}'.");
    }

    private static string ResolveConfigPath(string installedPath, string persistentPath)
    {
        if (!File.Exists(persistentPath))
        {
            return installedPath;
        }

        if (!File.Exists(installedPath)
            || File.GetLastWriteTimeUtc(installedPath) < File.GetLastWriteTimeUtc(persistentPath))
        {
            return persistentPath;
        }

        try
        {
            string installedJson = File.ReadAllText(installedPath);
            if (installedJson.Equals(File.ReadAllText(persistentPath), StringComparison.Ordinal))
            {
                return persistentPath;
            }

            ModConfig? installed = JsonConvert.DeserializeObject<ModConfig>(installedJson);
            ModConfig normalizedInstalled = ModConfig.Normalize(installed, _ => { });
            ModConfig normalizedDefault = ModConfig.Normalize(new ModConfig(), _ => { });
            JToken installedToken = JToken.FromObject(normalizedInstalled);
            JToken defaultToken = JToken.FromObject(normalizedDefault);
            return JToken.DeepEquals(installedToken, defaultToken) ? persistentPath : installedPath;
        }
        catch
        {
            // A newer edited installed file should surface its own parse error instead of silently
            // falling back to an older persistent configuration.
            return installedPath;
        }
    }

    private static void EnsurePersistentConfig()
    {
        Directory.CreateDirectory(_configDirectory);
        string destination = Path.Combine(_configDirectory, ConfigFileName);
        if (File.Exists(destination))
        {
            return;
        }

        string source = Path.Combine(_packagePath, ConfigFileName);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("The packaged CustomStart config.json was not found.", source);
        }

        File.Copy(source, destination, overwrite: false);
        Debug.Log(LogPrefix + $"Created persistent configuration at '{destination}'.");
    }

    private static string GetPersistentConfigDirectory()
    {
        DirectoryInfo? parent = Directory.GetParent(Application.persistentDataPath);
        string root = parent?.FullName ?? Application.persistentDataPath;
        return Path.Combine(root, "Quasimorph_ModConfigs", "CustomStart");
    }

    private static string CalculateGameAssemblyHash()
    {
        string assemblyPath = typeof(FactionSystem).Assembly.Location;
        using FileStream stream = File.OpenRead(assemblyPath);
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
    }

    private static int CreateRandomSeed()
    {
        byte[] bytes = new byte[4];
        using RandomNumberGenerator generator = RandomNumberGenerator.Create();
        generator.GetBytes(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }

    private static void WriteReport(StartPlan plan)
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            string path = Path.Combine(_configDirectory, ReportFileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(plan, Formatting.Indented));
            Debug.Log(LogPrefix + $"Wrote start report to '{path}'.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(LogPrefix + "Could not write the start report. " + exception.Message);
        }
    }

    private static void InstallHarmonyOnce()
    {
        if (_harmonyInstalled)
        {
            return;
        }

        new Harmony(HarmonyId).PatchAll(Assembly.GetExecutingAssembly());
        _harmonyInstalled = true;
    }

    private sealed class PreparedStart
    {
        public PreparedStart(
            ModConfig config,
            string configSource,
            string profileName,
            StartProfile profile,
            int seed,
            bool allowStationTransfers)
        {
            Config = config;
            ConfigSource = configSource;
            ProfileName = profileName;
            Profile = profile;
            Seed = seed;
            AllowStationTransfers = allowStationTransfers;
        }

        public ModConfig Config { get; }

        public string ConfigSource { get; }

        public string ProfileName { get; }

        public StartProfile Profile { get; }

        public int Seed { get; }

        public bool AllowStationTransfers { get; }
    }

    private sealed class LoadedConfig
    {
        public LoadedConfig(ModConfig config, string sourcePath, bool isValid)
        {
            Config = config;
            SourcePath = sourcePath;
            IsValid = isValid;
        }

        public ModConfig Config { get; }

        public string SourcePath { get; }

        public bool IsValid { get; }
    }
}

[HarmonyPatch(typeof(ComponentsLayout), nameof(ComponentsLayout.CreateGlobalComponents))]
internal static class ComponentsLayoutCreateGlobalComponentsPatch
{
    [HarmonyPrefix]
    private static void Prefix(bool initContent)
    {
        Plugin.PrepareGlobalComponents(initContent);
    }
}

[HarmonyPatch(typeof(SpaceTimeSystem), nameof(SpaceTimeSystem.Create))]
internal static class SpaceTimeSystemCreatePatch
{
    [HarmonyPostfix]
    private static void Postfix(SpaceTime __result)
    {
        Plugin.AdjustFreshGameTime(__result);
    }
}

[HarmonyPatch(typeof(MercenarySystem), nameof(MercenarySystem.FillStartMercsAndClasses))]
internal static class MercenarySystemFillStartMercsAndClassesPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        Plugin.ApplyPreparedStart();
    }
}

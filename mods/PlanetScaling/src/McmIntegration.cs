using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ModConfigMenu;
using ModConfigMenu.Contracts;
using ModConfigMenu.Implementations;
using ModConfigMenu.Objects;
using UnityEngine;

namespace PlanetScaling;

internal static class McmIntegration
{
    private const string LogPrefix = "[PlanetScaling] ";

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
            ModConfigMenuAPI.RegisterModConfig("PlanetScaling", CreateValues(config), OnSave);
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

    private static List<IConfigValue> CreateValues(ModConfig config)
    {
        ModConfig defaults = new();
        return new List<IConfigValue>
        {
            new RangeConfig<float>(
                nameof(ModConfig.ShipVisualScale),
                config.ShipVisualScale,
                defaults.ShipVisualScale,
                0.02f,
                1f,
                "Scale",
                "Scale of the Magnum model, its local lights, and attached effects.",
                "Magnum visual scale"),
            new RangeConfig<float>(
                nameof(ModConfig.CameraDistanceScale),
                config.CameraDistanceScale,
                defaults.CameraDistanceScale,
                0.05f,
                1f,
                "Scale",
                "Multiplier for the normal orbital camera distance.",
                "Orbital camera distance"),
            new RangeConfig<float>(
                nameof(ModConfig.ShipScreenCameraDistanceScale),
                config.ShipScreenCameraDistanceScale,
                defaults.ShipScreenCameraDistanceScale,
                0.02f,
                1f,
                "Scale",
                "Multiplier for the camera on the Magnum upgrades and modules overview.",
                "Ship overview camera distance"),
            new ConfigValue(
                "__ApplyNote",
                "Changes apply the next time the orbital view is loaded.",
                "Application")
        };
    }

    private static bool OnSave(Dictionary<string, object> values, out string feedbackMessage)
    {
        feedbackMessage = string.Empty;
        ModConfig config = _config ?? throw new InvalidOperationException("MCM configuration was not initialized.");
        try
        {
            config.ShipVisualScale = GetFloat(values, nameof(ModConfig.ShipVisualScale));
            config.CameraDistanceScale = GetFloat(values, nameof(ModConfig.CameraDistanceScale));
            config.ShipScreenCameraDistanceScale = GetFloat(
                values,
                nameof(ModConfig.ShipScreenCameraDistanceScale));
            ModConfig.Validate(config);
            _save?.Invoke(config);
            feedbackMessage = "Saved. Changes apply the next time the orbital view is loaded.";
            return true;
        }
        catch (Exception exception)
        {
            feedbackMessage = "PlanetScaling could not save these settings: " + exception.Message;
            return false;
        }
    }

    private static float GetFloat(IReadOnlyDictionary<string, object> values, string key)
    {
        return Convert.ToSingle(values[key], CultureInfo.InvariantCulture);
    }
}

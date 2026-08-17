using System;

namespace PlanetScaling;

[Serializable]
public sealed class ModConfig
{
    public float ShipVisualScale { get; set; } = 0.12f;

    public float CameraDistanceScale { get; set; } = 0.25f;

    public float ShipScreenCameraDistanceScale { get; set; } = 0.12f;

    public static ModConfig Validate(ModConfig? config)
    {
        config ??= new ModConfig();
        config.ShipVisualScale = Clamp(config.ShipVisualScale, 0.02f, 1f);
        config.CameraDistanceScale = Clamp(config.CameraDistanceScale, 0.05f, 1f);
        config.ShipScreenCameraDistanceScale = Clamp(config.ShipScreenCameraDistanceScale, 0.02f, 1f);
        return config;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return min;
        }

        return Math.Max(min, Math.Min(max, value));
    }
}

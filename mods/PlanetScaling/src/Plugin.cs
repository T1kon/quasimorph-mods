using System;
using System.IO;
using System.Reflection;
using Cinemachine;
using HarmonyLib;
using MGSC;
using Newtonsoft.Json;
using UnityEngine;

namespace PlanetScaling;

public static class Plugin
{
    private const string HarmonyId = "quasimorph.planet_scaling";
    private const string LogPrefix = "[PlanetScaling] ";

    private static readonly FieldInfo CurrentDistanceTField = GetCameraField("_currentDistanceT");
    private static readonly FieldInfo MinCameraDistanceField = GetCameraField("_minCameraDistance");
    private static readonly FieldInfo MaxCameraDistanceField = GetCameraField("_maxCameraDistance");
    private static readonly FieldInfo ShipScreenCameraField =
        typeof(TravelingCameraManager).GetField(
            "_shipScreenCamera",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(TravelingCameraManager).FullName, "_shipScreenCamera");
    private static ModConfig _config = new();
    private static TravelingCameraManager? _cameraManager;
    private static CameraRotator? _cameraRotator;
    private static int _configuredSpaceshipId;
    private static int _configuredShipScreenCameraId;
    private static bool _harmonyInstalled;

    [Hook(ModHookType.SpaceStarted)]
    public static void OnSpaceStarted(IModContext context)
    {
        try
        {
            _config = LoadConfig(context.ModContentPath);
            InstallHarmonyOnce();

            Spaceship? spaceship = context.State.Get<Spaceship>();
            if (spaceship == null || spaceship.GetInstanceID() == _configuredSpaceshipId)
            {
                return;
            }

            _configuredSpaceshipId = spaceship.GetInstanceID();
            ConfigureShipVisual(spaceship);

            _cameraManager = spaceship.CameraManager;
            _cameraRotator = _cameraManager.GetComponent<CameraRotator>();
            ApplyConfiguredCameraDistance();
            ConfigureShipScreenCamera(GetShipScreenCamera());

            Debug.Log(
                LogPrefix
                + $"Applied ship scale {_config.ShipVisualScale:0.###} and camera distance scale "
                + $"{_config.CameraDistanceScale:0.###}.");
        }
        catch (Exception exception)
        {
            Debug.LogError(LogPrefix + "Failed to configure the orbital view.");
            Debug.LogException(exception);
        }
    }

    [Hook(ModHookType.SpaceFinished)]
    public static void OnSpaceFinished(IModContext context)
    {
        _cameraManager = null;
        _cameraRotator = null;
        _configuredSpaceshipId = 0;
        _configuredShipScreenCameraId = 0;
    }

    internal static void ApplyConfiguredCameraDistance(
        CameraRotator? rotator = null,
        CinemachineVirtualCamera? camera = null)
    {
        if (_cameraManager == null || _cameraRotator == null)
        {
            return;
        }

        rotator ??= _cameraRotator;
        camera ??= _cameraManager.CurrentCamera;
        if (rotator != _cameraRotator)
        {
            return;
        }

        float distanceScale;
        if (camera == _cameraManager.SpaceCamera)
        {
            distanceScale = _config.CameraDistanceScale;
        }
        else if (camera == GetShipScreenCamera())
        {
            ConfigureShipScreenCamera(camera);
            return;
        }
        else
        {
            return;
        }

        CinemachineFramingTransposer? transposer =
            camera.GetCinemachineComponent<CinemachineFramingTransposer>();
        if (transposer == null)
        {
            return;
        }

        float currentDistanceT = (float)CurrentDistanceTField.GetValue(rotator);
        float minDistance = (float)MinCameraDistanceField.GetValue(rotator);
        float maxDistance = (float)MaxCameraDistanceField.GetValue(rotator);
        float vanillaDistance = Mathf.Lerp(minDistance, maxDistance, currentDistanceT);
        transposer.m_CameraDistance = vanillaDistance * distanceScale;
    }

    private static void ConfigureShipVisual(Spaceship spaceship)
    {
        Transform? visualRoot = spaceship.Skin.transform.parent;
        if (visualRoot == null)
        {
            throw new InvalidOperationException("The Magnum visual root was not found.");
        }

        visualRoot.localScale *= _config.ShipVisualScale;
        ScaleModuleCameraOffsets(visualRoot);
        ScaleLocalLights(visualRoot);
        ScaleParticleEffects(visualRoot);
    }

    private static void ScaleModuleCameraOffsets(Transform visualRoot)
    {
        foreach (CinemachineVirtualCamera camera in
                 visualRoot.GetComponentsInChildren<CinemachineVirtualCamera>(includeInactive: true))
        {
            if (!RequiresScaledModuleOffset(camera.name))
            {
                continue;
            }

            CinemachineCameraOffset? offset = camera.GetComponent<CinemachineCameraOffset>();
            if (offset == null)
            {
                Debug.LogWarning(LogPrefix + $"Module camera '{camera.name}' has no camera offset component.");
                continue;
            }

            offset.m_Offset *= _config.ShipVisualScale;
        }
    }

    private static bool RequiresScaledModuleOffset(string cameraName)
    {
        return cameraName == "research"
            || cameraName == "hangar"
            || cameraName == "cloning"
            || cameraName == "supply";
    }

    private static void ScaleLocalLights(Transform visualRoot)
    {
        RescaleLocalLights(visualRoot, _config.ShipVisualScale);
    }

    private static void RescaleLocalLights(Transform visualRoot, float scale)
    {
        float intensityScale = scale * scale;
        foreach (Light light in visualRoot.GetComponentsInChildren<Light>(includeInactive: true))
        {
            if (light.type == LightType.Directional)
            {
                continue;
            }

            light.range *= scale;
            light.intensity *= intensityScale;
        }
    }

    private static void ScaleParticleEffects(Transform visualRoot)
    {
        foreach (ParticleSystem particleSystem in
                 visualRoot.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
        {
            ParticleSystem.MainModule main = particleSystem.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            // Existing particles were emitted before the SpaceStarted hook using vanilla sizing.
            // Clearing them lets the next emission use hierarchy-aware scaling immediately.
            particleSystem.Clear(withChildren: false);
        }

    }

    private static void ConfigureShipScreenCamera(CinemachineVirtualCamera? camera)
    {
        if (camera == null || camera.GetInstanceID() == _configuredShipScreenCameraId)
        {
            return;
        }

        Cinemachine3rdPersonFollow? follow = camera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();
        if (follow == null)
        {
            Debug.LogWarning(LogPrefix + "Ship overview camera has no Cinemachine3rdPersonFollow component.");
            return;
        }

        float scale = _config.ShipScreenCameraDistanceScale;
        follow.ShoulderOffset *= scale;
        follow.VerticalArmLength *= scale;
        follow.CameraDistance *= scale;
        follow.CameraRadius *= scale;
        _configuredShipScreenCameraId = camera.GetInstanceID();
    }

    private static CinemachineVirtualCamera? GetShipScreenCamera()
    {
        if (_cameraManager == null)
        {
            return null;
        }

        return ShipScreenCameraField.GetValue(_cameraManager) as CinemachineVirtualCamera;
    }

    private static ModConfig LoadConfig(string contentPath)
    {
        string path = Path.Combine(contentPath, "config.json");
        if (!File.Exists(path))
        {
            Debug.LogWarning(LogPrefix + "config.json was not found; using defaults.");
            return new ModConfig();
        }

        try
        {
            string json = File.ReadAllText(path);
            return ModConfig.Validate(JsonConvert.DeserializeObject<ModConfig>(json));
        }
        catch (Exception exception)
        {
            Debug.LogWarning(LogPrefix + "Could not read config.json; using defaults. " + exception.Message);
            return new ModConfig();
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

    private static FieldInfo GetCameraField(string name)
    {
        return typeof(CameraRotator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(CameraRotator).FullName, name);
    }
}

[HarmonyPatch(typeof(CameraRotator), "HandleZoomInput")]
internal static class CameraRotatorHandleZoomInputPatch
{
    [HarmonyPostfix]
    private static void Postfix(CameraRotator __instance)
    {
        Plugin.ApplyConfiguredCameraDistance(__instance);
    }
}

[HarmonyPatch(typeof(CameraRotator), nameof(CameraRotator.InitializeWithCamera))]
internal static class CameraRotatorInitializeWithCameraPatch
{
    [HarmonyPostfix]
    private static void Postfix(CameraRotator __instance, CinemachineVirtualCamera cam)
    {
        Plugin.ApplyConfiguredCameraDistance(__instance, cam);
    }
}

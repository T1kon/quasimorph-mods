using System;
using System.IO;
using MGSC;
using UnityEngine;

namespace PlanetScalingTextures;

public static class Plugin
{
    private const string LogPrefix = "[PlanetScalingTextures] ";
    private const string JupiterId = "jupiter";
    private const string DiffuseProperty = "_DiffuseTex";
    private const string NightProperty = "_CloudAndNightTex";

    private static Texture2D? _diffuseTexture;
    private static Texture2D? _nightTexture;

    [Hook(ModHookType.SpaceStarted)]
    public static void OnSpaceStarted(IModContext context)
    {
        try
        {
            SpaceObjects? spaceObjects = context.State.Get<SpaceObjects>();
            if (spaceObjects == null || !spaceObjects.Values.TryGetValue(JupiterId, out SpaceObject jupiter))
            {
                Debug.LogWarning(LogPrefix + "Jupiter was not available in space mode.");
                return;
            }

            ReleaseTextures();
            _diffuseTexture = LoadTexture(context.ModContentPath, "jupiter-cassini.png");
            _nightTexture = LoadTexture(context.ModContentPath, "jupiter-cassini-night.png");

            int changedMaterialCount = ConfigureJupiterMaterials(jupiter);
            if (changedMaterialCount == 0)
            {
                ReleaseTextures();
                Debug.LogWarning(LogPrefix + "Jupiter's normal material was not found.");
                return;
            }

            Debug.Log(
                LogPrefix
                + $"Applied 3600x1800 Cassini textures to {changedMaterialCount} Jupiter material(s).");
        }
        catch (Exception exception)
        {
            ReleaseTextures();
            Debug.LogError(LogPrefix + "Failed to replace Jupiter's textures.");
            Debug.LogException(exception);
        }
    }

    [Hook(ModHookType.SpaceFinished)]
    public static void OnSpaceFinished(IModContext context)
    {
        ReleaseTextures();
    }

    private static Texture2D LoadTexture(string contentPath, string filename)
    {
        string path = Path.Combine(contentPath, "Textures", filename);
        byte[] bytes = File.ReadAllBytes(path);
        Texture2D texture = new(2, 2, TextureFormat.RGB24, mipChain: false, linear: false);
        if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: true))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidDataException($"Could not decode texture '{path}'.");
        }

        texture.name = "PlanetScalingTextures_" + filename;
        texture.filterMode = FilterMode.Point;
        texture.wrapModeU = TextureWrapMode.Repeat;
        texture.wrapModeV = TextureWrapMode.Clamp;
        texture.anisoLevel = 1;
        return texture;
    }

    private static int ConfigureJupiterMaterials(SpaceObject jupiter)
    {
        int changedMaterialCount = 0;
        foreach (Renderer renderer in jupiter.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null
                    || !material.HasProperty(DiffuseProperty)
                    || !material.HasProperty(NightProperty)
                    || material.GetTexture(DiffuseProperty)?.name != JupiterId)
                {
                    continue;
                }

                material.SetTexture(DiffuseProperty, _diffuseTexture);
                material.SetTexture(NightProperty, _nightTexture);
                changedMaterialCount++;
            }
        }

        return changedMaterialCount;
    }

    private static void ReleaseTextures()
    {
        if (_diffuseTexture != null)
        {
            UnityEngine.Object.Destroy(_diffuseTexture);
            _diffuseTexture = null;
        }

        if (_nightTexture != null)
        {
            UnityEngine.Object.Destroy(_nightTexture);
            _nightTexture = null;
        }
    }
}

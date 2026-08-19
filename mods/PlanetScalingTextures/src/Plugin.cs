using System;
using System.Collections.Generic;
using System.IO;
using MGSC;
using UnityEngine;

namespace PlanetScalingTextures;

public static class Plugin
{
    private const string LogPrefix = "[PlanetScalingTextures] ";
    private const string DiffuseProperty = "_DiffuseTex";
    private const string NightProperty = "_CloudAndNightTex";

    private static readonly BodyTextureSpec[] BodyTextureSpecs =
    {
        new(
            "jupiter",
            "Jupiter",
            "jupiter",
            "jupiter-cassini.png",
            "jupiter-cassini-night.png"),
        new("moon", "Moon", "moon", "moon-lroc.png", "moon-lroc-night.png"),
    };

    private static readonly List<Texture2D> LoadedTextures = new();
    private static readonly List<MaterialTextureBinding> ActiveBindings = new();

    [Hook(ModHookType.SpaceStarted)]
    public static void OnSpaceStarted(IModContext context)
    {
        ReleaseTextures();

        try
        {
            SpaceObjects? spaceObjects = context.State.Get<SpaceObjects>();
            if (spaceObjects == null)
            {
                Debug.LogWarning(LogPrefix + "Space objects were not available in space mode.");
                return;
            }

            foreach (BodyTextureSpec spec in BodyTextureSpecs)
            {
                ApplyBodyTextures(context.ModContentPath, spaceObjects, spec);
            }
        }
        catch (Exception exception)
        {
            ReleaseTextures();
            Debug.LogError(LogPrefix + "Failed to replace planetary textures.");
            Debug.LogException(exception);
        }
    }

    [Hook(ModHookType.SpaceFinished)]
    public static void OnSpaceFinished(IModContext context)
    {
        ReleaseTextures();
    }

    private static void ApplyBodyTextures(
        string contentPath,
        SpaceObjects spaceObjects,
        BodyTextureSpec spec)
    {
        if (!spaceObjects.Values.TryGetValue(spec.SpaceObjectId, out SpaceObject body))
        {
            Debug.LogWarning(LogPrefix + $"{spec.DisplayName} was not available in space mode.");
            return;
        }

        Texture2D? diffuseTexture = null;
        Texture2D? nightTexture = null;
        List<MaterialTextureBinding> bodyBindings = new();
        try
        {
            diffuseTexture = LoadTexture(contentPath, spec.DiffuseFilename);
            nightTexture = LoadTexture(contentPath, spec.NightFilename);

            int changedMaterialCount = ConfigureMaterials(
                body,
                spec,
                diffuseTexture,
                nightTexture,
                bodyBindings);
            if (changedMaterialCount == 0)
            {
                Debug.LogWarning(LogPrefix + $"{spec.DisplayName}'s normal material was not found.");
                return;
            }

            ActiveBindings.AddRange(bodyBindings);
            LoadedTextures.Add(diffuseTexture);
            LoadedTextures.Add(nightTexture);
            diffuseTexture = null;
            nightTexture = null;

            Debug.Log(
                LogPrefix
                + $"Applied 3600x1800 textures to {changedMaterialCount} "
                + $"{spec.DisplayName} material(s).");
        }
        catch (Exception exception)
        {
            RestoreBindings(bodyBindings);
            Debug.LogError(LogPrefix + $"Failed to replace {spec.DisplayName}'s textures.");
            Debug.LogException(exception);
        }
        finally
        {
            DestroyTexture(diffuseTexture);
            DestroyTexture(nightTexture);
        }
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

    private static int ConfigureMaterials(
        SpaceObject body,
        BodyTextureSpec spec,
        Texture2D diffuseTexture,
        Texture2D nightTexture,
        ICollection<MaterialTextureBinding> bindings)
    {
        int changedMaterialCount = 0;
        foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(includeInactive: true))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null
                    || !material.HasProperty(DiffuseProperty)
                    || !material.HasProperty(NightProperty)
                    || material.GetTexture(DiffuseProperty)?.name != spec.ExpectedDiffuseTextureName)
                {
                    continue;
                }

                bindings.Add(
                    new MaterialTextureBinding(
                        material,
                        material.GetTexture(DiffuseProperty),
                        material.GetTexture(NightProperty),
                        diffuseTexture,
                        nightTexture));
                material.SetTexture(DiffuseProperty, diffuseTexture);
                material.SetTexture(NightProperty, nightTexture);
                changedMaterialCount++;
            }
        }

        return changedMaterialCount;
    }

    private static void ReleaseTextures()
    {
        RestoreBindings(ActiveBindings);
        ActiveBindings.Clear();

        foreach (Texture2D texture in LoadedTextures)
        {
            DestroyTexture(texture);
        }

        LoadedTextures.Clear();
    }

    private static void RestoreBindings(IList<MaterialTextureBinding> bindings)
    {
        for (int index = bindings.Count - 1; index >= 0; index--)
        {
            bindings[index].Restore();
        }
    }

    private static void DestroyTexture(Texture2D? texture)
    {
        if (texture != null)
        {
            UnityEngine.Object.Destroy(texture);
        }
    }

    private sealed class MaterialTextureBinding
    {
        private readonly Material _material;
        private readonly Texture? _originalDiffuse;
        private readonly Texture? _originalNight;
        private readonly Texture _replacementDiffuse;
        private readonly Texture _replacementNight;

        public MaterialTextureBinding(
            Material material,
            Texture? originalDiffuse,
            Texture? originalNight,
            Texture replacementDiffuse,
            Texture replacementNight)
        {
            _material = material;
            _originalDiffuse = originalDiffuse;
            _originalNight = originalNight;
            _replacementDiffuse = replacementDiffuse;
            _replacementNight = replacementNight;
        }

        public void Restore()
        {
            if (_material == null)
            {
                return;
            }

            if (_material.GetTexture(DiffuseProperty) == _replacementDiffuse)
            {
                _material.SetTexture(DiffuseProperty, _originalDiffuse);
            }

            if (_material.GetTexture(NightProperty) == _replacementNight)
            {
                _material.SetTexture(NightProperty, _originalNight);
            }
        }
    }

    private sealed class BodyTextureSpec
    {
        public BodyTextureSpec(
            string spaceObjectId,
            string displayName,
            string expectedDiffuseTextureName,
            string diffuseFilename,
            string nightFilename)
        {
            SpaceObjectId = spaceObjectId;
            DisplayName = displayName;
            ExpectedDiffuseTextureName = expectedDiffuseTextureName;
            DiffuseFilename = diffuseFilename;
            NightFilename = nightFilename;
        }

        public string SpaceObjectId { get; }

        public string DisplayName { get; }

        public string ExpectedDiffuseTextureName { get; }

        public string DiffuseFilename { get; }

        public string NightFilename { get; }
    }
}

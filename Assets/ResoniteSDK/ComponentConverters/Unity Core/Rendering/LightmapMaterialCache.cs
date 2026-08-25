using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Bridges Unity's baked-lightmap system (including Bakery's Unity-compatible non-directional
// mode, which also writes through the standard lightmapIndex/lightmapScaleOffset/
// LightmapSettings.lightmaps mechanism - see the Bakery note on LightmapDecoder) into the
// conversion pipeline.
//
// Renderite has no custom-shader support, so a "lit by lightmap" shader can't be shipped to
// Resonite directly. Instead, when a MeshRenderer's material is Unity's Standard shader in
// Opaque mode with a valid baked lightmap, this resolves (creating on first use) a persisted
// Material asset using the ResoniteSDK/BakedLightmapStandard marker shader, carrying the decoded
// lightmap texture + ScaleOffset as extra properties. BakedLightmapStandardConverter then routes
// that marker shader to PBS_MultiUV_Metallic, folding the lightmap into the SecondaryAlbedo (UV1)
// slot. Any material/renderer combination that doesn't meet the eligibility criteria below is
// returned unchanged and falls through to the regular converter.
//
// Key invariants:
//  - Variants are real project assets at a deterministic path under
//    Assets/ResoniteSDK/Generated/LightmapVariants/<sceneGUID>/, not in-memory DontSave clones - a
//    DontSave cache wouldn't survive a domain reload (leaving stale `Source` references on
//    converter GameObjects) and has no on-disk identity to prevent two scenes colliding on the
//    same generated name. The deterministic path *is* the cache key: AssetDatabase.LoadAssetAtPath
//    always returns the same instance for it, and AssetDatabase.CreateAsset creates it once if
//    missing.
//  - Because a "hit" is just "the asset already exists", every property is unconditionally
//    re-read from source and (re-)written on every call in GetVariantOrOriginalInner - otherwise
//    edits to the source material after first conversion would never propagate. Only whether
//    anything actually changed is tracked, to avoid dirtying/reserializing when nothing did.
//  - The asset name embeds the owning scene's GUID (unsaved scenes fall back to a shared
//    "unsaved" bucket, see GetSceneGuid) and a bit-exact hash of the lightmap ScaleOffset (see
//    HashScaleOffset/FloatBits, not float equality/GetHashCode), so two scenes or two bakes can
//    never collide on the same variant.
//  - GetVariantOrOriginal never throws: eligibility checks are defensive, and the lookup/creation
//    body is wrapped in try/catch that logs and falls back to the original material.
//  - A session-scoped Dictionary<path, Material> (_materialByPath) fronts the
//    AssetDatabase.LoadAssetAtPath lookup purely for performance; the on-disk asset stays the
//    source of truth, so a domain reload clearing the dictionary just refills it on the next call.
//  - Variant assets can become orphaned (old scene GUID/hash combos left behind after a scene is
//    deleted/renamed/re-baked) - there's no automatic sweep, since enumerating every scene GUID
//    that ever existed isn't reliable. ClearGeneratedLightmapVariants() below nukes the entire
//    generated folder on demand; everything in it is reproducible by re-running scene conversion.
//  - GetVariantOrOriginalInner has an additional branch, taken only for a directional lightmapper
//    bake (LightmapData.lightmapDir != null), that substitutes a per-renderer combined texture
//    from DirectionalLightmapBaker in place of the shared-atlas decode - see that branch's inline
//    comment and DirectionalLightmapBaker.cs's header for the full design.
public static class LightmapMaterialCache
{
    const string BakedLightmapShaderName = "ResoniteSDK/BakedLightmapStandard";

    // Shaders verified (not assumed) to be a property-for-property match with Unity's built-in
    // Standard shader for every field this file reads (_Color, _MainTex+Scale/Offset, _Metallic,
    // _Glossiness, _MetallicGlossMap, _BumpMap, _BumpScale, _OcclusionMap, _EmissionMap,
    // _EmissionColor, _Cutoff, _Mode, _EMISSION keyword), so the same read/copy code below
    // produces correct results for them too. "Autodesk Interactive" matters in practice: it's
    // Unity's FBX-importer fallback shader for materials with no explicit shader baked in, common
    // across off-the-shelf asset packs.
    //
    // Do NOT add a shader here on assumption alone - a mismatched property name silently
    // reads/writes the wrong value (GetFloat on a missing property returns 0, indistinguishable
    // from a legitimately-set 0). Confirm against the shader's actual Properties block first.
    //
    // Must be public, not internal: this file compiles into Assembly-CSharp, while
    // LightmapTestHarness.cs (Assets/Editor/) compiles into the separate Assembly-CSharp-Editor
    // assembly, which `internal` visibility doesn't reach.
    public static readonly HashSet<string> StandardCompatibleShaderNames = new HashSet<string>
    {
        "Standard",
        "Silent/Filamented",
        "Standard_Culloff",
        "Autodesk Interactive",
    };

    // Session-scoped memory front-cache for lightmap-variant Material assets, keyed by asset path.
    // See the class-level comment above for the "AssetDatabase is still the source of truth"
    // invariant this relies on.
    static readonly Dictionary<string, Material> _materialByPath = new Dictionary<string, Material>();

    // Tracks whether GetVariantOrOriginalInner actually created a brand new variant Material asset
    // (via AssetDatabase.CreateAsset) during the current top-level GetVariantOrOriginal call, so
    // the wrapper below can call AssetDatabase.SaveAssets() at most once per call - and only when
    // there's a genuinely new asset that needs to exist on disk - rather than unconditionally on
    // every call, which would be a needless performance hit on every already-converted material.
    static bool _createdNewAssetThisCall;

    /// <summary>
    /// Deletes the entire Assets/ResoniteSDK/Generated/LightmapVariants folder (variant materials
    /// AND decoded lightmap PNGs alike - LightmapDecoder writes its output under the same root,
    /// via LightmapVariantStorage), and clears both in-memory front caches so nothing here can
    /// hand out a reference to something that was just deleted. Every asset under that folder is
    /// fully reproducible by re-running scene conversion, so this is always safe to run. See the
    /// class-level comment for why orphaned variant assets can accumulate here over time.
    /// </summary>
    // Unchanged and still actively used - its only caller is now SceneConverter.ConvertScene(),
    // which calls it automatically whenever the "Force Refresh Generated Lightmaps" toggle is on.
    // There is no manual button for it.
    public static void ClearGeneratedLightmapVariants()
    {
        _materialByPath.Clear();
        LightmapDecoder.ClearMemoryCache();

        if (!AssetDatabase.IsValidFolder(LightmapVariantStorage.RootFolder))
        {
            Debug.Log($"[ResoniteSDK] Nothing to clear - \"{LightmapVariantStorage.RootFolder}\" does not exist.");
            return;
        }

        if (AssetDatabase.DeleteAsset(LightmapVariantStorage.RootFolder))
            Debug.Log($"[ResoniteSDK] Deleted \"{LightmapVariantStorage.RootFolder}\". " +
                "It will be fully regenerated the next time affected scenes are converted.");
        else
            Debug.LogWarning($"[ResoniteSDK] Failed to delete \"{LightmapVariantStorage.RootFolder}\" - see the Console above for details.");
    }

    /// <summary>
    /// Returns a lightmap-carrying variant of <paramref name="source"/> if the renderer has a
    /// valid baked lightmap assigned and the material is eligible (Standard shader, Opaque mode).
    /// Otherwise returns <paramref name="source"/> unchanged.
    /// </summary>
    public static Material GetVariantOrOriginal(Renderer renderer, Material source)
    {
        if (renderer == null || source == null)
            return source;

        _createdNewAssetThisCall = false;

        try
        {
            var result = GetVariantOrOriginalInner(renderer, source);

            // Only persist when this specific call actually created a brand new variant asset -
            // property-only updates to an already-existing variant are left for Unity's normal
            // dirty-asset save path, and a non-eligible/unchanged material has nothing to save at
            // all.
            if (_createdNewAssetThisCall)
                AssetDatabase.SaveAssets();

            return result;
        }
        catch (Exception ex)
        {
            // Never let a lightmap-variant failure take down the whole material conversion -
            // fall back to the original, un-lightmapped material instead.
            Debug.LogWarning($"[ResoniteSDK] LightmapMaterialCache: failed to build a lightmap variant for material " +
                $"\"{source.name}\" on renderer \"{renderer.name}\": {ex}. Falling back to the original material without the baked lightmap.");
            return source;
        }
    }

    static Material GetVariantOrOriginalInner(Renderer renderer, Material source)
    {
        int lightmapIndex = renderer.lightmapIndex;

        if (lightmapIndex < 0 || lightmapIndex >= LightmapSettings.lightmaps.Length)
            return source;

        var lightmapData = LightmapSettings.lightmaps[lightmapIndex];

        if (lightmapData.lightmapColor == null)
            return source;

        if (source.shader == null || !StandardCompatibleShaderNames.Contains(source.shader.name))
            return source;

        // Guard the _Mode read below - some Standard-named shader variants/older serialized
        // materials may not expose it. Treat those defensively as non-eligible rather than
        // throwing out of Material.GetFloat.
        if (!source.HasProperty("_Mode"))
            return source;

        // v1 only handles Opaque (_Mode == 0). Cutout/Fade/Transparent fall through to the
        // regular conversion path.
        if (source.GetFloat("_Mode") != 0f)
            return source;

        var shader = Shader.Find(BakedLightmapShaderName);

        if (shader == null)
        {
            Debug.LogWarning($"[ResoniteSDK] LightmapMaterialCache: could not find shader \"{BakedLightmapShaderName}\". " +
                $"Falling back to the original material without the baked lightmap for \"{source.name}\".");
            return source;
        }

        var lightmapScaleOffset = renderer.lightmapScaleOffset;

        var sceneGuid = GetSceneGuid(renderer);
        var sourceIdentity = GetSourceIdentity(source);
        var stHash = HashScaleOffset(lightmapScaleOffset);

        var assetName = $"LM_{sourceIdentity}_{lightmapIndex}_{stHash}.mat";
        var folder = LightmapVariantStorage.GetSceneFolder(sceneGuid);
        var path = $"{folder}/{assetName}";

        LightmapVariantStorage.EnsureFolder(folder);

        var variant = GetCachedOrLoadMaterial(path);

        bool changed = variant == null;

        if (variant == null)
        {
            // Material.name should be the bare asset name, not the ".mat"-suffixed *filename* -
            // Unity itself never includes the extension in an asset's Object.name (compare any
            // other .mat in the Project window), so leaving the extension in here would be
            // inconsistent with every other asset in the project and would show "Foo.mat" as the
            // material's displayed name inside the Inspector/hierarchy.
            variant = new Material(shader) { name = Path.GetFileNameWithoutExtension(assetName) };
            AssetDatabase.CreateAsset(variant, path);
            _materialByPath[path] = variant;
            _createdNewAssetThisCall = true;
        }
        else if (variant.shader != shader)
        {
            // Defensive: force the marker shader back if it was ever swapped out from under us
            // (e.g. a stale asset left over from before this shader existed), so the property
            // copies below land on the properties we expect.
            variant.shader = shader;
            changed = true;
        }

        // Decode (or reuse the already-decoded, hash-checked) baked lightmap texture for this
        // scene/index pair. See LightmapDecoder for the decode + persistence logic.
        var bakedLightmapTex = LightmapDecoder.GetDecodedLightmap(sceneGuid, lightmapIndex, lightmapData.lightmapColor);

        // GetDecodedLightmap can itself return null (missing decode shader, failed
        // AssetDatabase import - see its own doc comment) even once every eligibility check
        // above has passed. Bail out to the same `return source;` contract as those checks,
        // rather than falling through and binding a null "_BakedLightmap" on the variant.
        if (bakedLightmapTex == null)
            return source;

        var bakedLightmapST = lightmapScaleOffset;

        // --- Experimental "bake in normals" path ---------------------------------------------
        // Taken only when this lightmap slot was baked with a directional lightmapper
        // (lightmapData.lightmapDir != null, set only when LightingSettings.directionalityMode
        // was CombinedDirectional at bake time). Gated on the baked data itself, not a flag from
        // the caller, so a NonDirectional bake takes none of this code and is unaffected.
        //
        // Resonite/Renderite has no directional-lightmap material input, so a per-renderer
        // combined texture from DirectionalLightmapBaker substitutes for the shared-atlas decode
        // - see that file's header comment for the full approximation this performs
        // (per-renderer geometry-normal patch x UnityCG.cginc's DecodeDirectionalLightmap()
        // formula, baked once into a static texture at Editor conversion time).
        if (lightmapData.lightmapDir != null)
        {
            var normalBaked = DirectionalLightmapBaker.GetNormalBakedLightmap(renderer, sceneGuid, lightmapIndex, lightmapData, lightmapScaleOffset);

            if (normalBaked != null)
            {
                // normalBaked is already cropped 1:1 to exactly this renderer's own lightmap
                // tile (see DirectionalLightmapBaker.GetNormalBakedLightmapInner) - it needs no
                // further scale/offset, unlike the shared whole-atlas bakedLightmapTex above.
                bakedLightmapTex = normalBaked;
                bakedLightmapST = new Vector4(1f, 1f, 0f, 0f);
            }
            // else: DirectionalLightmapBaker already Debug.LogWarning'd the specific reason
            // (missing UV2 channel, missing internal shader, etc.) and returned null - fall
            // back to the plain atlas-wide decode already computed above, exactly as if this
            // renderer's lightmap had been baked non-directional.
        }
        // ------------------------------------------------------------------------------------

        // Every property below is re-read from source and (re-)written on *every* call, even
        // when the variant asset already existed - only whether anything actually changed is
        // tracked, to decide if EditorUtility.SetDirty needs to run.
        changed |= SetColorIfChanged(variant, "_Color", source.GetColor("_Color"));
        changed |= SetTextureIfChanged(variant, "_MainTex", source.GetTexture("_MainTex"));
        changed |= SetTextureScaleIfChanged(variant, "_MainTex", source.GetTextureScale("_MainTex"));
        changed |= SetTextureOffsetIfChanged(variant, "_MainTex", source.GetTextureOffset("_MainTex"));

        changed |= SetFloatIfChanged(variant, "_Metallic", source.GetFloat("_Metallic"));
        changed |= SetFloatIfChanged(variant, "_Glossiness", source.GetFloat("_Glossiness"));
        changed |= SetTextureIfChanged(variant, "_MetallicGlossMap", source.GetTexture("_MetallicGlossMap"));

        changed |= SetTextureIfChanged(variant, "_BumpMap", source.GetTexture("_BumpMap"));
        changed |= SetFloatIfChanged(variant, "_BumpScale", source.GetFloat("_BumpScale"));

        changed |= SetTextureIfChanged(variant, "_OcclusionMap", source.GetTexture("_OcclusionMap"));

        changed |= SetTextureIfChanged(variant, "_EmissionMap", source.GetTexture("_EmissionMap"));
        changed |= SetColorIfChanged(variant, "_EmissionColor", source.GetColor("_EmissionColor"));

        changed |= SetFloatIfChanged(variant, "_Cutoff", source.GetFloat("_Cutoff"));
        changed |= SetFloatIfChanged(variant, "_Mode", source.GetFloat("_Mode"));

        // BakedLightmapStandard.shader only branches on the _EMISSION keyword, so that's the only
        // one worth syncing (other Standard keywords like _METALLICGLOSSMAP/_NORMALMAP do nothing
        // on this shader). Uses the non-obsolete IsKeywordEnabled/EnableKeyword/DisableKeyword API
        // rather than the obsolete Material.shaderKeywords.
        bool sourceEmissionEnabled = source.IsKeywordEnabled("_EMISSION");

        if (variant.IsKeywordEnabled("_EMISSION") != sourceEmissionEnabled)
        {
            if (sourceEmissionEnabled)
                variant.EnableKeyword("_EMISSION");
            else
                variant.DisableKeyword("_EMISSION");

            changed = true;
        }

        changed |= SetTextureIfChanged(variant, "_BakedLightmap", bakedLightmapTex);
        changed |= SetVectorIfChanged(variant, "_BakedLightmapST", bakedLightmapST);

        // Desaturated companion for BakedLightmapStandardConverter's additive fill slot (see
        // LightmapDecoder.GetDecodedLightmap's desaturate param doc comment). Only decoded when
        // the fill it feeds is actually enabled (AdditiveFillStrength > 0) - when disabled,
        // SecondaryEmissiveColor is pure black regardless of what's bound here, so decoding
        // (RenderTexture blit + resize + PNG encode + AssetDatabase import) and uploading it over
        // ResoniteLink would be a visually-inert but real CPU/disk/network/payload cost.
        if (BakedLightmapStandardConverter.AdditiveFillStrength > 0f)
        {
            var bakedLightmapGrayTex = LightmapDecoder.GetDecodedLightmap(sceneGuid, lightmapIndex, lightmapData.lightmapColor, desaturate: true);
            changed |= SetTextureIfChanged(variant, "_BakedLightmapGray", bakedLightmapGrayTex);
        }
        else if (variant.GetTexture("_BakedLightmapGray") != null)
        {
            // Fill was previously enabled (variant already has a gray texture bound) and has
            // since been turned back off - clear the stale reference so a future re-enable can't
            // silently pick up out-of-date pixel data before this converter runs again.
            changed |= SetTextureIfChanged(variant, "_BakedLightmapGray", null);
        }

        if (changed)
            EditorUtility.SetDirty(variant);

        return variant;
    }

    /// <summary>
    /// Resolves the GUID of the scene the given renderer lives in, so variant asset paths never
    /// collide between two scenes converting the same source material with a different bake.
    /// </summary>
    static string GetSceneGuid(Renderer renderer)
    {
        var scenePath = renderer.gameObject.scene.path;

        if (string.IsNullOrEmpty(scenePath))
        {
            // Scene has never been saved to disk (e.g. a brand-new untitled scene), so there's no
            // GUID to key variants off of. Falling back to a fixed "unsaved" bucket means lightmap
            // variants across multiple never-saved scenes could theoretically collide, but there's
            // no meaningful identity to disambiguate them with until the scene is actually saved.
            Debug.LogWarning($"[ResoniteSDK] LightmapMaterialCache: scene for renderer \"{renderer.name}\" has not been saved to disk; " +
                "lightmap variants will be stored under the shared \"unsaved\" bucket instead of a scene-specific one.");
            return "unsaved";
        }

        var guid = AssetDatabase.AssetPathToGUID(scenePath);

        return string.IsNullOrEmpty(guid) ? "unsaved" : guid;
    }

    /// <summary>
    /// Resolves a stable identity string for the source material to use in the variant's asset
    /// name.
    /// </summary>
    static string GetSourceIdentity(Material source)
    {
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId) && !string.IsNullOrEmpty(guid))
            return $"{guid}_{localId}";

        // Non-asset material (e.g. a runtime-only instance never saved to disk) -
        // TryGetGUIDAndLocalFileIdentifier can't resolve a stable identity for these, so we fall
        // back to the material's InstanceID. NOTE: this is NOT stable across Editor
        // sessions/domain reloads for what is logically "the same" material, so repeated
        // conversions of a non-asset Standard material can each mint a new variant asset here
        // (old ones become orphaned). This is a rare edge case in practice, since virtually all
        // authored materials referenced by a Unity scene's renderers are real .mat assets.
        return $"instid{source.GetInstanceID()}";
    }

    /// <summary>
    /// Bit-exact hash of the lightmap ScaleOffset Vector4, used in the variant's asset filename.
    /// We deliberately hash the raw bit patterns (not the float values via equality/GetHashCode)
    /// so two ScaleOffsets are only ever treated as "the same" when they are bit-for-bit
    /// identical - repeated bakes producing float values that differ only in the last few bits of
    /// mantissa precision must not be able to collide into a stale-looking cache hit.
    /// </summary>
    static string HashScaleOffset(Vector4 st)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + FloatBits(st.x);
            hash = hash * 31 + FloatBits(st.y);
            hash = hash * 31 + FloatBits(st.z);
            hash = hash * 31 + FloatBits(st.w);
            return hash.ToString("x8");
        }
    }

    /// <summary>
    /// Bit-exact int32 representation of a float's underlying bit pattern. Equivalent to
    /// BitConverter.SingleToInt32Bits, implemented manually via GetBytes/ToInt32 since
    /// SingleToInt32Bits isn't available on every .NET/Mono runtime version Unity might be
    /// running under.
    /// </summary>
    static int FloatBits(float value) => BitConverter.ToInt32(BitConverter.GetBytes(value), 0);

    static bool SetColorIfChanged(Material material, string property, Color value)
    {
        if (material.GetColor(property) == value)
            return false;

        material.SetColor(property, value);
        return true;
    }

    static bool SetFloatIfChanged(Material material, string property, float value)
    {
        if (material.GetFloat(property) == value)
            return false;

        material.SetFloat(property, value);
        return true;
    }

    static bool SetTextureIfChanged(Material material, string property, Texture value)
    {
        if (material.GetTexture(property) == value)
            return false;

        material.SetTexture(property, value);
        return true;
    }

    static bool SetTextureScaleIfChanged(Material material, string property, Vector2 value)
    {
        if (material.GetTextureScale(property) == value)
            return false;

        material.SetTextureScale(property, value);
        return true;
    }

    static bool SetTextureOffsetIfChanged(Material material, string property, Vector2 value)
    {
        if (material.GetTextureOffset(property) == value)
            return false;

        material.SetTextureOffset(property, value);
        return true;
    }

    static bool SetVectorIfChanged(Material material, string property, Vector4 value)
    {
        if (material.GetVector(property) == value)
            return false;

        material.SetVector(property, value);
        return true;
    }

    /// <summary>
    /// Session memory front-cache lookup for a variant Material at <paramref name="path"/>. The
    /// on-disk asset (AssetDatabase) remains the actual source of truth - see the class-level
    /// comment for why this is safe even if the dictionary is empty/stale (e.g. right after a
    /// domain reload).
    /// </summary>
    static Material GetCachedOrLoadMaterial(string path)
    {
        if (_materialByPath.TryGetValue(path, out var cached))
        {
            // Unity's overloaded null check catches a destroyed/unloaded Material here (Unity
            // pseudo-null), not just a real null reference - a stale entry like that is evicted so
            // it can't be handed out again, and the lookup falls through to AssetDatabase below.
            if (cached != null)
                return cached;

            _materialByPath.Remove(path);
        }

        var loaded = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (loaded != null)
            _materialByPath[path] = loaded;

        return loaded;
    }
}

/// <summary>
/// Shared path-building/folder-creation helpers for the on-disk lightmap variant repository, used
/// by both LightmapMaterialCache (material variants) and LightmapDecoder (decoded lightmap
/// textures), so both always agree on exactly which folder a given scene's generated assets live
/// under.
/// </summary>
internal static class LightmapVariantStorage
{
    public const string RootFolder = "Assets/ResoniteSDK/Generated/LightmapVariants";

    public static string GetSceneFolder(string sceneGuid) => $"{RootFolder}/{sceneGuid}";

    /// <summary>
    /// Creates every missing folder segment of <paramref name="folderPath"/> (which must start
    /// with "Assets"), using AssetDatabase.CreateFolder rather than raw filesystem APIs so Unity
    /// registers each folder as a real asset immediately.
    /// </summary>
    public static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        var parts = folderPath.Split('/');
        var current = parts[0]; // "Assets"

        for (int i = 1; i < parts.Length; i++)
        {
            var next = $"{current}/{parts[i]}";

            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }
}

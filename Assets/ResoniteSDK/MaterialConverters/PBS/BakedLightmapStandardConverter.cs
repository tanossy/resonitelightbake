using FrooxEngine;
using UnityEngine;

// Converts the ResoniteSDK/BakedLightmapStandard marker shader (see BakedLightmapStandard.shader
// and LightmapMaterialCache) into PBS_MultiUV_Metallic, folding the baked lightmap into the
// SecondaryAlbedo slot (UV1) since Renderite has no custom-shader support and MeshConverter
// already forwards Unity's mesh.uv2 into Resonite's TexCoord1.
//
// Does not inherit from StandardBaseConverter<TWrapper, TMaterial>: that base requires
// `TMaterial : FrooxEngine.PBS_Material`, but PBS_MultiUV_Metallic derives from the separate
// PBS_MultiUV_Material hierarchy (per-channel AlbedoScale/AlbedoOffset instead of a single shared
// TextureScale/TextureOffset), so the property mapping is reimplemented here.
//
// AlbedoColor/EmissiveColor go through ColorGradingApproximation.Apply(...) so Tonemap
// Compensation still affects baked-lightmap materials, matching StandardBaseConverter's handling
// of non-lightmapped ones.
[MaterialConverter(false, "ResoniteSDK/BakedLightmapStandard")]
public class BakedLightmapStandardConverter : ResoniteMaterialConverter
{
    // Strength of an additive "ambient fill" on top of the multiplicative SecondaryAlbedo
    // lightmap blend, approximating Unity's baked-GI shadow lifting (0 = pure multiply, 1 = full
    // additive). Kept at 0: a pure additive blend crushes contrast (adding a fixed value shrinks a
    // dark/bright ratio far more than a light/bright one - e.g. 9:1 becomes ~2.5:1 at +0.3), so
    // brightness is instead boosted upstream via LightmapDecoder.RangeScale, which preserves
    // ratios by acting as a gain before the multiply.
    public static float AdditiveFillStrength = 0.0f;

    // Compensates for LightConverter.IntensityMultiplier: brighter lights make the same
    // Smoothness value produce disproportionately stronger specular highlights than in Unity.
    // Applied only to the scalar Smoothness value - a MetallicMap-driven alpha channel needs the
    // equivalent attenuation baked into the texture itself instead.
    public static float SmoothnessCompensation = 0.05f;

    // High-Metallic surfaces are almost all specular reflection, so lowering Smoothness alone
    // only dulls the highlight without ever removing it. Metallic is attenuated separately at
    // send time to actually tame the glare.
    public static float MetallicCompensation = 0.0f;

    // Fast-iteration mode for large scenes: skips every source texture upload (albedo/normal/
    // metallic/occlusion/emission), sending only geometry, material colors, and the baked
    // lightmap, so a scene appears in-world before a full texture upload could complete.
    public static bool LightmapPreviewUploadOnly = false;

    public PBS_MultiUV_MetallicWrapper PBS;

    public override IAssetProvider<FrooxEngine.Material> UpdateConversion(UnityEngine.Material material, IConversionContext context)
    {
        if (PBS == null)
            PBS = gameObject.AddComponent<PBS_MultiUV_MetallicWrapper>();

        var data = PBS.Data;

        data.RenderQueue = material.renderQueue;

        // --- Alpha handling / culling (explicit) ---
        // LightmapMaterialCache only ever produces this marker material for Opaque-mode Standard
        // materials, and the marker shader has no per-material Cull property (Standard is always
        // back-face culled), so these are hardcoded rather than derived from the source material.
        // AlphaClip still forwards _Cutoff in case a future version relaxes the Opaque-only
        // restriction.
        data.Culling = FrooxEngine.Culling.Back;
        data.AlphaHandling = FrooxEngine.AlphaHandling.Opaque;
        data.AlphaClip = material.GetFloat("_Cutoff");

        // --- Albedo (UV0) ---
        data.AlbedoColor = ColorGradingApproximation.Apply(material.GetColor("_Color")).ToColorX_sRGB();
        data.AlbedoTexture = LightmapPreviewUploadOnly ? null : context.GetITexture2D(material.GetTexture("_MainTex"));
        var mainTexScale = material.GetTextureScale("_MainTex");
        var mainTexOffset = material.GetTextureOffset("_MainTex");
        data.AlbedoScale = mainTexScale;
        data.AlbedoOffset = mainTexOffset;
        data.AlbedoUV = 0;

        // --- Normal (UV0) ---
        // Unity's Standard shader samples normal/occlusion/emission with the same uv_MainTex
        // transform as albedo, so _MainTex's ScaleOffset is mirrored here rather than left at the
        // zeroed Sync<Vector2> default (which would collapse every sample to a single texel).
        data.NormalMap = LightmapPreviewUploadOnly ? null : context.GetITexture2D(material.GetTexture("_BumpMap"));
        data.NormalScale = material.GetFloat("_BumpScale");
        data.NormalMapScale = mainTexScale;
        data.NormalMapOffset = mainTexOffset;
        data.NormalMapUV = 0;

        // --- Occlusion (UV0) ---
        data.OcclusionMap = LightmapPreviewUploadOnly ? null : context.GetITexture2D(material.GetTexture("_OcclusionMap"));
        data.OcclusionMapScale = mainTexScale;
        data.OcclusionMapOffset = mainTexOffset;
        data.OcclusionMapUV = 0;

        // --- Emission (UV0) ---
        if (material.IsKeywordEnabled("_EMISSION"))
        {
            data.EmissiveColor = ColorGradingApproximation.Apply(material.GetColor("_EmissionColor")).ToColorX_sRGB();
            data.EmissiveMap = LightmapPreviewUploadOnly ? null : context.GetITexture2D(material.GetTexture("_EmissionMap"));
        }
        else
        {
            // There's no actual toggle for emission on the Resonite version, so just set it to black
            data.EmissiveColor = Color.black.ToColorX_sRGB();
        }
        data.EmissionMapScale = mainTexScale;
        data.EmissionMapOffset = mainTexOffset;
        data.EmissionMapUV = 0;

        // --- Metallic / Smoothness (UV0) ---
        data.Metallic = material.GetFloat("_Metallic") * Mathf.Clamp01(MetallicCompensation);
        data.Smoothness = material.GetFloat("_Glossiness") * Mathf.Clamp01(SmoothnessCompensation);
        data.MetallicMap = LightmapPreviewUploadOnly ? null : context.GetITexture2D(material.GetTexture("_MetallicGlossMap"));
        data.MetallicMapScale = mainTexScale;
        data.MetallicMapOffset = mainTexOffset;
        data.MetallicMapUV = 0;

        // --- Baked lightmap (UV1) ---
        // Written by LightmapMaterialCache onto this marker material's _BakedLightmap /
        // _BakedLightmapST.
        var bakedLightmap = context.GetITexture2D(material.GetTexture("_BakedLightmap"));
        // Desaturated (luma-only) companion for the additive fill below, so per-object lightmap
        // hue doesn't leak into the brightness-only approximation. Only fetched when the additive
        // fill is enabled - at AdditiveFillStrength=0 it would contribute nothing.
        var bakedLightmapGray = AdditiveFillStrength > 0f
            ? context.GetITexture2D(material.GetTexture("_BakedLightmapGray"))
            : null;
        var lightmapScaleOffset = material.GetVector("_BakedLightmapST");
        var lightmapScale = new Vector2(lightmapScaleOffset.x, lightmapScaleOffset.y);
        var lightmapOffset = new Vector2(lightmapScaleOffset.z, lightmapScaleOffset.w);

        // Multiplicative half: always on, preserves the base albedo's own color instead of
        // washing it toward the lightmap's own hue/brightness.
        data.SecondaryAlbedoTexture = bakedLightmap;
        data.SecondaryAlbedoScale = lightmapScale;
        data.SecondaryAlbedoOffset = lightmapOffset;
        data.SecondaryAlbedoUV = 1;

        // Additive half: a damped, desaturated copy approximating the ambient fill Unity's baked
        // GI would provide (Resonite has no baked-GI pipeline of its own). Desaturated because the
        // raw lightmap carries each object's own local color cast (e.g. window-side cool,
        // lamp-side warm); adding that directly made differently-lit objects diverge in hue in a
        // way real GI light transport never does.
        data.SecondaryEmissiveMap = bakedLightmapGray;
        data.SecondaryEmissionMapScale = lightmapScale;
        data.SecondaryEmissionMapOffset = lightmapOffset;
        data.SecondaryEmissionMapUV = 1;
        var fill = Mathf.Clamp01(AdditiveFillStrength);
        data.SecondaryEmissiveColor = new Color(fill, fill, fill, 1f).ToColorX_sRGB();

        return PBS.Data;
    }
}

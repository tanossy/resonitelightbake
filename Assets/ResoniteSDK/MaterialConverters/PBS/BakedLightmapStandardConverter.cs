using FrooxEngine;
using UnityEngine;

// Converts the ResoniteSDK/BakedLightmapStandard marker shader (see BakedLightmapStandard.shader
// and LightmapMaterialCache) into PBS_MultiUV_Metallic, folding the baked lightmap into the
// SecondaryAlbedo slot (UV1) since Renderite has no custom-shader support and MeshConverter
// already forwards Unity's mesh.uv2 into Resonite's TexCoord1.
//
// This does NOT inherit from StandardBaseConverter<TWrapper, TMaterial>, because that base is
// constrained to `TMaterial : FrooxEngine.PBS_Material`, and PBS_MultiUV_Metallic instead derives
// from the separate PBS_MultiUV_Material hierarchy (different field set: AlbedoScale/AlbedoOffset
// per-channel instead of a single shared TextureScale/TextureOffset). The property-by-property
// mapping below otherwise follows the same conventions as StandardConverter/StandardBaseConverter
// (context.GetITexture2D for all textures, Color.ToColorX_sRGB() for colors, "_EMISSION" keyword
// gating for emissive).
//
// AlbedoColor/EmissiveColor here are routed through ColorGradingApproximation.Apply(...), for
// consistency with StandardBaseConverter's handling of non-lightmapped materials. Without it,
// Tonemap Compensation would have no effect on baked-lightmap materials, which make up most
// scenes using this converter.
[MaterialConverter(false, "ResoniteSDK/BakedLightmapStandard")]
public class BakedLightmapStandardConverter : ResoniteMaterialConverter
{
    // Controls the strength of an additive "ambient fill" layered on top of the multiplicative
    // SecondaryAlbedo lightmap blend, approximating the shadow-lifting effect Unity's baked GI
    // provides (0 = pure multiply, 1 = the old EmissiveLightmapMode=true full-additive behavior).
    // A pure multiply blend preserves color contrast correctly but can look too dark in shadowed
    // areas; a pure additive blend restores brightness but crushes contrast, since adding a fixed
    // value to a dark color shrinks its ratio to a bright color far more than to a light color
    // (e.g. a 9:1 reflectance ratio shrinks to about 2.5:1 after adding +0.3). Because of this,
    // additive fill is kept at 0 and brightness is instead boosted upstream via
    // LightmapDecoder.RangeScale, which preserves ratios by acting as a gain before the multiply.
    public static float AdditiveFillStrength = 0.0f;

    // Compensates for LightConverter.IntensityMultiplier increasing light brightness: with
    // brighter lights, the same Smoothness value produces proportionally stronger specular
    // highlights than it did in Unity (e.g. cloth-like materials can start looking like polished
    // metal). Applied as a multiplier on the scalar Smoothness value only - materials that use a
    // MetallicMap (where Smoothness comes from the texture's alpha channel instead) need an
    // equivalent attenuation baked into the texture itself when it's regenerated.
    public static float SmoothnessCompensation = 0.05f;

    // Metallic surfaces have almost no diffuse reflection, so nearly everything visible on them is
    // specular reflection - lowering Smoothness alone only makes that reflection duller, it never
    // stops it, and even a pure-black AlbedoColor keeps mirroring bright light sources. Smoothness
    // compensation alone therefore can't fully tame glare on high-Metallic materials, so Metallic
    // itself is also attenuated at send time via this multiplier.
    public static float MetallicCompensation = 0.0f;

    // Verification mode for large baked scenes. The first in-world check only needs geometry,
    // material colors, and the baked lightmap; uploading every source albedo/normal/metallic/
    // occlusion/emission texture can overwhelm ResoniteLink before anything appears in-world.
    // Defaults to false since the full texture upload path (including SecondaryAlbedoTexture/URL)
    // has been verified working.
    public static bool LightmapPreviewUploadOnly = false;

    public PBS_MultiUV_MetallicWrapper PBS;

    public override IAssetProvider<FrooxEngine.Material> UpdateConversion(UnityEngine.Material material, IConversionContext context)
    {
        if (PBS == null)
            PBS = gameObject.AddComponent<PBS_MultiUV_MetallicWrapper>();

        var data = PBS.Data;

        data.RenderQueue = material.renderQueue;

        // --- Alpha handling / culling (explicit) ---
        // LightmapMaterialCache only ever produces this marker material for Opaque-mode ("_Mode"
        // == 0) Standard materials (see LightmapMaterialCache's "_Mode != 0f" eligibility guard),
        // and the marker shader itself doesn't expose a per-material Cull property (Standard is
        // always back-face culled), so these three are hardcoded rather than derived from the
        // source material. AlphaClip still forwards _Cutoff in case a future v2 relaxes the
        // Opaque-only restriction, at which point AlphaHandling would need to switch based on
        // material.GetFloat("_Mode") again. Enum values verified against
        // BindingsGenerator/Resonite.UnitySDK.Bindings/Generated/Enums/FrooxEngine/FrooxEngine/AlphaHandling.cs
        // and .../Culling.cs (Opaque = 0, Back = 2).
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
        // Unity's Standard shader samples the normal/occlusion/emission maps using the same
        // uv_MainTex transform as albedo (only the detail albedo map gets its own UV2 transform),
        // so we mirror _MainTex's ScaleOffset here rather than leaving these at their zeroed
        // Sync<Vector2> default (which would otherwise collapse every sample to a single texel).
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
        // Written by LightmapMaterialCache from LightmapSettings.lightmaps[i].lightmapColor and
        // renderer.lightmapScaleOffset onto this marker material's _BakedLightmap / _BakedLightmapST.
        var bakedLightmap = context.GetITexture2D(material.GetTexture("_BakedLightmap"));
        // Desaturated (luma-only) companion, see LightmapMaterialCache/LightmapDecoder's
        // desaturate doc comments - used for the additive fill below so per-object baked-lightmap
        // hue (window=cool, lamp=warm) doesn't leak into the brightness-only approximation.
        //
        // Only fetched/uploaded when the additive fill is actually enabled - see
        // AdditiveFillStrength's doc comment and the matching guard in
        // LightmapMaterialCache.GetVariantOrOriginalInner. At AdditiveFillStrength=0 this texture
        // contributes nothing (SecondaryEmissiveColor below is pure black), so uploading it would
        // be pure network waste.
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

        // Additive half: a damped, desaturated copy of the same data, approximating the ambient
        // fill Unity's baked GI would otherwise provide (Resonite has no baked-GI pipeline of its
        // own). AdditiveFillStrength=0 recovers the old pure-multiply behavior; 1 recovers the old
        // pure-additive (EmissiveLightmapMode=true) behavior applied on top of the multiply.
        // Desaturated rather than full-color: the raw baked lightmap carries each object's own
        // local color (window-side=cool, lamp-side=warm), and adding that directly made
        // differently-lit objects (couch near the window vs. bed near the lamp) diverge in hue
        // from each other in a way Unity's actual GI light transport never does.
        data.SecondaryEmissiveMap = bakedLightmapGray;
        data.SecondaryEmissionMapScale = lightmapScale;
        data.SecondaryEmissionMapOffset = lightmapOffset;
        data.SecondaryEmissionMapUV = 1;
        var fill = Mathf.Clamp01(AdditiveFillStrength);
        data.SecondaryEmissiveColor = new Color(fill, fill, fill, 1f).ToColorX_sRGB();

        return PBS.Data;
    }
}

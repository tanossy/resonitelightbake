using FrooxEngine;
using UnityEngine;

// Heuristic converter: any Unity shader whose name contains "water" (case-insensitive)
// -> Resonite PBS_Metallic + native Panner2D UV scroll.
//
// There is no dedicated "water" material/shader type in Resonite; the community pattern
// (confirmed by live-world inspection) is a plain PBS_Metallic/PBS_Specular material with a
// Panner2D component scrolling its TextureOffset field over time. This converter targets
// custom water shaders (e.g. MomomaShader/Surface/ParallaxWater, "SeaWater") that have no
// dedicated converter of their own. Since these are arbitrary third-party shaders, only the
// properties guaranteed to exist on every Material (mainTexture/mainTextureScale/
// mainTextureOffset, "_Color") are read unconditionally; everything else (_BumpMap/_BumpScale/
// _Metallic/_Glossiness) is read behind HasProperty(...) since most water shaders don't follow
// the Standard-shader naming convention.
//
// Panner2D setup mirrors Assets/ResoniteSDK/MaterialConverters/Custom/TestPanningConverter.cs.
[MaterialConverter(true)]
public class WaterPanningConverter : ResoniteMaterialConverter
{
    public FrooxEngine.PBS_MetallicWrapper PBS;
    public FrooxEngine.Panner2DWrapper Panner;

    // Default UV scroll speed (UV units/second): none of the matched water shaders expose a
    // readable flow-speed property, so this is a fixed, deliberately gentle fallback. Prefer
    // reading a shader-specific speed property via HasProperty if one is ever found.
    private static readonly Vector2 DefaultPanningSpeed = new Vector2(0.02f, 0.015f);

    public static float? EvaluateHeuristicConversion(UnityEngine.Material material)
    {
        if (material == null || material.shader == null)
            return null;

        if (material.shader.name.IndexOf("water", System.StringComparison.OrdinalIgnoreCase) < 0)
            return null;

        return 0;
    }

    public override IAssetProvider<FrooxEngine.Material> UpdateConversion(UnityEngine.Material material, IConversionContext context)
    {
        if (PBS == null)
            PBS = gameObject.AddComponent<FrooxEngine.PBS_MetallicWrapper>();

        if (Panner == null)
            Panner = gameObject.AddComponent<FrooxEngine.Panner2DWrapper>();

        var data = PBS.Data;
        var pannerData = Panner.Data;

        data.RenderQueue = material.renderQueue;

        // Always present on any UnityEngine.Material, regardless of shader.
        data.AlbedoTexture = context.GetITexture2D(material.mainTexture);
        data.TextureScale = material.mainTextureScale;
        data.AlbedoColor = GetColorOrDefault(material, "_Color", Color.white).ToColorX_sRGB();

        // Shader-specific - guarded, since these are arbitrary custom water shaders that are
        // not guaranteed to use the Standard-shader property naming convention.
        data.NormalMap = context.GetITexture2D(GetTextureOrDefault(material, "_BumpMap"));
        data.NormalScale = GetFloatOrDefault(material, "_BumpScale", 1f);

        // Fall back to a high metallic/smoothness default so water reads as wet/reflective
        // even when the shader exposes no _Metallic/_Glossiness of its own.
        data.Metallic = GetFloatOrDefault(material, "_Metallic", 0.8f);
        data.Smoothness = GetFloatOrDefault(material, "_Glossiness", 0.95f);

        // Panner2D drives the PBS material's TextureOffset field to scroll its UVs over time.
        pannerData.Enabled = true;
        pannerData.persistent = true;
        pannerData._target = data.TextureOffset_Element.Member;
        pannerData._speed = DefaultPanningSpeed;

        // Carry over whatever static texture offset the source material already had.
        pannerData._offset = material.mainTextureOffset;

        // We don't need it to repeat & wrap around.
        pannerData._repeat = new Vector2(float.PositiveInfinity, float.PositiveInfinity);

        return data;
    }

    private static float GetFloatOrDefault(UnityEngine.Material material, string property, float fallback = 0f)
    {
        if (material == null || !material.HasProperty(property))
            return fallback;

        return material.GetFloat(property);
    }

    private static Color GetColorOrDefault(UnityEngine.Material material, string property, Color fallback)
    {
        if (material == null || !material.HasProperty(property))
            return fallback;

        return material.GetColor(property);
    }

    private static Texture GetTextureOrDefault(UnityEngine.Material material, string property)
    {
        if (material == null || !material.HasProperty(property))
            return null;

        return material.GetTexture(property);
    }
}

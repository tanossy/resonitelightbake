using UnityEngine;

public static class LightHelper
{
    public static void SetFrom(this FrooxEngine.Light resonite, UnityEngine.Light unity, IConversionContext context)
    {
        resonite.SetFrom((UnityEngine.Behaviour)unity);

        switch (unity.type)
        {
            case UnityEngine.LightType.Point:
                resonite.LightType = Renderite.Shared.LightType.Point;
                break;

            case UnityEngine.LightType.Spot:
                resonite.LightType = Renderite.Shared.LightType.Spot;
                break;

            case UnityEngine.LightType.Directional:
                resonite.LightType = Renderite.Shared.LightType.Directional;
                break;

            default:
                // Not supported, set it to invalid value
                resonite.LightType = (Renderite.Shared.LightType)(255);
                break;
        }

        // Send-time intensity tuning is factored out to LightTuning.cs (same folder). A white
        // balance shift used to live there too, blending color toward white on send; it was
        // removed, so color now passes through unchanged.
        resonite.Intensity = LightTuning.ApplyIntensity(unity.intensity);
        resonite.Color = new ColorX(unity.color);

        switch (unity.shadows)
        {
            case UnityEngine.LightShadows.None:
                resonite.ShadowType = Renderite.Shared.ShadowType.None;
                break;

            case UnityEngine.LightShadows.Hard:
                resonite.ShadowType = Renderite.Shared.ShadowType.Hard;
                break;

            case UnityEngine.LightShadows.Soft:
                resonite.ShadowType = Renderite.Shared.ShadowType.Soft;
                break;
        }

        resonite.ShadowStrength = unity.shadowStrength;
        resonite.ShadowNearPlane = unity.shadowNearPlane;
        resonite.ShadowMapResolution = unity.shadowCustomResolution;
        resonite.ShadowBias = unity.shadowBias;
        resonite.ShadowNormalBias = unity.shadowNormalBias;

        resonite.Range = unity.range;
        resonite.SpotAngle = unity.spotAngle;

        resonite.Cookie = context.GetITexture(unity.cookie);
    }
}

public class LightConverter : ResoniteSingleComponentConverter<Light, FrooxEngine.LightWrapper>
{
    protected override void UpdateConversion(Light target, IConversionContext context)
    {
        Binding.Data.SetFrom(target, context);
    }
}

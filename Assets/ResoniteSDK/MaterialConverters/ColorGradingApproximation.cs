using UnityEngine;

// Resonite's PostProcessingSettings component only exposes Bloom/AO/MotionBlur/SSR/Antialiasing -
// no Color Grading equivalent - so Unity's color grading/tonemap can't be reproduced as a screen
// effect. Instead it's approximated by baking it into each material's Albedo/Emissive color at
// conversion time. The actual math lives in PPv2ToneMapMath.cs; this is a thin call-site wrapper so
// StandardBaseConverter.cs / BakedLightmapStandardConverter.cs don't need to change if the approach
// evolves.
//
// Only Saturation is applied (via ApplySaturationOnly) rather than the full grading pipeline - see
// that method's comment for why. Reflection Probe intensity correction is separate and controlled
// solely by ToneMapCompensationState.Enabled (the "Send Tonemap Compensation" toggle in the Resonite
// SDK Manager panel), which also gates MaterialGradingEnabled below.
public static class ColorGradingApproximation
{
    public static bool MaterialGradingEnabled = true;

    public static Color Apply(Color linearColor)
    {
        if (!ToneMapCompensationState.Enabled || !MaterialGradingEnabled)
            return linearColor;

        var c = new Vector3(linearColor.r, linearColor.g, linearColor.b);
        var graded = PPv2ToneMapMath.ApplySaturationOnly(c);

        return new Color(graded.x, graded.y, graded.z, linearColor.a);
    }
}

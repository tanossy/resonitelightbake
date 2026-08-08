using UnityEngine;

// The PostProcessingSettings component on the Resonite side only has 5 fields — Bloom/AO/MotionBlur/
// SSR/Antialiasing — with no equivalent for Color Grading (Contrast/Saturation/Tonemapping), so it
// can't be reproduced as a screen effect. Instead, Unity's color grading/tonemap effect is approximated
// by baking it into each material's Albedo/Emissive color at conversion time.
//
// The actual math (WhiteBalance, Contrast in LogC space, Saturation, NeutralTonemap) lives in
// Assets/ResoniteSDK/ToneMapCompensation/PPv2ToneMapMath.cs (kept independent of the main SDK); this
// file is a thin wrapper acting as the call site, so existing call sites in StandardBaseConverter.cs /
// BakedLightmapStandardConverter.cs don't need to change. Setting
// ToneMapCompensationState.Enabled = false disables the approximation entirely (the "Send Tonemap
// Compensation" toggle in the Resonite SDK Manager panel).
//
// Materials in typical scenes are often `_Color = white (1,1,1)` with the actual color coming from the
// texture. Applying the full grading pipeline (including Contrast) pushes values above the near-white
// pivot even further toward white, which washes out the texture's original colors — so material color
// grading currently applies Saturation only (PPv2ToneMapMath.ApplySaturationOnly()), preserving the
// scene's saturation setting without the Contrast/WhiteBalance/Tonemap side effects. Reflection Probe
// intensity correction is unaffected by this and remains controlled solely by
// ToneMapCompensationState.Enabled.
//
// To revert to full grading, replace the body of Apply() below with either `return linearColor;`
// (equivalent to disabling material grading) or `PPv2ToneMapMath.ApplyGrading(c)` (full application).
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

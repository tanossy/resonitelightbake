using UnityEngine;

// Send-time light tuning, factored out of the official LightConverter.cs so that file's only
// change is swapping in two lines:
// `resonite.Intensity = LightTuning.ApplyIntensity(unity.intensity);` /
// `resonite.Color = new ColorX(LightTuning.ApplyColor(unity.color));`
public static class LightTuning
{
    // Compensates for a live, measured brightness gap: the same Light.intensity value that
    // looks correctly lit in the Unity Editor (combined with baked indirect lighting) renders
    // noticeably darker once sent through unchanged, most likely due to differences between
    // Unity and Resonite in Point/Spot Light falloff curves and luminance conversion. Values
    // much above ~2 start over-brightening specular-dominant materials (TV screens, metal,
    // glass) - those surfaces have almost no diffuse reflection, so darkening Albedo can't
    // compensate, and a high-Metallic surface that reads as black in Unity can end up glowing
    // in Resonite instead. 1.8 is the current live-tuned value.
    public static float IntensityMultiplier = 1.8f;

    // Blends each light's color toward white at send time (0 = unchanged, 1 = pure white),
    // compensating for a warm-toned baked scene reading noticeably more yellow in Resonite than
    // in the Unity Editor. 0.7 is the current live-tuned value.
    //
    // Known limitation: this Lerps every light uniformly toward white regardless of its
    // original hue, so a scene with multiple differently-colored lights would have those color
    // differences diluted equally. Fine for a single-color-scheme scene; a hue-aware version is
    // a natural follow-up if that ever matters.
    public static float WhiteBalanceShift = 0.7f;

    public static float ApplyIntensity(float unityIntensity) => unityIntensity * IntensityMultiplier;

    public static Color ApplyColor(Color unityColor) => Color.Lerp(unityColor, Color.white, Mathf.Clamp01(WhiteBalanceShift));
}

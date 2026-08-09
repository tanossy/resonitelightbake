using UnityEngine;

// Send-time light tuning, factored out of the official LightConverter.cs so that file's only
// change is swapping in two lines:
// `resonite.Intensity = LightTuning.ApplyIntensity(unity.intensity);` /
// `resonite.Color = new ColorX(LightTuning.ApplyColor(unity.color));`
public static class LightTuning
{
    // A fixed multiplier here (this file's earlier design) was tuned for one scene and badly
    // overexposed a different scene sent later, whose native light intensities were on a
    // completely different scale (a fixed multiplier tuned for lights around ~0.5 turned a
    // different scene's Directional Light of 5.0 into 9.0, blown out). Fixed multipliers don't
    // generalize across scenes with different native brightness scales.
    //
    // Replaced with a self-normalizing ceiling: every send, the scene's single brightest Light
    // is found, and the effective multiplier is computed so THAT light lands exactly at
    // IntensityCeiling; every other light in the scene is scaled by that same ratio (relative
    // brightness between lights within one scene is preserved, only the overall scale adapts
    // per scene). 0.9 is the current live-tuned starting value.
    //
    // Known limitation: this only bounds the single brightest light, not the cumulative
    // brightness of many lights added together - a scene with dozens of moderate-intensity
    // fill lights, each individually under the ceiling, can still sum to an overexposed result.
    public static float IntensityCeiling = 0.9f;

    // Blends each light's color toward white at send time (0 = unchanged, 1 = pure white),
    // compensating for a warm-toned baked scene reading noticeably more yellow in Resonite than
    // in the Unity Editor. 0.7 is the current live-tuned value.
    //
    // Known limitation: this Lerps every light uniformly toward white regardless of its
    // original hue, so a scene with multiple differently-colored lights would have those color
    // differences diluted equally. Fine for a single-color-scheme scene; a hue-aware version is
    // a natural follow-up if that ever matters.
    public static float WhiteBalanceShift = 0.7f;

    public static float ApplyIntensity(float unityIntensity) => unityIntensity * GetEffectiveIntensityMultiplier();

    public static Color ApplyColor(Color unityColor) => Color.Lerp(unityColor, Color.white, Mathf.Clamp01(WhiteBalanceShift));

    // Recomputed from the live scene on every call rather than cached: this only ever runs
    // during an Editor-time scene conversion (never per-frame/runtime), so even a few hundred
    // lights costs nothing worth caching for, and a fresh scan avoids any risk of a stale
    // cached max surviving a scene edit between sends.
    static float GetEffectiveIntensityMultiplier()
    {
        float sceneMax = GetSceneMaxLightIntensity();

        // No positive-intensity lights found (empty scene, or every light at 0) - nothing to
        // scale against. Pass through unchanged rather than dividing by zero.
        if (sceneMax <= 0f)
            return 1f;

        return IntensityCeiling / sceneMax;
    }

    static float GetSceneMaxLightIntensity()
    {
        float max = 0f;

        foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
        {
            if (light != null && light.intensity > max)
                max = light.intensity;
        }

        return max;
    }
}

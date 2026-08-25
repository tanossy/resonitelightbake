using UnityEngine;

// Send-time light tuning, factored out of the official LightConverter.cs so that file's only
// change is swapping in one line: `resonite.Intensity = LightTuning.ApplyIntensity(unity.intensity);`
public static class LightTuning
{
    // Self-normalizing ceiling instead of a fixed multiplier: every send, the scene's single
    // brightest Light is found, and the effective multiplier is computed so THAT light lands
    // exactly at IntensityCeiling; every other light is scaled by the same ratio, preserving
    // relative brightness between lights while adapting the overall scale per scene. A fixed
    // multiplier doesn't generalize across scenes with different native brightness scales.
    //
    // Known limitation: this only bounds the single brightest light, not cumulative brightness -
    // many moderate-intensity fill lights, each individually under the ceiling, can still sum to
    // an overexposed result.
    public static float IntensityCeiling = 0.9f;

    // A WhiteBalanceShift field and ApplyColor() method used to live here, blending each light's
    // color toward white at send time. It went through several rounds of tuning but was removed
    // as unnecessary; light color now passes through unchanged, same as before that feature
    // existed.

    public static float ApplyIntensity(float unityIntensity) => unityIntensity * GetEffectiveIntensityMultiplier();

    // Recomputed from the live scene on every call rather than cached: this only runs during
    // Editor-time scene conversion (never per-frame), so a fresh scan is cheap and avoids a stale
    // cached max surviving a scene edit between sends.
    static float GetEffectiveIntensityMultiplier()
    {
        float sceneMax = GetSceneMaxLightIntensity();

        // No positive-intensity lights found - nothing to scale against.
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

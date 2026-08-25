using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

// Converts a Unity "Post Processing Stack v2" (com.unity.postprocessing) Global Volume into
// Resonite's FrooxEngine.PostProcessingSettings component.
//
// Resonite's PostProcessingSettings only exposes 5 fields (MotionBlurIntensity, BloomIntensity,
// AmbientOcclusionIntensity, ScreenSpaceReflections, Antialiasing) - there is no equivalent for
// Color Grading/Vignette/Depth of Field/Exposure, so those PPv2 effects are not converted.
//
// Mapping quirks:
//  - PPv2's MotionBlur has no "intensity", only `shutterAngle` (0-360); approximated as
//    shutterAngle / 360f. Not a 1:1 mapping.
//  - ScreenSpaceReflections maps to whether the SSR effect is enabled in the profile, since PPv2
//    has no separate "amount" dial and Resonite's field is boolean.
//  - Antialiasing is configured on `PostProcessLayer` (camera-side), not the volume/profile - the
//    first PostProcessLayer found scene-wide is used; Off if none exists.
//  - Only Global volumes convert to real PostProcessingSettings data (Resonite has no local/
//    collider-bound post-processing concept). If multiple Global volumes exist, the first one
//    encountered wins and the rest are warned about and left at engine defaults. Local
//    (isGlobal == false) volumes are out of scope and also left at defaults.
//
// UNVERIFIED: whether the PostProcessingSettings slot needs to sit under a specific parent (Root/
// UserRoot) to apply world-wide has not been checked against a running client - verify in-world
// before relying on it.
public static class PostProcessingSettingsHelper
{
    public static void SetFrom(this FrooxEngine.PostProcessingSettings resonite, PostProcessVolume unity, IConversionContext context)
    {
        var profile = unity.sharedProfile;

        if (profile == null)
        {
            Debug.LogWarning($"[PostProcessingConverter] Global Post Process Volume '{unity.name}' has no profile assigned, skipping PostProcessingSettings conversion.");
            return;
        }

        // Bloom
        if (profile.TryGetSettings<Bloom>(out var bloom) && bloom.enabled.value)
            resonite.BloomIntensity = bloom.intensity.value;
        else
            resonite.BloomIntensity = 0f;

        // Ambient occlusion
        if (profile.TryGetSettings<AmbientOcclusion>(out var ao) && ao.enabled.value)
            resonite.AmbientOcclusionIntensity = ao.intensity.value;
        else
            resonite.AmbientOcclusionIntensity = 0f;

        // Motion blur - approximated from shutterAngle (see file header).
        if (profile.TryGetSettings<MotionBlur>(out var motionBlur) && motionBlur.enabled.value)
            resonite.MotionBlurIntensity = Mathf.Clamp01(motionBlur.shutterAngle.value / 360f);
        else
            resonite.MotionBlurIntensity = 0f;

        resonite.ScreenSpaceReflections = profile.TryGetSettings<ScreenSpaceReflections>(out var ssr) && ssr.enabled.value;

        // Antialiasing lives on PostProcessLayer (camera-side), not on the volume/profile.
        resonite.Antialiasing = ResolveAntialiasing();
    }

    static Renderite.Shared.AntiAliasingMethod ResolveAntialiasing()
    {
        // Picks the first PostProcessLayer found scene-wide; an arbitrary pick if multiple
        // cameras use different antialiasing modes (accepted simplification).
#if UNITY_2023_1_OR_NEWER
        var layer = Object.FindFirstObjectByType<PostProcessLayer>();
#else
        var layer = Object.FindObjectOfType<PostProcessLayer>();
#endif

        if (layer == null)
            return Renderite.Shared.AntiAliasingMethod.Off;

        switch (layer.antialiasingMode)
        {
            case PostProcessLayer.Antialiasing.None:
                return Renderite.Shared.AntiAliasingMethod.Off;

            case PostProcessLayer.Antialiasing.FastApproximateAntialiasing:
                return Renderite.Shared.AntiAliasingMethod.FXAA;

            case PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing:
                return Renderite.Shared.AntiAliasingMethod.SMAA;

            case PostProcessLayer.Antialiasing.TemporalAntialiasing:
                return Renderite.Shared.AntiAliasingMethod.TAA;

            default:
                // Resonite's CTAA has no Unity/PPv2 equivalent, so it's unreachable here.
                return Renderite.Shared.AntiAliasingMethod.Off;
        }
    }
}

public class PostProcessingConverter : ResoniteSingleComponentConverter<PostProcessVolume, FrooxEngine.PostProcessingSettingsWrapper>
{
    // Tracks which Global volume "won" the first-wins policy. Unity's overloaded `== null` check
    // detects destroyed volumes, so a stale claim is released naturally when its volume goes away.
    static PostProcessVolume s_claimedGlobalVolume;

    // Avoids re-warning on every UpdateConversion pass (e.g. Realtime Mode) for the same volume.
    static readonly System.Collections.Generic.HashSet<PostProcessVolume> s_warnedVolumes = new System.Collections.Generic.HashSet<PostProcessVolume>();

    protected override void UpdateConversion(PostProcessVolume target, IConversionContext context)
    {
        // Local/collider-bound volumes are out of scope - left at engine defaults.
        if (!target.isGlobal)
            return;

        if (s_claimedGlobalVolume != null && s_claimedGlobalVolume != target)
        {
            if (s_warnedVolumes.Add(target))
                Debug.LogWarning($"[PostProcessingConverter] Multiple Global Post Process Volumes found in the scene. " +
                    $"'{s_claimedGlobalVolume.name}' was already converted as the world-wide PostProcessingSettings; " +
                    $"'{target.name}' will be skipped (first-wins policy). Resonite has no concept of multiple " +
                    $"world-wide post processing configs.");
            return;
        }

        s_claimedGlobalVolume = target;

        Binding.Data.SetFrom(target, context);
    }
}

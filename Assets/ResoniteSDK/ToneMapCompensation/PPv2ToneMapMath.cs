using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

// Resonite (Renderite) applies no ColorGrading-equivalent post-processing on the main camera
// (confirmed in Renderite.Unity.Renderer: CameraPostprocessingManager.cs skips ColorGrading items on
// the primary camera), so Unity's PPv2 color grading/tonemap never reaches the Resonite side and
// highlights come through raw/harsh. There's no ResoniteLink ImportTexture3D (only
// ImportTexture2D/ImportCubemap/ImportMesh/ImportAudioClip), so baking the tonemap curve into a 3D
// LUT and applying it via Resonite's LUT Material isn't possible. Instead this reproduces Unity's
// PPv2 formulas directly, applied separately to material colors (ColorGradingApproximation) and
// Reflection Probe intensity (ReflectionProbeConverter) as an approximation.
//
// Formulas/constants are taken directly from the com.unity.postprocessing@3.4.0 package:
//   - PostProcessing/Shaders/Builtins/Lut3DBaker.compute (pipeline order)
//   - PostProcessing/Shaders/Colors.hlsl (WhiteBalance / LogC / Contrast / Saturation / NeutralTonemap)
//   - PostProcessing/Runtime/Utils/ColorUtilities.cs (temperature/tint -> _ColorBalance)
//   - PostProcessing/Runtime/Effects/ColorGrading.cs (UI parameter -> scale conversion)
//
// Coverage: only gradingMode=HighDefinitionRange with tonemapper=None/Neutral is implemented. ACES
// uses an entirely different ACEScc/ACEScg pipeline and is not supported (tonemap stage is skipped;
// only Contrast/Saturation/WhiteBalance apply). ChannelMixer, Lift/Gamma/Gain, and the Hue-related
// curves (HueVsHue/SatVsSat/LumVsSat) are treated as identity transforms - scenes that actually edit
// these will see reduced approximation accuracy.
public static class PPv2ToneMapMath
{
    // --- LogC (equivalent to ARRI LogC; matches the ParamsLogC constants in Colors.hlsl exactly) ---
    const float LogC_cut = 0.011361f;
    const float LogC_a = 5.555556f;
    const float LogC_b = 0.047996f;
    const float LogC_c = 0.244161f;
    const float LogC_d = 0.386036f;
    const float LogC_e = 5.301883f;
    const float LogC_f = 0.092819f;

    // Contrast pivot, exactly the constant (from ACES.hlsl) used by LogGrade() in Lut3DBaker.compute.
    // Note this is a pivot in LogC space, not 0.5 in linear space.
    const float ACEScc_MIDGRAY = 0.4135884f;

    static readonly Matrix4x4 LIN_2_LMS_MAT = MakeMatrix(
        3.90405e-1f, 5.49941e-1f, 8.92632e-3f,
        7.08416e-2f, 9.63172e-1f, 1.35775e-3f,
        2.31082e-2f, 1.28021e-1f, 9.36245e-1f);

    static readonly Matrix4x4 LMS_2_LIN_MAT = MakeMatrix(
        2.85847e+0f, -1.62879e+0f, -2.48910e-2f,
        -2.10182e-1f, 1.15820e+0f, 3.24281e-4f,
        -4.18120e-2f, -1.18169e-1f, 1.06867e+0f);

    static Matrix4x4 MakeMatrix(
        float m00, float m01, float m02,
        float m10, float m11, float m12,
        float m20, float m21, float m22)
    {
        var m = Matrix4x4.identity;
        m.m00 = m00; m.m01 = m01; m.m02 = m02;
        m.m10 = m10; m.m11 = m11; m.m12 = m12;
        m.m20 = m20; m.m21 = m21; m.m22 = m22;
        return m;
    }

    static Vector3 MulMat(Matrix4x4 m, Vector3 v) => new Vector3(
        m.m00 * v.x + m.m01 * v.y + m.m02 * v.z,
        m.m10 * v.x + m.m11 * v.y + m.m12 * v.z,
        m.m20 * v.x + m.m21 * v.y + m.m22 * v.z);

    public struct ResolvedSettings
    {
        public bool Found;
        public bool TonemapperSupported; // false = ACES or other unsupported tonemapper
        public bool ApplyTonemap;

        public Vector3 ColorBalance; // Coefficient passed to WhiteBalance() (in CAT02 LMS space)
        public Vector3 ColorFilter;
        public float ContrastMultiplier;
        public float SaturationMultiplier;
        public float HueShift01; // 0..1 (360 degrees normalized to 1)
    }

    static bool s_resolved;
    static ResolvedSettings s_settings;

    // PPv2's contrast slider maps to `value/100+1`, close to a no-op at typical low slider values -
    // much of Unity's visual "punch" actually comes from effects Resonite has no equivalent for (AO,
    // Bloom, etc.), so mirroring the slider alone undershoots the look. This is an extra
    // Resonite-only coefficient multiplied into ContrastMultiplier. 1.0 = no extra effect; 1.4 is the
    // current tuned value.
    public static float ResoniteExtraContrast = 1.4f;

    public static ResolvedSettings GetSettings()
    {
        if (!s_resolved)
            Resolve();

        return s_settings;
    }

    public static void Invalidate() => s_resolved = false;

    static void Resolve()
    {
        s_resolved = true;
        s_settings = default;

#if UNITY_2023_1_OR_NEWER
        var volumes = Object.FindObjectsByType<PostProcessVolume>(FindObjectsSortMode.None);
#else
        var volumes = Object.FindObjectsOfType<PostProcessVolume>();
#endif

        foreach (var volume in volumes)
        {
            if (!volume.isGlobal || volume.sharedProfile == null)
                continue;

            if (!volume.sharedProfile.TryGetSettings<ColorGrading>(out var cg) || !cg.enabled.value)
                continue;

            s_settings.Found = true;

            s_settings.ContrastMultiplier = (cg.contrast.value / 100f + 1f) * ResoniteExtraContrast;
            s_settings.SaturationMultiplier = cg.saturation.value / 100f + 1f;
            s_settings.HueShift01 = cg.hueShift.value / 360f;
            s_settings.ColorFilter = new Vector3(cg.colorFilter.value.r, cg.colorFilter.value.g, cg.colorFilter.value.b);
            s_settings.ColorBalance = ComputeColorBalance(cg.temperature.value, cg.tint.value);

            s_settings.TonemapperSupported = cg.tonemapper.value == Tonemapper.Neutral || cg.tonemapper.value == Tonemapper.None;
            s_settings.ApplyTonemap = cg.tonemapper.value == Tonemapper.Neutral;

            break; // Use the first global volume found (same policy as PostProcessingConverter)
        }
    }

    // Direct port of ColorUtilities.ComputeColorBalance() (no guessing).
    static Vector3 ComputeColorBalance(float temperature, float tint)
    {
        float t1 = temperature / 60f;
        float t2 = tint / 60f;

        float x = 0.31271f - t1 * (t1 < 0f ? 0.1f : 0.05f);
        float y = StandardIlluminantY(x) + t2 * 0.05f;

        var w1 = new Vector3(0.949237f, 1.03542f, 1.08728f);
        var w2 = CIExyToLMS(x, y);
        return new Vector3(w1.x / w2.x, w1.y / w2.y, w1.z / w2.z);
    }

    static float StandardIlluminantY(float x) => 2.87f * x - 3f * x * x - 0.27509507f;

    static Vector3 CIExyToLMS(float x, float y)
    {
        float Y = 1f;
        float X = Y * x / y;
        float Z = Y * (1f - x - y) / y;

        float L = 0.7328f * X + 0.4296f * Y - 0.1624f * Z;
        float M = -0.7036f * X + 1.6975f * Y + 0.0061f * Z;
        float S = 0.0030f * X + 0.0136f * Y + 0.9834f * Z;

        return new Vector3(L, M, S);
    }

    static Vector3 WhiteBalance(Vector3 c, Vector3 balance)
    {
        var lms = MulMat(LIN_2_LMS_MAT, c);
        lms = Vector3.Scale(lms, balance);
        return MulMat(LMS_2_LIN_MAT, lms);
    }

    static Vector3 LinearToLogC(Vector3 x) => new Vector3(
        LogC_c * Mathf.Log10(LogC_a * x.x + LogC_b) + LogC_d,
        LogC_c * Mathf.Log10(LogC_a * x.y + LogC_b) + LogC_d,
        LogC_c * Mathf.Log10(LogC_a * x.z + LogC_b) + LogC_d);

    static Vector3 LogCToLinear(Vector3 x) => new Vector3(
        (Mathf.Pow(10f, (x.x - LogC_d) / LogC_c) - LogC_b) / LogC_a,
        (Mathf.Pow(10f, (x.y - LogC_d) / LogC_c) - LogC_b) / LogC_a,
        (Mathf.Pow(10f, (x.z - LogC_d) / LogC_c) - LogC_b) / LogC_a);

    static float Luminance(Vector3 c) => Vector3.Dot(c, new Vector3(0.2126729f, 0.7151522f, 0.0721750f));

    static Vector3 Contrast(Vector3 c, float midpoint, float contrast) =>
        (c - new Vector3(midpoint, midpoint, midpoint)) * contrast + new Vector3(midpoint, midpoint, midpoint);

    static Vector3 Saturation(Vector3 c, float sat)
    {
        float luma = Luminance(c);
        return new Vector3(luma, luma, luma) + sat * (c - new Vector3(luma, luma, luma));
    }

    // Direct port of NeutralCurve()/NeutralTonemap() from Colors.hlsl (including constants, no guessing).
    static float NeutralCurveScalar(float x, float a, float b, float c, float d, float e, float f) =>
        ((x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f)) - e / f;

    static Vector3 NeutralCurve(Vector3 x, float a, float b, float c, float d, float e, float f) => new Vector3(
        NeutralCurveScalar(x.x, a, b, c, d, e, f),
        NeutralCurveScalar(x.y, a, b, c, d, e, f),
        NeutralCurveScalar(x.z, a, b, c, d, e, f));

    public static Vector3 NeutralTonemap(Vector3 x)
    {
        const float a = 0.2f, b = 0.29f, c = 0.24f, d = 0.272f, e = 0.02f, f = 0.3f;
        const float whiteLevel = 5.3f;
        const float whiteClip = 1.0f;

        float whiteScaleScalar = 1f / NeutralCurveScalar(whiteLevel, a, b, c, d, e, f);
        var whiteScale = new Vector3(whiteScaleScalar, whiteScaleScalar, whiteScaleScalar);

        x = NeutralCurve(Vector3.Scale(x, whiteScale), a, b, c, d, e, f);
        x = Vector3.Scale(x, whiteScale);
        x /= whiteClip;

        return x;
    }

    // Ports Lut3DBaker.compute's pipeline order: LogGrade -> decode -> LinearGrade -> Tonemap.
    // Input/output are linear-space colors.
    public static Vector3 ApplyGrading(Vector3 linearColor)
    {
        var settings = GetSettings();

        if (!settings.Found)
            return linearColor;

        // LogGrade: apply contrast in LogC space
        var logSpace = LinearToLogC(linearColor);
        logSpace = Contrast(logSpace, ACEScc_MIDGRAY, settings.ContrastMultiplier);

        // Convert back to linear space and apply WhiteBalance, ColorFilter, and Saturation
        var c = LogCToLinear(logSpace);
        c = WhiteBalance(c, settings.ColorBalance);
        c = Vector3.Scale(c, settings.ColorFilter);
        c = new Vector3(Mathf.Max(0f, c.x), Mathf.Max(0f, c.y), Mathf.Max(0f, c.z));
        c = Saturation(c, settings.SaturationMultiplier);

        if (settings.ApplyTonemap)
            c = NeutralTonemap(c);

        return new Vector3(Mathf.Max(0f, c.x), Mathf.Max(0f, c.y), Mathf.Max(0f, c.z));
    }

    // Full ApplyGrading() would push near-white materials (_Color = white, common when the real color
    // comes from a texture) even further toward white as ContrastMultiplier rises above the
    // ACEScc_MIDGRAY pivot, washing out the texture. This applies Saturation only, preserving the
    // scene's saturation setting without Contrast/WhiteBalance/Tonemap.
    public static Vector3 ApplySaturationOnly(Vector3 linearColor)
    {
        var settings = GetSettings();

        if (!settings.Found)
            return linearColor;

        return Saturation(linearColor, settings.SaturationMultiplier);
    }

    // Approximates Reflection Probe intensity attenuation using the actual NeutralTonemap
    // compression ratio at a representative brightness (linear 1.0, i.e. "a moderately bright
    // reflection"). A single scalar can't reproduce the full nonlinear curve, but this stays grounded
    // in the real formula rather than skipping correction entirely.
    public static float ComputeReflectionProbeCompensationFactor()
    {
        var settings = GetSettings();

        if (!settings.Found || !settings.ApplyTonemap)
            return 1f;

        const float representativeBrightness = 1.0f;
        var input = new Vector3(representativeBrightness, representativeBrightness, representativeBrightness);
        var toneMapped = NeutralTonemap(input);

        float ratio = Luminance(toneMapped) / representativeBrightness;

        return Mathf.Clamp(ratio, 0.05f, 1f);
    }
}

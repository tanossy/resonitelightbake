using FrooxEngine;
using UnityEngine;

// Unity AudioReverbFilter (+ optional AudioEchoFilter) -> Resonite AudioZitaReverb.
//
// UNVERIFIED: this converter has not been checked against a running Resonite client. It is
// unconfirmed whether AudioZitaReverb works at all when attached standalone to the same Slot
// as an AudioOutput (as opposed to being routed through an AudioListener/spatial reverb zone).
//
// Resonite exposes only one DSP effect component, AudioZitaReverb - there is no Resonite
// equivalent for low-pass/high-pass/distortion/chorus/compressor/plain-delay-echo, and the
// legacy AudioReverbZone only works on non-spatialized audio, so neither is used here.
// AudioZitaReverb's field names (InDelay/Crossover/RT60Low/RT60Mid/HighFrequencyDamping/
// EQ1*/EQ2*/Mix/Level) line up 1:1 with the open-source "Zita-Rev1" algorithm by Fons
// Adriaensen (delay/xover/rtlow/rtmid/fdamp/eq1/eq2/mix/level); the mapping below leans on
// that algorithm's publicly documented semantics (delay=ms pre-delay, xover=Hz low/mid
// crossover, rtlow/rtmid=seconds decay per band, fdamp=Hz threshold above which decay
// shortens, mix=0..1 dry/wet, level=dB output gain) since this can't be checked against
// Resonite's own source - treat it as a well-reasoned guess, not fact.
//
// Unity's AudioReverbFilter mirrors the classic EAX/I3DL2 parameter set (values/ranges
// confirmed by decompiling UnityEngine.AudioModule.dll): dryLevel/room/roomHF/roomLF (mB),
// decayTime (sec, explicitly "at LOW frequencies" per Unity's own doc), decayHFRatio (ratio
// of HF decay to decayTime), reflectionsLevel/reflectionsDelay (early reflections, no Zita
// equivalent), reverbLevel/reverbDelay (late reverb level/delay), hfReference/lfReference (Hz
// reference points), diffusion/density (%, no Zita equivalent), reverbPreset (enum).
// NOTE: Unity's compiled doc string for `reflectionsDelay` is a copy-paste of `reverbLevel`'s
// (says "level in millibels" for a property named "*Delay") - treated as a seconds value here
// per every other I3DL2 implementation, though it ends up unused since Zita has no
// early-reflections stage.
// NOTE: when `reverbPreset != User`, it's unconfirmed whether reading the individual
// properties back via the Editor-side C# API returns the resolved preset values or stale
// defaults - a warning is logged so results can be spot-checked, or set the preset to `User`
// before converting to guarantee accurate reads.
//
// Resonite-side presets (PresetUnderwater etc.) cannot be applied at Editor conversion time:
// both the generated binding and an independent ilspycmd decompile of AudioZitaReverb show
// every "PresetXxx" member is a runtime RPC method requiring a connected/synced session, not
// a settable field - there are only 11 float Sync fields on the type (the same 11 driven by
// SetFrom() below) and no preset-selector field at all. This is a hard platform limitation. If
// a future Resonite version adds one, apply it in SetFrom() gated on
// `unityReverb.reverbPreset == AudioReverbPreset.Underwater`.

public static class AudioEffectHelper
{
    /// <summary>
    /// Best-effort mapping from Unity's AudioReverbFilter (and, optionally, a sibling
    /// AudioEchoFilter) to a Resonite AudioZitaReverb. See file header for the reasoning
    /// and caveats behind every formula used here.
    /// </summary>
    public static void SetFrom(this FrooxEngine.AudioZitaReverb resonite, UnityEngine.AudioReverbFilter unityReverb, UnityEngine.AudioEchoFilter unityEcho)
    {
        // Set the basics (Enabled, persistent, etc. - same helper AudioSourceConverter uses)
        resonite.SetFrom((UnityEngine.Behaviour)unityReverb);

        if (unityReverb.reverbPreset != UnityEngine.AudioReverbPreset.User)
        {
            Debug.LogWarning(
                $"AudioReverbFilter on '{unityReverb.gameObject.name}' uses preset " +
                $"'{unityReverb.reverbPreset}' instead of 'User'. Whether Unity's C# property " +
                $"getters resolve to the preset's actual values in Editor scripts (as opposed " +
                $"to stale/default values) is unconfirmed - please verify the converted " +
                $"AudioZitaReverb values manually, or switch the preset to 'User' before " +
                $"converting for a guaranteed-accurate read.");
        }

        // decayTime is Unity's documented low-frequency decay time -> RT60Low directly.
        // decayHFRatio scales it to the higher band (I3DL2: HF decay = DecayTime * DecayHFRatio).
        float rt60Low = unityReverb.decayTime;
        float rt60Mid = unityReverb.decayTime * unityReverb.decayHFRatio;

        resonite.RT60Low = rt60Low;
        resonite.RT60Mid = rt60Mid;

        // Zita's "xover" crossover is a low frequency (tens-to-hundreds Hz), matching Unity's
        // lfReference range - hfReference (1000-20000 Hz) is the wrong range for this.
        resonite.Crossover = unityReverb.lfReference;

        // Heuristic, unverified: hfReference is the right range for Zita's "fdamp" (frequency
        // above which decay shortens). decayHFRatio is folded in as a bias - ratio<1 pulls the
        // damping frequency down (damps earlier), ratio>1 pushes it up, ratio==1 leaves it as-is.
        resonite.HighFrequencyDamping = Mathf.Max(1f, unityReverb.hfReference * unityReverb.decayHFRatio);

        // Zita's InDelay (ms) is the pre-delay into the reverb; Unity's reverbDelay (sec) is
        // documented as the same concept (late-reverb delay relative to first reflection).
        // reflectionsDelay is deliberately not used - it's early-reflections timing, a stage
        // Zita-Rev1 doesn't model.
        float inDelayMs = unityReverb.reverbDelay * 1000f;

        // Zita's Mix is a plain 0..1 dry/wet blend; Unity has no direct equivalent. Approximate
        // it from dryLevel (mB attenuation of the dry signal): convert to a linear gain and treat
        // (1 - gain) as the wet proportion. Not an exact inverse of Unity's DSP mixing, just a
        // perceptually similar knob.
        float dryLevelDb = unityReverb.dryLevel / 100f;
        float dryGainLinear = Mathf.Pow(10f, dryLevelDb / 20f);
        float mix = Mathf.Clamp01(1f - dryGainLinear);

        // Zita's Level is overall output gain in dB. Unity splits this across `room` (overall/
        // mid-band level) and `reverbLevel` (late-reverb level *relative to* room) - summing them
        // before the mB->dB conversion approximates total wet loudness.
        float levelDb = (unityReverb.room + unityReverb.reverbLevel) / 100f;

        // roomHF/roomLF/reflectionsLevel/reflectionsDelay/diffusion/density are intentionally not
        // converted - Zita-Rev1 has no early-reflections stage or per-band room gain beyond
        // RT60Low/RT60Mid/HighFrequencyDamping, and no diffusion/density equivalent. EQ1/EQ2 stay
        // at Resonite defaults since AudioReverbFilter has no parametric EQ to source from.

        // Optional: fold in a sibling AudioEchoFilter so its setup isn't silently dropped. This
        // is a rough approximation - Echo's discrete repeats and Zita's continuous tail sound
        // quite different - not an attempt to reproduce discrete echo taps.
        if (unityEcho != null)
        {
            // Unity's Echo delay is already in ms. Take the larger of the two candidate delays so
            // the audible gap isn't shorter than the echo's own delay.
            inDelayMs = Mathf.Max(inDelayMs, unityEcho.delay);

            // decayRatio (0=fast decay, 1=near-infinite ringing) stretches the reverb tail so a
            // long, ringing echo still produces a correspondingly long reverb. The 1x-5x range is
            // an arbitrary choice to keep RT60 plausible.
            float echoExtension = 1f + Mathf.Clamp01(unityEcho.decayRatio) * 4f;
            rt60Low *= echoExtension;
            rt60Mid *= echoExtension;

            resonite.RT60Low = rt60Low;
            resonite.RT60Mid = rt60Mid;

            // Echo's dryMix/wetMix don't sum to 1, so derive an approximate wet proportion from
            // their ratio and use it only if wetter than the reverb-only mix.
            float echoTotal = unityEcho.dryMix + unityEcho.wetMix;
            if (echoTotal > 0.0001f)
            {
                float echoWetProportion = unityEcho.wetMix / echoTotal;
                mix = Mathf.Max(mix, echoWetProportion);
            }
        }

        resonite.InDelay = inDelayMs;
        resonite.Mix = mix;
        resonite.Level = levelDb;
    }
}

public class AudioEffectConverter : ResoniteComponentConverter<AudioReverbFilter>
{
    public AudioZitaReverbWrapper Reverb;

    protected override void UpdateConversion(AudioReverbFilter target, IConversionContext context)
    {
        // Only handle AudioReverbFilter that lives alongside an AudioSource - that's the only
        // configuration where Unity's audio filter chain (and the AudioZitaReverb attachment
        // pattern used here) is meaningful.
        var unityAudioSource = GetComponent<UnityEngine.AudioSource>();

        if (unityAudioSource == null)
        {
            Debug.LogWarning(
                $"AudioReverbFilter on '{target.gameObject.name}' has no sibling AudioSource - " +
                $"skipping conversion. This converter only supports reverb filters attached " +
                $"alongside an AudioSource.");
            return;
        }

        // AudioSourceConverter is expected to have already created the AudioOutputWrapper on this
        // GameObject by the time this runs (its AudioSource is required to appear first in the
        // component list). If it hasn't happened yet, skip and retry on the next conversion pass.
        var output = GetComponent<AudioOutputWrapper>();

        if (output == null)
        {
            Debug.LogWarning(
                $"AudioReverbFilter on '{target.gameObject.name}': no AudioOutputWrapper found " +
                $"yet (sibling AudioSource not converted yet?) - will retry on next conversion " +
                $"pass.");
            return;
        }

        if (Reverb == null)
            Reverb = gameObject.AddComponent<AudioZitaReverbWrapper>();

        // UNVERIFIED (see file header): AudioZitaReverb is added to the same Slot as the
        // AudioOutput with no explicit reference between them - AudioDSP_Effect has no such field.
        var unityEcho = GetComponent<UnityEngine.AudioEchoFilter>();

        Reverb.Data.SetFrom(target, unityEcho);
    }

    // Guarded on ExplicitCleanupRequested (see ResoniteComponentConverter.cs) so this doesn't
    // redundantly re-destroy Reverb when the whole GameObject is already being torn down as a
    // unit (e.g. Bakery's scene-setup restore during a bake).
    protected override void Cleanup()
    {
        if (!ExplicitCleanupRequested)
            return;

        if (Reverb != null)
            DestroyImmediate(Reverb);
    }
}

// PoiyomiBakeStandin.cs
//
// Temporarily swaps Poiyomi-shaded materials to a plain Standard-shader stand-in for
// baking, then restores the originals afterward. Editor-only extension; the SDK
// itself is not modified.
//
// Why: LightmapMaterialCache only accepts shader.name == "Standard", so Poiyomi
// materials are silently skipped and never receive a baked lightmap (no warning is
// raised). Poiyomi happens to reuse Standard-compatible property names for base
// color/normal map/emission (not for metallic/smoothness), so re-shading to Standard
// and baking through the existing Standard pipeline is the simplest way to get Poiyomi
// surfaces lit. The toon look is lost on the baked result — an accepted tradeoff.
//
// Only Opaque-equivalent Poiyomi materials are swapped (per
// PoiyomiBlendModeComputer.FromPoiyomi). Cutout/Fade/Transparent materials (foliage,
// hair cards, etc.) are left to the normal non-baked PoiyomiConverter path instead,
// because LightmapMaterialCache requires _Mode == 0 (Opaque) and forcing them opaque
// would break their silhouette.
//
// The original .mat assets are never modified — only Renderer.sharedMaterials
// references are swapped, and always restored after conversion.
using System.Collections.Generic;
using FrooxEngine;
using UnityEngine;

public static class PoiyomiBakeStandin
{
    // Poiyomi source material -> generated Standard stand-in, reused across bake/convert
    // cycles instead of creating a new material every time. If a source reference goes
    // stale (e.g. reloaded after a scene reload), Unity's fake-null check after
    // TryGetValue catches it and a fresh stand-in is created for that key.
    static readonly Dictionary<UnityEngine.Material, UnityEngine.Material> _standinByPoiyomiSource = new Dictionary<UnityEngine.Material, UnityEngine.Material>();

    // Renderers actually overwritten by SwapIn() -> their original sharedMaterials.
    // Non-null only while a swap is active (RestoreAll() sets it back to null after
    // restoring). This makes RestoreAll() a safe no-op if called before any swap, and
    // makes SwapIn() idempotent — a second call won't overwrite recorded originals
    // with a second round of stand-ins.
    static Dictionary<Renderer, UnityEngine.Material[]> _originalMaterialsByRenderer;

    // Called by LightmapTestHarness at the start of a bake, right after
    // SetSceneLightsEnabled(true).
    public static void SwapIn()
    {
        if (_originalMaterialsByRenderer != null)
        {
            // Idempotency guard: don't overwrite recorded originals with stand-ins from
            // a second SwapIn() call.
            Debug.Log("[PoiyomiBakeStandin] SwapIn: a swap is already active; call ignored (call RestoreAll() first).");
            return;
        }

        var recorded = new Dictionary<Renderer, UnityEngine.Material[]>();

        foreach (var renderer in CollectCandidateRenderers())
        {
            var sourceMaterials = renderer.sharedMaterials;
            UnityEngine.Material[] replacement = null; // only allocated if a slot actually changes

            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                var source = sourceMaterials[i];
                if (!IsEligiblePoiyomiOpaque(source))
                    continue;

                var standin = GetOrCreateStandin(source);
                if (standin == null)
                    continue;

                if (replacement == null)
                    replacement = (UnityEngine.Material[])sourceMaterials.Clone();
                replacement[i] = standin;
            }

            if (replacement != null)
            {
                recorded[renderer] = sourceMaterials;
                renderer.sharedMaterials = replacement;
            }
        }

        _originalMaterialsByRenderer = recorded;

        if (recorded.Count > 0)
            Debug.Log($"[PoiyomiBakeStandin] SwapIn: swapped Poiyomi->Standard standin material(s) on {recorded.Count} renderer(s) for baking.");
    }

    // Called from the finally block wrapping Convert()'s SendCurrentScene() call, so it
    // always runs whether the bake succeeded or threw.
    public static void RestoreAll()
    {
        if (_originalMaterialsByRenderer == null)
            return; // No swap is active, or RestoreAll() already consumed it.

        // Each renderer is restored independently: if one throws (destroyed renderer,
        // mismatched material count, etc.) the rest still get restored and
        // _originalMaterialsByRenderer is still cleared, instead of leaving the swap
        // stuck active and the standins in place indefinitely.
        int restored = 0;
        int failed = 0;
        foreach (var kvp in _originalMaterialsByRenderer)
        {
            var renderer = kvp.Key;
            if (renderer == null)
                continue; // Renderer was destroyed or the scene changed since SwapIn(); nothing to restore.

            try
            {
                renderer.sharedMaterials = kvp.Value;
                restored++;
            }
            catch (System.Exception ex)
            {
                failed++;
                Debug.LogError($"[PoiyomiBakeStandin] RestoreAll: failed to restore original material(s) on renderer \"{renderer.name}\": {ex}. Continuing with the remaining renderer(s).");
            }
        }

        if (restored > 0)
            Debug.Log($"[PoiyomiBakeStandin] RestoreAll: restored original Poiyomi material(s) on {restored} renderer(s).");
        if (failed > 0)
            Debug.LogWarning($"[PoiyomiBakeStandin] RestoreAll: {failed} renderer(s) failed to restore - see the error(s) above. " +
                "Those renderer(s) may still be showing the temporary Standard standin material.");

        _originalMaterialsByRenderer = null;
    }

    static IEnumerable<Renderer> CollectCandidateRenderers()
    {
        foreach (var mr in UnityEngine.Object.FindObjectsOfType<UnityEngine.MeshRenderer>())
            yield return mr;
        foreach (var smr in UnityEngine.Object.FindObjectsOfType<UnityEngine.SkinnedMeshRenderer>())
            yield return smr;
    }

    // Detection mirrors PoiyomiConverter.EvaluateHeuristicConversion; gated to
    // Opaque-equivalent materials only (see PoiyomiBlendModeComputer.FromPoiyomi).
    static bool IsEligiblePoiyomiOpaque(UnityEngine.Material material)
    {
        if (material == null || material.shader == null)
            return false;

        if (!material.shader.name.Contains(".poiyomi/"))
            return false;

        return PoiyomiBlendModeComputer.FromPoiyomi(material) == BlendMode.Opaque;
    }

    static UnityEngine.Material GetOrCreateStandin(UnityEngine.Material source)
    {
        if (_standinByPoiyomiSource.TryGetValue(source, out var cached) && cached != null)
        {
            // The source .mat asset is never modified, but its Poiyomi properties can
            // still be edited between bakes — resync on every cache hit so the next
            // bake picks up those changes (mirrors LightmapMaterialCache's own
            // reread-every-time approach).
            SyncStandinFromSource(cached, source);
            return cached;
        }

        var shader = UnityEngine.Shader.Find("Standard");
        if (shader == null)
        {
            Debug.LogWarning("[PoiyomiBakeStandin] Could not find the built-in \"Standard\" shader; " +
                $"skipping standin for Poiyomi material \"{source.name}\".");
            return null;
        }

        var standin = new UnityEngine.Material(shader) { name = $"{source.name}_StandardStandin" };
        SyncStandinFromSource(standin, source);

        _standinByPoiyomiSource[source] = standin;
        return standin;
    }

    // Reads from the Poiyomi source are HasProperty-guarded since property sets vary by
    // Poiyomi version. The stand-in is always a fresh built-in Standard material, so
    // writes to it never need guarding.
    static void SyncStandinFromSource(UnityEngine.Material standin, UnityEngine.Material source)
    {
        if (source.HasProperty("_MainTex"))
        {
            standin.mainTexture = source.mainTexture;
            standin.mainTextureScale = source.mainTextureScale;
            standin.mainTextureOffset = source.mainTextureOffset;
        }

        if (source.HasProperty("_Color"))
            standin.color = source.color;

        UnityEngine.Texture bumpMap = source.HasProperty("_BumpMap") ? source.GetTexture("_BumpMap") : null;
        if (bumpMap != null)
        {
            standin.SetTexture("_BumpMap", bumpMap);
            float bumpScale = source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") : 1f;
            standin.SetFloat("_BumpScale", bumpScale);
            standin.EnableKeyword("_NORMALMAP");
        }
        else
        {
            standin.SetTexture("_BumpMap", null);
            standin.DisableKeyword("_NORMALMAP");
        }

        bool emissionEnabled = source.HasProperty("_EnableEmission") && source.GetFloat("_EnableEmission") > 0f;
        if (emissionEnabled)
        {
            Color emissiveColor = source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.black;
            float emissiveStrength = source.HasProperty("_EmissionStrength") ? source.GetFloat("_EmissionStrength") : 1f;
            standin.SetColor("_EmissionColor", emissiveColor * emissiveStrength);

            if (source.HasProperty("_EmissionMap"))
                standin.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));

            standin.EnableKeyword("_EMISSION");
            // Unity's baking pipeline (Progressive/Enlighten) needs this flag to pick up
            // emission as a GI contribution; the _EMISSION keyword only affects rendering.
            standin.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            standin.SetColor("_EmissionColor", Color.black);
            standin.SetTexture("_EmissionMap", null);
            standin.DisableKeyword("_EMISSION");
            standin.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        // No-op in practice (IsEligiblePoiyomiOpaque already confirmed Opaque, and a
        // fresh Standard material defaults to _Mode == 0) — set explicitly for clarity.
        standin.SetFloat("_Mode", 0f);
    }
}

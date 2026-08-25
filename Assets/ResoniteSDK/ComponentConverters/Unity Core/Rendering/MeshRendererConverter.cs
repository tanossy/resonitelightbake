using System.Collections.Generic;
using UnityEngine;

public static class MeshRendererHelper
{
    public static void SetFrom(this FrooxEngine.MeshRenderer resonite, UnityEngine.MeshRenderer unity, IConversionContext context)
    {
        // allowLightmap: true - this is the real MeshRenderer entry point (as opposed to the
        // shared Renderer-level helper below, which SkinnedMeshRendererConverter also funnels
        // through), so lightmap-variant substitution is allowed here.
        resonite.SetFrom((UnityEngine.Renderer)unity, context, allowLightmap: true);

        var meshFilter = unity.transform.GetComponent<MeshFilter>();

        if (ConversionPassState.ShouldConvertMeshes)
        {
            if (meshFilter == null)
                resonite.Mesh = null;
            else
                resonite.Mesh = context.GetMesh(meshFilter.sharedMesh);
        }
    }

    // allowLightmap defaults to false so that SkinnedMeshRendererHelper.SetFrom (which calls this
    // same shared helper for everything except mesh/skinning data) never routes materials through
    // LightmapMaterialCache - skinned meshes are not lightmapped by Unity's baked GI system, and
    // the marker-shader/variant substitution path below is only meaningful for static MeshRenderer
    // slots. Only MeshRendererHelper.SetFrom(MeshRenderer, ...) above opts in explicitly.
    public static void SetFrom(this FrooxEngine.MeshRenderer resonite, UnityEngine.Renderer unity, IConversionContext context, bool allowLightmap = false)
    {
        resonite.persistent = true;
        resonite.Enabled = unity.enabled;

        switch (unity.shadowCastingMode)
        {
            case UnityEngine.Rendering.ShadowCastingMode.On:
                resonite.ShadowCastMode = Renderite.Shared.ShadowCastMode.On;
                break;

            case UnityEngine.Rendering.ShadowCastingMode.Off:
                resonite.ShadowCastMode = Renderite.Shared.ShadowCastMode.Off;
                break;

            case UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly:
                resonite.ShadowCastMode = Renderite.Shared.ShadowCastMode.ShadowOnly;
                break;

            case UnityEngine.Rendering.ShadowCastingMode.TwoSided:
                resonite.ShadowCastMode = Renderite.Shared.ShadowCastMode.DoubleSided;
                break;
        }

        switch (unity.motionVectorGenerationMode)
        {
            case MotionVectorGenerationMode.Object:
                resonite.MotionVectorMode = Renderite.Shared.MotionVectorMode.Object;
                break;

            case MotionVectorGenerationMode.Camera:
                resonite.MotionVectorMode = Renderite.Shared.MotionVectorMode.Camera;
                break;

            case MotionVectorGenerationMode.ForceNoMotion:
                resonite.MotionVectorMode = Renderite.Shared.MotionVectorMode.NoMotion;
                break;
        }

        resonite.SortingOrder = unity.sortingOrder;

        if (!ConversionPassState.ShouldConvertMaterials)
            return;

        var sourceMaterials = unity.sharedMaterials;

        // Tracks slots not eligible for lightmap-variant substitution (non-Standard shader,
        // non-Opaque mode, etc. - see LightmapMaterialCache), so we can warn once on a split
        // outcome instead of silently importing a patchwork of lit/unlit materials.
        List<string> nonEligibleMaterialNames = null;

        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            var originalMat = sourceMaterials[i];
            var mat = originalMat;

            // Null slots are normal (fewer materials than submeshes) and were never eligible for
            // substitution, so they're excluded from eligibility bookkeeping to avoid a false warning.
            if (allowLightmap && originalMat != null)
            {
                mat = LightmapMaterialCache.GetVariantOrOriginal(unity, originalMat);

                if (ReferenceEquals(mat, originalMat))
                {
                    nonEligibleMaterialNames ??= new List<string>();
                    nonEligibleMaterialNames.Add(originalMat.name);
                }
            }

            var converted = context.GetMaterial(mat);

            if (resonite.Materials.Count == i)
                resonite.Materials.Add(converted);
            else
                resonite.Materials[i] = converted;
        }

        // Only warn on a mixed outcome (some slots lightmapped, some not) - a renderer with no
        // baked lightmap at all is the common case and would otherwise spam this warning.
        if (nonEligibleMaterialNames != null && nonEligibleMaterialNames.Count > 0 && nonEligibleMaterialNames.Count < sourceMaterials.Length)
        {
            Debug.LogWarning($"[ResoniteSDK] Renderer '{unity.name}': materials {string.Join(", ", nonEligibleMaterialNames)} " +
                "are not lightmap-eligible (non-Standard or non-Opaque); those slots will import without baked lighting.");
        }

        while (resonite.Materials.Count > sourceMaterials.Length)
            resonite.Materials.RemoveAt(resonite.Materials.Count - 1);
    }
}

public class MeshRendererConverter : ResoniteSingleComponentConverter<MeshRenderer, FrooxEngine.MeshRendererWrapper>
{
    protected override void UpdateConversion(MeshRenderer target, IConversionContext context)
    {
        Binding.Data.SetFrom(target, context);
    }
}

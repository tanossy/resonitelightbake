using FrooxEngine;

public static class MeshColliderHelper
{
    public static void SetFrom(this FrooxEngine.MeshCollider resonite, UnityEngine.MeshCollider unity, IConversionContext context)
    {
        if (unity.convex)
            throw new System.InvalidOperationException($"Unity mesh collider is convex. You need to use ConvexHullCollider instead");

        resonite.SetFrom((UnityEngine.Collider)unity);

        if (ConversionPassState.ShouldConvertMeshes)
            resonite.Mesh = context.GetMesh(unity.sharedMesh);

        // Unity Mesh Colliders are one-sided:
        // https://docs.unity3d.com/6000.3/Documentation/Manual/mesh-colliders-introduction.html
        resonite.Sidedness = MeshColliderSidedness.Front;
    }

    public static void SetFrom(this FrooxEngine.ConvexHullCollider resonite, UnityEngine.MeshCollider unity, IConversionContext context)
    {
        if (!unity.convex)
            throw new System.InvalidOperationException($"Unity mesh collider is not convex. You need to use MeshCollider instead");

        resonite.SetFrom((UnityEngine.Collider)unity);

        if (ConversionPassState.ShouldConvertMeshes)
            resonite.Mesh = context.GetMesh(unity.sharedMesh);
    }
}

public class MeshColliderConverter : ResoniteComponentConverter<UnityEngine.MeshCollider>
{
    public MeshColliderWrapper MeshBinding;
    public ConvexHullColliderWrapper ConvexHullBinding;

    protected override void UpdateConversion(UnityEngine.MeshCollider target, IConversionContext context)
    {
        // Resonite represents Convex Hull and Mesh colliders as separate components rather than
        // one with a toggle, so switch between them based on Unity's convex flag.
        if(target.convex)
        {
            if (MeshBinding != null)
                DestroyImmediate(MeshBinding);

            if (ConvexHullBinding == null)
                ConvexHullBinding = gameObject.AddComponent<ConvexHullColliderWrapper>();

            ConvexHullBinding.Data.SetFrom(target, context);
        }
        else
        {
            if (ConvexHullBinding != null)
                DestroyImmediate(ConvexHullBinding);

            if (MeshBinding == null)
                MeshBinding = gameObject.AddComponent<MeshColliderWrapper>();

            MeshBinding.Data.SetFrom(target, context);
        }
    }

    protected override void Cleanup()
    {
        if (MeshBinding != null)
            DestroyImmediate(MeshBinding);

        if (ConvexHullBinding != null)
            DestroyImmediate(ConvexHullBinding);
    }
}

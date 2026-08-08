using UnityEditor;
using UnityEngine;

// Filter applied to the scene-root traversal in SceneConverter.ConvertScene(), excluding Unity
// constructs that have no meaningful Resonite equivalent (its own camera system, and unconvertible
// prefab references). Externalized here so the change on the official SDK side (SceneConverter.cs)
// stays to a single line - `.Where(g => !SceneRootFilter.ShouldExclude(g))`.
public static class SceneRootFilter
{
    // Resonite has its own view/camera system, so Unity's Camera component has no use there;
    // broken prefab references (e.g. VRChat SDK components reported as "Missing Prefab" when the
    // VRC SDK isn't installed) also have no corresponding component in Resonite and can't be
    // converted at all. These are excluded at the scene-root level only - children carrying this
    // issue are not currently handled, since the known problem cases are top-level scene root
    // objects, so a root-level filter is sufficient for now.
    public static bool ShouldExclude(GameObject root)
    {
        // Unity's own Camera - Resonite has no use for it and it has no Resonite-side
        // equivalent component anyway.
        if (root.GetComponent<UnityEngine.Camera>() != null)
            return true;

        // Any prefab instance whose source asset is missing (e.g. VRChat SDK components like
        // VRCWorld/VRC_SceneDescriptor when the VRC SDK isn't installed) can't meaningfully be
        // converted - PrefabUtility can't even tell us what components it was supposed to have.
        // Detected generically via PrefabInstanceStatus.MissingAsset rather than by name, so it
        // also catches any other missing-prefab junk beyond the VRCWorld case.
        if (PrefabUtility.GetPrefabInstanceStatus(root) == PrefabInstanceStatus.MissingAsset)
            return true;

        return false;
    }
}

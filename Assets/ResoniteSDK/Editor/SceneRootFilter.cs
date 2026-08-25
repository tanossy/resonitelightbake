using System.Linq;
using UnityEditor;
using UnityEngine;

// Filter applied to the scene-root traversal in SceneConverter.ConvertScene(), excluding Unity
// constructs that have no meaningful Resonite equivalent (its own camera system, and unconvertible
// prefab/script references). Externalized here so the change on the official SDK side
// (SceneConverter.cs) stays to a single line - `.Where(g => !SceneRootFilter.ShouldExclude(g))`.
//
// The missing-script check below was added after two VRChat-derived leftover hierarchies
// (VRCWorld, and a "Easy Mirror" asset) kept showing up in a sent scene despite the MissingAsset
// check already in place. Investigated directly in the scene/prefab YAML rather than guessing:
// VRCWorld is a plain (non-broken-prefab) GameObject carrying MonoBehaviours whose script guids
// have no matching .meta in the project (the VRChat SDK assembly isn't installed), and Easy Mirror
// is a perfectly normal, resolvable prefab instance whose nested "UI" child carries the same kind
// of unresolvable script reference - so neither is caught by the MissingAsset check, which only
// looks at the root's own prefab status. Both reduce to the same underlying, non-VRChat-specific
// fact: the hierarchy contains a MonoBehaviour Unity couldn't resolve, which it always surfaces as
// a literal null entry in GetComponentsInChildren<Component>(true) - the same idiom Unity's own
// "select prefabs with missing scripts" editor tooling uses. Checking for that instead of matching
// on a name generalizes to any unresolvable-script junk, VRChat-derived or not, and (unlike the
// MissingAsset check) has to walk the whole hierarchy rather than just the root, since Easy
// Mirror's missing script sits on a descendant.
public static class SceneRootFilter
{
    // Resonite has its own view/camera system, so Unity's Camera component has no use there;
    // broken prefab references (e.g. VRChat SDK components reported as "Missing Prefab" when the
    // VRC SDK isn't installed) also have no corresponding component in Resonite and can't be
    // converted at all. These two are excluded at the scene-root level only (the third condition
    // below walks the whole hierarchy, since not every case is caught at the root).
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

        // Catches leftover hierarchies (see file-level comment) that aren't broken prefab
        // instances themselves but contain a component Unity couldn't resolve somewhere in their
        // tree - typically because the asset that defines it (e.g. the VRChat SDK) isn't
        // installed in this project.
        if (HasMissingScriptInHierarchy(root))
            return true;

        // Some third-party tools (e.g. Bakery's own lightmap-cache bookkeeping object,
        // "!ftraceLightmaps") create scene GameObjects with HideFlags.HideInHierarchy - the
        // tool itself considers them internal and hides them from the Unity Hierarchy window,
        // though GetRootGameObjects() still enumerates them for scene conversion. Checked via
        // this flag rather than by name/type, so it generalizes to any other tool's similarly
        // self-hidden bookkeeping objects.
        if ((root.hideFlags & HideFlags.HideInHierarchy) != 0)
            return true;

        return false;
    }

    // True if `root` or any of its descendants (active or inactive) carries a MonoBehaviour whose
    // backing script Unity could not resolve. Unity represents such a component slot as a literal
    // null entry in GetComponentsInChildren<Component>(true) - the standard editor-tooling idiom
    // for finding "Missing Script" objects, and unlike PrefabInstanceStatus it also covers scripts
    // missing on an otherwise perfectly normal (non-prefab, or non-broken-prefab) GameObject.
    static bool HasMissingScriptInHierarchy(GameObject root)
    {
        return root.GetComponentsInChildren<Component>(true).Any(c => c == null);
    }
}

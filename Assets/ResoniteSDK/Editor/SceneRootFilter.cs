using System.Linq;
using UnityEditor;
using UnityEngine;

// Filter applied to the scene-root traversal in SceneConverter.ConvertScene(), excluding Unity
// constructs with no meaningful Resonite equivalent. Externalized so the change on the official
// SDK side (SceneConverter.cs) stays to a single line -
// `.Where(g => !SceneRootFilter.ShouldExclude(g))`.
public static class SceneRootFilter
{
    public static bool ShouldExclude(GameObject root)
    {
        // Resonite has its own camera system; Unity's Camera has no equivalent there.
        if (root.GetComponent<UnityEngine.Camera>() != null)
            return true;

        // A prefab instance whose source asset is missing (e.g. VRChat SDK components when the
        // VRC SDK isn't installed) can't meaningfully be converted - PrefabUtility can't even
        // tell us what components it was supposed to have.
        if (PrefabUtility.GetPrefabInstanceStatus(root) == PrefabInstanceStatus.MissingAsset)
            return true;

        // Catches leftover hierarchies that aren't broken prefab instances themselves but
        // contain a component Unity couldn't resolve somewhere in their tree (e.g. a
        // non-broken-prefab GameObject with MonoBehaviours whose script guids have no matching
        // asset, or a resolvable prefab whose descendant carries such a component) - not caught
        // by the MissingAsset check above, which only looks at the root's own prefab status.
        if (HasMissingScriptInHierarchy(root))
            return true;

        // Some third-party tools (e.g. Bakery's lightmap-cache bookkeeping object) create scene
        // GameObjects with HideFlags.HideInHierarchy, which GetRootGameObjects() still enumerates
        // during scene conversion despite the tool hiding them from the Hierarchy window.
        if ((root.hideFlags & HideFlags.HideInHierarchy) != 0)
            return true;

        return false;
    }

    // True if `root` or any descendant (active or inactive) carries a MonoBehaviour whose backing
    // script Unity could not resolve - represented as a literal null entry in
    // GetComponentsInChildren<Component>(true), the same idiom Unity's own "missing scripts"
    // editor tooling uses.
    static bool HasMissingScriptInHierarchy(GameObject root)
    {
        return root.GetComponentsInChildren<Component>(true).Any(c => c == null);
    }
}

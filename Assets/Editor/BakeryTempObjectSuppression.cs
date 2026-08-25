// BakeryTempObjectSuppression.cs
//
// Excludes Bakery's own bookkeeping GameObject ("!ftraceLightmaps", holding a
// ftLightmapsStorage component) from the Resonite.UnitySDK scene conversion.
// Editor-only extension; the SDK itself is not modified.
//
// Compiled only when Bakery is present (#if BAKERY_INCLUDED, managed by
// BakeryPresenceDefine.cs) — without Bakery, there is no such object to suppress.
//
// "!ftraceLightmaps" is Bakery's bake-result bookkeeping and must never be deleted
// from the scene, so this only stops it (and anything else Bakery ever puts on the
// same object) from being converted, using the SDK's official
// [ConverterSupressionHandler] extension point: register a converter for
// ftLightmapsStorage whose handler clears the whole toConvert list for that Transform.
//
// Known limitation: suppression only stops component conversion on that Transform.
// The SDK unconditionally creates a Slot for every root GameObject regardless of its
// components, and there is no official hook to skip a root object entirely — so
// "!ftraceLightmaps" still shows up in Resonite as an empty Slot (no Bakery data is
// ever sent, which is the actual goal here).
#if BAKERY_INCLUDED
using System.Collections.Generic;
using UnityEngine;

public class BakeryTempObjectSuppressionConverter : ResoniteComponentConverter<ftLightmapsStorage>
{
    protected override void UpdateConversion(ftLightmapsStorage target, IConversionContext context)
    {
        // Intentionally empty: ftLightmapsStorage has no Resonite-side equivalent and must
        // never produce an AddComponent/UpdateComponent message. Do not call
        // EnsureComponent<...> here.
    }

    protected override void Cleanup()
    {
        // Nothing was ever created in UpdateConversion, so there is nothing to destroy.
    }

    [ConverterSupressionHandler]
    public static void SuppressBakeryStorageObject(Transform root, List<Component> toConvert)
    {
        // 'root' here is the GameObject currently being processed, not the scene root. Only
        // touch GameObjects that actually carry Bakery's own storage component (normally just
        // the single "!ftraceLightmaps" object; name-agnostic so it still works if Bakery ever
        // renames it).
        if (root.GetComponent<ftLightmapsStorage>() == null)
            return;

        // Clear the WHOLE list, not just ftLightmapsStorage's own entry, in case Bakery ever
        // places another convertible component on the same object. This never touches the
        // scene hierarchy itself — only which components get sent to Resonite.
        toConvert.Clear();
    }
}
#endif

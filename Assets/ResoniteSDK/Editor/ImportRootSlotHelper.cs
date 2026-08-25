using ResoniteLink;
using System.Threading.Tasks;

// Best-effort lookup for a pre-existing "Unity Import" slot directly under World Root, left over
// from a previous Editor session/connection. Called from SceneConverter.EnsureImportRootSlot().
// Returns its ID if found, or null on any failure (not found, not connected, query error) - the
// caller always falls back to allocating a fresh ID, so a failure here must never block conversion.
public static class ImportRootSlotHelper
{
    public static string TryFindExistingId(LinkInterface link, string slotName)
    {
        if (link == null || !link.IsConnected)
            return null;

        try
        {
            var response = Task.Run(async () => await link.GetSlotData(new GetSlot()
            {
                SlotID = "Root",
                Depth = 1,
                IncludeComponentData = false,
            })).GetAwaiter().GetResult();

            if (response == null || !response.Success || response.Data?.Children == null)
                return null;

            foreach (var child in response.Data.Children)
                if (child.Name?.Value == slotName)
                    return child.ID;
        }
        catch
        {
            // Swallow: worst case we allocate a fresh ID and leave the old tree orphaned, rather
            // than fail the whole conversion over a best-effort lookup.
        }

        return null;
    }
}

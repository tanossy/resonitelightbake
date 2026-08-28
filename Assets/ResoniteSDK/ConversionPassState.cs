public enum ResoniteSdkConversionPass
{
    Full,
    MeshesOnly,
    MaterialsOnly,
    LightmapsOnly,
}

public static class ConversionPassState
{
    public static ResoniteSdkConversionPass ActivePass { get; set; } = ResoniteSdkConversionPass.Full;

    // Lives here (rather than as a field on the main SDK panel) so it can be driven from the
    // Lightmap Baker panel, keeping the main panel limited to vanilla Resonite.UnitySDK.
    // Same decoupling pattern as ToneMapCompensationState.
    public static bool ForceRefreshGeneratedLightmaps { get; set; } = true;

    public static bool ShouldConvertMeshes =>
        ActivePass != ResoniteSdkConversionPass.MaterialsOnly &&
        ActivePass != ResoniteSdkConversionPass.LightmapsOnly;

    public static bool ShouldConvertMaterials => ActivePass != ResoniteSdkConversionPass.MeshesOnly;

    public static bool ShouldUploadSourceMaterialTextures =>
        ActivePass != ResoniteSdkConversionPass.LightmapsOnly;
}

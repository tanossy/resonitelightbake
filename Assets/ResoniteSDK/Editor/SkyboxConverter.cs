using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public class SkyboxConverter
{
    public const string SKYBOX_ROOT_NAME = "__UnitySkybox";

    public GameObject SkyboxRoot;

    public FrooxEngine.SkyboxWrapper Skybox;
    [NonSerialized]
    public FrooxEngine.AmbientLightSH2Wrapper AmbientLight;
    public FrooxEngine.ReflectionProbeWrapper ReflectionProbe;
    [NonSerialized]
    public FrooxEngine.ReflectionProbeSH2Wrapper ReflectionProbeSH2;
    [NonSerialized]
    public FrooxEngine.ValueCopySH_L2_Wrapper ValueCopy;

    // Unity cannot serialize the generated wrapper fields containing
    // Sync<UnityEngine.Rendering.SphericalHarmonicsL2>, so SH2 sky ambient is disabled by
    // default; skybox material/probe conversion still works either way.
    public static bool ConvertSphericalHarmonics = false;

    public void EnsureRoot()
    {
        if (SkyboxRoot != null)
            return;

        // Use explicit Unity-aware `!= null` checks here, not `?.`: if these fields are in
        // Unity's "fake null" state (native side destroyed, C# reference not null — e.g. after
        // the __UnitySkybox root is destroyed and the fields survive a domain reload), the
        // null-conditional operator skips Unity's operator== override and `.gameObject` throws
        // a MissingReferenceException instead of being safely skipped.
        if (Skybox != null)
            SkyboxRoot = Skybox.gameObject;
        else if (AmbientLight != null)
            SkyboxRoot = AmbientLight.gameObject;
        else if (ReflectionProbe != null)
            SkyboxRoot = ReflectionProbe.gameObject;

        if(SkyboxRoot == null)
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            SkyboxRoot = roots.FirstOrDefault(r => r.name == SKYBOX_ROOT_NAME);
        }

        if(SkyboxRoot == null)
            SkyboxRoot = new GameObject(SKYBOX_ROOT_NAME);
    }

    // TODO: assumes reflections and ambient light both come from the skybox; other ambient
    // light setups aren't converted.
    public void ConvertCurrentSkybox(IConversionContext context)
    {
        EnsureComponent(ref Skybox);
        EnsureComponent(ref ReflectionProbe);

        if (!ConvertSphericalHarmonics)
        {
            RemoveComponent(ref AmbientLight);
            RemoveComponent(ref ReflectionProbeSH2);
            RemoveComponent(ref ValueCopy);
        }
        else
        {
            EnsureComponent(ref AmbientLight);
            EnsureComponent(ref ReflectionProbeSH2);
            EnsureComponent(ref ValueCopy);
        }

        var skyboxMaterial = context.GetMaterial(RenderSettings.skybox);
        Skybox.Data.Material = skyboxMaterial;

        // This reflection probe feeds both specular reflections and the SH2 ambient
        // calculation below.
        ReflectionProbe.Data.SkyboxOnly = true;
        ReflectionProbe.Data.BoxSize = Vector3.one * 1000000;
        ReflectionProbe.Data.ClearFlags = Renderite.Shared.ReflectionProbeClear.Skybox;
        ReflectionProbe.Data.HDR = true;
        ReflectionProbe.Data.ProbeType = Renderite.Shared.ReflectionProbeType.OnChanges;
        ReflectionProbe.Data.Intensity = 1f;

        while (ReflectionProbe.Data.ChangesSources.Count < 2)
            ReflectionProbe.Data.ChangesSources.Add();

        ReflectionProbe.Data.ChangesSources[0] = Skybox.Data;
        ReflectionProbe.Data.ChangesSources[1] = skyboxMaterial;

        if (!ConvertSphericalHarmonics)
            return;

        ReflectionProbeSH2.Data.Probe = ReflectionProbe.Data;
        // Tuned to roughly match Unity's own SH2 calculation.
        ReflectionProbeSH2.Data.Order0Scale = 1.5f;
        ReflectionProbeSH2.Data.Order1Scale = 0.5f;
        ReflectionProbeSH2.Data.Order2Scale = 0.5f;

        ValueCopy.Data.Source = ReflectionProbeSH2.Data.AmbientLight_Element.Member;
        ValueCopy.Data.Target = AmbientLight.Data.AmbientLight_Element.Member;
    }

    void EnsureComponent<T>(ref T component)
        where T : ResoniteComponent
    {
        if (component != null)
            return;

        component = SkyboxRoot.GetComponent<T>();

        if (component == null)
            component = SkyboxRoot.AddComponent<T>();
    }

    void RemoveComponent<T>(ref T component)
        where T : ResoniteComponent
    {
        if (component == null && SkyboxRoot != null)
            component = SkyboxRoot.GetComponent<T>();

        if (component == null)
            return;

        UnityEngine.Object.DestroyImmediate(component);
        component = null;
    }
}

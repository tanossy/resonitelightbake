using System;
using UnityEngine;

[ExecuteInEditMode]
public abstract class ResoniteComponentConverter : MonoBehaviour
{
    [SerializeField]
    public Component Target;

    // A converter and its Resonite-side wrapper component(s) live on the same GameObject as
    // the original Unity component. Explicitly destroying the wrapper in Cleanup() is only
    // needed when a caller destroys the converter component alone and the GameObject survives
    // (SceneConverter's orphaned-target cleanup, ResoniteLinkWindow.CleanupConverters()) -
    // whenever the whole GameObject is destroyed as a unit instead (deleting a Light in the
    // Hierarchy, or Bakery tearing down its temporary bake scene), the wrapper is already part
    // of that same destroy cascade, and re-destroying it logs Unity's "Destroying object
    // multiple times" warning. The two explicit-destroy call sites set this flag right before
    // their DestroyImmediate(converter) call; every other OnDestroy() leaves it false, and
    // Cleanup() implementations skip their wrapper-destroy calls in that case.
    public bool ExplicitCleanupRequested;

    public void Initialize(Component target)
    {
        Target = target;

        // Run any initialization code
        Initialize();
    }

    public abstract void UpdateConversion(IConversionContext context);

    protected abstract void Initialize();
    protected abstract void Cleanup();

    [ExecuteInEditMode]
    void OnDestroy() => Cleanup();
}

/// <summary>
/// This is the best class to derive from when you need versatility in how the component converts. 
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class ResoniteComponentConverter<T> : ResoniteComponentConverter
    where T : Component
{
    protected sealed override void Initialize() => Initialize((T)Target);
    public sealed override void UpdateConversion(IConversionContext context) => UpdateConversion((T)Target, context);

    protected virtual void Initialize(T target) {  }
    protected abstract void UpdateConversion(T target, IConversionContext context);

    protected TComponent EnsureComponent<TComponent, TWrapper>(ref TWrapper wrapper, 
        Action<TComponent> onAdded = null)
        where TWrapper : ResoniteComponent<TComponent>
        where TComponent : ResoniteObject, FrooxEngine.IWorldElement, new()
    {
        if (wrapper == null)
            wrapper = gameObject.AddComponent<TWrapper>();

        var data = wrapper.Data;

        onAdded?.Invoke(data);

        return data;
    }
}

/// <summary>
/// This provides convenient way to define conversions that map 1:1 Unity component to a Resonite component.
/// It automatically handles the instantiation and cleanup, so you only need to worry about providing the conversion update code.
/// </summary>
/// <typeparam name="TUnity"></typeparam>
/// <typeparam name="TResoniteWrapper"></typeparam>
public abstract class ResoniteSingleComponentConverter<TUnity, TResoniteWrapper> : ResoniteComponentConverter<TUnity>
    where TUnity : Component
    where TResoniteWrapper : ResoniteComponent
{
    public TResoniteWrapper Binding;

    protected override void Initialize(TUnity target)
    {
        base.Initialize(target);

        Binding = gameObject.AddComponent<TResoniteWrapper>();
    }

    protected override void Cleanup()
    {
        if (!ExplicitCleanupRequested)
            return;

        // Cleanup the binding if it still exists
        if (Binding == null)
            return;

        DestroyImmediate(Binding);
    }
}

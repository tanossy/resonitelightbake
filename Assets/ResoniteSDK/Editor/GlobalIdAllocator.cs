// Process-wide, monotonically increasing ID source for SceneConverter.AllocateId(). Needed
// because ResoniteLink's server-assigned UniqueSessionId can repeat across reconnects; a
// per-SceneConverter-instance counter would then regenerate the same ID sequence (e.g.
// "Unity_0__0"), colliding with the previous run's object still on the server and causing a
// fatal "ID already in use" error. This counter is never reset, so IDs never repeat for the
// lifetime of the Editor process regardless of how many SceneConverters are created.
public static class GlobalIdAllocator
{
    static long _pool;

    public static long Next() => System.Threading.Interlocked.Increment(ref _pool) - 1;
}

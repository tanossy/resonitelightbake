// UniqueSessionId is assigned by the server during ResoniteLink's handshake, and is not fully
// under our control - depending on how a client reconnects, the same value can be returned again.
// The original per-SceneConverter-instance ID-allocation counter reset every time a new
// SceneConverter was created, so if a new instance was created while UniqueSessionId stayed the
// same, ID generation would reproduce the exact same sequence of strings as before (e.g.
// "Unity_0__0"), colliding with the previous run's same-named object still present on the server
// and causing an "ID '...' is already in use" fatal error that drops the converter into an
// IsCorrupted state.
//
// This process-wide monotonically increasing static counter guarantees that, no matter how many
// times SceneConverter gets recreated, the same number is never generated twice for the lifetime
// of this Unity Editor process. SceneConverter.AllocateId() sources its IDs from this class.
public static class GlobalIdAllocator
{
    static long _pool;

    public static long Next() => System.Threading.Interlocked.Increment(ref _pool) - 1;
}

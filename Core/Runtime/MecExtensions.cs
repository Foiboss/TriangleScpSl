using MEC;

namespace TriangleScpSl.Core.Runtime;

internal static class MecExtensions
{
    public static CoroutineHandle Run(this IEnumerator<float> coroutine) => Timing.RunCoroutine(coroutine);

    public static bool Kill(this CoroutineHandle handle)
    {
        if (!handle.IsRunning)
            return false;

        handle.IsRunning = false;
        return true;
    }
}
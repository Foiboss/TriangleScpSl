using System.Collections;
using UnityEngine;

namespace TriangleScpSl.Core.Runtime;

public static class CoroutineHost
{
    static CoroutineHostBehaviour? host;

    public static Coroutine Run(IEnumerator routine)
    {
        EnsureHost();
        return host!.StartCoroutine(routine);
    }

    public static void Stop(Coroutine? coroutine)
    {
        if (host is null || coroutine is null)
            return;

        host.StopCoroutine(coroutine);
    }

    public static void Shutdown()
    {
        if (host is null)
            return;

        UnityEngine.Object.Destroy(host.gameObject);
        host = null;
    }

    static void EnsureHost()
    {
        if (host is not null)
            return;

        var go = new GameObject("TriangleScpSl.CoroutineHost")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };

        UnityEngine.Object.DontDestroyOnLoad(go);
        host = go.AddComponent<CoroutineHostBehaviour>();
    }

    sealed class CoroutineHostBehaviour : MonoBehaviour;
}
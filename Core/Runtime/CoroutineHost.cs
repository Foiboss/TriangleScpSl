using System.Collections;
using UnityEngine;

namespace TriangleScpSl.Core.Runtime;

public static class CoroutineHost
{
    static CoroutineHostBehaviour? _host;

    public static Coroutine Run(IEnumerator routine)
    {
        EnsureHost();
        return _host!.StartCoroutine(routine);
    }

    public static void Stop(Coroutine? coroutine)
    {
        if (_host is null || coroutine is null)
            return;

        _host.StopCoroutine(coroutine);
    }

    public static void Shutdown()
    {
        if (_host is null)
            return;

        UnityEngine.Object.Destroy(_host.gameObject);
        _host = null;
    }

    static void EnsureHost()
    {
        if (_host is not null)
            return;

        var go = new GameObject("TriangleScpSl.CoroutineHost")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };

        UnityEngine.Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<CoroutineHostBehaviour>();
    }

    sealed class CoroutineHostBehaviour : MonoBehaviour;
}
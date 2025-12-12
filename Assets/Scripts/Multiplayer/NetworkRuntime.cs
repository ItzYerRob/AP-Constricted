using Unity.Netcode;
using UnityEngine;
public sealed class NetworkRuntime : MonoBehaviour
{
    void Awake() {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.gameObject != gameObject) { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);
        NetworkManager.Singleton.OnTransportFailure += () => Debug.LogError("Transport failed; Relay allocation invalid; NM shutting down.");
    }
}

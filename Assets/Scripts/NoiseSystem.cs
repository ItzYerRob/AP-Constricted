using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public struct NoiseEvent {
    public Vector3 Position;
    public float Radius; //how far it can be heard
    public float Loudness; //normalized 0–1, for AI weighting
    public NetworkObject Source; //who made the noise
}

public interface INoiseListener { void OnNoiseHeard(in NoiseEvent e); }

public class NoiseSystem : MonoBehaviour {
    public static NoiseSystem Instance { get; private set; }

    private readonly List<INoiseListener> _listeners = new();

    void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Register(INoiseListener listener) {
        if (!_listeners.Contains(listener)) _listeners.Add(listener);
    }

    public void Unregister(INoiseListener listener) {
        _listeners.Remove(listener);
    }

    //Called by noise emitters (server only).
    public void EmitNoise(NoiseEvent e) {

        //Simple listener dispatch;
        foreach (var listener in _listeners) listener.OnNoiseHeard(e);
    }
}

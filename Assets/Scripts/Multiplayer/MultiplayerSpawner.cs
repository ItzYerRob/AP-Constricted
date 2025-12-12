// PerPlayerSpawner.cs
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PerPlayerSpawner : NetworkBehaviour
{
    [SerializeField] NetworkObject extraPrefab;

    readonly Dictionary<ulong, NetworkObject> _spawned = new();

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        //Spawn for already-connected clients
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            TrySpawnFor(kvp.Key);
        }

        NetworkManager.Singleton.OnClientConnectedCallback += TrySpawnFor;
        NetworkManager.Singleton.OnClientDisconnectCallback += DespawnFor;
    }

    void OnDestroy()
    {
        if (!IsServer || NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback -= TrySpawnFor;
        NetworkManager.Singleton.OnClientDisconnectCallback -= DespawnFor;
    }

    void TrySpawnFor(ulong clientId)
    {
        if (_spawned.ContainsKey(clientId)) return;
        var instance = Instantiate(extraPrefab);
        instance.SpawnWithOwnership(clientId);
        _spawned[clientId] = instance;
    }

    void DespawnFor(ulong clientId)
    {
        if (_spawned.TryGetValue(clientId, out var obj) && obj != null && obj.IsSpawned)
            obj.Despawn();
        _spawned.Remove(clientId);
    }
}

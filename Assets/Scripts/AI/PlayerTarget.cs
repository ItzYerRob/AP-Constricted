using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class PlayerTarget : NetworkBehaviour
{
    //A static list containing all active player Transforms.
    public static readonly List<Transform> AllPlayers = new List<Transform>();

    public override void OnNetworkSpawn() {
        //Add this player's transform to the list when it spawns
        if (!AllPlayers.Contains(transform)) {
            AllPlayers.Add(transform);
        }
    }

    public override void OnNetworkDespawn() {
        //Remove this player's transform from the list when it despawns
        if (AllPlayers.Contains(transform)) {
            AllPlayers.Remove(transform);
        }
    }

    //Fallback for when the object is just disabled/enabled
    void OnDisable() {
        if (AllPlayers.Contains(transform)) {
            AllPlayers.Remove(transform);
        }
    }
}
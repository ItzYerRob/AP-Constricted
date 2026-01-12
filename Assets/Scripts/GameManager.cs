using UnityEngine;
using System;

public sealed class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public bool IsDebug = false;

    public int PlayerLevel { get; private set; } = 1;

    public bool PickedUpKeys { get; private set; } = false;

    public float BadNoise = 0;
    // public float BadNoise { get; set; } = 0;

    public event Action<int> OnPlayerLevelChanged;

    void Awake()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (Instance != null && Instance != this)
        {
            Debug.LogError("Multiple GameManager instances detected. Destroying the newest one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void AddLevels(int amount) {
        if (amount <= 0) return;
        PlayerLevel += amount;
        OnPlayerLevelChanged?.Invoke(PlayerLevel);
        //Maybe play SFX/VFX, etc.
        Debug.Log($"Player leveled up to {PlayerLevel}");
    }

    public void PickedUpKey() {
        PickedUpKeys = true;
        Debug.Log($"Player picked up key");
    }

    void FixedUpdate() {
        Debug.Log("Bad Noise:" + BadNoise);
    }
}

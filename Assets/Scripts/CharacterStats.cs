using System.Collections;
using UnityEngine;
using Unity.Netcode;

public class CharacterStats : NetworkBehaviour
{
    [Header("Base Stats")]
    public float maxHealth = 100f;
    public float maxStamina = 100f;
    public float healthRegen = 0f;
    public float staminaRegen = 0f;

    [Header("Flags")]
    public bool AreWeAPlayer = false;
    public bool HaveWeAHealthBar = false;

    [Header("Movement")]
    public float gravityForce;
    public float moveSpeed, sprintSpeed, rotationSpeed, jumpForce;
    public int maxJumpCount;

    [Header("Combat / Death")]
    public bool isImmune = false;
    public GameObject ragdollPrefab;
    public float ragdollLifetime = 5f;
    public Animator animator;

    [Header("UI")]
    public ResourceBar resourceBar; //For players, this will be local HUD

    //Networked state
    public NetworkVariable<float> currentHealth = new NetworkVariable<float>(
        value: 0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> currentStamina = new NetworkVariable<float>(
        value: 0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    public override void OnNetworkSpawn() {
        base.OnNetworkSpawn();

        if (IsServer) {
            if(AreWeAPlayer) {
                currentHealth.Value = maxHealth/20;
                currentStamina.Value = maxStamina/20;
            }
            else {
                currentHealth.Value = maxHealth;
                currentStamina.Value = maxStamina;
            } 
        }

        //Subscribe to NetworkVariable changes on all clients
        currentHealth.OnValueChanged += OnHealthChanged;
        currentStamina.OnValueChanged += OnStaminaChanged;

        //Only the owning client should hook up the local HUD
        if (AreWeAPlayer && IsOwner)
        {
            if (resourceBar == null) {
                //from some UI manager that is local-only
                resourceBar = UIManager.Instance.playerResourceBar;
            }

            if (resourceBar != null)
            {
                resourceBar.AreWeAPlayer = true;
                resourceBar.SetMaxHealth((int)maxHealth, this);
                resourceBar.SetMaxStamina((int)maxStamina, this);

                //Force initial sync with current network values
                resourceBar.SetHealth((int)currentHealth.Value, this);
                resourceBar.SetStamina((int)currentStamina.Value);
            }
        }
    }

    private void OnDestroy()
    {
        //Unsubscribe to avoid leaks / callbacks into destroyed objects
        currentHealth.OnValueChanged -= OnHealthChanged;
        currentStamina.OnValueChanged -= OnStaminaChanged;
    }

    private void Update() {
        if (!IsServer) return;

        if (!AreWeAPlayer && currentHealth.Value <= 0) {
            HandleDeathServer();
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        //Server drives regen so all clients see consistent results
        if (healthRegen > 0f) {
            currentHealth.Value = Mathf.Min(
                maxHealth,
                currentHealth.Value + healthRegen * Time.fixedDeltaTime
            );
        }

        if (staminaRegen > 0f) {
            currentStamina.Value = Mathf.Min(
                maxStamina,
                currentStamina.Value + staminaRegen * Time.fixedDeltaTime
            );
        }
    }

    private void OnHealthChanged(float oldValue, float newValue) {
        if (resourceBar != null && AreWeAPlayer) { resourceBar.SetHealth((int)newValue, this); }
    }

    private void OnStaminaChanged(float oldValue, float newValue) {
        if (resourceBar != null && AreWeAPlayer) { resourceBar.SetStamina((int)newValue); }
    }

    #region Damage API

    public void ApplyDamage(float damage)
    {
        if (!IsServer) return;
        if (isImmune) return;

        currentHealth.Value -= damage;
        Debug.Log($"{gameObject.name} took {damage} damage. Health now {currentHealth.Value}");

        if (AreWeAPlayer && animator != null)
        {
            //Drive animation via RPC so only relevant client plays it
            PlayFlinchClientRpc();
        }

        if (!AreWeAPlayer && currentHealth.Value <= 0)
        {
            HandleDeathServer();
        }
    }

    public void Heal(float damage)
    {
        if (!IsServer) return;

        currentHealth.Value += damage;
        if(currentHealth.Value >= maxHealth) { currentHealth.Value = maxHealth; }
        Debug.Log($"{gameObject.name} healed {damage} damage. Health now {currentHealth.Value}");
    }

    //This is what clients call to request damage (for debug or client-predicted hits)
    [ServerRpc]
    public void RequestDamageServerRpc(float damage) { ApplyDamage(damage); }

    public void ApplyStaminaCost(float value) {
        if (!IsServer) return;
        currentStamina.Value = Mathf.Max(0f, currentStamina.Value - value);
    }

    [ServerRpc]
    public void RequestStaminaCostServerRpc(float value) {
        ApplyStaminaCost(value);
    }

    #endregion

    #region Death / Ragdoll

    [ClientRpc]
    private void PlayFlinchClientRpc() {
        if (AreWeAPlayer && animator != null) {
            animator.Play("Flinch");
        }
    }

    private void HandleDeathServer() {
        if (ragdollPrefab != null) {
            var ragdoll = Instantiate(ragdollPrefab, transform.position, transform.rotation);

            var netObj = ragdoll.GetComponent<NetworkObject>();
            if (netObj != null) {
                netObj.Spawn(); //Everyone sees the ragdoll
                Destroy(ragdoll, ragdollLifetime);
            }
            else {
                //If not networked, only server will see it
                Destroy(ragdoll, ragdollLifetime);
            }
        }

        // Despawn the character over the network
        var thisNetObj = GetComponent<NetworkObject>();
        if (thisNetObj != null && thisNetObj.IsSpawned)
        {
            thisNetObj.Despawn();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion

    public float GetMaxHealth() => maxHealth;
}
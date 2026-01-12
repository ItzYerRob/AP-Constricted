using TMPro;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Interaction Prompt")]
    public GameObject interactPrompt;
    public TextMeshProUGUI interactLabel;

    [Header("Player HUD")]
    //This is the local player's HUD resource bar
    public ResourceBar playerResourceBar;

    [Header("Cutscene")]
    public GameObject startGameScreen;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        DontDestroyOnLoad(gameObject);
        
        startGameScreen.SetActive(true);
    }

    public void SetInteractLabel(string text, bool available, string reason = null) {
        if (interactLabel == null) return;

        interactLabel.text = available 
            ? text 
            : $"{text} ({reason ?? "Unavailable"})";

        interactLabel.alpha = available ? 1f : 0.6f;
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.E)) { Destroy(startGameScreen, 0.5f); }
    }
}
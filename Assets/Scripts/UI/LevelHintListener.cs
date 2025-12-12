using UnityEngine;

public class LevelHintListener : MonoBehaviour
{
    void OnEnable(){ TrySubscribe(); }

    void Start(){ TrySubscribe(); }

    private void TrySubscribe() {
        if (GameManager.Instance != null) GameManager.Instance.OnPlayerLevelChanged -= HandleLevelChanged;
        if (GameManager.Instance != null) GameManager.Instance.OnPlayerLevelChanged += HandleLevelChanged;
    }

    void OnDisable() {
        if (GameManager.Instance != null) GameManager.Instance.OnPlayerLevelChanged -= HandleLevelChanged;
    }

    private void HandleLevelChanged(int newLevel) {
        if (newLevel == 2) {
            Debug.Log("LevelHintListener level 2");
            UIHintManager.Instance?.ShowHint(
                "You’ve unlocked Manipulation! Hold Right Mouse to lift objects."
            );
        }
    }
    
}
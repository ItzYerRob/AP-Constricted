using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class UIHintManager : MonoBehaviour
{
    public static UIHintManager Instance { get; private set; }

    [Header("Hint UI References")]
    [SerializeField] private CanvasGroup hintCanvasGroup;
    [SerializeField] private TMP_Text hintText;

    [Header("Display Settings")]
    [SerializeField] private float displayDuration = 4f;
    [SerializeField] private float fadeDuration = 1f;

    private Coroutine currentRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (hintCanvasGroup != null) hintCanvasGroup.alpha = 0f;
    }

    //Displays a temporary hint message that fades out automatically.
    public void ShowHint(string message) {
        if (hintText == null || hintCanvasGroup == null) {
            Debug.LogWarning("UIHintManager missing references.");
            return;
        }

        if (currentRoutine != null) StopCoroutine(currentRoutine);

        currentRoutine = StartCoroutine(ShowHintRoutine(message));
    }

    private IEnumerator ShowHintRoutine(string message) {
        hintText.text = message;

        //Fade in
        float t = 0f;
        while (t < fadeDuration) {
            t += Time.deltaTime;
            hintCanvasGroup.alpha = Mathf.Lerp(0f, 1f, t / fadeDuration);
            yield return null;
        }
        hintCanvasGroup.alpha = 1f;

        yield return new WaitForSeconds(displayDuration);

        //Fade out
        t = 0f;
        while (t < fadeDuration) {
            t += Time.deltaTime;
            hintCanvasGroup.alpha = Mathf.Lerp(1f, 0f, t / fadeDuration);
            yield return null;
        }
        hintCanvasGroup.alpha = 0f;
        currentRoutine = null;
    }
}

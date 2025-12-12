using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
using TMPro;
#endif

public enum QTEResult { None, Success, Fail }

public struct QTEOverrides {
    //Timing/visuals (unchanged from your manager version)
    public float? qteDuration;
    public AnimationCurve fillCurve;
    public bool overrideFillCurve;
    public float? minInnerScale, maxInnerScale;
    public float? successThreshold;
    public bool? earlyPressFails;
    public bool? autoSucceedOnThreshold;
    public KeyCode? successKey; //Still respected if no multi-key array provided
    public Color? baseColor, fullColor;

    //Multi-key/sequence specifics
    public KeyCode[] forcedKeys;   //if provided, overrides random selection
    public bool? allowDuplicates;
    public int? keysToPick;
    public QTEKeyMode? keyMode;
}


public class QTEManager : MonoBehaviour {
    public static QTEManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject root;
    public Image innerCircle;
    public Image outerRing;

    [Tooltip("Text label that shows the key(s) to press.")]
    public TextMeshProUGUI keyLabel;

    [Header("Defaults")]
    public QTEConfig defaultConfig;

    private bool _active;
    private ILockable _currentLockable;

    void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (root == null || innerCircle == null) {
            Debug.LogError("[QTEManager] UI references not assigned.");
            enabled = false;
            return;
        }

        innerCircle.type = Image.Type.Filled;
        if (innerCircle.fillMethod != Image.FillMethod.Radial360)
            innerCircle.fillMethod = Image.FillMethod.Radial360;

        root.SetActive(false);
    }

    public bool RequestQTE(ILockable requester, QTEOverrides? overrides = null) {
        if (_active) return false;
        StartCoroutine(RunQTECoroutine(requester, Resolve(overrides)));
        return true;
    }

    private (float duration, AnimationCurve curve, float minS, float maxS,
             float threshold, bool earlyFail, bool autoSucceed,
             Color baseC, Color fullC,
             List<KeyCode> activeKeys, QTEKeyMode mode) Resolve(QTEOverrides? o)
    {
        var cfg = defaultConfig;

        float duration = o?.qteDuration ?? cfg.qteDuration;
        var curve = (o?.overrideFillCurve ?? false) ? o.Value.fillCurve : cfg.fillCurve;
        float minS = o?.minInnerScale ?? cfg.minInnerScale;
        float maxS = o?.maxInnerScale ?? cfg.maxInnerScale;
        float threshold = o?.successThreshold ?? cfg.successThreshold;
        bool earlyFail = o?.earlyPressFails ?? cfg.earlyPressFails;
        bool autoSucceed = o?.autoSucceedOnThreshold ?? cfg.autoSucceedOnThreshold;
        var baseC = o?.baseColor ?? cfg.baseColor;
        var fullC = o?.fullColor ?? cfg.fullColor;

        //Determine keys:
        List<KeyCode> activeKeys = new List<KeyCode>();
        QTEKeyMode mode = o?.keyMode ?? cfg.keyMode;

        if (o.HasValue && o.Value.forcedKeys != null && o.Value.forcedKeys.Length > 0) {
            activeKeys.AddRange(o.Value.forcedKeys);
        }
        else
        {
            //Fallback: single key from legacy field OR random from pool
            var pool = (cfg.keyPool != null && cfg.keyPool.Length > 0)
                ? cfg.keyPool
                : new KeyCode[] { cfg.successKey };

            int count = Mathf.Max(1, o?.keysToPick ?? cfg.keysToPick);
            bool allowDup = o?.allowDuplicates ?? cfg.allowDuplicates;

            if (allowDup)
            {
                for (int i = 0; i < count; i++)
                    activeKeys.Add(pool[Random.Range(0, pool.Length)]);
            }
            else
            {
                //Sample without replacement
                List<KeyCode> bag = new List<KeyCode>(pool);
                for (int i = 0; i < count && bag.Count > 0; i++)
                {
                    int idx = Random.Range(0, bag.Count);
                    activeKeys.Add(bag[idx]);
                    bag.RemoveAt(idx);
                }
            }
        }

        if (activeKeys.Count == 0)
            activeKeys.Add(cfg.successKey); //Absolute fallback

        return (duration, curve, minS, maxS, threshold, earlyFail, autoSucceed,
                baseC, fullC, activeKeys, mode);
    }

    private IEnumerator RunQTECoroutine(ILockable requester,
        (float duration, AnimationCurve curve, float minS, float maxS,
         float threshold, bool earlyFail, bool autoSucceed,
         Color baseC, Color fullC,
         List<KeyCode> activeKeys, QTEKeyMode mode) p)
    {
        _active = true;
        _currentLockable = requester;

        //UI init
        root.SetActive(true);
        innerCircle.fillAmount = 0f;
        innerCircle.color = p.baseC;
        SetInnerScale(p.minS);
        if (outerRing != null) outerRing.enabled = true;

        //Label for keys
        if (keyLabel != null) {
            keyLabel.text = BuildKeyLabel(p.activeKeys, p.mode);
            keyLabel.enabled = true;
        }

        float elapsed = 0f;
        float lastFill = 0f;

        int seqIndex = 0; //for AllInOrder

        while (elapsed < p.duration) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / p.duration);
            float progress = Mathf.Clamp01(p.curve.Evaluate(t));

            innerCircle.fillAmount = progress;
            innerCircle.color = Color.Lerp(p.baseC, p.fullC, progress);
            SetInnerScale(Mathf.Lerp(p.minS, p.maxS, progress));

            //Auto success when crossing threshold
            if (p.autoSucceed && lastFill < p.threshold && progress >= p.threshold) {
                Finish(QTEResult.Success);
                yield break;
            }

            //Input handling
            if (progress >= p.threshold) {
                if (p.mode == QTEKeyMode.AnyOne)
                {
                    if (AnyKeyDown(p.activeKeys))
                    {
                        Finish(QTEResult.Success);
                        yield break;
                    }
                }
                else { //AllInOrder
                    //Must press keys in order, early wrong press can optionally fail via earlyFail
                    if (AnyKeyDown(p.activeKeys))
                    {
                        if (Input.GetKeyDown(p.activeKeys[seqIndex]))
                        {
                            seqIndex++;
                            UpdateSequenceLabel(seqIndex, p.activeKeys);
                            if (seqIndex >= p.activeKeys.Count)
                            {
                                Finish(QTEResult.Success);
                                yield break;
                            }
                        }
                        else if (defaultConfig.earlyPressFails) //Use same flag for simplicity
                        {
                            Finish(QTEResult.Fail);
                            yield break;
                        }
                    }
                }
            }
            else {
                //Pre-threshold behaviour
                if (AnyKeyDown(p.activeKeys) && defaultConfig.earlyPressFails) {
                    Finish(QTEResult.Fail);
                    yield break;
                }
            }

            lastFill = progress;
            yield return null;
        }

        Finish(QTEResult.Fail);
        yield break;

        //Local helpers to close the QTE and notify
        void Finish(QTEResult result)
        {
            root.SetActive(false);
            if (keyLabel != null) keyLabel.enabled = false;

            var target = _currentLockable;
            _currentLockable = null;
            _active = false;

            if (target != null)
            {
                if (result == QTEResult.Success)
                {
                    target.Locked = false;
                    target.OnUnlockSucceeded();
                }
                else
                {
                    target.OnUnlockFailed();
                }
            }
        }
    }

    private void SetInnerScale(float s)
    {
        var tr = innerCircle.rectTransform;
        tr.localScale = new Vector3(s, s, 1f);
    }

    private static bool AnyKeyDown(List<KeyCode> keys)
    {
        for (int i = 0; i < keys.Count; i++)
            if (Input.GetKeyDown(keys[i])) return true;
        return false;
    }

    private static string BuildKeyLabel(List<KeyCode> keys, QTEKeyMode mode)
    {
        //"SPACE" or "E / F" or "E F Q" for sequences
        if (keys == null || keys.Count == 0) return "";
        if (mode == QTEKeyMode.AllInOrder) {
            return string.Join(" \u2192 ", keys.ConvertAll(DisplayName)); //Arrow
        }
        return string.Join(" / ", keys.ConvertAll(DisplayName));
    }

    private static string DisplayName(KeyCode k)
    {
        //Make KeyCode names friendlier
        string s = k.ToString();
        //Common prettifications
        if (s.StartsWith("Alpha")) return s.Substring(5); //Alpha1 -> 1
        if (s == "Return") return "Enter";
        if (s == "LeftShift") return "LShift";
        if (s == "RightShift") return "RShift";
        if (s == "LeftControl") return "LCtrl";
        if (s == "RightControl") return "RCtrl";
        if (s == "LeftAlt") return "LAlt";
        if (s == "RightAlt") return "RAlt";
        return s.ToUpperInvariant();
    }

    private void UpdateSequenceLabel(int seqIndex, List<KeyCode> keys)
    {
        if (keyLabel == null) return;
        //Grey-out completed keys with brackets, ex, "[E] F Q"
        var parts = new List<string>(keys.Count);
        for (int i = 0; i < keys.Count; i++) {
            var name = DisplayName(keys[i]);
            parts.Add(i < seqIndex ? $"[{name}]" : name);
        }
        keyLabel.text = string.Join(" \u2192 ", parts);
    }
}
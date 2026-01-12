using UnityEngine;

public enum QTEKeyMode
{
    AnyOne,      //Success if any one of the shown keys is pressed (default)
    AllInOrder   //Must press the shown keys in order (sequence)
}

[CreateAssetMenu(menuName = "QTE/QTE Config", fileName = "QTE_DefaultConfig")]
public class QTEConfig : ScriptableObject
{
    [Header("Timing & Curves")]
    public float qteDuration = 2f;
    public AnimationCurve fillCurve = AnimationCurve.Linear(0, 0, 1, 1);
    public float minInnerScale = 0.85f;
    public float maxInnerScale = 1.15f;

    [Header("Success Logic")]
    [Range(0f, 1f)] public float successThreshold = 0.7f;
    public bool earlyPressFails = true;
    public bool autoSucceedOnThreshold = false;

    [Header("Input & Colors")]
    public KeyCode successKey = KeyCode.Space;
    public Color baseColor = Color.white;
    public Color fullColor = Color.red;

    [Header("Multi-key / Random Selection")]
    [Tooltip("Pool to randomly pick keys from when no override is provided.")]
    public KeyCode[] keyPool = new KeyCode[] {KeyCode.H, KeyCode.F, KeyCode.J };

    [Tooltip("How many keys to pick from the pool for a QTE (1 = show one key).")]
    [Min(1)] public int keysToPick = 1;

    [Tooltip("Whether repeated keys are allowed when picking multiple.")]
    public bool allowDuplicates = false;

    [Tooltip("How the chosen keys must be pressed.")]
    public QTEKeyMode keyMode = QTEKeyMode.AnyOne;
}

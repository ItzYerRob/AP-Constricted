// PlayerRow.cs
using TMPro;
using UnityEngine;

public class PlayerRow : MonoBehaviour
{
    [SerializeField] TMP_Text nameText;
    [SerializeField] TMP_Text statusText;

    //Simple binder; extend as needed (ping?)
    public void Bind(string displayName, string status, bool isLocal)
    {
        nameText.text  = isLocal ? $"{displayName} (You)" : displayName;
        statusText.text = status;
    }
}
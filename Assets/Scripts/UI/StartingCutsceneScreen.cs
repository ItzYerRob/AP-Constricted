using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class StartingCutsceneScreen : MonoBehaviour
{
    void Awake() {
        this.gameObject.SetActive(true);
    }

    void Update() {
        if (Input.GetKeyDown(KeyCode.E)) {
            Destroy(this.gameObject, 0.5f);
        }
    }
}

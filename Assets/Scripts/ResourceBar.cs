using UnityEngine;
using UnityEngine.UI;

public class ResourceBar : MonoBehaviour
{
    public Slider sliderHealth, sliderStamina;

    public SpriteRenderer healthBarRenderer, staminaBarRenderer;
    public float originalHealthWidth = 2.0f, originalStaminaWidth = 2.0f;

    //Interpreted as HUD vs world-space bar, not network
    public bool AreWeAPlayer = false;

    private void Start() {
        if (!AreWeAPlayer) {
            if (healthBarRenderer == null)
                healthBarRenderer = GetComponent<SpriteRenderer>();

            originalHealthWidth = healthBarRenderer.transform.localScale.x;
            healthBarRenderer.color = Color.green;
        }
    }

    public void SetMaxHealth(int maxhealth, CharacterStats characterStats) {
        if (AreWeAPlayer) {
            if (sliderHealth != null) {
                sliderHealth.maxValue = maxhealth;
                sliderHealth.value = maxhealth;
            }
        }
        else {
            float newWidth = originalHealthWidth * ((float)maxhealth / characterStats.GetMaxHealth());
            var newScale = healthBarRenderer.transform.localScale;
            newScale.x = newWidth;
            healthBarRenderer.transform.localScale = newScale;
        }
    }

    public void SetHealth(int health, CharacterStats characterStats) {
        if (AreWeAPlayer) {
            if (sliderHealth != null) sliderHealth.value = health;
        }
        else {
            float healthPercentage = (float)health / characterStats.GetMaxHealth();
            float newWidth = originalHealthWidth * healthPercentage;

            healthBarRenderer.color = Color.Lerp(Color.red, Color.green, healthPercentage);

            var newScale = healthBarRenderer.transform.localScale;
            newScale.x = newWidth;
            healthBarRenderer.transform.localScale = newScale;
        }
    }

    public void SetMaxStamina(int maxstamina, CharacterStats characterStats) {
        if (AreWeAPlayer && sliderStamina != null) {
            sliderStamina.maxValue = maxstamina;
            sliderStamina.value = maxstamina;
        }
    }

    public void SetStamina(int stamina) {
        if (AreWeAPlayer && sliderStamina != null) { sliderStamina.value = stamina; }
    }
}
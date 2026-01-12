using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class EnemyAudioController : MonoBehaviour
{
    [Header("Audio Clips")]
    public AudioClip idleClipA;
    public AudioClip idleClipB;
    public AudioClip pursueClip;

    [Header("Settings")]
    public float idleSwitchInterval = 1.0f; // Time between alternating idle clips

    private AudioSource audioSource;
    private EnemyNavmeshMotor motor;
    private Coroutine idleCoroutine;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        // Find EnemyNavmeshMotor on parent
        motor = GetComponentInParent<EnemyNavmeshMotor>();
        if (motor == null)
        {
            Debug.LogError("EnemyAudioController: No EnemyNavmeshMotor found in parent hierarchy!");
        }

        audioSource.loop = false; // We'll handle looping manually
    }

    void OnEnable()
    {
        StartIdleSounds();
    }

    void OnDisable()
    {
        StopAllCoroutines();
        audioSource.Stop();
    }

    void Update()
    {
        if (motor == null) return;

        // Check if pursuing (has a target)
        if (motor.target != null)
        {
            // Switch to pursue clip if not already playing
            if (audioSource.clip != pursueClip || !audioSource.isPlaying)
            {
                PlayPursueSound();
            }
        }
        else
        {
            // Switch back to idle if not already
            if (idleCoroutine == null)
            {
                StartIdleSounds();
            }
        }
    }

    private void StartIdleSounds()
    {
        StopAllCoroutines();
        idleCoroutine = StartCoroutine(IdleSoundRoutine());
    }

    private IEnumerator IdleSoundRoutine()
    {
        while (true)
        {
            // Play clip A
            audioSource.clip = idleClipA;
            audioSource.Play();
            yield return new WaitForSeconds(idleClipA.length + idleSwitchInterval);

            // Play clip B
            audioSource.clip = idleClipB;
            audioSource.Play();
            yield return new WaitForSeconds(idleClipB.length + idleSwitchInterval);
        }
    }

    private void PlayPursueSound()
    {
        StopAllCoroutines();
        idleCoroutine = null;
        audioSource.clip = pursueClip;
        audioSource.loop = true; // Pursue sound loops continuously
        audioSource.Play();
    }
}

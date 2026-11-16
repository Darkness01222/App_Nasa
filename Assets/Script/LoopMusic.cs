
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class LoopMusic : MonoBehaviour
{
    [Header("Configuracion de audio")]
    public AudioClip audioClip;

    [Range(0f, 1f)]
    public float volume = 0.5f;

    public bool playOnStart = true;

    private AudioSource audioSource;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        audioSource.playOnAwake = true;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f; // Audio 2D
    }

    void Start()
    {
        audioSource.clip = audioClip;
        audioSource.volume = volume;

        if (playOnStart)
            PlaySound();
    }

    public void PlaySound()
    {
        if (audioSource.clip != null && !audioSource.isPlaying)
            audioSource.Play();
    }

    public void StopSound()
    {
        audioSource.Stop();
    }

    public void PauseSound()
    {
        audioSource.Pause();
    }

    public void ResumeSound()
    {
        audioSource.UnPause();
    }

    void Update()
    {
        audioSource.volume = volume;
    }
}

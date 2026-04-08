using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Shared clips")]
    [SerializeField] private AudioClip defaultBondSuccess;
    [SerializeField] private AudioClip grabClip;
    [SerializeField] private AudioClip resetClip;
    [SerializeField] private AudioClip proximityClip;

    [Range(0f, 1f)][SerializeField] private float sfxVolume = 0.9f;

    private AudioSource _src;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        _src = GetComponent<AudioSource>();
    }

    // Called by BondManager — uses per-recipe clip if set, else default
    public void PlayBondSuccess(AudioClip overrideClip = null)
        => Play(overrideClip != null ? overrideClip : defaultBondSuccess);

    public void PlayGrab() => Play(grabClip);
    public void PlayReset() => Play(resetClip);
    public void PlayProximity() => Play(proximityClip, 0.4f);

    private void Play(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null) return;
        _src.PlayOneShot(clip, sfxVolume * volumeScale);
    }
}

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// First-person briefing cutscene controller.
///
/// Plays an ordered sequence of camera shots with subtitle text.
/// Each shot moves a dedicated briefing camera to a predefined Transform,
/// optionally plays a voice-over AudioClip, and waits a set duration.
///
/// The player can skip the entire briefing with the configured key (default: Space).
///
/// Usage:
///   1. Assign briefingCamera and (optionally) briefingUI.
///   2. Add BriefingShot entries; each references a scene Transform as the camera anchor.
///   3. Call PlayBriefing(callback) — PvPMatchManager does this automatically.
///   4. Hook OnSubtitleChanged to your UI Text/TextMeshPro component.
/// </summary>
public class PvPBriefingController : MonoBehaviour
{
    // ─── Shot Data ────────────────────────────────────────────────────────

    [System.Serializable]
    public class BriefingShot
    {
        [Tooltip("Transform used as camera position/rotation for this shot")]
        public Transform cameraAnchor;

        [TextArea(2, 6)]
        public string subtitleText;

        [Tooltip("Time (seconds) to hold this shot")]
        public float holdTime = 3f;

        [Tooltip("Optional voice-over clip")]
        public AudioClip voiceLine;
    }

    // ─── Inspector Fields ─────────────────────────────────────────────────

    [Header("Sequence")]
    [SerializeField] private BriefingShot[] shots;
    [SerializeField] private float          fadeInTime  = 0.5f;
    [SerializeField] private float          fadeOutTime = 0.5f;

    [Header("References")]
    [SerializeField] private Camera     briefingCamera;
    [SerializeField] private GameObject briefingUI;
    [SerializeField] private AudioSource audioSource;

    [Header("Skip")]
    [SerializeField] private KeyCode skipKey = KeyCode.Space;

    // ─── Events ───────────────────────────────────────────────────────────

    [Header("Events")]
    /// <summary>Fires when subtitle text should change (hook to UI Text).</summary>
    public UnityEvent<string> OnSubtitleChanged;
    public UnityEvent         OnBriefingStarted;
    public UnityEvent         OnBriefingCompleted;

    // ─── State ────────────────────────────────────────────────────────────

    private bool             isPlaying;
    private Action           onCompleteCallback;
    private Coroutine        playCoroutine;

    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void Update()
    {
        if (isPlaying && Input.GetKeyDown(skipKey))
            SkipBriefing();
    }

    // ─── Public API ───────────────────────────────────────────────────────

    /// <summary>
    /// Start the briefing sequence. <paramref name="onComplete"/> is called when finished (or skipped).
    /// </summary>
    public void PlayBriefing(Action onComplete = null)
    {
        onCompleteCallback = onComplete;

        if (playCoroutine != null)
            StopCoroutine(playCoroutine);

        playCoroutine = StartCoroutine(PlaySequence());
    }

    /// <summary>Skip to the end of the briefing immediately.</summary>
    public void SkipBriefing()
    {
        isPlaying = false;

        if (playCoroutine != null)
        {
            StopCoroutine(playCoroutine);
            playCoroutine = null;
        }

        CompleteBriefing();
    }

    // ─── Sequence Coroutine ───────────────────────────────────────────────

    private IEnumerator PlaySequence()
    {
        isPlaying = true;

        if (briefingCamera != null) briefingCamera.gameObject.SetActive(true);
        if (briefingUI     != null) briefingUI.SetActive(true);

        OnBriefingStarted?.Invoke();

        if (shots != null && shots.Length > 0)
        {
            foreach (var shot in shots)
            {
                if (!isPlaying) break;
                yield return StartCoroutine(PlayShot(shot));
            }
        }
        else
        {
            // No shots configured — brief pause then continue
            yield return new WaitForSeconds(2f);
        }

        CompleteBriefing();
    }

    private IEnumerator PlayShot(BriefingShot shot)
    {
        // Position the briefing camera
        if (briefingCamera != null && shot.cameraAnchor != null)
        {
            briefingCamera.transform.SetPositionAndRotation(
                shot.cameraAnchor.position,
                shot.cameraAnchor.rotation);
        }

        // Update subtitle
        OnSubtitleChanged?.Invoke(shot.subtitleText ?? string.Empty);

        // Fade-in delay
        yield return new WaitForSeconds(fadeInTime);

        // Voice-over
        if (shot.voiceLine != null && audioSource != null)
            audioSource.PlayOneShot(shot.voiceLine);

        // Hold
        float elapsed = 0f;
        while (isPlaying && elapsed < shot.holdTime)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Fade-out delay
        yield return new WaitForSeconds(fadeOutTime);
    }

    // ─── Completion ───────────────────────────────────────────────────────

    private void CompleteBriefing()
    {
        isPlaying = false;

        if (briefingCamera != null) briefingCamera.gameObject.SetActive(false);
        if (briefingUI     != null) briefingUI.SetActive(false);

        OnSubtitleChanged?.Invoke(string.Empty);
        OnBriefingCompleted?.Invoke();
        onCompleteCallback?.Invoke();
        onCompleteCallback = null;
    }
}

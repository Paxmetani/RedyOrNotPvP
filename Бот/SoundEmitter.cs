using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Система воспроизведения звуков для AI и игрока
/// СИНГЛТОН: Глобальный доступ через SoundEmitter.Instance
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class SoundEmitter : MonoBehaviour
{
    [System.Serializable]
    public class SoundClip
    {
        public string soundName;
        public AudioClip[] clips; // Массив для вариаций
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.5f, 1.5f)] public float pitchMin = 0.9f;
        [Range(0.5f, 1.5f)] public float pitchMax = 1.1f;
    }

    [Header("Sound Library")]
    [SerializeField] private List<SoundClip> soundLibrary = new List<SoundClip>();

    [Header("Settings")]
    [SerializeField] private float minTimeBetweenSounds = 0.1f;
    [SerializeField] private bool dontDestroyOnLoad = true;

    private AudioSource audioSource;
    private float lastSoundTime;
    private Dictionary<string, SoundClip> soundDictionary;

    private static SoundEmitter instance;

    /// <summary>
    /// Получить глобальный экземпляр SoundEmitter
    /// </summary>
    public static SoundEmitter Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<SoundEmitter>();
                if (instance == null)
                {
                    Debug.LogError("[SoundEmitter] Instance not found in scene!");
                }
            }
            return instance;
        }
    }

    private void Awake()
    {
        // Синглтон логика
        if (instance != null && instance != this)
        {
            Debug.LogWarning("[SoundEmitter] Multiple instances detected. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);
        }

        audioSource = GetComponent<AudioSource>();

        // Создание словаря для быстрого доступа
        soundDictionary = new Dictionary<string, SoundClip>();
        foreach (var sound in soundLibrary)
        {
            if (!soundDictionary.ContainsKey(sound.soundName))
            {
                soundDictionary.Add(sound.soundName, sound);
            }
        }
    }

    public void PlaySound(string soundName)
    {
        if (Time.time - lastSoundTime < minTimeBetweenSounds) return;

        if (soundDictionary.TryGetValue(soundName, out SoundClip sound))
        {
            if (sound.clips.Length == 0) return;

            AudioClip clip = sound.clips[Random.Range(0, sound.clips.Length)];
            float pitch = Random.Range(sound.pitchMin, sound.pitchMax);

            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, sound.volume);

            lastSoundTime = Time.time;
        }
        else
        {
            Debug.LogWarning($"Sound '{soundName}' not found in library!");
        }
    }

    /// <summary>
    /// Статический метод для быстрого вызова звука
    /// </summary>
    public static void Play(string soundName)
    {
        if (Instance != null)
        {
            Instance.PlaySound(soundName);
        }
    }

    public void PlaySoundAtPosition(string soundName, Vector3 position)
    {
        if (soundDictionary.TryGetValue(soundName, out SoundClip sound))
        {
            if (sound.clips.Length == 0) return;

            AudioClip clip = sound.clips[Random.Range(0, sound.clips.Length)];
            AudioSource.PlayClipAtPoint(clip, position, sound.volume);
        }
    }

    /// <summary>
    /// Статический метод для воспроизведения звука в позиции
    /// </summary>
    public static void PlayAt(string soundName, Vector3 position)
    {
        if (Instance != null)
        {
            Instance.PlaySoundAtPosition(soundName, position);
        }
    }

    public void StopAllSounds()
    {
        audioSource.Stop();
    }

    /// <summary>
    /// Статический метод для остановки всех звуков
    /// </summary>
    public static void StopAll()
    {
        if (Instance != null)
        {
            Instance.StopAllSounds();
        }
    }
}
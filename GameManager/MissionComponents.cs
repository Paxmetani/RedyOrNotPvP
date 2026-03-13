using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Улика которую можно собрать
/// </summary>
public class EvidenceItem : MonoBehaviour, IInteractable
{
    [Header("Evidence Info")]
    public string evidenceId;
    [TextArea(2, 4)]
    public string description = "Evidence";

    [Header("Settings")]
    [SerializeField] private bool destroyOnCollect = true;
    [SerializeField] private GameObject collectEffect;
    [SerializeField] private AudioClip collectSound;

    [Header("Events")]
    public UnityEvent OnCollected;

    private bool isCollected = false;
    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    public void Interact()
    {
        Collect();
    }

    public void StopInteract() { }

    public void Collect()
    {
        if (isCollected) return;
        isCollected = true;

        // Эффект
        if (collectEffect != null)
            Instantiate(collectEffect, transform.position, Quaternion.identity);

        // Звук
        if (collectSound != null)
            audioSource.PlayOneShot(collectSound);

        OnCollected?.Invoke();

        if (destroyOnCollect)
        {
            // Небольшая задержка чтобы звук успел проиграться
            Destroy(gameObject, collectSound != null ? collectSound.length : 0.1f);
        }
    }

    public bool IsCollected => isCollected;
}

/// <summary>
/// Зона для выполнения цели (достичь точки, защитить область и т.д.)
/// </summary>
public class ObjectiveZone : MonoBehaviour
{
    [Header("Objective")]
    public string objectiveId;
    public ObjectiveZoneType zoneType = ObjectiveZoneType.ReachZone;

    [Header("Settings")]
    [SerializeField] private bool requireAllPlayers = false;
    [SerializeField] private float holdTime = 0f; // Время удержания
    [SerializeField] private bool showProgress = true;

    [Header("Visuals")]
    [SerializeField] private GameObject activeVisual;
    [SerializeField] private GameObject completedVisual;

    [Header("Events")]
    public UnityEvent OnPlayerEntered;
    public UnityEvent OnPlayerExited;
    public UnityEvent OnObjectiveCompleted;
    public UnityEvent<float> OnProgressChanged;

    private bool isCompleted = false;
    private bool playerInZone = false;
    private float currentHoldTime = 0f;

    private void Update()
    {
        if (isCompleted) return;

        if (zoneType == ObjectiveZoneType.HoldZone && playerInZone)
        {
            currentHoldTime += Time.deltaTime;
            
            float progress = holdTime > 0 ? currentHoldTime / holdTime : 1f;
            OnProgressChanged?.Invoke(progress);

            if (currentHoldTime >= holdTime)
            {
                CompleteObjective();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isCompleted) return;

        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            OnPlayerEntered?.Invoke();

            if (zoneType == ObjectiveZoneType.ReachZone)
            {
                CompleteObjective();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (isCompleted) return;

        if (other.CompareTag("Player"))
        {
            playerInZone = false;
            OnPlayerExited?.Invoke();

            // Сбросить прогресс если вышел
            if (zoneType == ObjectiveZoneType.HoldZone)
            {
                currentHoldTime = 0f;
                OnProgressChanged?.Invoke(0f);
            }
        }
    }

    public void CompleteObjective()
    {
        if (isCompleted) return;
        isCompleted = true;

        // Визуал
        if (activeVisual != null)
            activeVisual.SetActive(false);
        if (completedVisual != null)
            completedVisual.SetActive(true);

        OnObjectiveCompleted?.Invoke();
    }

    public bool IsCompleted => isCompleted;
    public float Progress => holdTime > 0 ? currentHoldTime / holdTime : (isCompleted ? 1f : 0f);
}

public enum ObjectiveZoneType
{
    ReachZone,      // Просто дойти до точки
    HoldZone,       // Удерживать позицию
    ExtractionZone  // Точка эвакуации
}

/// <summary>
/// Точка эвакуации
/// </summary>
public class ExtractionZone : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float extractionTime = 5f;
    [SerializeField] private bool requireObjectivesComplete = true;

    [Header("UI")]
    [SerializeField] private GameObject extractionUI;
    [SerializeField] private UnityEngine.UI.Image progressBar;

    [Header("Events")]
    public UnityEvent OnExtractionStarted;
    public UnityEvent OnExtractionCancelled;
    public UnityEvent OnExtractionComplete;

    private bool playerInZone = false;
    private bool isExtracting = false;
    private float extractionProgress = 0f;

    private void Update()
    {
        if (playerInZone && isExtracting)
        {
            extractionProgress += Time.deltaTime / extractionTime;

            if (progressBar != null)
                progressBar.fillAmount = extractionProgress;

            if (extractionProgress >= 1f)
            {
                CompleteExtraction();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = true;
            TryStartExtraction();
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Player") && !isExtracting)
        {
            TryStartExtraction();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInZone = false;
            CancelExtraction();
        }
    }

    private void TryStartExtraction()
    {
        if (isExtracting) return;

        // Проверить можно ли эвакуироваться
        if (requireObjectivesComplete)
        {
            var gm = GameManagerTactical.Instance;
            if (gm != null && gm.CurrentMission != null)
            {
                foreach (var obj in gm.CurrentMission.objectives)
                {
                    if (obj.isRequired && !obj.isCompleted)
                    {
                        // Показать сообщение
                        var ui = FindFirstObjectByType<TacticalUIManager>();
                        ui?.ShowExtractionDenied();
                        return;
                    }
                }
            }
        }

        StartExtraction();
    }

    private void StartExtraction()
    {
        isExtracting = true;
        extractionProgress = 0f;

        if (extractionUI != null)
            extractionUI.SetActive(true);

        OnExtractionStarted?.Invoke();
    }

    private void CancelExtraction()
    {
        if (!isExtracting) return;

        isExtracting = false;
        extractionProgress = 0f;

        if (extractionUI != null)
            extractionUI.SetActive(false);

        if (progressBar != null)
            progressBar.fillAmount = 0f;

        OnExtractionCancelled?.Invoke();
    }

    private void CompleteExtraction()
    {
        isExtracting = false;

        if (extractionUI != null)
            extractionUI.SetActive(false);

        OnExtractionComplete?.Invoke();

        // Завершить миссию
        GameManagerTactical.Instance?.RequestExtraction();
    }
}

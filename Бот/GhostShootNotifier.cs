using UnityEngine;

/// <summary>
/// FIXED: ���������� AI � ������ � ����������� ������
/// </summary>
public class GunshotNotifier : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float notificationRadius = 30f;
    [SerializeField] private LayerMask aiLayer;

    [Header("Sound Ranges")]
    [SerializeField] private float gunshotRange = 60f;
    [SerializeField] private float footstepRange = 15f;
    [SerializeField] private float explosionRange = 80f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = false;

    /// <summary>
    /// IMPROVED: ��������� � ����� ��������
    /// </summary>
    public void NotifyGunshot(Vector3 position, float volume = 1f, HealthManager shooter = null)
    {
        NotifySound(position, SoundType.Gunshot, volume, shooter);
    }

    /// <summary>
    /// MAIN:  ��������� � ����� � ����������� ������
    /// </summary>
    public void NotifySound(Vector3 position, SoundType soundType, float volume = 1f, HealthManager source = null)
    {
        // ���������� ������
        float effectiveRange = GetSoundRange(soundType) * volume;

        // ����� ���� AI � �������
        Collider[] nearbyColliders = Physics.OverlapSphere(position, effectiveRange, aiLayer);

        int notifiedCount = 0;

        foreach (var col in nearbyColliders)
        {
            SmartEnemyAI ai = col.GetComponent<SmartEnemyAI>();
            if (ai == null)
                ai = col.GetComponentInParent<SmartEnemyAI>();

            if (ai == null || ai.IsDead()) continue;

            // ��������: ������ �������
            if (source != null)
            {
                if (TeamResolver.IsAlly(source, ai))
                {
                    if (showDebugLogs && Time.frameCount % 120 == 0)
                        Debug.Log($"[Sound] {ai.name} - Ignored {soundType} from ally {source.name}");

                    continue; // �� ��������� �� ���������
                }
            }

            // ���������� ������������� � ������ ����������
            float distance = Vector3.Distance(position, ai.Transform.position);
            float intensity = CalculateIntensity(distance, effectiveRange, volume);

            // ��������� AI
            if (ai.Perception != null)
            {
                ai.Perception.OnHearSound(position, soundType, intensity);
                notifiedCount++;
            }
        }

        if (showDebugLogs && notifiedCount > 0)
        {
            Debug.Log($"[Sound] {soundType} at {position} - Notified {notifiedCount} AI");
        }
    }

    private float GetSoundRange(SoundType type)
    {
        switch (type)
        {
            case SoundType.Gunshot: return gunshotRange;
            case SoundType.Footsteps: return footstepRange;
            case SoundType.Explosion: return explosionRange;
            case SoundType.VoiceShout: return 25f;
            default: return notificationRadius;
        }
    }

    private float CalculateIntensity(float distance, float maxRange, float volume)
    {
        float distanceFactor = 1f - (distance / maxRange);
        distanceFactor = Mathf.Clamp01(distanceFactor);

        return distanceFactor * volume;
    }
}

public enum SoundType
{
    None,
    Footsteps,
    Gunshot,
    Explosion,
    VoiceShout,
    Alert
}

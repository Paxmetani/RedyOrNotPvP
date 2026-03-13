using System.Collections;
using UnityEngine;

public class GrenadeCase : MonoBehaviour
{
    [Header("Grenade Settings")]
    public float fuseTime = 3f;          // Время до взрыва
    public float effectDuration = 8f;    // Длительность эффекта после взрыва

    [Header("References")]
    public Rigidbody rb;
    public ParticleSystem particle;
    public AudioSource audioSource;
    public AudioClip hitSound, explosionSound;
    public Transform defaultPosition;
    public DamageZone damageZone;

    private Coroutine grenadeLifecycleRoutine;
    private bool isActivated = false;

    void Awake()
    {
        rb = rb ?? GetComponent<Rigidbody>();
        DeactivateGrenade();
    }

    // НОВОЕ: применить параметры оружия (aimAssistStrength -> длительность облака)
    public void ApplyWeaponParams(WeaponObject weapon)
    {
        if (weapon == null) return;
        // используем aimAssistStrength как длительность эффекта (сек)
        effectDuration = Mathf.Clamp(weapon.aimAssistStrength, 0.05f, 60f);

        // опционально: подстроить длительность партиклов под облако
        if (particle != null)
        {
            var main = particle.main;
            // это не меняет визуал радикально, но гарантирует автозавершение
            main.duration = Mathf.Max(main.duration, effectDuration);
        }
    }

    /// <summary>
    /// Бросок гранаты с заданной силой.
    /// </summary>
    public void Throw(Vector3 position, Vector3 force)
    {
        if (isActivated || grenadeLifecycleRoutine != null) return;

        transform.position = position;
        rb.isKinematic = false;
        gameObject.SetActive(true);
        isActivated = false;

        grenadeLifecycleRoutine = StartCoroutine(GrenadeLifecycle(force));
    }

    /// <summary>
    /// Жизненный цикл гранаты: бросок, взрыв, возврат.
    /// </summary>
    private IEnumerator GrenadeLifecycle(Vector3 force)
    {
        rb.AddForce(force, ForceMode.Impulse);

        // Ждём время до взрыва
        yield return new WaitForSeconds(fuseTime);
        ActivateGrenade();

        // Ждём время действия эффекта
        yield return new WaitForSeconds(effectDuration);
        ReturnToDefault();
    }

    /// <summary>
    /// Активация гранаты (взрыв, эффекты, урон).
    /// </summary>
    private void ActivateGrenade()
    {
        if (isActivated) return;
        isActivated = true;

        // Включить партиклы
        if (particle != null)
        {
            particle.gameObject.SetActive(true);
            particle.Play();
        }

        // Включить зону урона (с FlashbangEffect)
        if (damageZone != null)
        {
            damageZone.gameObject.SetActive(true);

            // Если это флешбанг - активировать эффект
            var flashEffect = damageZone.GetComponent<FlashbangEffect>();
            if (flashEffect != null)
            {
                flashEffect.Detonate();
            }
        }

        // Звук взрыва
        if (audioSource != null && explosionSound != null)
        {
            audioSource.PlayOneShot(explosionSound);
        }

        // Уведомить AI о взрыве
        var notifier = damageZone.GetComponent<GunshotNotifier>();
        if (notifier != null)
        {
            notifier.NotifySound(transform.position, SoundType.Explosion, 1.5f);
        }
    }

    /// <summary>
    /// Возврат гранаты в исходное состояние.
    /// </summary>
    public void ReturnToDefault()
    {
      GameObject.Destroy(this.gameObject);
    }

    /// <summary>
    /// Деактивация гранаты без возврата.
    /// </summary>
    private void DeactivateGrenade()
    {
        isActivated = false;

        if (particle != null)
        {
            particle.Stop();
            particle.gameObject.SetActive(false);
        }

        if (damageZone != null)
            damageZone.gameObject.SetActive(false);

        rb.isKinematic = true;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Обработка столкновения гранаты.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.layer == 9 && audioSource != null && hitSound != null)
        {
            audioSource.PlayOneShot(hitSound);
        }
    }
}

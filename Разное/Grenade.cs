using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class Grenade : MonoBehaviour
{
    [Header("Settings")]
    public int WeaponSlot = 4;
    public float throwForce = 15f;
    public float upwardForce = 5f;

    private ItemManager itemManager;
    private WeaponStats weaponStats;
    private WeaponObject weaponItem;
    private Animator animator;

    // Текущие заряды (используем maxMagazineSize как.maximum)
    public int charges => currentCharges;
    private int currentCharges;

    private bool isThrowing = false;
    private bool isReloading = false;
    private float reloadTimer = 0f;

    public bool isMine = false;

    void Start()
    {
        InitializeComponents();
    }

    /// <summary>
    /// Инициализация компонентов.
    /// </summary>
    private void InitializeComponents()
    {
        weaponStats = GetComponent<WeaponStats>();
        itemManager = GetComponentInParent<ItemManager>();
        weaponItem = weaponStats != null ? weaponStats.itemSettings.item as WeaponObject : null;
        animator = GetComponentInChildren<Animator>();

        // Инициализируем заряды
        int max = weaponItem != null ? Mathf.Max(1, weaponItem.maxMagazineSize) : 1;
        currentCharges = max;
        if (weaponStats != null) weaponStats.RestoreMagazineCount(currentCharges);

        // Сброс таймеров
        if (weaponStats != null)
        {
            weaponStats.currentFireTimer = 0f;
            weaponStats.currentReloadTime = 0f;
        }
        isThrowing = false;
        isReloading = false;
        reloadTimer = 0f;

        // Подписка на событие броска
        if (itemManager != null && itemManager.inputManager != null)
            itemManager.inputManager.onFireInputDown.AddListener(Fire);
    }

    /// <summary>
    /// Бросок гранаты.
    /// </summary>
    public void Throw(Vector3 direction, float forceMultiplier = 1f)
    {
        if (weaponItem == null || weaponItem.bulletPref == null) return;
        if (isThrowing) return;
        if (currentCharges <= 0) return; // нет зарядов

        isThrowing = true;

        // Спавн гранаты из префаба
        GameObject grenadeInstance = Instantiate(weaponItem.bulletPref, transform.position, Quaternion.identity);
        if (!isMine)
        {
            GrenadeCase grenadeCase = grenadeInstance.GetComponent<GrenadeCase>();
            if (grenadeCase != null)
            {
                grenadeCase.Throw(transform.position, direction * throwForce * forceMultiplier + Vector3.up * upwardForce);

            }
        }
        else
        {
            ExplosiveMine grenadeCase = grenadeInstance.GetComponent<ExplosiveMine>();
            if (grenadeCase != null)
            {
                grenadeCase.Throw(transform.position, direction * throwForce * forceMultiplier + Vector3.up * upwardForce);
            }
        }

        // Потратить заряд и обновить UI
        currentCharges = Mathf.Max(0, currentCharges - 1);
        if (weaponStats != null) weaponStats.RestoreMagazineCount(currentCharges);

        // Если не полный магазин — запускать перезарядку (по одному заряду раз в reloadDuration)
        if (currentCharges < Mathf.Max(1, weaponItem.maxMagazineSize))
        {
            isReloading = true;
            reloadTimer = 0f;
        }
    }

    /// <summary>
    /// Обновление состояния гранаты.
    /// </summary>
    void Update()
    {
        UpdateWeaponUI();
        ManageAnimations();
        HandleFireTimer();
        HandleReloadTimer();
    }

    /// <summary>
    /// Таймер для броска (задержка между бросками = timeBetweenShots).
    /// </summary>
    private void HandleFireTimer()
    {
        if (!isThrowing) return;

        if (weaponStats != null && weaponItem != null)
        {
            if (weaponStats.currentFireTimer > 0)
            {
                weaponStats.currentFireTimer -= Time.deltaTime;
            }
            else
            {
                // Перезадаём КД между бросками
                weaponStats.currentFireTimer = weaponItem.timeBetweenShots;
                isThrowing = false;
            }
        }
        else
        {
            // Фолбэк — снимем флаг
            isThrowing = false;
        }
    }

    /// <summary>
    /// Таймер перезарядки зарядов: каждые reloadDuration восстанавливается 1 заряд до максимума.
    /// </summary>
    private void HandleReloadTimer()
    {
        if (weaponItem == null) return;

        int max = Mathf.Max(1, weaponItem.maxMagazineSize);
        if (!isReloading || currentCharges >= max) return;

        reloadTimer += Time.deltaTime;

        if (reloadTimer >= weaponItem.reloadDuration)
        {
            reloadTimer = 0f;
            currentCharges = Mathf.Min(currentCharges + 1, max);
            if (weaponStats != null) weaponStats.RestoreMagazineCount(currentCharges);

            if (itemManager != null && itemManager.audioSource != null && weaponItem.reloadSFX != null)
                itemManager.audioSource.PlayOneShot(weaponItem.reloadSFX);

            if (currentCharges >= max)
            {
                isReloading = false;
            }
        }
    }

    /// <summary>
    /// Управление анимациями.
    /// </summary>
    private void ManageAnimations()
    {
        if (animator == null || itemManager == null || itemManager.playerController == null) return;

        if (!isThrowing)
        {
            if (itemManager.playerController.playerState.running)
            {
                animator.SetInteger("State", 1);
            }
            else
            {
                animator.SetInteger("State", 0);
            }
        }
    }

    /// <summary>
    /// Выполнение броска (по инпуту).
    /// </summary>
    private void Fire()
    {
        if (!gameObject.activeSelf) return;
        if (isThrowing) return;
        if (weaponItem == null) return;

        // Нет зарядов — ждём автоперезарядку
        if (currentCharges <= 0)
        {
            isReloading = true;
            itemManager.UIReference.Gadget2UI.SetEmpty(true); // Показать индикатор пустого
            return;
        }

        Vector3 fireDirection = (itemManager != null && itemManager.cameraLook != null)
            ? itemManager.cameraLook.transform.forward
            : transform.forward;

        Throw(fireDirection);
    }

    /// <summary>
    /// Обновление UI оружия.
    /// </summary>
    private void UpdateWeaponUI()
    {
        if (itemManager == null || itemManager.UIReference == null || weaponItem == null) return;

        switch (WeaponSlot)
        {
            case 1:
                itemManager.UIReference.SpecialUI.UpdateUI(weaponItem);
                break;
            case 2:
                itemManager.UIReference.WeaponUI.UpdateUI(weaponItem);
                break;
            case 3:
                itemManager.UIReference.Gadget1UI.UpdateUI(weaponItem);
                break;
            case 4:
                itemManager.UIReference.Gadget2UI.UpdateUI(weaponItem);
                break;
            default:
                itemManager.UIReference.WeaponUI.UpdateUI(weaponItem);
                break;
        }
    }

    private void OnEnable()
    {
        if (itemManager == null)
        {
            itemManager = GetComponentInParent<ItemManager>();
        }
        if (itemManager != null && itemManager.UIReference == null)
        {
            var uiGo = GameObject.FindGameObjectWithTag("UIManager");
            if (uiGo != null) itemManager.UIReference = uiGo.GetComponent<UIManager>();
        }
        if (itemManager != null && itemManager.firstPersonItemHeadbob != null)
            itemManager.firstPersonItemHeadbob.playMovementAnimations = false;
    }

    private void OnDisable()
    {
        if (itemManager != null && itemManager.firstPersonItemHeadbob != null)
            itemManager.firstPersonItemHeadbob.playMovementAnimations = true;
    }

    private void OnDestroy()
    {
        if (itemManager != null && itemManager.inputManager != null)
            itemManager.inputManager.onFireInputDown.RemoveListener(Fire);
    }
}


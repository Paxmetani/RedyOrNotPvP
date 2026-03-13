using UnityEngine;

[CreateAssetMenu(fileName = "New Weapon Object", menuName = "Inventory System/Items/Equipement/Weapon")]
public class WeaponObject : EquipementObject
{
    [Header("Weapon UI")]
    public int cost;
    public int index;
    public int necessaryCards = 30;
    public WeaponRarity rarity = WeaponRarity.Common;
    public string bulletName;
    public float weight;

    [Header("Weapon Settings")]
    public WeaponType weaponType;
    public bool holdAds;
    public bool sniper;
    public bool automaticFire;
    [Range(0, 50)] public float aimAssistStrength;
    public RecoilProfile recoilProfile;
    public int weaponDamage;
    public int maxMagazineSize;
    public int maxBullets;
    [Range(0, 31)] public float timeBetweenShots;
    [Range(0, 31)] public float reloadDuration;
    public AmmunitionObject ammunitionType;
    public ParticleSystem hitImpact;
    public LineRenderer trail;
    [Header("Sound Effects")]
    public AudioClip fireSFX;
    public AudioClip reloadSFX;
    public AudioClip aimSFX;
    public AudioClip cancelAimSFX;
    public float range = 100f;

    [Header("Visual Switching (optional)")]
    // Если задано — используем как «визуальную модель» (меш/скин без логики).
    // Если пусто — будет использован старый prefab.
    public GameObject modelPrefab;
    public GameObject visualPrefab;

    // Один из вариантов (любой можно оставить null):
    public RuntimeAnimatorController animatorController;        // отдельный контроллер
    public AnimatorOverrideController animatorOverride;         // override для общего базового контроллера

    [Header("Socket Names (fallback, если нет WeaponModelRig на модели)")]
    public string muzzleSocketName = "Muzzle";
    public string shellSocketName = "ShellEject";

    private void Awake()
    {
        type = ItemType.equipement;
        equipementType = EquipementType.weapon;
    }
}

public enum WeaponType
{
    fireArm,
    melee,
    throwable,
}

public enum WeaponRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
}
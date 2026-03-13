using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EquipementObject : ItemObject
{
    [Header("Equipement Settings")]
    public EquipementType equipementType;
}
public enum EquipementType
{
    weapon,
    armor,
    basic,
}

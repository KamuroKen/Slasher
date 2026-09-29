using UnityEngine;

public enum ItemCategory { Consumable, Key }
public enum ItemRarity { Common, Rare, Epic }

[CreateAssetMenu(menuName = "Slasher/Item")]
public sealed class ItemDefinition : ScriptableObject
{
    [Tooltip("Stable identifier for saves, e.g. potion_heal_common.")]
    public string id;
    public ItemCategory category;
    public ItemRarity rarity;
    public string displayName;
    [Tooltip("Flavour text only. Numbers are generated from the fields below.")]
    [TextArea(3, 12)] public string description;
    public Sprite icon;
    [Min(1)] public int maxStack = 1;
    [Min(0)] public int healing;
    [Tooltip("Seconds before any quick slot can be used again after this item.")]
    [Min(0f)] public float useCooldown = 0.5f;

    public bool CanQuickSlot => category == ItemCategory.Consumable;

    public string EffectText
    {
        get
        {
            if (category == ItemCategory.Key) return "Opens a matching door. Consumed on use.";
            return healing > 0 ? "Restores " + healing + " HP" : "";
        }
    }

    // Bronze / teal / gold, matching the three colour variants on the icon sheet.
    public static Color RarityColor(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare => new Color(.5f, .84f, .76f),
        ItemRarity.Epic => new Color(1f, .83f, .42f),
        _ => new Color(.91f, .79f, .63f),
    };

    public static Color SlotTint(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare => new Color(.72f, .95f, .88f),
        ItemRarity.Epic => new Color(1f, .92f, .5f),
        _ => new Color(1f, .85f, .66f),
    };
}

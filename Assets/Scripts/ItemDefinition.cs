using UnityEngine;

public enum ItemCategory { Consumable, Key, Elixir, Offering }
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
    [Tooltip("Permanent max HP bonus: drinking an elixir or offering this item at the altar.")]
    [Min(0)] public int maxHealthBonus;
    [Tooltip("Permanent sword damage bonus: drinking an elixir or offering this item at the altar.")]
    [Min(0)] public int damageBonus;
    [Tooltip("Seconds before any quick slot can be used again after this item.")]
    [Min(0f)] public float useCooldown = 0.5f;

    public bool CanQuickSlot => category == ItemCategory.Consumable || category == ItemCategory.Elixir;

    public string EffectText
    {
        get
        {
            if (category == ItemCategory.Key) return "Opens a matching door. Consumed on use.";
            if (category == ItemCategory.Elixir) return "Permanently: " + BonusText;
            if (category == ItemCategory.Offering) return "Offer it at the altar in the Library: " + BonusText + " for good.";
            return healing > 0 ? "Restores " + healing + " HP" : "";
        }
    }

    private string BonusText
    {
        get
        {
            string text = maxHealthBonus > 0 ? "+" + maxHealthBonus + " max HP" : "";
            if (damageBonus > 0) text += (text.Length > 0 ? ", " : "") + "+" + damageBonus + " damage";
            return text;
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

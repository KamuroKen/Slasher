using System;
using UnityEngine;

// Items the player starts the level with. Edit the list in the Inspector on the Player;
// Quick Slot 1-4 also puts the item into that quick slot, 0 leaves it in the bag.
[RequireComponent(typeof(PlayerInventory))]
public sealed class PlayerStartingItems : MonoBehaviour
{
    [Serializable] public sealed class Entry
    {
        public ItemDefinition item;
        [Min(1)] public int count = 1;
        [Tooltip("1-4 puts the item into that quick slot, 0 leaves it in the bag.")]
        [Range(0, PlayerInventory.QuickCapacity)] public int quickSlot;
    }
    [SerializeField] private Entry[] kit = new Entry[0];

    private void Start()
    {
        var inventory = GetComponent<PlayerInventory>();
        foreach (var entry in kit)
        {
            if (entry == null || entry.item == null) continue;
            inventory.Add(entry.item, entry.count);
            if (entry.quickSlot > 0) inventory.Assign(entry.quickSlot - 1, entry.item);
        }
    }
}

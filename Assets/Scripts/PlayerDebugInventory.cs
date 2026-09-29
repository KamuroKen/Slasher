using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Test kit for the inventory. Does nothing in release builds.
// F1 - give the kit again, F2 - take damage (potions are not used at full HP), F3 - empty the inventory.
[RequireComponent(typeof(PlayerInventory))]
public sealed class PlayerDebugInventory : MonoBehaviour
{
    [Serializable] public sealed class Entry
    {
        public ItemDefinition item;
        [Min(1)] public int count = 1;
        [Tooltip("1-4 puts the item into that quick slot, 0 leaves it in the bag.")]
        [Range(0, PlayerInventory.QuickCapacity)] public int quickSlot;
    }
    [SerializeField] private Entry[] kit = new Entry[0];
    [SerializeField] private bool giveOnStart = true;
    [SerializeField, Min(1)] private int selfDamage = 20;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private PlayerInventory inventory;
    private Damageable health;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        health = GetComponent<Damageable>();
    }

    private void Start() { if (giveOnStart) Give(); }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f1Key.wasPressedThisFrame) Give();
        if (keyboard.f2Key.wasPressedThisFrame && health != null) health.TryTakeDamage(selfDamage, transform.position);
        if (keyboard.f3Key.wasPressedThisFrame) inventory.Clear();
    }

    public void Give()
    {
        foreach (var entry in kit)
        {
            if (entry == null || entry.item == null) continue;
            inventory.Add(entry.item, entry.count);
            if (entry.quickSlot > 0) inventory.Assign(entry.quickSlot - 1, entry.item);
        }
    }
#endif
}

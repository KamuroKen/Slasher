using System;
using UnityEngine;

public sealed class PlayerInventory : MonoBehaviour
{
    public const int Capacity = 20;
    public const int QuickCapacity = 4;
    [Serializable] public sealed class Stack
    {
        public ItemDefinition item;
        public int count;
    }
    [SerializeField] private Stack[] slots = new Stack[Capacity];
    [SerializeField] private ItemDefinition[] quick = new ItemDefinition[QuickCapacity];
    private float cooldownUntil, cooldownDuration;
    public event Action Changed;
    public Stack Get(int index) => index >= 0 && index < Capacity ? slots[index] : null;
    public ItemDefinition Quick(int index) => index >= 0 && index < QuickCapacity ? quick[index] : null;

    // One shared cooldown for all quick slots, so potions cannot be chained.
    public float CooldownRemaining => Mathf.Max(0f, cooldownUntil - Time.time);
    public float CooldownFraction => cooldownDuration > 0f ? Mathf.Clamp01(CooldownRemaining / cooldownDuration) : 0f;

    private void Awake()
    {
        if (slots == null || slots.Length != Capacity) Array.Resize(ref slots, Capacity);
        if (quick == null || quick.Length != QuickCapacity) Array.Resize(ref quick, QuickCapacity);
    }

    private static bool IsEmpty(Stack stack) => stack == null || stack.item == null || stack.count <= 0;

    public int Count(ItemDefinition item)
    {
        if (item == null) return 0;
        int total = 0;
        foreach (var slot in slots) if (slot != null && slot.item == item) total += slot.count;
        return total;
    }

    // Returns the amount that did not fit; callers retain any remaining world pickup.
    public int Add(ItemDefinition item, int count)
    {
        if (item == null || count <= 0) return count;
        int remaining = count;
        foreach (var slot in slots)
        {
            if (slot == null || slot.item != item) continue;
            int added = Mathf.Min(remaining, Mathf.Max(0, item.maxStack - slot.count));
            slot.count += added; remaining -= added;
        }
        for (int i = 0; i < Capacity && remaining > 0; i++)
        {
            if (!IsEmpty(slots[i])) continue;
            int added = Mathf.Min(remaining, Mathf.Max(1, item.maxStack));
            slots[i] = new Stack { item = item, count = added }; remaining -= added;
        }
        if (remaining != count) Changed?.Invoke();
        return remaining;
    }

    // Takes from the last stacks first so full stacks stay full. Returns the amount removed.
    public int Remove(ItemDefinition item, int count)
    {
        if (item == null || count <= 0) return 0;
        int removed = 0;
        for (int i = Capacity - 1; i >= 0 && removed < count; i--)
        {
            var slot = slots[i];
            if (slot == null || slot.item != item || slot.count <= 0) continue;
            int taken = Mathf.Min(slot.count, count - removed);
            slot.count -= taken; removed += taken;
            if (slot.count == 0) slots[i] = null;
        }
        if (removed > 0) Changed?.Invoke();
        return removed;
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Capacity || to < 0 || to >= Capacity || from == to) return;
        var source = slots[from]; var destination = slots[to];
        if (IsEmpty(source)) return;
        int moved = 0;
        if (destination != null && destination.item == source.item)
        {
            moved = Mathf.Min(source.count, Mathf.Max(0, source.item.maxStack - destination.count));
            source.count -= moved; destination.count += moved;
            if (source.count == 0) slots[from] = null;
        }
        // Different items, or a full stack of the same item: swap places.
        if (moved == 0) { slots[to] = source; slots[from] = destination; }
        Changed?.Invoke();
    }

    // Puts an item into a quick slot. An item lives in one quick slot only:
    // assigning it again moves it, and whatever was in the target slot takes its old place.
    public bool Assign(int index, ItemDefinition item)
    {
        if (index < 0 || index >= QuickCapacity) return false;
        if (item == null)
        {
            if (quick[index] == null) return false;
            quick[index] = null; Changed?.Invoke(); return true;
        }
        if (!item.CanQuickSlot || Count(item) == 0) return false;
        int previous = Array.IndexOf(quick, item);
        if (previous == index) return true;
        if (previous >= 0) quick[previous] = quick[index];
        quick[index] = item;
        Changed?.Invoke();
        return true;
    }

    public bool Use(ItemDefinition item, Damageable health)
    {
        if (item == null || health == null || item.category != ItemCategory.Consumable) return false;
        if (Count(item) == 0 || CooldownRemaining > 0f || !health.TryHeal(item.healing)) return false;
        cooldownDuration = item.useCooldown;
        cooldownUntil = Time.time + item.useCooldown;
        return Remove(item, 1) == 1;
    }

    public void Clear()
    {
        for (int i = 0; i < Capacity; i++) slots[i] = null;
        for (int i = 0; i < QuickCapacity; i++) quick[i] = null;
        cooldownUntil = cooldownDuration = 0f;
        Changed?.Invoke();
    }
}

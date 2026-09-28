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
    public event Action Changed;
    public Stack Get(int index) => index >= 0 && index < Capacity ? slots[index] : null;
    public ItemDefinition Quick(int index) => index >= 0 && index < QuickCapacity ? quick[index] : null;

    private void Awake()
    {
        if (slots == null || slots.Length != Capacity) Array.Resize(ref slots, Capacity);
        if (quick == null || quick.Length != QuickCapacity) Array.Resize(ref quick, QuickCapacity);
    }

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
            if (slots[i] != null && slots[i].item != null && slots[i].count > 0) continue;
            int added = Mathf.Min(remaining, Mathf.Max(1, item.maxStack));
            slots[i] = new Stack { item = item, count = added }; remaining -= added;
        }
        if (remaining != count) Changed?.Invoke();
        return remaining;
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Capacity || to < 0 || to >= Capacity || from == to) return;
        var source = slots[from]; var destination = slots[to];
        if (source == null || source.item == null || source.count <= 0) return;
        if (destination != null && destination.item == source.item)
        {
            int moved = Mathf.Min(source.count, Mathf.Max(0, source.item.maxStack - destination.count));
            source.count -= moved; destination.count += moved;
            if (source.count == 0) slots[from] = null;
        }
        else { slots[to] = source; slots[from] = destination; }
        Changed?.Invoke();
    }

    public void Assign(int index, ItemDefinition item)
    {
        if (index < 0 || index >= QuickCapacity || (item != null && Count(item) == 0)) return;
        quick[index] = item; Changed?.Invoke();
    }

    public bool Use(ItemDefinition item, Damageable health)
    {
        if (item == null || health == null || Count(item) == 0 || !health.TryHeal(item.healing)) return false;
        for (int i = 0; i < Capacity; i++)
        {
            if (slots[i] == null || slots[i].item != item || slots[i].count <= 0) continue;
            if (--slots[i].count == 0) slots[i] = null;
            Changed?.Invoke(); return true;
        }
        return false;
    }
}

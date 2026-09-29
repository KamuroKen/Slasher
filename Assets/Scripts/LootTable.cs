using System;
using UnityEngine;

// A list of possible drops shared by chests and enemies. Each roll spawns ItemPickups on the floor.
[Serializable]
public sealed class LootTable
{
    [Serializable] public sealed class Drop
    {
        public ItemDefinition item;
        [Range(0f, 1f)] public float chance = 1f;
        [Min(1)] public int min = 1;
        [Min(1)] public int max = 1;
    }
    public Drop[] drops = new Drop[0];

    public int Spawn(Vector3 origin, float scatter)
    {
        int spawned = 0;
        foreach (var drop in drops)
        {
            if (drop == null || drop.item == null || UnityEngine.Random.value > drop.chance) continue;
            int amount = UnityEngine.Random.Range(drop.min, Mathf.Max(drop.min, drop.max) + 1);
            var offset = (Vector3)(UnityEngine.Random.insideUnitCircle * scatter);
            ItemPickup.Spawn(drop.item, amount, origin + offset);
            spawned++;
        }
        return spawned;
    }
}

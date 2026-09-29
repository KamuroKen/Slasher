using UnityEngine;

// Drops items on the enemy's body when it dies. Add to an enemy prefab and fill the table.
[RequireComponent(typeof(Damageable))]
public sealed class EnemyLoot : MonoBehaviour
{
    [SerializeField] private LootTable loot = new LootTable();
    [SerializeField, Min(0f)] private float scatter = 0.3f;
    private Damageable health;

    private void Awake() => health = GetComponent<Damageable>();
    private void OnEnable() => health.Died += DropLoot;
    private void OnDisable() => health.Died -= DropLoot;

    private void DropLoot() => loot.Spawn(transform.position, scatter);
}

using UnityEngine;

// An item lying in the world. Touching it adds it to the player's inventory;
// whatever does not fit stays on the ground.
[RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D))]
public sealed class ItemPickup : MonoBehaviour
{
    [SerializeField] private ItemDefinition item;
    [SerializeField, Min(1)] private int count = 1;
    [SerializeField, Min(0f)] private float bobHeight = 0.06f;
    private SpriteRenderer spriteRenderer;
    private Vector3 restPosition;

    public static ItemPickup Spawn(ItemDefinition item, int count, Vector3 position, int sortingOrder = 5)
    {
        var go = new GameObject("Pickup " + (item != null ? item.displayName : "?"));
        go.transform.position = position;
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = sortingOrder;
        var trigger = go.AddComponent<CircleCollider2D>();
        trigger.radius = 0.4f;
        var pickup = go.AddComponent<ItemPickup>();
        pickup.Set(item, count);
        return pickup;
    }

    public void Set(ItemDefinition newItem, int newCount)
    {
        item = newItem;
        count = Mathf.Max(1, newCount);
        Apply();
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        GetComponent<Collider2D>().isTrigger = true;
        restPosition = transform.position;
        Apply();
    }

    private void OnValidate()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null && item != null) sr.sprite = item.icon;
    }

    private void Apply()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) spriteRenderer.sprite = item != null ? item.icon : null;
    }

    private void Update()
    {
        if (bobHeight > 0f) transform.position = restPosition + Vector3.up * (Mathf.Sin(Time.time * 3f) * bobHeight);
    }

    private void OnTriggerEnter2D(Collider2D other) => TryCollect(other);

    private void TryCollect(Collider2D other)
    {
        if (item == null) return;
        var inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null) return;
        count = inventory.Add(item, count);
        if (count <= 0) Destroy(gameObject);
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

// A chest the player opens once with E while standing next to it. Its loot spills onto the floor as pickups.
public sealed class LootChest : MonoBehaviour
{
    [SerializeField] private LootTable loot = new LootTable();
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite openSprite;
    [SerializeField, Min(0.1f)] private float reach = 1.2f;
    [SerializeField, Min(0f)] private float scatter = 0.5f;
    [Tooltip("Where loot lands relative to the chest.")]
    [SerializeField] private Vector2 dropOffset = new Vector2(0f, -0.6f);
    [Tooltip("Open by walking into the chest instead of pressing E.")]
    [SerializeField] private bool openOnTouch;
    private InputAction interact;
    private PlayerInventory player;
    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
    }

    private void OnEnable() => interact.Enable();
    private void OnDisable() => interact.Disable();
    private void OnDestroy() => interact.Dispose();

    private void Update()
    {
        if (IsOpen || openOnTouch || PlayerUI.GameplayBlocked || !interact.WasPressedThisFrame()) return;
        if (player == null) player = FindFirstObjectByType<PlayerInventory>();
        if (player != null && Vector2.Distance(player.transform.position, transform.position) <= reach) Open();
    }

    private void OnTriggerEnter2D(Collider2D other) => TouchOpen(other);
    private void OnCollisionEnter2D(Collision2D collision) => TouchOpen(collision.collider);

    private void TouchOpen(Collider2D other)
    {
        if (openOnTouch && !IsOpen && other.GetComponentInParent<PlayerInventory>() != null) Open();
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        if (spriteRenderer != null && openSprite != null) spriteRenderer.sprite = openSprite;
        loot.Spawn(transform.position + (Vector3)dropOffset, scatter);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, .85f, .4f, .6f);
        Gizmos.DrawWireSphere(transform.position, reach);
    }
}

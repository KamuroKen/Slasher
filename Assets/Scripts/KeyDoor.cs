using UnityEngine;
using UnityEngine.Events;

// A door that opens when the player touches it carrying the required key. The key is consumed.
// Each door references its own key asset, so different keys open different places.
public sealed class KeyDoor : MonoBehaviour
{
    [SerializeField] private ItemDefinition requiredKey;
    [Tooltip("Objects to switch off when the door opens, e.g. the blocking collider or the closed door sprite.")]
    [SerializeField] private GameObject[] disableOnOpen = new GameObject[0];
    [SerializeField] private UnityEvent opened = new UnityEvent();
    public bool IsOpen { get; private set; }
    public ItemDefinition RequiredKey => requiredKey;

    private void OnCollisionEnter2D(Collision2D collision) => TryOpen(collision.collider);
    private void OnTriggerEnter2D(Collider2D other) => TryOpen(other);

    public bool TryOpen(Collider2D other)
    {
        if (IsOpen || requiredKey == null || other == null) return false;
        var inventory = other.GetComponentInParent<PlayerInventory>();
        if (inventory == null || inventory.Remove(requiredKey, 1) == 0) return false;
        IsOpen = true;
        foreach (var target in disableOnOpen) if (target != null) target.SetActive(false);
        opened.Invoke();
        return true;
    }
}

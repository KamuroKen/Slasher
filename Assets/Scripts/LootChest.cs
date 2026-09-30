using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// A chest the player opens once with E while standing next to it: it plays its opening frames and its loot
// spills onto the floor in front of it. The Chest_Common / Chest_Rare / Chest_Epic prefabs can be placed anywhere;
// the loot list of each placed chest can be changed in the Inspector.
[DisallowMultipleComponent]
public sealed class LootChest : MonoBehaviour
{
    [SerializeField] private LootTable loot = new LootTable();
    [Tooltip("When every roll fails, drop the first item of the list anyway, so a chest is never empty.")]
    [SerializeField] private bool neverEmpty = true;

    [Header("Look")]
    [Tooltip("Chest frames, closed first and open last.")]
    [SerializeField] private Sprite[] frames = new Sprite[0];
    [SerializeField, Min(1f)] private float framesPerSecond = 10f;
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Header("Use")]
    [Tooltip("How close the player must stand to the chest to open it.")]
    [SerializeField, Min(0.1f)] private float reach = 0.75f;
    [SerializeField] private TMP_Text prompt;

    [Header("Loot")]
    [Tooltip("Where the loot lands relative to the chest.")]
    [SerializeField] private Vector2 dropOffset = new Vector2(0f, -0.9f);
    [SerializeField, Min(0f)] private float scatter = 0.4f;

    private InputAction interact;
    private PlayerInventory player;
    private Collider2D playerCollider, body;
    private InteractPrompt hint;
    public bool IsOpen { get; private set; }

    private void Awake()
    {
        interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        foreach (var candidate in GetComponents<Collider2D>())
            if (!candidate.isTrigger) { body = candidate; break; }
        if (spriteRenderer != null && frames.Length > 0 && frames[0] != null) spriteRenderer.sprite = frames[0];
        hint = new InteractPrompt(prompt);
    }

    private void OnEnable() => interact.Enable();
    private void OnDisable() => interact.Disable();
    private void OnDestroy() => interact.Dispose();

    private void Update()
    {
        if (IsOpen || !FindPlayer()) { hint.Update(); return; }
        if (PlayerUI.GameplayBlocked || !InReach()) { hint.Hide(); return; }
        hint.Show("[E] Open");
        if (interact.WasPressedThisFrame()) Open();
        hint.Update();
    }

    private bool FindPlayer()
    {
        if (player != null) return true;
        player = FindFirstObjectByType<PlayerInventory>();
        if (player == null) return false;
        foreach (var candidate in player.GetComponentsInChildren<Collider2D>())
            if (!candidate.isTrigger) { playerCollider = candidate; break; }
        return true;
    }

    // Measured between the colliders, so it works from any side of the chest.
    private bool InReach()
    {
        if (playerCollider != null && body != null && playerCollider.enabled && body.enabled)
        {
            var distance = Physics2D.Distance(playerCollider, body);
            if (distance.isValid) return distance.distance <= reach;
        }
        return Vector2.Distance(player.transform.position, transform.position) <= reach + 0.5f;
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        hint.Hide();
        StartCoroutine(PlayOpening());
    }

    private IEnumerator PlayOpening()
    {
        var wait = new WaitForSeconds(1f / framesPerSecond);
        for (int i = 1; i < frames.Length; i++)
        {
            yield return wait;
            if (spriteRenderer != null && frames[i] != null) spriteRenderer.sprite = frames[i];
        }
        SpawnLoot();
    }

    private void SpawnLoot()
    {
        Vector3 origin = transform.position + (Vector3)dropOffset;
        if (loot.Spawn(origin, scatter) > 0 || !neverEmpty) return;
        foreach (var drop in loot.drops)
        {
            if (drop == null || drop.item == null) continue;
            ItemPickup.Spawn(drop.item, Mathf.Max(1, drop.min), origin);
            return;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, .85f, .4f, .6f);
        Gizmos.DrawWireSphere(transform.position, reach + 0.5f);
        Gizmos.color = new Color(.4f, 1f, .5f, .8f);
        Gizmos.DrawWireSphere(transform.position + (Vector3)dropOffset, Mathf.Max(0.05f, scatter));
    }
}

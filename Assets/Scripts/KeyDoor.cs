using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

// A locked door. The player walks up to it and presses E: with the matching key in the bag the key is used up,
// the door plays its opening frames and the passage opens. Without the key a hint above the door says which key
// is needed. The door art may be painted as a tile from the level palette: when the game starts the door takes
// over the tile under it and draws it itself, so the level looks the same in the editor.
[DisallowMultipleComponent]
public sealed class KeyDoor : MonoBehaviour
{
    [Tooltip("Key item that opens this door. Empty = the door opens with E, no key needed.")]
    [SerializeField] private ItemDefinition requiredKey;

    [Header("Look")]
    [Tooltip("Door frames, closed first and open last.")]
    [SerializeField] private Sprite[] frames = new Sprite[0];
    [SerializeField, Min(1f)] private float framesPerSecond = 10f;
    [SerializeField] private SpriteRenderer doorRenderer;
    [Tooltip("Draws the wall top above the doorway over the player, so the player walks under it.")]
    [SerializeField] private SpriteRenderer lintelRenderer;
    [Tooltip("Height of that wall top in pixels, counted from the top of the frame.")]
    [SerializeField, Min(0)] private int lintelPixels = 16;
    [Tooltip("Tilemap with the painted door. Empty = found automatically when the game starts.")]
    [SerializeField] private Tilemap doorTilemap;
    [SerializeField] private Vector3Int doorCell;

    [Header("Passage")]
    [Tooltip("Blocks the passage while the door is closed.")]
    [SerializeField] private Collider2D blocker;
    [Tooltip("How close the player must stand to the door to use it.")]
    [SerializeField, Min(0.1f)] private float reach = 0.75f;

    [Header("Hint")]
    [SerializeField] private TMP_Text prompt;
    [Tooltip("Word after [E] in the hint, e.g. Open or Exit.")]
    [SerializeField] private string actionLabel = "Open";
    [SerializeField, Min(0f)] private float usedMessageSeconds = 1.5f;

    private InputAction interact;
    private PlayerInventory player;
    private Collider2D playerCollider;
    private Sprite lintelSprite;
    private InteractPrompt hint;

    public bool IsOpen { get; private set; }
    // Raised once the opening animation has finished and the passage is free.
    public event System.Action Opened;

    private void Awake()
    {
        interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
        if (doorRenderer == null) doorRenderer = GetComponent<SpriteRenderer>();
        if (blocker == null) blocker = GetComponent<Collider2D>();
        AdoptPaintedTile();
        if (doorRenderer != null && frames.Length > 0) doorRenderer.sprite = frames[0];
        SetUpLintel();
        hint = new InteractPrompt(prompt);
    }

    private void OnEnable() => interact.Enable();
    private void OnDisable() => interact.Disable();

    private void OnDestroy()
    {
        interact.Dispose();
        if (lintelSprite != null) Destroy(lintelSprite);
    }

    // The painted door tile is replaced by this object's renderers, which can open and sort around the player.
    private void AdoptPaintedTile()
    {
        if (frames.Length == 0) return;
        if (doorTilemap == null || !IsDoorSprite(doorTilemap.GetSprite(doorCell)))
        {
            doorTilemap = null;
            foreach (var tilemap in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            {
                var near = tilemap.WorldToCell(transform.position);
                for (int x = -1; x <= 1 && doorTilemap == null; x++)
                    for (int y = -1; y <= 1 && doorTilemap == null; y++)
                    {
                        var cell = near + new Vector3Int(x, y, 0);
                        if (IsDoorSprite(tilemap.GetSprite(cell))) { doorTilemap = tilemap; doorCell = cell; }
                    }
                if (doorTilemap != null) break;
            }
        }
        if (doorTilemap == null) return;
        transform.position = doorTilemap.GetCellCenterWorld(doorCell);
        doorTilemap.SetTile(doorCell, null);
    }

    private bool IsDoorSprite(Sprite sprite)
    {
        if (sprite == null) return false;
        foreach (var frame in frames) if (frame == sprite) return true;
        return false;
    }

    // Cuts the wall top off the closed frame; it stays the same in every frame.
    private void SetUpLintel()
    {
        if (lintelRenderer == null) return;
        var frame = frames.Length > 0 ? frames[0] : null;
        if (frame == null || lintelPixels <= 0) { lintelRenderer.enabled = false; return; }
        var rect = frame.packed ? frame.textureRect : frame.rect;
        float height = Mathf.Min(lintelPixels, rect.height);
        var lintelRect = new Rect(rect.x, rect.yMax - height, rect.width, height);
        // Same pivot point as the whole frame, so both pieces line up.
        var pivot = new Vector2(frame.pivot.x / rect.width, (frame.pivot.y - (rect.height - height)) / height);
        lintelSprite = Sprite.Create(frame.texture, lintelRect, pivot, frame.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        lintelSprite.name = frame.name + " (wall top)";
        lintelRenderer.sprite = lintelSprite;
        lintelRenderer.transform.position = doorRenderer != null ? doorRenderer.transform.position : transform.position;
        lintelRenderer.enabled = true;
    }

    private void Update()
    {
        if (IsOpen) { hint.Update(); return; }
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerInventory>();
            if (player == null) return;
            foreach (var candidate in player.GetComponentsInChildren<Collider2D>())
                if (!candidate.isTrigger) { playerCollider = candidate; break; }
        }
        if (PlayerUI.GameplayBlocked || !InReach()) { hint.Hide(); return; }

        bool hasKey = requiredKey == null || player.Count(requiredKey) > 0;
        hint.Show(hasKey ? "[E] " + actionLabel : "Locked - needs " + KeyName());
        if (interact.WasPressedThisFrame())
        {
            if (hasKey) Unlock();
            else hint.Shake();
        }
        hint.Update();
    }

    private bool InReach()
    {
        if (playerCollider != null && blocker != null && blocker.enabled && playerCollider.enabled)
        {
            var distance = Physics2D.Distance(playerCollider, blocker);
            if (distance.isValid) return distance.distance <= reach;
        }
        Vector2 point = player.transform.position;
        Vector2 closest = blocker != null ? (Vector2)blocker.bounds.ClosestPoint(point) : (Vector2)transform.position;
        return Vector2.Distance(point, closest) <= reach + 0.5f;
    }

    private string KeyName()
    {
        string color = ColorUtility.ToHtmlStringRGB(ItemDefinition.RarityColor(requiredKey.rarity));
        return $"<color=#{color}>{requiredKey.displayName}</color>";
    }

    // Uses up the key from the bag and opens the door.
    public bool Unlock()
    {
        if (IsOpen) return false;
        if (requiredKey != null && (player == null || player.Remove(requiredKey, 1) == 0)) return false;
        Open();
        if (requiredKey != null) hint.Show(KeyName() + " used", usedMessageSeconds);
        else hint.Hide();
        return true;
    }

    // Opens the door without a key, e.g. from a lever or a puzzle.
    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        StartCoroutine(PlayOpening());
    }

    private IEnumerator PlayOpening()
    {
        var wait = new WaitForSeconds(1f / framesPerSecond);
        for (int i = 1; i < frames.Length; i++)
        {
            yield return wait;
            if (doorRenderer != null && frames[i] != null) doorRenderer.sprite = frames[i];
        }
        if (blocker != null) blocker.enabled = false;
        Opened?.Invoke();
    }

    private void OnDrawGizmosSelected()
    {
        var area = blocker != null ? blocker : GetComponent<Collider2D>();
        if (area == null) return;
        var bounds = area.bounds;
        Gizmos.color = new Color(1f, .85f, .4f, .6f);
        Gizmos.DrawWireCube(bounds.center, bounds.size + Vector3.one * (2f * reach));
    }
}

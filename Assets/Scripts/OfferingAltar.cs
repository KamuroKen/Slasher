using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

// The hand-shaped altar in the Library. Bring an offering item (the Blessed Parchment) and press E: the item is
// used up, the sphere in the hand dissolves, then the offering's permanent bonus (max HP, damage) is granted and
// the sphere lights up again. Can be used any number of times. When the altar is painted as a tile, this object
// takes the tile over at start and draws the animation itself.
[DisallowMultipleComponent]
public sealed class OfferingAltar : MonoBehaviour
{
    [Tooltip("Item the altar accepts; its Max Health Bonus and Damage Bonus are what the player receives.")]
    [SerializeField] private ItemDefinition offering;

    [Header("Look")]
    [Tooltip("Resting loop: the sphere floating above the hand.")]
    [SerializeField] private Sprite[] idleFrames = new Sprite[0];
    [SerializeField, Min(1f)] private float idleFramesPerSecond = 8f;
    [Tooltip("Played once per offering: the sphere fades away.")]
    [SerializeField] private Sprite[] offerFrames = new Sprite[0];
    [SerializeField, Min(1f)] private float offerFramesPerSecond = 10f;
    [SerializeField] private SpriteRenderer altarRenderer;
    [Tooltip("Tilemap with the painted altar. Empty = found automatically when the game starts.")]
    [SerializeField] private Tilemap altarTilemap;
    [SerializeField] private Vector3Int altarCell;

    [Header("Use")]
    [Tooltip("Area the player must stand next to (a trigger around the altar base).")]
    [SerializeField] private Collider2D useArea;
    [SerializeField, Min(0.1f)] private float reach = 0.6f;
    [SerializeField] private TMP_Text prompt;
    [SerializeField, Min(0f)] private float rewardMessageSeconds = 2.5f;

    private InputAction interact;
    private PlayerInventory player;
    private Damageable playerHealth;
    private Collider2D playerCollider;
    private InteractPrompt hint;
    private bool busy;
    private float messageUntil;

    private void Awake()
    {
        interact = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
        if (altarRenderer == null) altarRenderer = GetComponent<SpriteRenderer>();
        if (useArea == null) useArea = GetComponent<Collider2D>();
        AdoptPaintedTile();
        if (altarRenderer != null && idleFrames.Length > 0) altarRenderer.sprite = idleFrames[0];
        hint = new InteractPrompt(prompt);
    }

    private void OnEnable() => interact.Enable();
    private void OnDisable() => interact.Disable();
    private void OnDestroy() => interact.Dispose();

    private void AdoptPaintedTile()
    {
        if (idleFrames.Length == 0) return;
        if (altarTilemap == null || !IsAltarSprite(altarTilemap.GetSprite(altarCell)))
        {
            altarTilemap = null;
            foreach (var tilemap in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            {
                var near = tilemap.WorldToCell(transform.position);
                for (int x = -1; x <= 1 && altarTilemap == null; x++)
                    for (int y = -1; y <= 1 && altarTilemap == null; y++)
                    {
                        var cell = near + new Vector3Int(x, y, 0);
                        if (IsAltarSprite(tilemap.GetSprite(cell))) { altarTilemap = tilemap; altarCell = cell; }
                    }
                if (altarTilemap != null) break;
            }
        }
        if (altarTilemap == null) return;
        transform.position = altarTilemap.GetCellCenterWorld(altarCell);
        altarTilemap.SetTile(altarCell, null);
    }

    private bool IsAltarSprite(Sprite sprite)
    {
        if (sprite == null) return false;
        foreach (var frame in idleFrames) if (frame == sprite) return true;
        return false;
    }

    private void Update()
    {
        if (busy) { hint.Update(); return; }
        if (altarRenderer != null && idleFrames.Length > 0)
            altarRenderer.sprite = idleFrames[(int)(Time.time * idleFramesPerSecond) % idleFrames.Length];
        if (!FindPlayer() || Time.time < messageUntil) { hint.Update(); return; }
        if (PlayerUI.GameplayBlocked || !InReach()) { hint.Hide(); return; }

        bool has = offering != null && player.Count(offering) > 0;
        hint.Show(has ? "[E] Offer " + ItemName() : offering != null ? "The altar awaits " + ItemName() : "The altar is silent");
        if (interact.WasPressedThisFrame())
        {
            if (has) StartCoroutine(Offer());
            else hint.Shake();
        }
        hint.Update();
    }

    private bool FindPlayer()
    {
        if (player != null) return true;
        player = FindFirstObjectByType<PlayerInventory>();
        if (player == null) return false;
        playerHealth = player.GetComponent<Damageable>();
        foreach (var candidate in player.GetComponentsInChildren<Collider2D>())
            if (!candidate.isTrigger) { playerCollider = candidate; break; }
        return true;
    }

    private bool InReach()
    {
        if (playerCollider != null && useArea != null && playerCollider.enabled && useArea.enabled)
        {
            var distance = Physics2D.Distance(playerCollider, useArea);
            if (distance.isValid) return distance.distance <= reach;
        }
        return Vector2.Distance(player.transform.position, transform.position) <= reach + 1f;
    }

    private string ItemName()
    {
        string color = ColorUtility.ToHtmlStringRGB(ItemDefinition.RarityColor(offering.rarity));
        return $"<color=#{color}>{offering.displayName}</color>";
    }

    private IEnumerator Offer()
    {
        if (player.Remove(offering, 1) == 0) yield break;
        busy = true;
        hint.Hide();
        var wait = new WaitForSeconds(1f / offerFramesPerSecond);
        foreach (var frame in offerFrames)
        {
            if (altarRenderer != null && frame != null) altarRenderer.sprite = frame;
            yield return wait;
        }
        player.ApplyBonus(offering, playerHealth);
        var reward = new System.Collections.Generic.List<string>();
        if (offering.maxHealthBonus > 0) reward.Add("+" + offering.maxHealthBonus + " max HP");
        if (offering.damageBonus > 0) reward.Add("+" + offering.damageBonus + " damage");
        hint.Show(string.Join(", ", reward), rewardMessageSeconds);
        messageUntil = Time.time + rewardMessageSeconds;
        busy = false; // the idle loop brings the sphere back
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Animator))]
public sealed class PlayerAttack : MonoBehaviour
{
    [SerializeField, Min(1)] private int swordDamage = 20;
    [SerializeField, Min(0.1f)] private float swordReach = 0.85f;
    [SerializeField, Min(0.1f)] private float swordRadius = 0.6f;
    [SerializeField] private LayerMask damageLayers = ~0;
    [Header("Timing in seconds")]
    [SerializeField, Min(0.01f)] private float duration = 9f / 12f;
    [SerializeField, Min(0f)] private float hitStart = 4f / 12f;
    [SerializeField, Min(0f)] private float hitEnd = 7f / 12f;
    private readonly HashSet<Damageable> hitTargets = new HashSet<Damageable>();
    private readonly List<Collider2D> hitColliders = new List<Collider2D>();
    private Animator animator;
    private InputAction attackAction;
    private Vector2 direction;
    private float elapsed;
    public bool IsAttacking { get; private set; }
    // Permanent growth from elixirs and the altar.
    public void AddDamage(int amount) => swordDamage = Mathf.Max(1, swordDamage + amount);
    private Vector2 SwordCenter => (Vector2)transform.position + Vector2.up * 0.3f + direction * swordReach;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        attackAction = new InputAction("Attack", InputActionType.Button);
        attackAction.AddBinding("<Mouse>/leftButton");
        attackAction.AddBinding("<Keyboard>/space");
    }

    private void OnEnable() => attackAction.Enable();

    public bool TryStart(string facing)
    {
        if (PlayerUI.GameplayBlocked || (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())) return false;
        if (!isActiveAndEnabled || IsAttacking || !attackAction.WasPressedThisFrame()) return false;
        elapsed = 0f;
        hitTargets.Clear();
        direction = facing == "Up" ? Vector2.up : facing == "Down" ? Vector2.down
            : facing == "Right" ? Vector2.right : Vector2.left;
        IsAttacking = true;
        animator.Play(Animator.StringToHash("Sword_" + facing), 0, 0f);
        return true;
    }

    public bool Advance()
    {
        if (!IsAttacking) return false;
        float previous = elapsed;
        elapsed += Time.deltaTime;
        // Crossing the interval must still deal damage when a frame is slow.
        if (elapsed >= hitStart && previous < hitEnd) ApplyDamage();
        if (elapsed >= duration) Cancel();
        return IsAttacking;
    }

    private void ApplyDamage()
    {
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(damageLayers);
        Physics2D.OverlapCircle(SwordCenter, swordRadius, filter, hitColliders);
        foreach (var hit in hitColliders)
        {
            if (hit == null || hit.transform.IsChildOf(transform)) continue;
            var target = hit.GetComponentInParent<Damageable>();
            if (target == null || target.transform.IsChildOf(transform)) continue;
            if (hitTargets.Add(target)) target.TryTakeDamage(swordDamage, transform.position);
        }
    }

    public void Cancel()
    {
        IsAttacking = false;
        hitTargets.Clear();
    }

    private void OnDisable() { attackAction.Disable(); Cancel(); }
    private void OnDestroy() => attackAction.Dispose();
    private void OnValidate()
    {
        hitEnd = Mathf.Clamp(hitEnd, 0f, duration);
        hitStart = Mathf.Clamp(hitStart, 0f, hitEnd);
    }
    private void OnDrawGizmosSelected()
    {
        if (!IsAttacking) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(SwordCenter, swordRadius);
    }
}

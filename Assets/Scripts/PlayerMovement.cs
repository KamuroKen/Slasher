using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(Animator))]
public sealed class PlayerMovement : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
    [SerializeField] private Vector2 arenaMin = new Vector2(-15.5f, -9.5f);
    [SerializeField] private Vector2 arenaMax = new Vector2(15.5f, 9.5f);
    [Header("Sword attack")]
    [SerializeField, Min(1)] private int swordDamage = 20;
    [SerializeField, Min(0.1f)] private float swordReach = 0.85f;
    [SerializeField, Min(0.1f)] private float swordRadius = 0.6f;
    [SerializeField] private LayerMask damageLayers = ~0;

    // Sword sheets contain nine frames at 12 FPS. The blade is active on frames 4–6.
    private const float AttackDuration = 9f / 12f;
    private const float HitStart = 4f / 12f;
    private const float HitEnd = 7f / 12f;
    private InputAction attackAction;
    private float attackElapsed;
    private Vector2 attackDirection;
    private readonly HashSet<Damageable> hitTargets = new HashSet<Damageable>();
    private readonly List<Collider2D> hitColliders = new List<Collider2D>();
    public bool IsAttacking { get; private set; }

    private Rigidbody2D body;
    private Animator animator;
    private InputAction moveAction;
    private Vector2 movement;
    private string facing = "Down";
    private int currentState;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        attackAction = new InputAction("Attack", InputActionType.Button);
        attackAction.AddBinding("<Mouse>/leftButton");
        attackAction.AddBinding("<Keyboard>/space");
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
    }

    private void OnEnable()
    {
        moveAction.Enable();
        attackAction.Enable();
    }

    private void Update()
    {
        if (IsAttacking)
        {
            float previous = attackElapsed;
            attackElapsed += Time.deltaTime;
            // Check crossed intervals as well, so a slow frame cannot skip the hit.
            if (attackElapsed >= HitStart && previous < HitEnd) ApplySwordDamage();
            if (attackElapsed < AttackDuration) return;
            IsAttacking = false;
            currentState = 0;
        }
        movement = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        bool moving = movement.sqrMagnitude > 0.001f;
        if (moving)
        {
            if (Mathf.Abs(movement.x) > Mathf.Abs(movement.y))
                facing = movement.x > 0f ? "Right" : "Left";
            else
                facing = movement.y > 0f ? "Up" : "Down";
        }
        if (attackAction.WasPressedThisFrame())
        {
            IsAttacking = true;
            attackElapsed = 0f;
            hitTargets.Clear();
            movement = Vector2.zero;
            body.linearVelocity = Vector2.zero;
            attackDirection = facing == "Up" ? Vector2.up : facing == "Down" ? Vector2.down
                : facing == "Right" ? Vector2.right : Vector2.left;
            animator.Play(Animator.StringToHash("Sword_" + facing), 0, 0f);
            return;
        }
        int state = Animator.StringToHash((moving ? "Run_" : "Idle_") + facing);
        if (state == currentState) return;
        animator.Play(state, 0, 0f);
        currentState = state;
    }

    private Vector2 SwordCenter => (Vector2)transform.position + Vector2.up * 0.3f + attackDirection * swordReach;

    private void ApplySwordDamage()
    {
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(damageLayers);
        Physics2D.OverlapCircle(SwordCenter, swordRadius, filter, hitColliders);
        foreach (var hit in hitColliders)
        {
            if (hit == null || hit.transform.IsChildOf(transform)) continue;
            var target = hit.GetComponentInParent<Damageable>();
            if (target == null || target.transform == transform || target.transform.IsChildOf(transform)) continue;
            if (hitTargets.Add(target)) target.TakeDamage(swordDamage);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!IsAttacking) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(SwordCenter, swordRadius);
    }

    private void FixedUpdate()
    {
        Vector2 next = body.position + movement * (moveSpeed * Time.fixedDeltaTime);
        next.x = Mathf.Clamp(next.x, arenaMin.x, arenaMax.x);
        next.y = Mathf.Clamp(next.y, arenaMin.y, arenaMax.y);
        body.MovePosition(next);
    }

    private void OnDisable()
    {
        moveAction.Disable();
        attackAction.Disable();
        IsAttacking = false;
        currentState = 0;
        hitTargets.Clear();
        movement = Vector2.zero;
        body.linearVelocity = Vector2.zero;
    }

    private void OnDestroy()
    {
        moveAction.Dispose();
        attackAction.Dispose();
    }
}

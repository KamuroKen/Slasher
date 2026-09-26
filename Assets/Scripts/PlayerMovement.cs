using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(Animator), typeof(PlayerAttack))]
public sealed class PlayerMovement : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f;
    [SerializeField, Min(0f)] private float boundaryInset = 0.5f;
    private Rigidbody2D body;
    private Animator animator;
    private PlayerAttack attack;
    private ArenaBounds arena;
    private InputAction moveAction;
    private Vector2 movement;
    private float hurtUntil;
    private int currentState;
    public string Facing { get; private set; } = "Down";

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        attack = GetComponent<PlayerAttack>();
        arena = FindFirstObjectByType<ArenaBounds>();
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
    }

    private void OnEnable() => moveAction.Enable();

    private void Update()
    {
        if (Time.time < hurtUntil) return;
        // Movement drives the attack tick so damage, input and animation have a fixed order.
        if (attack.Advance()) return;
        movement = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        bool moving = movement.sqrMagnitude > 0.001f;
        if (moving)
            Facing = Mathf.Abs(movement.x) > Mathf.Abs(movement.y)
                ? (movement.x > 0f ? "Right" : "Left")
                : (movement.y > 0f ? "Up" : "Down");
        if (attack.TryStart(Facing))
        {
            StopMotion();
            return;
        }
        int state = Animator.StringToHash((moving ? "Run_" : "Idle_") + Facing);
        if (state == currentState) return;
        animator.Play(state, 0, 0f);
        currentState = state;
    }

    public void PlayHurt(float duration)
    {
        InterruptActions();
        hurtUntil = Time.time + duration;
        animator.Play(Animator.StringToHash("Hurt_" + Facing), 0, 0f);
    }

    private void StopMotion()
    {
        movement = Vector2.zero;
        body.linearVelocity = Vector2.zero;
        currentState = 0;
    }

    private void InterruptActions()
    {
        attack.Cancel();
        StopMotion();
    }

    private void FixedUpdate()
    {
        Vector2 next = body.position + movement * (moveSpeed * Time.fixedDeltaTime);
        body.MovePosition(arena != null ? arena.Clamp(next, Vector2.one * boundaryInset) : next);
    }

    private void OnDisable()
    {
        moveAction.Disable();
        InterruptActions();
    }

    private void OnDestroy() => moveAction.Dispose();
}

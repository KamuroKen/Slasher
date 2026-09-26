using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Animator), typeof(Damageable))]
[RequireComponent(typeof(SpriteHitFlash))]
public sealed class EnemyController : MonoBehaviour
{
    public enum BehaviourState { Idle, Chase, Attack, Recovery, Hit, Dead }
    [SerializeField, Min(0f)] private float detectionRadius = 6f;
    [SerializeField, Min(0f)] private float loseTargetRadius = 8f;
    [SerializeField, Min(0f)] private float moveSpeed = 2.5f;
    [SerializeField, Min(0f)] private float attackDistance = 0.9f;
    [SerializeField, Min(0f)] private float hitDistance = 1.15f;
    [SerializeField, Min(1)] private int attackDamage = 10;
    [SerializeField, Min(0f)] private float windupSeconds = 0.3f;
    [SerializeField, Min(0.1f)] private float attackSeconds = 0.5f;
    [SerializeField, Min(0f)] private float attackCooldown = 1f;
    [SerializeField, Min(0f)] private float hitStunSeconds = 0.2f;
    [SerializeField, Min(0f)] private float knockbackSpeed = 4f;
    [SerializeField, Min(0f)] private float deathSeconds = 0.625f;
    [SerializeField, Min(0f)] private float boundaryInset = 0.5f;
    private ArenaBounds arena;
    private Rigidbody2D body;
    private Animator animator;
    private Damageable health;
    private SpriteHitFlash flash;
    private PlayerHealth target;
    private Vector2 velocity, knockback, attackDirection;
    private float stateTime, nextAttackTime;
    private bool hitResolved;
    private string facing = "Left";
    private int animationState;
    public BehaviourState State { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        arena = FindFirstObjectByType<ArenaBounds>();
        animator = GetComponent<Animator>();
        health = GetComponent<Damageable>();
        flash = GetComponent<SpriteHitFlash>();
    }
    private void OnEnable() { health.Damaged += OnDamaged; health.Died += OnDied; }
    private void OnDisable()
    {
        health.Damaged -= OnDamaged; health.Died -= OnDied;
        velocity = Vector2.zero;
        if (body != null && body.simulated) body.linearVelocity = Vector2.zero;
    }
    private void Start()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) target = player.GetComponent<PlayerHealth>();
        Play("Idle_" + facing);
    }

    private void Update()
    {
        stateTime += Time.deltaTime;
        velocity = Vector2.zero;
        if (State == BehaviourState.Dead)
        {
            UpdateDeath();
            return;
        }
        if (State == BehaviourState.Hit && UpdateHit()) return;
        if (target == null || target.Health.IsDead)
        {
            Rest(BehaviourState.Idle);
            return;
        }
        if (State == BehaviourState.Attack)
        {
            UpdateAttack();
            return;
        }
        if (State == BehaviourState.Recovery && Time.time < nextAttackTime) return;
        UpdatePursuit();
    }

    private void UpdateDeath()
    {
        if (stateTime >= deathSeconds) Destroy(gameObject);
    }

    private bool UpdateHit()
    {
        if (stateTime < hitStunSeconds) return true;
        knockback = Vector2.zero;
        Enter(BehaviourState.Idle);
        return false;
    }

    private void UpdateAttack()
    {
        if (!hitResolved && stateTime >= windupSeconds)
        {
            hitResolved = true;
            Vector2 offset = (Vector2)target.transform.position - body.position;
            bool inFront = offset.sqrMagnitude < 0.001f || Vector2.Dot(offset.normalized, attackDirection) >= 0.5f;
            if (offset.magnitude <= hitDistance && inFront)
                target.Health.TryTakeDamage(attackDamage, body.position);
        }
        if (stateTime >= attackSeconds) Rest(BehaviourState.Recovery);
    }

    private void UpdatePursuit()
    {
        Vector2 toPlayer = (Vector2)target.transform.position - body.position;
        float distance = toPlayer.magnitude;
        float radius = State == BehaviourState.Chase ? Mathf.Max(detectionRadius, loseTargetRadius) : detectionRadius;
        if (distance > radius)
        {
            Rest(BehaviourState.Idle);
            return;
        }
        Face(toPlayer);
        if (distance <= attackDistance)
        {
            if (Time.time < nextAttackTime)
            {
                Rest(BehaviourState.Recovery);
                return;
            }
            attackDirection = Direction(facing);
            hitResolved = false;
            nextAttackTime = Time.time + attackSeconds + attackCooldown;
            Enter(BehaviourState.Attack);
            Play("Attack_"+facing, true);
            return;
        }
        Enter(BehaviourState.Chase);
        velocity = toPlayer.normalized * moveSpeed;
        Play("Idle_" + facing);
    }

    private void FixedUpdate()
    {
        if (State == BehaviourState.Dead) return;
        Vector2 step = State == BehaviourState.Hit ? knockback : velocity;
        Vector2 next = body.position + step * Time.fixedDeltaTime;
        body.MovePosition(arena != null ? arena.Clamp(next, Vector2.one * boundaryInset) : next);
        knockback *= Mathf.Exp(-10f * Time.fixedDeltaTime);
    }

    private void OnDamaged(Vector2 source)
    {
        velocity = Vector2.zero;
        Vector2 away = body.position - source;
        knockback = (away.sqrMagnitude > 0.001f ? away.normalized : -Direction(facing)) * knockbackSpeed;
        nextAttackTime = Time.time + hitStunSeconds + attackCooldown;
        Enter(BehaviourState.Hit);
        Play("Hit_"+facing, true);
        flash.Play();
    }
    private void OnDied()
    {
        Enter(BehaviourState.Dead);
        velocity = knockback = Vector2.zero;
        body.linearVelocity = Vector2.zero;
        body.simulated = false;
        foreach (var collider in GetComponentsInChildren<Collider2D>()) collider.enabled = false;
        flash.Stop();
        Play("Death", true);
    }
    private void Rest(BehaviourState state)
    {
        Enter(state);
        Play("Idle_" + facing);
    }

    private void Enter(BehaviourState next)
    {
        if (State == next && next != BehaviourState.Hit) return;
        State = next;
        stateTime = 0f;
    }
    private void Face(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return;
        facing = Mathf.Abs(direction.x) > Mathf.Abs(direction.y) ? (direction.x>0 ? "Right":"Left") : (direction.y>0 ? "Up":"Down");
    }
    private static Vector2 Direction(string direction) => direction == "Up" ? Vector2.up : direction == "Down" ? Vector2.down : direction == "Right" ? Vector2.right : Vector2.left;
    private void Play(string state, bool restart = false)
    {
        int hash = Animator.StringToHash(state);
        if (!restart && animationState == hash) return;
        animator.Play(hash, 0, 0f); animationState = hash;
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f,0.8f,0.2f,0.6f); Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(transform.position, attackDistance);
    }
}

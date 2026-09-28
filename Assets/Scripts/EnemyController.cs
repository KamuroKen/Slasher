using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(Animator), typeof(Damageable))]
[RequireComponent(typeof(SpriteHitFlash))]
public class EnemyController : MonoBehaviour
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
    private SpriteRenderer spriteRenderer;
    protected virtual string MovingAction => "Walk";
    protected virtual bool DirectionalDeath => true;
    protected virtual bool MirrorWest => true;
    private Damageable health;
    private SpriteHitFlash flash;
    private PlayerHealth target;
    private Vector2 velocity, knockback, attackDirection;
    private float stateTime, nextAttackTime;
    private bool hitResolved;
    private string facing = "Left";
    private int animationState;
    public BehaviourState State { get; private set; }

    protected virtual void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        arena = FindFirstObjectByType<ArenaBounds>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        health = GetComponent<Damageable>();
        flash = GetComponent<SpriteHitFlash>();
    }
    protected virtual void OnEnable() { health.Damaged += OnDamaged; health.Died += OnDied; }
    protected virtual void OnDisable()
    {
        health.Damaged -= OnDamaged; health.Died -= OnDied;
        velocity = Vector2.zero;
        if (body != null && body.simulated) body.linearVelocity = Vector2.zero;
    }
    protected virtual void Start()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) target = player.GetComponent<PlayerHealth>();
        PlayAction("Idle");
    }

    protected virtual void Update()
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
        facing = SelectFacing(toPlayer);
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
            PlayAction("Attack", true);
            return;
        }
        Enter(BehaviourState.Chase);
        velocity = toPlayer.normalized * moveSpeed;
        PlayAction(MovingAction);
    }

    protected virtual void FixedUpdate()
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
        PlayAction("Hit", true);
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
        PlayAction("Death", true);
    }
    private void Rest(BehaviourState state)
    {
        Enter(state);
        PlayAction("Idle");
    }

    private void Enter(BehaviourState next)
    {
        if (State == next && next != BehaviourState.Hit) return;
        State = next;
        stateTime = 0f;
    }
    // Five authored directions form eight views by mirroring the western ones.
    protected virtual string SelectFacing(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.001f) return facing;
        int sector = (Mathf.RoundToInt(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg / 45f) + 8) % 8;
        switch (sector)
        {
            case 0: return "Right";
            case 1: return "UpRight";
            case 2: return "Up";
            case 3: return "UpLeft";
            case 4: return "Left";
            case 5: return "DownLeft";
            case 6: return "Down";
            default: return "DownRight";
        }
    }

    private static Vector2 Direction(string direction)
    {
        float x = direction.Contains("Right") ? 1f : direction.Contains("Left") ? -1f : 0f;
        float y = direction.Contains("Up") ? 1f : direction.Contains("Down") ? -1f : 0f;
        return new Vector2(x, y).normalized;
    }

    private void PlayAction(string action, bool restart = false)
    {
        string animationFacing = facing;
        if (MirrorWest)
        {
            spriteRenderer.flipX = facing.Contains("Left");
            animationFacing = facing.Replace("Left", "Right");
        }
        string state = action == "Death" && !DirectionalDeath ? action : action + "_" + animationFacing;
        Play(state, restart);
    }
    private void Play(string state, bool restart = false)
    {
        int hash = Animator.StringToHash(state);
        if (!restart && animationState == hash) return;
        animator.Play(hash, 0, 0f); animationState = hash;
    }
    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f,0.8f,0.2f,0.6f); Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(transform.position, attackDistance);
    }
}

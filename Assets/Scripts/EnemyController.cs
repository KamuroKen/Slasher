using System.Collections.Generic;
using UnityEngine;

// Shared melee enemy: idle -> chase -> telegraphed attack -> recovery, with hit stagger and death.
// Animator states are named Action_Direction (Walk_Right, Attack_UpRight...). Missing states fall back
// to a non-directional or simpler action once and are reported in the console.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(Animator), typeof(Damageable))]
[RequireComponent(typeof(SpriteHitFlash))]
public class EnemyController : MonoBehaviour
{
    public enum BehaviourState { Idle, Chase, Attack, Recovery, Hit, Dead, Search }

    [Header("Senses and movement")]
    [SerializeField, Min(0f)] private float detectionRadius = 6f;
    [SerializeField, Min(0f)] private float loseTargetRadius = 8f;
    [SerializeField, Min(0f)] private float moveSpeed = 2.5f;
    [SerializeField, Min(0f)] private float boundaryInset = 0.5f;

    [Header("Awareness")]
    [Tooltip("The enemy only notices the player when no wall is in between.")]
    [SerializeField] private bool requireLineOfSight = true;
    [Tooltip("Layers that block the view. Nothing = Walls.")]
    [SerializeField] private LayerMask sightBlockers;
    [SerializeField, Min(0.02f)] private float sightCheckInterval = 0.15f;
    [Tooltip("How long the enemy keeps going to the last seen position before giving up.")]
    [SerializeField, Min(0f)] private float searchSeconds = 2f;
    [Tooltip("When hit, the enemy also wakes up allies within this radius. 0 = off.")]
    [SerializeField, Min(0f)] private float alertRadius = 4f;

    [Header("Steering")]
    [Tooltip("Solid layers the enemy walks around. Nothing = Walls + Default. Player and enemies are never obstacles.")]
    [SerializeField] private LayerMask obstacles;
    [Tooltip("How far ahead the enemy checks for walls, in units.")]
    [SerializeField, Min(0f)] private float lookAhead = 0.8f;
    [SerializeField, Range(4, 32)] private int steeringDirections = 16;
    [SerializeField, Min(0.02f)] private float steeringInterval = 0.1f;
    [Tooltip("Layer of other enemies used for spacing. Nothing = Enemies.")]
    [SerializeField] private LayerMask allies;
    [SerializeField, Min(0f)] private float separationRadius = 1f;
    [SerializeField, Min(0f)] private float separationStrength = 1.5f;

    [Header("Attack")]
    [Tooltip("Gap between this enemy's collider and the player's collider at which the attack starts.")]
    [SerializeField, Min(0f)] private float attackReach = 0.25f;
    [Tooltip("Gap at the hit moment within which the blow lands. Keep it larger than Attack Reach so a step back is needed to dodge.")]
    [SerializeField, Min(0f)] private float hitReach = 0.5f;
    [SerializeField, Min(1)] private int attackDamage = 10;
    [SerializeField, Min(0f)] private float attackCooldown = 1f;
    [Tooltip("Share of the attack animation at which the blow lands. Ignored when the clip has an AttackHit animation event.")]
    [SerializeField, Range(0f, 1f)] private float hitMoment = 0.5f;

    [Header("Stagger")]
    [Tooltip("Hits absorbed without being staggered. 0 = every hit staggers.")]
    [SerializeField, Min(0)] private int poise;
    [Tooltip("Seconds without being hit after which absorbed hits are forgotten.")]
    [SerializeField, Min(0f)] private float poiseResetSeconds = 2f;
    [Tooltip("Hits during an attack do not interrupt it, so the player cannot stun-lock the enemy.")]
    [SerializeField] private bool armoredWhileAttacking = true;
    [Tooltip("Delay after a stagger before the enemy may attack again.")]
    [SerializeField, Min(0f)] private float counterDelay = 0.25f;
    [SerializeField, Min(0f)] private float knockbackSpeed = 4f;

    [Header("Timings")]
    [Tooltip("Take attack, stagger and death durations from the animation clips.")]
    [SerializeField] private bool useClipTimings = true;
    [Tooltip("Fallback when there is no clip or clip timings are off.")]
    [SerializeField, Min(0f)] private float windupSeconds = 0.3f;
    [SerializeField, Min(0.1f)] private float attackSeconds = 0.5f;
    [SerializeField, Min(0f)] private float hitStunSeconds = 0.2f;
    [SerializeField, Min(0f)] private float deathSeconds = 0.625f;

    public const string AttackHitEvent = nameof(AttackHit);
    private static readonly Dictionary<string, string> MirroredFacing = new Dictionary<string, string>
    {
        { "Left", "Right" }, { "UpLeft", "UpRight" }, { "DownLeft", "DownRight" },
    };
    private readonly Dictionary<(string action, string facing), (int hash, bool exact)> stateCache = new Dictionary<(string, string), (int, bool)>();
    private readonly Dictionary<AnimationClip, bool> clipHasHitEvent = new Dictionary<AnimationClip, bool>();
    private readonly List<AnimatorClipInfo> clipInfo = new List<AnimatorClipInfo>();
    private readonly List<RaycastHit2D> castHits = new List<RaycastHit2D>();
    private readonly List<Collider2D> neighbours = new List<Collider2D>();
    private ContactFilter2D obstacleFilter, allyFilter;
    private Vector2 lastSeenPosition, steerDirection, steerTarget;
    private float lastSeenTime = float.NegativeInfinity, nextSightCheck, nextSteerTime, alertedUntil;
    private bool canSeeTarget = true;

    private ArenaBounds arena;
    private Rigidbody2D body;
    private Animator animator;
    private SpriteRenderer spriteRenderer;
    private Collider2D bodyCollider, targetCollider;
    protected virtual string MovingAction => "Walk";
    protected virtual bool DirectionalDeath => true;
    protected virtual bool MirrorWest => true;
    private Damageable health;
    private SpriteHitFlash flash;
    private PlayerHealth target;
    private Vector2 velocity, knockback, attackDirection;
    private float stateTime, stateDuration, hitTime, nextAttackTime, lastHurtTime;
    private int absorbedHits;
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
        bodyCollider = SolidCollider(gameObject);
        // Empty masks fall back to the project layers made by Tools > Enemies > Setup Layers.
        if (sightBlockers.value == 0) sightBlockers = LayerMask.GetMask("Walls");
        if (obstacles.value == 0) obstacles = LayerMask.GetMask("Walls", "Default");
        if (allies.value == 0) allies = LayerMask.GetMask("Enemies");
        obstacleFilter = new ContactFilter2D { useTriggers = false };
        obstacleFilter.SetLayerMask(obstacles);
        allyFilter = new ContactFilter2D { useTriggers = false };
        if (allies.value != 0) allyFilter.SetLayerMask(allies);
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
        if (player != null)
        {
            target = player.GetComponent<PlayerHealth>();
            targetCollider = SolidCollider(player);
        }
        PlayAction("Idle");
    }

    private static Collider2D SolidCollider(GameObject owner)
    {
        Collider2D fallback = null;
        foreach (var candidate in owner.GetComponentsInChildren<Collider2D>())
        {
            if (!candidate.isTrigger) return candidate;
            if (fallback == null) fallback = candidate;
        }
        return fallback;
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
        if (stateTime >= stateDuration) Destroy(gameObject);
    }

    private bool UpdateHit()
    {
        if (stateTime < stateDuration) return true;
        knockback = Vector2.zero;
        Enter(BehaviourState.Idle);
        return false;
    }

    private void UpdateAttack()
    {
        if (!hitResolved && stateTime >= hitTime) ResolveHit();
        if (stateTime >= stateDuration) Rest(BehaviourState.Recovery);
    }

    // Called by an "AttackHit" animation event placed on the frame where the weapon connects.
    public void AttackHit()
    {
        if (State == BehaviourState.Attack && !hitResolved) ResolveHit();
    }

    private void ResolveHit()
    {
        hitResolved = true;
        if (target == null || target.Health.IsDead) return;
        Vector2 offset = (Vector2)target.transform.position - body.position;
        bool inFront = offset.sqrMagnitude < 0.001f || Vector2.Dot(offset.normalized, attackDirection) >= 0.5f;
        if (GapToTarget() <= hitReach && inFront)
            target.Health.TryTakeDamage(attackDamage, body.position);
    }

    // Distance between collider edges, so wide and narrow enemies use the same reach values.
    private float GapToTarget()
    {
        if (bodyCollider != null && targetCollider != null && bodyCollider.enabled && targetCollider.enabled)
        {
            var distance = Physics2D.Distance(bodyCollider, targetCollider);
            if (distance.isValid) return Mathf.Max(0f, distance.distance);
        }
        return Vector2.Distance(body.position, target.transform.position);
    }

    private void UpdatePursuit()
    {
        Vector2 targetPosition = target.transform.position;
        Vector2 toPlayer = targetPosition - body.position;
        float distance = toPlayer.magnitude;
        bool alerted = Time.time < alertedUntil;
        bool engaged = State == BehaviourState.Chase || State == BehaviourState.Search || alerted;
        float radius = engaged ? Mathf.Max(detectionRadius, loseTargetRadius) : detectionRadius;
        if ((distance <= radius || alerted) && CanSeeTarget())
        {
            lastSeenPosition = targetPosition;
            lastSeenTime = Time.time;
            facing = SelectFacing(toPlayer);
            if (GapToTarget() <= attackReach)
            {
                if (Time.time < nextAttackTime)
                {
                    Rest(BehaviourState.Recovery);
                    return;
                }
                StartAttack();
                return;
            }
            Enter(BehaviourState.Chase);
            MoveTowards(targetPosition);
            return;
        }
        // Lost sight: walk to where the player was last seen, then give up.
        if (engaged && Time.time - lastSeenTime <= searchSeconds)
        {
            Vector2 toLastSeen = lastSeenPosition - body.position;
            if (toLastSeen.sqrMagnitude > 0.09f)
            {
                Enter(BehaviourState.Search);
                facing = SelectFacing(toLastSeen);
                MoveTowards(lastSeenPosition);
                return;
            }
        }
        alertedUntil = 0f;
        Rest(BehaviourState.Idle);
    }

    private bool CanSeeTarget()
    {
        if (!requireLineOfSight || sightBlockers.value == 0) return true;
        if (Time.time < nextSightCheck) return canSeeTarget;
        nextSightCheck = Time.time + sightCheckInterval * Random.Range(0.8f, 1.2f);
        Vector2 from = bodyCollider != null ? (Vector2)bodyCollider.bounds.center : body.position;
        Vector2 to = targetCollider != null ? (Vector2)targetCollider.bounds.center : (Vector2)target.transform.position;
        canSeeTarget = !Physics2D.Linecast(from, to, sightBlockers);
        return canSeeTarget;
    }

    // Wakes the enemy up and sends it towards a position, e.g. where it was hit from.
    public void Alert(Vector2 position)
    {
        if (State == BehaviourState.Dead) return;
        lastSeenPosition = position;
        lastSeenTime = Time.time;
        alertedUntil = Time.time + Mathf.Max(searchSeconds, 1f);
        nextSightCheck = 0f;
    }

    private void AlertAllies(Vector2 position)
    {
        if (alertRadius <= 0f) return;
        neighbours.Clear();
        Physics2D.OverlapCircle(body.position, alertRadius, allyFilter, neighbours);
        foreach (var other in neighbours)
        {
            var ally = other.GetComponentInParent<EnemyController>();
            if (ally != null && ally != this) ally.Alert(position);
        }
    }

    private void MoveTowards(Vector2 goal)
    {
        Vector2 desired = goal - body.position;
        if (desired.sqrMagnitude < 0.0001f) return;
        desired.Normalize();
        if (Time.time >= nextSteerTime || steerTarget == Vector2.zero)
        {
            nextSteerTime = Time.time + steeringInterval;
            steerTarget = ChooseDirection(desired);
        }
        steerDirection = steerDirection == Vector2.zero
            ? steerTarget
            : Vector2.Lerp(steerDirection, steerTarget, 1f - Mathf.Exp(-12f * Time.deltaTime)).normalized;
        velocity = steerDirection * moveSpeed;
        PlayAction(MovingAction);
    }

    // Context steering: score directions around the wanted one, penalise walls ahead, add spacing from allies.
    private Vector2 ChooseDirection(Vector2 desired)
    {
        Vector2 separation = Separation();
        int count = Mathf.Max(4, steeringDirections);
        float startAngle = Mathf.Atan2(desired.y, desired.x);
        Vector2 best = desired;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            float angle = startAngle + i * (Mathf.PI * 2f / count);
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float score = Vector2.Dot(direction, desired) + Vector2.Dot(direction, separation) * separationStrength;
            score -= (1f - Clearance(direction)) * 3f;
            if (score > bestScore) { bestScore = score; best = direction; }
        }
        return best;
    }

    // 1 = free for Look Ahead units, 0 = a wall right next to the enemy.
    private float Clearance(Vector2 direction)
    {
        if (lookAhead <= 0f || obstacles.value == 0) return 1f;
        castHits.Clear();
        int count = body.Cast(direction, obstacleFilter, castHits, lookAhead);
        float nearest = lookAhead;
        for (int i = 0; i < count; i++)
        {
            var hit = castHits[i];
            // The player and other enemies are dynamic bodies; only scenery counts as an obstacle.
            if (hit.rigidbody != null && hit.rigidbody.bodyType == RigidbodyType2D.Dynamic) continue;
            if (hit.distance < nearest) nearest = hit.distance;
        }
        return nearest / lookAhead;
    }

    private Vector2 Separation()
    {
        if (separationRadius <= 0f || separationStrength <= 0f) return Vector2.zero;
        neighbours.Clear();
        Physics2D.OverlapCircle(body.position, separationRadius, allyFilter, neighbours);
        Vector2 push = Vector2.zero;
        foreach (var other in neighbours)
        {
            if (other.attachedRigidbody == body || other.GetComponentInParent<EnemyController>() == null) continue;
            Vector2 otherPosition = other.attachedRigidbody != null ? other.attachedRigidbody.position : (Vector2)other.transform.position;
            Vector2 away = body.position - otherPosition;
            float distance = away.magnitude;
            if (distance < 0.001f) away = Random.insideUnitCircle;
            push += away.normalized * (1f - Mathf.Clamp01(distance / separationRadius));
        }
        return Vector2.ClampMagnitude(push, 1f);
    }

    private void StartAttack()
    {
        attackDirection = Direction(facing);
        hitResolved = false;
        Enter(BehaviourState.Attack);
        float clipLength = PlayAction("Attack", true, out bool hasHitEvent);
        bool fromClip = useClipTimings && clipLength > 0f;
        stateDuration = fromClip ? clipLength : attackSeconds;
        // With an AttackHit event the clip decides; the timer only guards against a missing event.
        hitTime = hasHitEvent ? stateDuration : fromClip ? clipLength * hitMoment : Mathf.Min(windupSeconds, attackSeconds);
        nextAttackTime = Time.time + stateDuration + attackCooldown;
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
        flash.Play();
        // A hit from outside the detection radius still starts a chase, and nearby allies join in.
        Vector2 attacker = target != null ? (Vector2)target.transform.position : source;
        Alert(attacker);
        AlertAllies(attacker);
        if (Time.time - lastHurtTime > poiseResetSeconds) absorbedHits = 0;
        lastHurtTime = Time.time;
        // Armor and poise take the damage without interrupting what the enemy is doing.
        if (armoredWhileAttacking && State == BehaviourState.Attack) return;
        if (absorbedHits < poise) { absorbedHits++; return; }
        absorbedHits = 0;

        velocity = Vector2.zero;
        Vector2 away = body.position - source;
        facing = SelectFacing(-away);
        knockback = (away.sqrMagnitude > 0.001f ? away.normalized : -Direction(facing)) * knockbackSpeed;
        Enter(BehaviourState.Hit);
        float clipLength = PlayAction("Hit", true, out _);
        stateDuration = useClipTimings && clipLength > 0f ? clipLength : hitStunSeconds;
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + stateDuration + counterDelay);
    }
    private void OnDied()
    {
        Enter(BehaviourState.Dead);
        velocity = knockback = Vector2.zero;
        body.linearVelocity = Vector2.zero;
        body.simulated = false;
        foreach (var collider in GetComponentsInChildren<Collider2D>()) collider.enabled = false;
        flash.Stop();
        float clipLength = PlayAction("Death", true, out _);
        stateDuration = useClipTimings && clipLength > 0f ? clipLength : deathSeconds;
    }
    private void Rest(BehaviourState state)
    {
        Enter(state);
        steerDirection = steerTarget = Vector2.zero;
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

    private void PlayAction(string action) => PlayAction(action, false, out _);

    // Plays the state for the current facing. Returns the clip length in seconds when the exact
    // state exists and was (re)started, otherwise 0 so callers use their fallback timings.
    private float PlayAction(string action, bool restart, out bool hasHitEvent)
    {
        hasHitEvent = false;
        string animationFacing = facing;
        if (MirrorWest)
        {
            spriteRenderer.flipX = facing.Contains("Left");
            if (MirroredFacing.TryGetValue(facing, out var mirrored)) animationFacing = mirrored;
        }
        var (hash, exact) = ResolveState(action, animationFacing);
        if (hash == 0 || (!restart && animationState == hash)) return 0f;
        animator.Play(hash, 0, 0f); animationState = hash;
        if (!restart || !exact) return 0f;
        // Apply the new state now so its length and clip are known this frame.
        animator.Update(0f);
        animator.GetCurrentAnimatorClipInfo(0, clipInfo);
        if (clipInfo.Count > 0) hasHitEvent = HasHitEvent(clipInfo[0].clip);
        return animator.GetCurrentAnimatorStateInfo(0).length;
    }

    private bool HasHitEvent(AnimationClip clip)
    {
        if (clip == null) return false;
        if (clipHasHitEvent.TryGetValue(clip, out bool has)) return has;
        has = false;
        foreach (var animationEvent in clip.events) if (animationEvent.functionName == AttackHitEvent) { has = true; break; }
        clipHasHitEvent[clip] = has;
        return has;
    }

    private (int hash, bool exact) ResolveState(string action, string animationFacing)
    {
        var key = (action, animationFacing);
        if (stateCache.TryGetValue(key, out var cached)) return cached;
        bool directional = action != "Death" || DirectionalDeath;
        string wanted = directional ? action + "_" + animationFacing : action;
        string otherSpelling = directional ? action : action + "_" + animationFacing;
        (int hash, bool exact) result = (0, false);
        if (TryState(wanted, out int hash) || TryState(otherSpelling, out hash)) result = (hash, true);
        else
        {
            for (string substitute = Substitute(action); substitute != null; substitute = Substitute(substitute))
                if (TryState(substitute + "_" + animationFacing, out hash) || TryState(substitute, out hash)) { result = (hash, false); break; }
            Debug.LogWarning(result.hash != 0
                ? $"{name}: no animator state '{wanted}', playing a substitute instead."
                : $"{name}: no animator state '{wanted}' and no substitute for it.", this);
        }
        stateCache[key] = result;
        return result;
    }

    private bool TryState(string stateName, out int hash)
    {
        hash = Animator.StringToHash(stateName);
        return animator.HasState(0, hash);
    }

    private static string Substitute(string action) => action switch
    {
        "Hit" => "Idle",
        "Attack" => "Idle",
        "Walk" => "Idle",
        _ => null,
    };

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f); Gizmos.DrawWireSphere(transform.position, detectionRadius);
        var solid = bodyCollider != null ? bodyCollider : SolidCollider(gameObject);
        if (solid == null) return;
        var bounds = solid.bounds;
        Gizmos.color = Color.red; Gizmos.DrawWireCube(bounds.center, bounds.size + Vector3.one * (2f * attackReach));
        Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.5f); Gizmos.DrawWireCube(bounds.center, bounds.size + Vector3.one * (2f * hitReach));
    }
}

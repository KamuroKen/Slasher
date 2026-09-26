using UnityEngine;

[RequireComponent(typeof(Damageable), typeof(SpriteHitFlash), typeof(PlayerMovement))]
public sealed class PlayerHealth : MonoBehaviour
{
    [SerializeField, Min(0f)] private float invulnerabilitySeconds = 0.5f;
    [SerializeField, Min(0f)] private float hurtSeconds = 0.2f;
    [SerializeField, Min(0f)] private float flashSeconds = 0.12f;
    private Damageable health;
    private SpriteHitFlash flash;
    private PlayerMovement movement;
    private Animator animator;
    public Damageable Health => health;

    private void Awake()
    {
        health = GetComponent<Damageable>();
        flash = GetComponent<SpriteHitFlash>();
        movement = GetComponent<PlayerMovement>();
        animator = GetComponent<Animator>();
        health.InvulnerabilityDuration = invulnerabilitySeconds;
    }

    private void OnEnable() { health.Damaged += OnDamaged; health.Died += OnDied; }
    private void OnDisable() { health.Damaged -= OnDamaged; health.Died -= OnDied; }
    private void OnDamaged(Vector2 source)
    {
        movement.PlayHurt(hurtSeconds);
        flash.Play(flashSeconds, invulnerabilitySeconds);
    }

    private void OnDied()
    {
        movement.enabled = false;
        animator.Play(Animator.StringToHash("Death_" + movement.Facing), 0, 0f);
        GetComponent<Rigidbody2D>().simulated = false;
        foreach (var collider in GetComponentsInChildren<Collider2D>()) collider.enabled = false;
        flash.Stop();
    }

}

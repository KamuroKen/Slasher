using System;
using UnityEngine;

public sealed class Damageable : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField, Min(0f)] private float invulnerabilityDuration;
    [SerializeField] private int currentHealth;

    private float invulnerableUntil;
    public event Action<Vector2> Damaged;
    public event Action Died;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public float InvulnerabilityDuration { get => invulnerabilityDuration; set => invulnerabilityDuration = Mathf.Max(0f, value); }
    public bool IsInvulnerable => Time.time < invulnerableUntil;
    public bool IsDead => CurrentHealth <= 0;

    private void Awake() => currentHealth = maxHealth;

    public bool TryTakeDamage(int amount, Vector2 sourcePosition)
    {
        if (!isActiveAndEnabled || amount <= 0 || IsDead || IsInvulnerable) return false;
        currentHealth = Mathf.Max(0, currentHealth - amount);
        invulnerableUntil = Time.time + invulnerabilityDuration;
        // A lethal hit goes straight to death: no hit animation can override it.
        if (IsDead)
        {
            Died?.Invoke();
        }
        else
        {
            Damaged?.Invoke(sourcePosition);
        }
        return true;
    }
}

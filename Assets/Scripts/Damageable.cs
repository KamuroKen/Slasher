using UnityEngine;
using UnityEngine.Events;

// Add this to a future enemy's root, with a Collider2D on it or a child.
public sealed class Damageable : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField] private UnityEvent onDamaged = new UnityEvent();
    [SerializeField] private UnityEvent onDeath = new UnityEvent();

    public int CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0;

    private void Awake() => CurrentHealth = maxHealth;

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsDead) return;
        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        onDamaged.Invoke();
        if (IsDead) onDeath.Invoke();
    }
}

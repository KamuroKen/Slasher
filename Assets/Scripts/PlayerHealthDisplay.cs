using UnityEngine;

[RequireComponent(typeof(Damageable))]
public sealed class PlayerHealthDisplay : MonoBehaviour
{
    private Damageable health;
    private void Awake() => health = GetComponent<Damageable>();

    private void OnGUI()
    {
        GUI.Box(new Rect(16, 16, 170, 46), "HP  " + health.CurrentHealth + " / " + health.MaxHealth);
        var previous = GUI.color;
        GUI.color = new Color(0.25f, 0.85f, 0.45f);
        GUI.DrawTexture(new Rect(24, 46, 154f * health.CurrentHealth / health.MaxHealth, 7), Texture2D.whiteTexture);
        GUI.color = previous;
    }
}

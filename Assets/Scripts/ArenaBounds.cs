using UnityEngine;

public sealed class ArenaBounds : MonoBehaviour
{
    [SerializeField] private Vector2 min = new Vector2(-16f, -10f);
    [SerializeField] private Vector2 max = new Vector2(16f, 10f);

    public Vector2 Clamp(Vector2 position, Vector2 inset)
    {
        Vector2 origin = transform.position;
        return new Vector2(
            ClampAxis(position.x, origin.x + min.x, origin.x + max.x, inset.x),
            ClampAxis(position.y, origin.y + min.y, origin.y + max.y, inset.y));
    }

    private static float ClampAxis(float value, float min, float max, float inset)
        => max - min <= 2f * inset ? (min + max) * 0.5f : Mathf.Clamp(value, min + inset, max - inset);

    private void OnValidate() => max = Vector2.Max(min, max);
}

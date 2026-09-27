using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class PlayerCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    private Camera view;
    private ArenaBounds arena;

    private void Awake()
    {
        view = GetComponent<Camera>();
        arena = FindFirstObjectByType<ArenaBounds>();
    }

    private void LateUpdate()
    {
        if (target == null) return;
        float halfHeight = view.orthographicSize;
        Vector2 next = target.position;
        if (arena != null) next = arena.Clamp(next, new Vector2(halfHeight * view.aspect, halfHeight));
        transform.position = new Vector3(next.x, next.y, transform.position.z);
    }
}

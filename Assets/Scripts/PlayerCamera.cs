using UnityEngine;

[RequireComponent(typeof(Camera))]
public sealed class PlayerCamera : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector2 arenaMin = new Vector2(-16f, -10f);
    [SerializeField] private Vector2 arenaMax = new Vector2(16f, 10f);
    private Camera view;

    public void SetTarget(Transform value) => target = value;
    private void Awake() => view = GetComponent<Camera>();

    private void LateUpdate()
    {
        if (target == null) return;
        float halfHeight = view.orthographicSize;
        float halfWidth = halfHeight * view.aspect;
        float x = ClampCenter(target.position.x, arenaMin.x, arenaMax.x, halfWidth);
        float y = ClampCenter(target.position.y, arenaMin.y, arenaMax.y, halfHeight);
        transform.position = new Vector3(x, y, transform.position.z);
    }

    private static float ClampCenter(float value, float min, float max, float extent)
        => max - min <= 2f * extent ? (min + max) * 0.5f : Mathf.Clamp(value, min + extent, max - extent);
}

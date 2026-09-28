using UnityEngine;

// Bat keeps its four authored views, flying Idle and non-directional Death.
public sealed class BatController : EnemyController
{
    protected override string MovingAction => "Idle";
    protected override bool DirectionalDeath => false;
    protected override bool MirrorWest => false;

    protected override string SelectFacing(Vector2 direction)
    {
        return Mathf.Abs(direction.x) > Mathf.Abs(direction.y)
            ? (direction.x > 0f ? "Right" : "Left")
            : (direction.y > 0f ? "Up" : "Down");
    }
}

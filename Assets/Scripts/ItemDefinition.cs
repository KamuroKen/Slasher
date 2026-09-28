using UnityEngine;

[CreateAssetMenu(menuName = "Slasher/Item")]
public sealed class ItemDefinition : ScriptableObject
{
    public string displayName;
    [TextArea(3, 12)] public string description;
    [TextArea(2, 10)] public string statistics;
    public Sprite icon;
    [Min(1)] public int maxStack = 20;
    [Min(0)] public int healing = 25;
}

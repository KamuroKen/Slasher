using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Visual state of one inventory or quick slot. References are assigned in the saved UI;
// missing ones are looked up once by child name (Icon, Cooldown, Count, Key).
[DisallowMultipleComponent]
public sealed class InventorySlotView : MonoBehaviour
{
    public Image frame;
    public Image icon;
    [Tooltip("Optional. Dark radial overlay drawn over the icon while quick slots recharge.")]
    public Image cooldown;
    public TMP_Text count;
    [Tooltip("Optional. Quick slot number label.")]
    public TMP_Text key;

    static readonly Color DenyColor = new(1f, .35f, .35f);
    Color baseColor = Color.white;
    Vector2 iconPosition;
    float pulse, deny;
    bool bound;

    public void Bind()
    {
        if (bound) return;
        bound = true;
        if (frame == null) frame = GetComponent<Image>();
        if (icon == null) icon = Child<Image>("Icon");
        if (cooldown == null) cooldown = Child<Image>("Cooldown");
        if (count == null) count = Child<TMP_Text>("Count");
        if (key == null) key = Child<TMP_Text>("Key");
        if (icon != null)
        {
            iconPosition = icon.rectTransform.anchoredPosition;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }
        if (cooldown != null)
        {
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Radial360;
            cooldown.fillOrigin = (int)Image.Origin360.Top;
            cooldown.fillClockwise = false;
            cooldown.preserveAspect = true;
            cooldown.raycastTarget = false;
            cooldown.enabled = false;
        }
        if (count != null) count.raycastTarget = false;
        if (key != null) key.raycastTarget = false;
    }

    T Child<T>(string childName) where T : Component
    {
        var child = transform.Find(childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    public void Show(ItemDefinition item, int amount, bool depleted, Color frameColor, string keyLabel, bool showCount, bool ghosted)
    {
        Bind();
        baseColor = frameColor;
        ApplyFrame();
        var sprite = item != null ? item.icon : null;
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.color = new Color(1f, 1f, 1f, depleted ? .35f : ghosted ? .4f : 1f);
        }
        if (cooldown != null) cooldown.sprite = sprite;
        if (count != null)
        {
            count.text = amount.ToString();
            count.enabled = item != null && showCount;
        }
        if (key != null && keyLabel != null) key.text = keyLabel;
    }

    public void SetCooldown(float fraction)
    {
        if (cooldown == null) return;
        bool visible = fraction > 0f && cooldown.sprite != null;
        if (cooldown.enabled != visible) cooldown.enabled = visible;
        if (visible) cooldown.fillAmount = fraction;
    }

    public void Pulse() => pulse = 1f;
    public void Deny() => deny = 1f;

    void Update()
    {
        if (pulse <= 0f && deny <= 0f) return;
        float dt = Time.unscaledDeltaTime;
        pulse = Mathf.Max(0f, pulse - dt * 8f);
        deny = Mathf.Max(0f, deny - dt * 4f);
        if (icon != null)
        {
            icon.rectTransform.localScale = Vector3.one * (1f + .15f * pulse);
            icon.rectTransform.anchoredPosition = iconPosition + Vector2.right * (Mathf.Sin(deny * 40f) * 4f * deny);
        }
        ApplyFrame();
    }

    void ApplyFrame()
    {
        if (frame != null) frame.color = Color.Lerp(baseColor, DenyColor, deny);
    }
}

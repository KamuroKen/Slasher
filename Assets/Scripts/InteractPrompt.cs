using TMPro;
using UnityEngine;

// The hint above something the player can use ("[E] Open", "Locked - needs ..."). Shared by doors and chests:
// it pops in when shown, can shake when an action is refused and can hide itself after a few seconds.
// The owner calls Update() every frame.
public sealed class InteractPrompt
{
    private const float PopSeconds = 0.15f, ShakeSeconds = 0.3f, Overshoot = 1.7f;
    private readonly TMP_Text text;
    private readonly Vector3 position, scale;
    private string current;
    private float shownAt, shakeAt = float.NegativeInfinity, hideAt = float.PositiveInfinity;

    public InteractPrompt(TMP_Text text)
    {
        this.text = text;
        if (text == null) return;
        position = text.transform.localPosition;
        scale = text.transform.localScale;
        text.gameObject.SetActive(false);
    }

    public bool Visible => text != null && text.gameObject.activeSelf;

    // seconds: hide automatically after this long; infinity keeps it until Hide().
    public void Show(string value, float seconds = float.PositiveInfinity)
    {
        if (text == null) return;
        if (!text.gameObject.activeSelf)
        {
            text.gameObject.SetActive(true);
            shownAt = Time.time;
        }
        if (current != value) { current = value; text.text = value; }
        hideAt = float.IsPositiveInfinity(seconds) ? float.PositiveInfinity : Time.time + seconds;
    }

    public void Hide()
    {
        if (!Visible) return;
        text.gameObject.SetActive(false);
        current = null;
        hideAt = float.PositiveInfinity;
    }

    public void Shake() => shakeAt = Time.time;

    public void Update()
    {
        if (!Visible) return;
        if (Time.time >= hideAt) { Hide(); return; }
        float t = Mathf.Clamp01((Time.time - shownAt) / PopSeconds) - 1f;
        float pop = 1f + (Overshoot + 1f) * t * t * t + Overshoot * t * t; // ease-out-back
        text.transform.localScale = scale * pop;
        float shake = Time.time - shakeAt;
        float offset = shake < ShakeSeconds ? Mathf.Sin(shake * 60f) * 0.06f * (1f - shake / ShakeSeconds) : 0f;
        text.transform.localPosition = position + new Vector3(offset, 0f, 0f);
    }
}

using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class SpriteHitFlash : MonoBehaviour
{
    private static readonly int FlashAmount = Shader.PropertyToID("_FlashAmount");
    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock properties;
    private Material ownedMaterial;
    private float whiteUntil, blinkUntil;
    private Color originalColor;
    private bool playing;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalColor = spriteRenderer.color;
        properties = new MaterialPropertyBlock();
        if (!spriteRenderer.sharedMaterial.HasProperty(FlashAmount))
        {
            var shader = Shader.Find("Slasher/Sprite Hit Flash");
            if (shader != null)
            {
                ownedMaterial = new Material(shader);
                spriteRenderer.sharedMaterial = ownedMaterial;
            }
        }
    }

    public void Play(float whiteDuration = 0.12f, float blinkDuration = 0f)
    {
        if (!playing) originalColor = spriteRenderer.color;
        playing = true;
        whiteUntil = Time.time + whiteDuration;
        blinkUntil = Time.time + blinkDuration;
        Update();
    }

    private void Update()
    {
        if (!playing) return;
        spriteRenderer.GetPropertyBlock(properties);
        properties.SetFloat(FlashAmount, Time.time < whiteUntil ? 1f : 0f);
        spriteRenderer.SetPropertyBlock(properties);
        Color color = originalColor;
        if (Time.time < blinkUntil && Time.time >= whiteUntil)
            color.a *= Mathf.FloorToInt(Time.time * 14f) % 2 == 0 ? 0.3f : 1f;
        spriteRenderer.color = color;
        playing = Time.time < whiteUntil || Time.time < blinkUntil;
    }

    public void Stop()
    {
        whiteUntil = blinkUntil = 0f;
        if (spriteRenderer != null) Update();
    }

    private void OnDisable() => Stop();
    private void OnDestroy() { if (ownedMaterial != null) Destroy(ownedMaterial); }
}

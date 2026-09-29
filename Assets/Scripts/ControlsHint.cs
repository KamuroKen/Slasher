using UnityEngine;

// The controls panel on the right side of the screen (Canvas / controls hint). The panel is an ordinary UI object
// saved in the scene: change its texts, sizes and rows by hand (Ctrl+D on a row adds another one). This script
// creates nothing - it only slides the panel in when a run starts, keeps it for a while and fades it out.
// While the inventory or any menu is open the panel is hidden and its time stands still.
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class ControlsHint : MonoBehaviour
{
    [Tooltip("Seconds the panel stays on screen during gameplay. Time with the inventory or a menu open doesn't count.")]
    [SerializeField, Min(0f)] private float showSeconds = 15f;
    [Tooltip("Pause after the level starts before the panel slides in.")]
    [SerializeField, Min(0f)] private float delay = 0.5f;
    [SerializeField, Min(0.01f)] private float slideInSeconds = 0.3f;
    [SerializeField, Min(0.01f)] private float fadeOutSeconds = 0.5f;
    [Tooltip("How far to the right of its place the panel starts sliding in from.")]
    [SerializeField] private float slideDistance = 60f;

    private CanvasGroup group;
    private RectTransform rect;
    private Vector2 restPosition;
    private float elapsed;  // gameplay time since the level started
    private float shown;    // 0 = hidden, 1 = fully on screen

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        rect = (RectTransform)transform;
        restPosition = rect.anchoredPosition;
        group.interactable = false;
        group.blocksRaycasts = false; // never catches clicks meant for the game or the inventory
        Apply(0f, 0f);
    }

    private void Update()
    {
        var ui = PlayerUI.Instance;
        if (ui != null && ui.CurrentScreen != PlayerUI.Screen.Gameplay)
        {
            shown = 0f; // gone at once under the inventory and menus; slides back in when the game resumes
            Apply(0f, 0f);
            return;
        }

        elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        bool wanted = elapsed >= delay && elapsed < delay + showSeconds;
        if (wanted)
        {
            shown = Mathf.MoveTowards(shown, 1f, Time.unscaledDeltaTime / slideInSeconds);
            float eased = 1f - Mathf.Pow(1f - shown, 3f);
            Apply(eased, (1f - eased) * slideDistance);
        }
        else if (elapsed >= delay + showSeconds)
        {
            shown = Mathf.MoveTowards(shown, 0f, Time.unscaledDeltaTime / fadeOutSeconds);
            Apply(Mathf.SmoothStep(0f, 1f, shown), 0f);
            if (shown <= 0f) gameObject.SetActive(false); // done for this run; Retry reloads the level and shows it again
        }
    }

    private void Apply(float alpha, float offset)
    {
        group.alpha = alpha;
        rect.anchoredPosition = restPosition + new Vector2(offset, 0f);
    }
}

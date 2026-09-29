using System.Collections;
using UnityEngine;

// Turns a door into the level exit: once it has opened, the run is over and the Victory menu
// (Retry, Main Menu, Quit) appears. Put it next to a KeyDoor; without a required key the door opens with E.
[RequireComponent(typeof(KeyDoor))]
public sealed class LevelExit : MonoBehaviour
{
    [Tooltip("Seconds between the door being fully open and the end menu.")]
    [SerializeField, Min(0f)] private float delay = 0.4f;
    private KeyDoor door;

    private void Awake() => door = GetComponent<KeyDoor>();
    private void OnEnable() => door.Opened += Finish;
    private void OnDisable() => door.Opened -= Finish;

    private void Finish() => StartCoroutine(ShowEnd());

    private IEnumerator ShowEnd()
    {
        yield return new WaitForSeconds(delay);
        if (PlayerUI.Instance != null) PlayerUI.Instance.ShowVictory();
    }
}

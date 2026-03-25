using UnityEngine;

/// Temporary test script — on-screen buttons to trigger end game sequences.
/// Remove this script before shipping.
public class TestButtons : MonoBehaviour
{
    void OnGUI()
    {
        if (GUI.Button(new Rect(10, 10, 150, 50), "TEST WIN"))
        {
            Debug.Log("[TestTrigger] Forcing WIN sequence.");
            FindFirstObjectByType<EndGameAnimator>()?.PlayWinSequence();
        }

        if (GUI.Button(new Rect(10, 70, 150, 50), "TEST LOSE"))
        {
            Debug.Log("[TestTrigger] Forcing LOSE sequence.");
            FindFirstObjectByType<EndGameAnimator>()?.PlayLoseSequence();
        }

        if (GUI.Button(new Rect(10, 130, 150, 50), "TEST LAVA"))
        {
            Debug.Log("[TestTrigger] Spawning lava.");
            FindFirstObjectByType<LavaSpawner>()?.SpawnAll();
        }

        if (GUI.Button(new Rect(10, 190, 150, 50), "TEST FLAGS"))
        {
            Debug.Log("[TestTrigger] Spawning start/finish flags.");
            FindFirstObjectByType<StartFinishSpawner>()?.SpawnAll();
        }

        if (GUI.Button(new Rect(10, 250, 150, 50), "TEST RESTART"))
        {
            Debug.Log("[TestTrigger] Restarting into alternate room.");
            GameManager.Instance?.RestartToAlternateRoom();
        }
    }
}

using UnityEngine;

/// Temporary test script — on-screen buttons to trigger end game sequences.
/// Remove this script before shipping.
public class EndGameTestTrigger : MonoBehaviour
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
    }
}

using TMPro;
using UnityEngine;

public class ToolModeUI : MonoBehaviour
{
    public GridSystem gridSystem;
    public TextMeshProUGUI modeText;

    // void Update()
    // {
    //     if (gridSystem == null || modeText == null)
    //         return;

    //     modeText.text = "Mode: " + gridSystem.currentMode.ToString();
    // }
    void Start()
    {
        UpdateModeText();
    }

    public void UpdateModeText()
    {
        if (gridSystem == null || modeText == null)
            return;

        modeText.text = "Mode: " + gridSystem.currentMode.ToString();
    }
}
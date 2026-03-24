using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class HoldingTracker : MonoBehaviour
{
    public static HoldingTracker Instance { get; private set; }

    private readonly HashSet<XRGrabInteractable> heldTools = new HashSet<XRGrabInteractable>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RegisterHeldTool(XRGrabInteractable tool)
    {
        if (tool == null) return;
        heldTools.Add(tool);
    }

    public void UnregisterHeldTool(XRGrabInteractable tool)
    {
        if (tool == null) return;
        heldTools.Remove(tool);
    }

    public bool IsHoldingOtherTool(XRGrabInteractable tool)
    {
        if (heldTools.Count == 0) return false;
        if (tool != null && heldTools.Count == 1 && heldTools.Contains(tool)) return false;
        return tool == null || !heldTools.Contains(tool) || heldTools.Count > 1;
    }
}

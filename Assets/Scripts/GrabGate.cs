using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class GrabGate : MonoBehaviour
{
    private XRGrabInteractable grab;
    private HoldingTracker tracker;
    private bool isRegisteredHeld;
    private bool gameStart = false;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        tracker = HoldingTracker.Instance != null ? HoldingTracker.Instance : FindFirstObjectByType<HoldingTracker>();
    }

    private void OnEnable()
    {
        if (grab == null)
            grab = GetComponent<XRGrabInteractable>();

        grab.selectEntered.AddListener(OnSelectEntered);
        grab.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        if (grab == null) return;

        grab.selectEntered.RemoveListener(OnSelectEntered);
        grab.selectExited.RemoveListener(OnSelectExited);

        if (isRegisteredHeld && tracker != null)
        {
            tracker.UnregisterHeldTool(grab);
            isRegisteredHeld = false;
        }
    }

    private void Update()
    {
        if (!gameStart) return;
        if (tracker == null)
            tracker = HoldingTracker.Instance != null ? HoldingTracker.Instance : FindFirstObjectByType<HoldingTracker>();

        if (grab == null || tracker == null) return;

        bool shouldEnable = !tracker.IsHoldingOtherTool(grab) || grab.isSelected;
        if (grab.enabled != shouldEnable)
            grab.enabled = shouldEnable;
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (tracker == null || grab == null) return;

        tracker.RegisterHeldTool(grab);
        isRegisteredHeld = true;
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (tracker == null || grab == null) return;

        tracker.UnregisterHeldTool(grab);
        isRegisteredHeld = false;
    }

    public void OnGameStart()
    {
        gameStart = true;
    }
}

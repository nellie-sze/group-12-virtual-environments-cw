using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(VillagerAgent))]
public class VillagerGrabHook : MonoBehaviour
{
    private VillagerAgent agent;
    private XRGrabInteractable grab;

    void Awake()
    {
        agent = GetComponent<VillagerAgent>();
        grab = GetComponent<XRGrabInteractable>();
    }

    void OnEnable()
    {
        grab.selectEntered.AddListener(OnSelectEntered);
        grab.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        grab.selectEntered.RemoveListener(OnSelectEntered);
        grab.selectExited.RemoveListener(OnSelectExited);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        agent.BeginHold();
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        

        agent.EndHold(transform.position);
    }
}
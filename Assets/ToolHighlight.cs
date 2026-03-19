using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Attach to any tool that has an XRGrabInteractable.
/// Pulses the emission colour when a controller hovers over it.
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class ToolHighlight : MonoBehaviour
{
    [Header("Highlight")]
    public Color highlightColor = new Color(0f, 0.8f, 1f); // cyan glow
    [Range(0.5f, 5f)]
    public float pulseSpeed = 2f;
    [Range(0f, 2f)]
    public float pulseIntensity = 1.2f;

    private XRGrabInteractable grab;
    private Renderer[] renderers;
    private Material[][] originalMaterials;
    private Material[][] instanceMaterials;
    private bool isHovered = false;

    void Start()
    {
        grab = GetComponent<XRGrabInteractable>();
        grab.hoverEntered.AddListener(OnHoverEnter);
        grab.hoverExited.AddListener(OnHoverExit);

        // Cache renderers and create instanced materials so we don't modify shared assets
        renderers = GetComponentsInChildren<Renderer>();
        originalMaterials = new Material[renderers.Length][];
        instanceMaterials = new Material[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            originalMaterials[i] = renderers[i].sharedMaterials;
            instanceMaterials[i] = renderers[i].materials; // creates instances
            foreach (var mat in instanceMaterials[i])
                mat.EnableKeyword("_EMISSION");
        }
    }

    void OnDestroy()
    {
        if (grab != null)
        {
            grab.hoverEntered.RemoveListener(OnHoverEnter);
            grab.hoverExited.RemoveListener(OnHoverExit);
        }
    }

    void OnHoverEnter(HoverEnterEventArgs args) => isHovered = true;
    void OnHoverExit(HoverExitEventArgs args)
    {
        isHovered = false;
        RestoreOriginalMaterials();
    }

    void Update()
    {
        if (!isHovered) return;

        // Sine pulse: intensity oscillates between 0 and pulseIntensity
        float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
        Color glow = highlightColor * (t * pulseIntensity);
        SetEmission(glow);
    }

    void SetEmission(Color color)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].materials = instanceMaterials[i];
            foreach (var mat in instanceMaterials[i])
            {
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", color);
            }
        }
    }

    void RestoreOriginalMaterials()
    {
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterials = originalMaterials[i];
    }
}

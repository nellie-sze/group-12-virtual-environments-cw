using Unity.XR.CoreUtils;
using UnityEngine;

public class PlayerHeight : MonoBehaviour
{
    public XROrigin xrOrigin;
    public float targetY = 1.0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    Vector3 p = xrOrigin.transform.position;
    p.y = targetY;
    xrOrigin.transform.position = p;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}

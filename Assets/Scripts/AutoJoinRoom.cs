using System;
using Ubiq.Rooms;
using UnityEngine;

/// <summary>
/// Automatically joins a fixed room on Start so that every peer who launches
/// the game lands in the same shared room without any manual input.
///
/// The room GUID is hardcoded — all clients must share the same value.
/// Ubiq creates the room on the server the first time it is requested and
/// all subsequent peers join the existing one.
/// </summary>
public class AutoJoinRoom : MonoBehaviour
{
    [Tooltip("All peers must use the same GUID to end up in the same room.")]
    public string roomGuid = "051ad3d8-a785-4091-8e52-68a965c62afd";

    void Start()
    {
        Debug.Log($"AutoJoinRoom: Start on '{gameObject.name}' with configured roomGuid '{roomGuid}'.");

        RoomClient roomClient = RoomClient.Find(this);

        if (roomClient == null)
        {
            Debug.LogError("AutoJoinRoom: No RoomClient found in scene. Add a Ubiq NetworkScene or RoomClient to the scene.");
            return;
        }

        Debug.Log($"AutoJoinRoom: Found RoomClient on '{roomClient.gameObject.name}'.");

        if (!Guid.TryParse(roomGuid, out Guid guid))
        {
            Debug.LogError($"AutoJoinRoom: '{roomGuid}' is not a valid GUID.");
            return;
        }

        Debug.Log($"AutoJoinRoom: Parsed GUID successfully. Joining room {guid}...");
        roomClient.Join(guid);
        Debug.Log($"AutoJoinRoom: Join call sent for room {guid}.");
    }
}

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
    public string roomGuid = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    void Start()
    {
        RoomClient roomClient = RoomClient.Find(this);

        if (roomClient == null)
        {
            Debug.LogError("AutoJoinRoom: No RoomClient found in scene. Add a Ubiq NetworkScene or RoomClient to the scene.");
            return;
        }

        if (!Guid.TryParse(roomGuid, out Guid guid))
        {
            Debug.LogError($"AutoJoinRoom: '{roomGuid}' is not a valid GUID.");
            return;
        }

        roomClient.Join(guid);
        Debug.Log($"AutoJoinRoom: Joining room {guid}");
    }
}

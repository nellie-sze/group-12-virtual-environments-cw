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
    private const string NextRoomGuidPlayerPrefsKey = "AutoJoinRoom.NextRoomGuid";

    [Tooltip("Default room used on first launch.")]
    public string primaryRoomGuid = "051ad3d8-a785-4091-8e52-68a965c62afd";

    [Tooltip("Alternate room used after a restart. Replace this placeholder with your second real GUID.")]
    public string secondaryRoomGuid = "3de35113-07f7-4391-a213-44c98ccd5571";

    public static void SetNextRoomGuid(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
        {
            PlayerPrefs.DeleteKey(NextRoomGuidPlayerPrefsKey);
        }
        else
        {
            PlayerPrefs.SetString(NextRoomGuidPlayerPrefsKey, guid);
        }

        PlayerPrefs.Save();
    }

    public static string ConsumeNextRoomGuid()
    {
        if (!PlayerPrefs.HasKey(NextRoomGuidPlayerPrefsKey))
        {
            return null;
        }

        string guid = PlayerPrefs.GetString(NextRoomGuidPlayerPrefsKey, string.Empty);
        PlayerPrefs.DeleteKey(NextRoomGuidPlayerPrefsKey);
        PlayerPrefs.Save();
        return string.IsNullOrWhiteSpace(guid) ? null : guid;
    }

    public string GetAlternateRoomGuid(string currentGuid)
    {
        bool primaryValid = Guid.TryParse(primaryRoomGuid, out _);
        bool secondaryValid = Guid.TryParse(secondaryRoomGuid, out _);

        if (!primaryValid)
        {
            Debug.LogError($"AutoJoinRoom: primaryRoomGuid '{primaryRoomGuid}' is not a valid GUID.");
            return currentGuid;
        }

        if (!secondaryValid)
        {
            Debug.LogWarning($"AutoJoinRoom: secondaryRoomGuid '{secondaryRoomGuid}' is not valid. Falling back to primary room.");
            return primaryRoomGuid;
        }

        if (string.Equals(currentGuid, primaryRoomGuid, StringComparison.OrdinalIgnoreCase))
        {
            return secondaryRoomGuid;
        }

        if (string.Equals(currentGuid, secondaryRoomGuid, StringComparison.OrdinalIgnoreCase))
        {
            return primaryRoomGuid;
        }

        return secondaryRoomGuid;
    }

    public string GetCurrentConfiguredRoomGuid()
    {
        string nextOverride = ConsumeNextRoomGuid();
        if (!string.IsNullOrWhiteSpace(nextOverride))
        {
            if (Guid.TryParse(nextOverride, out _))
            {
                return nextOverride;
            }

            Debug.LogWarning($"AutoJoinRoom: Discarding invalid saved next-room GUID '{nextOverride}'. Falling back to configured primary room.");
        }

        return primaryRoomGuid;
    }

    void Start()
    {
        string roomGuid = GetCurrentConfiguredRoomGuid();
        Debug.Log($"AutoJoinRoom: Start on '{gameObject.name}' with selected roomGuid '{roomGuid}'. primary='{primaryRoomGuid}', secondary='{secondaryRoomGuid}'.");

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

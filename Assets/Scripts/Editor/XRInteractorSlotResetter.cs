using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Resets XRUIToolkitHandler's static interactor registration state before
/// each Play Mode enter. Without this, the static slot array persists across
/// sessions and fills up (especially with Multiplayer Play Mode), causing
/// "No available indices for interactor registration. 8/8 slots used" errors.
/// </summary>
[InitializeOnLoad]
static class XRInteractorSlotResetter
{
    static XRInteractorSlotResetter()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
            return;

        try
        {
            var assembly = Assembly.Load("Unity.XR.Interaction.Toolkit");
            var handlerType = assembly?.GetType("UnityEngine.XR.Interaction.Toolkit.UI.XRUIToolkitHandler");
            if (handlerType == null)
                return;

            var flags = BindingFlags.Static | BindingFlags.NonPublic;

            var usedIndices = handlerType.GetField("s_UsedIndices", flags);
            var registered  = handlerType.GetField("s_RegisteredInteractors", flags);

            if (usedIndices?.GetValue(null) is bool[] arr)
                Array.Clear(arr, 0, arr.Length);

            if (registered?.GetValue(null) is System.Collections.IDictionary dict)
                dict.Clear();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[XRInteractorSlotResetter] Could not reset XRI interactor slots: {e.Message}");
        }
    }
}

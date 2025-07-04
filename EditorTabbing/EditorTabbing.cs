﻿using System.Collections.Generic;
using System.Linq;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using ResoniteModLoader.Utility;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Key = FrooxEngine.Key;

namespace EditorTabbing
{
    public class EditorTabbing : ResoniteMod
    {
        public static ModConfiguration? Config;

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> OverlayCompatibilityBackwardsMovement = new ModConfigurationKey<bool>("OverlayCompatibilityBackwardsMovement", "Moves forward with Enter when Steam Overlay could be enabled to not trigger it.", () => true);

        private static bool hasUnconfirmedImeInput = false;
        private static bool launchedInDesktop = false;
        internal const string VERSION_CONSTANT = "3.0.1";
        public override string Author => "Banane9, hantabaru1014";
        public override string Link => "https://github.com/lill-la/ResoniteEditorTabbing";
        public override string Name => "EditorTabbing";
        public override string Version => VERSION_CONSTANT;
        private static bool SteamOverlayPossible => launchedInDesktop;

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony($"{Author}.{Name}");
            Config = GetConfiguration();
            Config?.Save(true);
            harmony.PatchAll();

            var outputDevice = Engine.Current.SystemInfo.HeadDevice;
            launchedInDesktop = outputDevice == HeadOutputDevice.Screen || outputDevice == HeadOutputDevice.Screen360;

            Keyboard.current.onIMECompositionChange += OnIMECompositionChange;
        }

        private void OnIMECompositionChange(IMECompositionString compStr)
        {
            // when you confirm the candidate, an empty string comes in.
            hasUnconfirmedImeInput = compStr.Count != 0;
        }

        [HarmonyPatch(typeof(TextEditor))]
        internal static class TextEditorPatch
        {
            private static void changeFocus(TextEditor current, bool backwards)
            {
                var direction = backwards ? -1 : 1;
                var maxParent = getObjectRoot(current.Slot).Parent;

                var currentParent = current.Slot.Parent;
                var child = current.Slot.ChildIndex;

                while (currentParent != null && currentParent != maxParent)
                {
                    child += direction;

                    if (child < 0 || child >= currentParent.ChildrenCount)
                    {
                        child = currentParent.ChildIndex;
                        currentParent = currentParent.Parent;
                        continue;
                    }

                    var possibleEditors = currentParent[child].GetComponentsInChildren<TextEditor>();
                    var editor = backwards ? possibleEditors.LastOrDefault() : possibleEditors.FirstOrDefault();

                    if (editor != null)
                    {
                        editor.Focus();
                        editor.RunInUpdates(1, editor.SelectAll);

                        return;
                    }
                }
            }

            [HarmonyPostfix]
            [HarmonyPatch("EditCoroutine")]
            private static void EditCoroutinePostfix(TextEditor __instance, ref IEnumerator<Context> __result)
            {
                __result = new EnumerableInjector<Context>(__result)
                {
                    // PostItem is after control has returned to the enumerator again,
                    // i.e. when there is an update - running before EditingRoutine checks it
                    PostItem = (originalItem, transformedItem, returned) =>
                    {
                        if (SteamOverlayPossible && Config!.GetValue(OverlayCompatibilityBackwardsMovement) && !__instance.InputInterface.GetKey(Key.Shift)
                            && (__instance.InputInterface.TypeDelta.Contains('\n') || __instance.InputInterface.TypeDelta.Contains('\r')))
                            __instance.RunInUpdates(1, () => changeFocus(__instance, false));

                        if (!hasUnconfirmedImeInput && __instance.InputInterface.GetKeyDown(Key.Tab))
                        {
                            __instance.Defocus();
                            changeFocus(__instance,
                                __instance.InputInterface.GetKey(Key.Shift) || (SteamOverlayPossible && Config!.GetValue(OverlayCompatibilityBackwardsMovement)));
                        }
                    }
                }.GetEnumerator();
            }

            private static Slot getObjectRoot(Slot slot)
            {
                var iObjRoot = slot.GetComponentInParents<IObjectRoot>(null!, true, false);
                var objectRoot = slot.GetObjectRoot();

                if (iObjRoot == null)
                    return objectRoot;

                if (objectRoot == slot || iObjRoot.Slot.HierachyDepth > objectRoot.HierachyDepth)
                    return iObjRoot.Slot;

                return objectRoot;
            }
        }
    }
}

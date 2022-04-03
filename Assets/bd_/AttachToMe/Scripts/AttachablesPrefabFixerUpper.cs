using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UdonSharp;
#if UNITY_EDITOR
using UnityEditor.Experimental.SceneManagement;
using System;
using UdonSharpEditor;
using UnityEditor;
using VRC.Udon.Editor.ProgramSources;
#endif
using UnityEngine;
using VRC.Udon;

namespace net.fushizen.attachable
{
    public class AttachablesPrefabFixerUpper : MonoBehaviour
    {
#if UNITY_EDITOR
        private bool IsUdonSharp10()
        {
            return typeof(UdonSharpEditor.UdonSharpEditorUtility)
                .GetMethod(nameof(UdonSharpEditor.UdonSharpEditorUtility.ConvertToUdonBehavioursWithUndo))
                ?.GetCustomAttributes(typeof(ObsoleteAttribute), false).Length > 0;
        }
        private void OnValidate()
        {
            if (PrefabUtility.IsPartOfPrefabAsset(this)
                || PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (this == null) return;

                if (IsUdonSharp10())
                {
                    DestroyImmediate(this);
                    return;
                }
                
                // If we're running on pre-1.0 UdonSharp, our prefabs are a bit busted, with proxy behaviours
                // disconnected from their backing udon behaviour. This code deletes the proxy components and prunes
                // duplicate udon behaviours that might have been created.
                foreach (var udonSharpBehaviour in GetComponentsInChildren<UdonSharpBehaviour>(true))
                {
                    DestroyImmediate(udonSharpBehaviour);
                }
            };
        }

#endif
    }
}

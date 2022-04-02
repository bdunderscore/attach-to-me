using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UdonSharp;
using UnityEditor.Experimental.SceneManagement;
#if UNITY_EDITOR
using System;
using UdonSharpEditor;
using UnityEditor;
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
                // disconnected from their backing udon behaviour. This code tries to fix things up a bit.
                var SetBackingUdonBehaviour = typeof(UdonSharpEditorUtility).GetMethod(
                    "SetBackingUdonBehaviour", BindingFlags.Static | BindingFlags.NonPublic);
                foreach (var udonSharpBehaviour in GetComponentsInChildren<UdonSharpBehaviour>(true))
                {
                    if (UdonSharpEditorUtility.GetBackingUdonBehaviour(udonSharpBehaviour) != null) continue;

                    var programAsset = UdonSharpEditorUtility.GetUdonSharpProgramAsset(udonSharpBehaviour);
                    List<UdonBehaviour> unlinkedBehaviours = new List<UdonBehaviour>();
                    foreach (var udonBehaviour in udonSharpBehaviour.GetComponents<UdonBehaviour>())
                    {
                        if (udonBehaviour.programSource == programAsset)
                        {
                            if (UdonSharpEditorUtility.GetBackingUdonBehaviour(udonSharpBehaviour) == null
                                && PrefabUtility.IsPartOfPrefabInstance(udonBehaviour))
                            {
                                SetBackingUdonBehaviour.Invoke(null, new object[] {udonSharpBehaviour, udonBehaviour});
                                udonSharpBehaviour.hideFlags = HideFlags.HideInInspector;
                                udonBehaviour.hideFlags = HideFlags.None;
                                EditorUtility.SetDirty(udonSharpBehaviour);
                                EditorUtility.SetDirty(udonBehaviour);
                            }
                            else
                            {
                                unlinkedBehaviours.Add(udonBehaviour);
                            }
                        }
                    }

                    if (UdonSharpEditorUtility.GetBackingUdonBehaviour(udonSharpBehaviour) != null)
                    {
                        // For some reason U#0.x creates duplicate UdonBehaviours in the prefab instance, so clean them
                        // up now that we've repaired the link to the prefab component.
                        foreach (var unlinkedBehaviour in unlinkedBehaviours)
                        {
                            DestroyImmediate(unlinkedBehaviour);
                        }
                    }
                }
                
                DestroyImmediate(this);
            };
        }

#endif
    }
}
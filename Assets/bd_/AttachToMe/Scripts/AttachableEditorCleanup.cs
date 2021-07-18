
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using System.Collections.Generic;

#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
#endif

namespace net.fushizen.attachable
{
    /// <summary>
    /// Cleans up orphaned Attach-To-Me helper scripts
    /// </summary>
    public class AttachableEditorCleanup : MonoBehaviour
    {
#if UNITY_EDITOR
        private void OnValidate()
        {
            EditorApplication.delayCall += () =>
            {
                if (this != null) Delay1();
                EditorApplication.QueuePlayerLoopUpdate();
            };
        }

        private void Delay1()
        {
            // We need to run after all AttachableConfig deferred updates run, so we defer one extra editor frame.
            EditorApplication.delayCall += () =>
            {
                if (this != null) ReapOrphans();
            };
        }

        private void ReapOrphans()
        {
            if (!gameObject.scene.IsValid()) return;
            if (PrefabUtility.IsPartOfPrefabAsset(gameObject)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            HashSet<AbstractUdonProgramSource> attachableScripts = new HashSet<AbstractUdonProgramSource>();
            attachableScripts.Add(UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(Attachable)));
            attachableScripts.Add(UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(AttachableInternalPostLateUpdate)));
            attachableScripts.Add(UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(AttachableInternalUpdateLoop)));
            attachableScripts.Add(UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(AttachableInternalPickupProxy)));

            List<UdonBehaviour> targets = new List<UdonBehaviour>();
            HashSet<UdonBehaviour> notOrphaned = new HashSet<UdonBehaviour>();

            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                foreach (var udon in root.GetComponentsInChildren<UdonBehaviour>(true))
                {
                    if (udon.programSource != null && attachableScripts.Contains(udon.programSource))
                    {
                        targets.Add(udon);
                    }
                }

                foreach (var config in root.GetComponentsInChildren<AttachableConfig>(true))
                {
                    notOrphaned.Add(config.attachable);
                    notOrphaned.Add(config.updateLoop);
                    notOrphaned.Add(config.postLateUpdateLoop);
                    notOrphaned.Add(config.pickupProxy);
                }
            }

            foreach (var target in targets)
            {
                if (!notOrphaned.Contains(target))
                {
                    DestroyImmediate(target);
                }
            }
        }
#endif
    }
}
/*
 * Copyright (c) 2021 bd_
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the
 * "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish,
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to
 * the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
 * CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
 * SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

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
#if UNITY_EDITOR && !COMPILER_UDONSHARP
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
                    notOrphaned.Add(config.pickupProxy);
                }
                
                foreach (var config in root.GetComponentsInChildren<Attachable>(true))
                {
                    //TODO - update
                }
            }

            foreach (var target in targets)
            {
                if (!notOrphaned.Contains(target))
                {
                    // TODO
                    //DestroyImmediate(target);
                }
            }
        }
#endif
    }
}
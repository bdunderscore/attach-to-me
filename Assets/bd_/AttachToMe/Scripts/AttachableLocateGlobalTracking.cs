﻿/*
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

using System;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    [DefaultExecutionOrder(-200)]
    public class AttachableLocateGlobalTracking : UdonSharpBehaviour
    {
        void Start()
        {
            // Construct and save the path to this object in a player tag
            string path = gameObject.name;

            Transform parent = transform.parent;
            while (parent != null)
            {
                path = $"{parent.gameObject.name}/{path}";
                parent = parent.parent;
            }

            if (Networking.LocalPlayer != null)
            {
                Networking.LocalPlayer.SetPlayerTag("net.fushizen.attachable.GlobalTrackingPath", path);                
            }
        }
        
        #if UNITY_EDITOR && !COMPILER_UDONSHARP

        private void OnValidate()
        {
            EditorApplication.delayCall += () =>
            {
                if (UdonSharpEditorUtility.GetBackingUdonBehaviour(this) != null) return;
                
                // If we're running on pre-1.0 UdonSharp, our prefabs are missing the backing U# behaviours initially.
                // Create them here - but note, because the ConvertToUdonBehaviours method is marked as strict obsolete
                // in 1.0 we need to call this reflectively.

                MethodInfo ConvertToUdonBehaviours = typeof(UdonSharpEditorUtility).GetMethod(
                    nameof(UdonSharpEditorUtility.ConvertToUdonBehaviours),
                    new[] {typeof(UdonSharpBehaviour[]), typeof(bool)}
                );
                if (ConvertToUdonBehaviours.GetCustomAttribute<ObsoleteAttribute>() == null)
                {
                    // Pre-1.0 U#
                    ConvertToUdonBehaviours.Invoke(null,
                        new object[]
                        {
                            gameObject.GetComponentsInChildren<UdonSharpBehaviour>(),
                            true
                        });
                }
            };
        }

#endif
    }
}
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.Components;

#if UNITY_EDITOR
using UdonSharpEditor;
#endif

namespace net.fushizen.attachable
{
#if !COMPILER_UDONSHARP
    /// <summary>
    /// This component acts as the authoritative source for configuration for attachables. By taking this configuration into our hands, we can
    /// support multi-object editing, ensure that prefabs don't break in future upgrades, and fix missing component references.
    /// </summary>
    [ExecuteInEditMode]
    public class AttachableConfig : MonoBehaviour
    {
        [HideInInspector]
        public bool isNewlyCreated = true;

        public Transform t_pickup;
        public Transform t_attachmentDirection;

        public float range = 2;
        [Range(0, 1)]
        public float directionality = 0;
        [FormerlySerializedAs("disableFingerTracking")] public bool disableFingerSelection;

        public bool perm_removeTracee = true;
        public bool perm_removeOwner = true;
        public bool perm_removeOther = true;
        public bool perm_fallback = true;

        public Animator c_animator;
        public string anim_onTrack, anim_onHeld, anim_onTrackLocal, anim_onHeldLocal;

        public float respawnTime;

        // TODO: Move to some common class
        internal static bool debugComponentsVisibleInInspector = false;

        // Bound components
        [SerializeField]
        internal UdonBehaviour attachable, updateLoop, postLateUpdateLoop, pickupProxy;

#if UNITY_EDITOR
        [MenuItem("Window/bd_/Attach-To-Me/Toggle Debug Mode")]
        static void ToggleDebug()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            debugComponentsVisibleInInspector = !debugComponentsVisibleInInspector;

            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var config in root.GetComponentsInChildren<AttachableConfig>(true))
                    {
                        config.OnValidate();
                    }
                    
                    foreach (var config in root.GetComponentsInChildren<Attachable>(true))
                    {
                        config.OnValidate();
                    }
                }
            }
        }

        private void FindReferences()
        {
            CheckReference(transform, ref attachable, typeof(Attachable));
            CheckReference(transform, ref updateLoop, typeof(AttachableInternalUpdateLoop));
            CheckReference(t_pickup, ref pickupProxy, typeof(AttachableInternalPickupProxy));

            attachable.enabled = true;
            updateLoop.enabled = false;
            pickupProxy.enabled = true;

            // Upgrade: Remove references to the postLateUpdateLoop
            if (postLateUpdateLoop != null)
            {
                DestroyImmediate(postLateUpdateLoop, true);
                postLateUpdateLoop = null;
            }
        }

        private void CheckReference(Transform refObject, ref UdonBehaviour udon, Type udonSharpClass)
        {
            var asset = UdonSharpEditorUtility.GetUdonSharpProgramAsset(udonSharpClass);

            if (udon != null && (udon.programSource == null || udon.gameObject != refObject.gameObject))
            {
                // destroy and recreate
                DestroyImmediate(udon);
                udon = null;
            }

            if (refObject == null) return;

            if (udon == null)
            {
                // Try to find a matching UdonBehaviour if the link was broken
                foreach (var candidate in refObject.gameObject.GetComponents<UdonBehaviour>())
                {
                    if (candidate.programSource == asset)
                    {
                        udon = candidate;
                        break;
                    }
                }

                if (udon == null)
                {
                    udon = refObject.gameObject.AddComponent<UdonBehaviour>();
                }
            }
            
            udon.programSource = asset;
            udon.hideFlags = debugComponentsVisibleInInspector ? HideFlags.None : HideFlags.HideInInspector;
        }

        private bool suppressDestroy = false;

        // When the config component is destroyed, destroy the child objects.
        private void OnDestroy()
        {
            TryDestroy(attachable);
            TryDestroy(updateLoop);
            TryDestroy(postLateUpdateLoop);
            TryDestroy(pickupProxy);
        }

        void TryDestroy(UdonBehaviour udon)
        {
            // Don't destroy synchronously as we might be destroying the gameObject; it would break iteration to destroy sibling components.
            EditorApplication.delayCall += () =>
            {
                if (udon != null)
                {
                    if (Application.isPlaying) Destroy(udon);
                    else DestroyImmediate(udon);
                }
            };
        }

        static Mesh directionMesh;

        private void OnDrawGizmos()
        {
            if (t_attachmentDirection == null) return;

            if (!(Selection.gameObjects.Contains(gameObject) || Selection.gameObjects.Contains(t_attachmentDirection.gameObject))) return;

            if (directionMesh == null)
            {
                var path = AssetDatabase.GUIDToAssetPath("eaf389c752150ad41b2be30249ed222f");
                if (path == null) return;

                directionMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (directionMesh == null) return;
            }

            var color = Color.magenta;
            Gizmos.color = color;

            Gizmos.DrawMesh(
                directionMesh,
                t_attachmentDirection.position,
                t_attachmentDirection.rotation,
                Vector3.one * range
            );
            /*
            var src = t_attachmentDirection.position;
            var dst = t_attachmentDirection.position + range * t_attachmentDirection.TransformDirection(Vector3.forward);

            Gizmos.DrawLine(src, dst);

            color.a = 0.5f;
            Gizmos.color = color;
            Gizmos.DrawSphere(dst, range * 0.025f);*/
        }

        private void OnValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (PrefabUtility.IsPartOfPrefabAsset(gameObject)) return;

            EditorApplication.delayCall += () =>
            {
                // Certain operations can't be done from OnValidate, so defer them to the next editor frame.
                if (this != null) this.DeferredValidate();
            };
        }

        void DeferredValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            FindReferences();
            SyncAll();

            if (AttachableVersion.IS_USHARP_10)
            {
                suppressDestroy = true;
                attachable.hideFlags = HideFlags.None;
                EditorUtility.SetDirty(attachable);
                DestroyImmediate(this);
            }
        }

        public void SyncAll() {
            Attachable attachable = gameObject.GetUdonSharpComponent<Attachable>();

            bool anythingChanged = false;

            this.attachable = UdonSharpEditorUtility.GetBackingUdonBehaviour(attachable);

            if (anim_onHeld == null) anim_onHeld = "";
            if (anim_onHeldLocal == null) anim_onHeldLocal = "";
            if (anim_onTrack == null) anim_onTrack = "";
            if (anim_onTrackLocal == null) anim_onTrackLocal = "";

            syncProp(ref anythingChanged, nameof(t_pickup));
            syncProp(ref anythingChanged, nameof(t_attachmentDirection));
            syncProp(ref anythingChanged, nameof(range));
            syncProp(ref anythingChanged, nameof(directionality));
            syncProp(ref anythingChanged, nameof(disableFingerSelection));
            syncProp(ref anythingChanged, nameof(perm_removeOther));
            syncProp(ref anythingChanged, nameof(perm_removeOwner));
            syncProp(ref anythingChanged, nameof(perm_removeTracee));
            syncProp(ref anythingChanged, nameof(perm_fallback));

            syncProp(ref anythingChanged, nameof(c_animator));
            syncProp(ref anythingChanged, nameof(anim_onHeld));
            syncProp(ref anythingChanged, nameof(anim_onHeldLocal));
            syncProp(ref anythingChanged, nameof(anim_onTrack));
            syncProp(ref anythingChanged, nameof(anim_onTrackLocal));

            respawnTime = Mathf.Max(0, Mathf.Min(3600, respawnTime));

            syncProp(ref anythingChanged, nameof(respawnTime));

            if (anythingChanged)
            {
                UdonSharpEditorUtility.CopyUdonToProxy(attachable);
                EditorUtility.SetDirty(attachable);
            }

            if (!PrefabUtility.IsPartOfPrefabInstance(this) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CheckGlobalTrackingReference(attachable);
            }

            isNewlyCreated = false;

            if (t_pickup != null)
            {
                VRCObjectSync curObjectSync;
                
                while (null != (curObjectSync = t_pickup.GetComponent<VRCObjectSync>()))
                {
                    DestroyImmediate(curObjectSync, true);
                }
            }
        }

        readonly static string GLOBAL_TRACKING_PREFAB_GUID = "ad542b70c3bbcb14eaf2cf1120ea0422";
        static GameObject locatedGlobalTrackingPrefab;

        internal static GameObject FindGlobalTrackingObject(Scene scene)
        {
            if (locatedGlobalTrackingPrefab != null && locatedGlobalTrackingPrefab.scene == scene) return locatedGlobalTrackingPrefab;

            var wantedSource = UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(AttachablesGlobalTracking));

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behaviour in root.GetComponentsInChildren<UdonBehaviour>())
                {
                    var source = behaviour.programSource;
                    if (source == wantedSource)
                    {
                        locatedGlobalTrackingPrefab = behaviour.gameObject;
                        return locatedGlobalTrackingPrefab;
                    }
                }
            }

            return null;
        }

        internal static void CheckGlobalTrackingReference(Attachable target)
        {
            if (!target.gameObject.scene.IsValid()) return;

            // Find an existing globaltracking object, if any
            var existingTrackingObject = FindGlobalTrackingObject(target.gameObject.scene);
            if (existingTrackingObject != null) {
                return;
            }

            // Create a global tracking object, since it doesn't exist
            var path = AssetDatabase.GUIDToAssetPath(GLOBAL_TRACKING_PREFAB_GUID);
            if (path == null) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Created global tracking prefab");
        }

        private void syncProp(ref bool anythingChanged, string name)
        {
            var ty = typeof(AttachableConfig);
            var prop = ty.GetField(name);

            var propType = prop.FieldType;

            object udonVal;
            bool found = attachable.publicVariables.TryGetVariableValue(name, out udonVal);

            if (isNewlyCreated)
            {
                if (found)
                {
                    prop.SetValue(this, udonVal);
                }
            } else
            {
                var curVal = prop.GetValue(this);

                if (!found) {
                    // new UdonVariable is generic, so we need to reflect to invoke it
                    var varType = typeof(UdonVariable<>).MakeGenericType(new Type[] { propType });

                    var ok =attachable.publicVariables.TryAddVariable(
                        (IUdonVariable) varType.GetConstructor(new Type[] { typeof(string), propType })
                            .Invoke(new object[] { name, curVal })
                    );

                    if (ok)
                    {
                        anythingChanged = true;
                    } else {
                        Debug.LogWarning("Failed to create variable " + name);
                    }
                } else {
                    if ((udonVal == null) != (curVal == null) || (curVal != null && !curVal.Equals(udonVal))) {
                        if (attachable.publicVariables.TrySetVariableValue(name, curVal))
                        {
                            anythingChanged = true;
                        } else {
                            Debug.LogWarning("Failed to set variable " + name);
                        }
                    }
                }
            }
        }

#endif
    }
#endif // !COMPILER_UDONSHARP
}

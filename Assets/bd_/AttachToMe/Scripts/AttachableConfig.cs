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
using VRC.Udon;

#if UNITY_EDITOR
using UdonSharpEditor;
#endif

namespace net.fushizen.attachable
{
    [RequireComponent(typeof(Attachable))]
    public class AttachableConfig : MonoBehaviour
    {
        [HideInInspector]
        public bool isNewlyCreated = true;

        public Transform t_pickup;
        public Transform t_attachmentDirection;

        public float range = 2;
        [Range(0, 1)]
        public float directionality = 0;
        public bool preferSelf = true;
        public bool trackOnUpdate;
        public bool disableFingerTracking;

        public bool perm_removeTracee = true;
        public bool perm_removeOwner = true;
        public bool perm_removeOther = true;

        public Animator c_animator;
        public string anim_onTrack, anim_onHeld, anim_onTrackLocal, anim_onHeldLocal;

#if UNITY_EDITOR
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
            SyncAll();
        }

        public void SyncAll() {
            Attachable attachable = gameObject.GetUdonSharpComponent<Attachable>();

            bool anythingChanged = false;

            syncProp(ref anythingChanged, ref t_pickup, ref attachable.t_pickup);
            syncProp(ref anythingChanged, ref t_attachmentDirection, ref attachable.t_attachmentDirection);
            syncProp(ref anythingChanged, ref range, ref attachable.range);
            syncProp(ref anythingChanged, ref directionality, ref attachable.directionality);
            syncProp(ref anythingChanged, ref preferSelf, ref attachable.preferSelf);
            syncProp(ref anythingChanged, ref trackOnUpdate, ref attachable.trackOnUpdate);
            syncProp(ref anythingChanged, ref disableFingerTracking, ref attachable.disableFingerSelection);
            syncProp(ref anythingChanged, ref perm_removeTracee, ref attachable.perm_removeTracee);
            syncProp(ref anythingChanged, ref perm_removeOwner, ref attachable.perm_removeOwner);
            syncProp(ref anythingChanged, ref perm_removeOther, ref attachable.perm_removeOther);

            syncProp(ref anythingChanged, ref c_animator, ref attachable.c_animator);
            syncProp(ref anythingChanged, ref anim_onHeld, ref attachable.anim_onHeld);
            syncProp(ref anythingChanged, ref anim_onHeldLocal, ref attachable.anim_onHeldLocal);
            syncProp(ref anythingChanged, ref anim_onTrack, ref attachable.anim_onTrack);
            syncProp(ref anythingChanged, ref anim_onTrackLocal, ref attachable.anim_onTrackLocal);

            if (!PrefabUtility.IsPartOfPrefabInstance(this) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                CheckGlobalTrackingReference(attachable);
            }

            if (anythingChanged)
            {
                Undo.RecordObjects(new UnityEngine.Object[] { attachable, UdonSharpEditorUtility.GetBackingUdonBehaviour(attachable) }, "Sync attachable configuration");
                UdonSharpEditorUtility.CopyProxyToUdon(attachable); 
            }

            isNewlyCreated = false;
        }

        readonly static string GLOBAL_TRACKING_PREFAB_GUID = "ad542b70c3bbcb14eaf2cf1120ea0422";
        readonly static string GLOBAL_TRACKING_COMPONENT_SOURCE_GUID = "58dfc26d6eda8924d8230b16333fa16a";

        internal static AttachablesGlobalTracking FindGlobalTrackingObject(Scene scene)
        {
            var sourcePath = AssetDatabase.GUIDToAssetPath(GLOBAL_TRACKING_COMPONENT_SOURCE_GUID);
            var wantedSource = AssetDatabase.LoadAssetAtPath<AbstractUdonProgramSource>(sourcePath);

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behaviour in root.GetComponentsInChildren<UdonBehaviour>())
                {
                    var source = behaviour.programSource;
                    if (source == wantedSource)
                    {
                        return (AttachablesGlobalTracking)UdonSharpEditorUtility.GetProxyBehaviour(behaviour);
                    }
                }
            }

            return null;
        }

        internal static void CheckGlobalTrackingReference(Attachable target)
        {
            if (target.globalTracking == null)
            {
                // Find an existing globaltracking object, if any
                var existingTrackingObject = FindGlobalTrackingObject(target.gameObject.scene);
                if (existingTrackingObject != null) {
                    target.globalTracking = existingTrackingObject;
                    UdonSharpEditorUtility.CopyProxyToUdon(target);
                    EditorUtility.SetDirty(target);
                    return;
                }

                // Create a global tracking object, since it doesn't exist
                var path = AssetDatabase.GUIDToAssetPath(GLOBAL_TRACKING_PREFAB_GUID);
                if (path == null) return;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) return;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(instance, "Created global tracking prefab");

                target.globalTracking = instance.GetUdonSharpComponent<AttachablesGlobalTracking>();
                UdonSharpEditorUtility.CopyProxyToUdon(target);
                EditorUtility.SetDirty(target);
            }
        }

        private void syncProp(ref bool anythingChanged, ref string newVal, ref string oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            }
            else if ((newVal == null || oldVal == null) || (newVal != null && !newVal.Equals(oldVal)))
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }

        private void syncProp(ref bool anythingChanged, ref bool newVal, ref bool oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            } else if (newVal != oldVal)
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }

        private void syncProp(ref bool anythingChanged, ref float newVal, ref float oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            }
            else if (newVal != oldVal)
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }

        private void syncProp(ref bool anythingChanged, ref Transform newVal, ref Transform oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            }
            else if (newVal != oldVal)
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }


        private void syncProp(ref bool anythingChanged, ref Animator newVal, ref Animator oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            }
            else if (newVal != oldVal)
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }

#endif
    }
}

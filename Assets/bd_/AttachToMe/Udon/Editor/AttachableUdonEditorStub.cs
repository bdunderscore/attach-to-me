using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace net.fushizen.attachable
{
    [CustomEditor(typeof(Attachable))]
    class AttachableUdonEditorStub : Editor
    {
        public override void OnInspectorGUI()
        {
            Attachable target = (Attachable)this.target;

            CheckGlobalTrackingReference();

            if (UdonSharpGUI.DrawConvertToUdonBehaviourButton(target) ||
                UdonSharpGUI.DrawProgramSource(target))
                return;

            // DrawDefaultInspector(); // - for testing
            
            EditorGUILayout.LabelField("Please use the AttachableConfig component to configure this component.");
            if (!target.GetComponent<AttachableConfig>())
            {
                if (GUILayout.Button("Create configuration component"))
                {
                    Undo.AddComponent<AttachableConfig>(target.gameObject);
                }
            }
        }

        readonly string GLOBAL_TRACKING_PREFAB_GUID = "ad542b70c3bbcb14eaf2cf1120ea0422";

        private void CheckGlobalTrackingReference()
        {
            Attachable target = (Attachable)this.target;

            if (target.globalTracking == null)
            {
                // Find an existing globaltracking object, if any
                foreach (var root in target.gameObject.scene.GetRootGameObjects())
                {
                    target.globalTracking = root.GetUdonSharpComponentInChildren<AttachablesGlobalTracking>();
                    if (target.globalTracking != null)
                    {
                        UdonSharpEditorUtility.CopyProxyToUdon(target);
                        EditorUtility.SetDirty(target);
                        return;
                    }
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
    }
}

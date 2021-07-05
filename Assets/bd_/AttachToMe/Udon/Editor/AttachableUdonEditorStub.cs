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
    public class AttachableUdonEditorStub : Editor
    {
        public override void OnInspectorGUI()
        {
            Attachable target = (Attachable)this.target;

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

    }
}

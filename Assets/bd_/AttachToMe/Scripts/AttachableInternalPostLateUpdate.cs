
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace net.fushizen.attachable
{

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [DefaultExecutionOrder(1)]
    public class AttachableInternalPostLateUpdate : UdonSharpBehaviour
    {
        Attachable a;
        void Start()
        {
            a = GetComponent<Attachable>();
        }

        public override void PostLateUpdate()
        {
            a._a_UpdateTracking();
        }
    }


#if UNITY_EDITOR && !COMPILER_UDONSHARP

    [CustomEditor(typeof(AttachableInternalPostLateUpdate))]
    class PostLateUpdateLoopEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Attachables internal component (PostLateUpdate)");
        }
    }

#endif
}
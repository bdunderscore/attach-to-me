
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [DefaultExecutionOrder(1)]
    public class AttachableInternalUpdateLoop : UdonSharpBehaviour
    {
        Attachable a;
        void Start()
        {
            a = GetComponent<Attachable>();
        }

        private void Update()
        {
            a._a_Update();
        }
    }
}
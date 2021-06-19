
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class UpdateLoop : UdonSharpBehaviour
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

        private void OnPreRender()
        {
            a._a_PreRender();
        }
    }
}
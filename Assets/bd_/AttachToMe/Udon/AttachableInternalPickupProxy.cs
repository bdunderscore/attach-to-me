
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    [AddComponentMenu("Attachable Internal/PickupProxy")]
    [DefaultExecutionOrder(1)]
    public class AttachableInternalPickupProxy : UdonSharpBehaviour
    {
        Attachable a;

        void Start()
        {

        }

        public void _a_SetController(Attachable a)
        {
            this.a = a;
        }

        public override void OnDrop()
        {
            a._a_OnDrop();
        }

        public override void OnPickup()
        {
            a._a_OnPickup();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            Networking.SetOwner(player, a.gameObject);
        }
    }
}
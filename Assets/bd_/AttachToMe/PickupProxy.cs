
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    public class PickupProxy : UdonSharpBehaviour
    {
        Attachable a;

        float suppressUseTime;

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
            suppressUseTime = Time.timeSinceLevelLoad;
            a._a_OnPickup();
        }


        public override void OnPickupUseUp()
        {
            Debug.Log("===> Use UP");
            suppressUseTime = -1;
        }

        public override void OnPickupUseDown()
        {
            Debug.Log("===> Use DOWN");
            if (Time.timeSinceLevelLoad < suppressUseTime + .5f)
            {
                suppressUseTime = Time.timeSinceLevelLoad;
                return;
            }

            a._a_Commit();
        }
    }
}
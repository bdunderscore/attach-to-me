﻿
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

        private void OnPreRender()
        {
            a._a_PreRender();
        }
    }
}
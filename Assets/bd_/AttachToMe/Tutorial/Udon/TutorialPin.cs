
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{

    public class TutorialPin : UdonSharpBehaviour
    {
        public Transform trackObject;
        public VRC_Pickup.PickupHand trackingHand;

        public Transform pinRoot;
        public Transform pinStem;
        public Transform canvas;

        public Transform fakeEye;

        public float scale;
        public float offset;

        void Start()
        {

        }

        void Update()
        {
            Vector3 target;

            if (trackingHand == VRC_Pickup.PickupHand.None)
            {
                if (trackObject == null) return;
                target = trackObject.position;
            } else if (trackingHand == VRC_Pickup.PickupHand.Left)
            {
                target = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            } else
            {
                target = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
            }

            Vector3 eyePos;

            if (fakeEye == null)
            {
                var eye = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                eyePos = eye.position;
            } else
            {
                eyePos = fakeEye.position;
            }

            var distance = Vector3.Distance(target, eyePos);

            pinStem.localScale = new Vector3(1, scale, 1);
            pinStem.position = target;

            var uiPos = target + new Vector3(0, scale, 0);

            var fwd = (eyePos - uiPos).normalized;

            uiPos -= - fwd * scale * offset;

            Quaternion revRotation = Quaternion.LookRotation(fwd, Vector3.up);
            pinRoot.position = target;
            canvas.position = uiPos;
            canvas.rotation = revRotation;
        }
    }

}
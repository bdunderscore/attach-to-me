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

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
using UdonSharpEditor;
#endif

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

        public bool shouldDisplay;

        Animator animator;

        void Start()
        {
            animator = GetComponent<Animator>();
            canvas.transform.localScale = Vector3.one * 0.5f;
        }

        public void _a_SetShouldDisplay(bool shouldDisplay)
        {
            Debug.Log($"=== shouldDisplay={shouldDisplay}");
            if (shouldDisplay == this.shouldDisplay) return;

            animator = GetComponent<Animator>();

            this.shouldDisplay = shouldDisplay;

            if (shouldDisplay) this.gameObject.SetActive(true);

            animator.SetBool("Show", shouldDisplay);
        }

        public override void Interact()
        {
            _a_SetShouldDisplay(!shouldDisplay);
        }

        void Update()
        {
            Vector3 target;

            if (!shouldDisplay && animator.GetCurrentAnimatorStateInfo(0).IsTag("done"))
            {
                gameObject.SetActive(false);
                return;
            }

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

            var uiPos = target + transform.TransformVector(new Vector3(0, scale, 0));

            var fwd = (eyePos - uiPos).normalized;

            //float offset = scale * this.offset;
            //offset = Mathf.Min(offset, Vector3.Distance(eyePos, uiPos) / 0.5f);

            //uiPos += transform.TransformVector(fwd * scale * offset);

            Quaternion revRotation = Quaternion.LookRotation(fwd, Vector3.up);
            pinRoot.position = target;
            canvas.position = uiPos;
            canvas.rotation = revRotation;
        }
    }

}
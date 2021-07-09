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
        readonly float F_EPSILON = 0.000001f;

        public Transform trackObject;
        public VRC_Pickup.PickupHand trackingHand;

        public Transform pinRoot;
        public Transform pinStem;
        public Transform canvas;

        public Transform fakeEye;

        public float scale;
        public float offset;

        public float angle_limit = 30f;
        public float min_distance = 0.1f;
        public float eye_scale_factor = 0.5f;
        public float eye_width_factor = 1.0f;

        public bool shouldDisplay;

        Animator animator;

        void Start()
        {
            animator = GetComponent<Animator>();
            animator.SetBool("Show", shouldDisplay);
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
            // The ui panel _and target cursor_ locations are restricted by both angle and distance.
            //
            // When the target is further out (by eye +Z) than the minimum distance, we simply constrain the
            // ui marker to be within the cone formed by the ANGLE_LIMIT around the eye forward vector.
            // The target is allowed to go further out.
            //
            // However, when the target or ui is closer than minimum distance, we project them onto the
            // plane formed at eye Z=MIN_DISTANCE. We then push the target ball outward on this plane by
            // however far behind the plane it was.

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
            Quaternion eyeRot;

            if (fakeEye == null)
            {
                var eye = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                eyePos = eye.position;
                eyeRot = eye.rotation;
            } else
            {
                eyePos = fakeEye.position;
                eyeRot = fakeEye.rotation;
            }

            var distance = Vector3.Distance(target, eyePos);
            var externalScale = transform.lossyScale.y;

            var uiPos = target + Vector3.up * scale * distance * externalScale;

            // Transform everything to eye coordinates
            Matrix4x4 eyeMat = Matrix4x4.TRS(eyePos, eyeRot, Vector3.one);
            Matrix4x4 eyeInv = Matrix4x4.Inverse(eyeMat);

            uiPos = eyeInv.MultiplyPoint3x4(uiPos);
            target = eyeInv.MultiplyPoint3x4(target);

            // Splat everything onto the min-distance plane
            float targetBehindDistance = Mathf.Max(0, min_distance - target.z);
            target.z = Mathf.Max(min_distance, target.z);
            uiPos.z = Mathf.Max(min_distance, target.z);

            var eyeScale = Mathf.Min(1, uiPos.z * eye_scale_factor);
            canvas.localScale = Vector3.one * eyeScale;

            // Limit the angle of the ui target, without changing its distance
            // To do this, we observe that we have a right triangle, with the
            // angle at the eye, adjacent being z, opposite being the magnitude of
            // the ui xy vector. We need just adjust the adjacent side so that we
            // get the right tangent: tan(|xy|/z)=angle
            var uiPlane = new Vector2(uiPos.x, uiPos.y);
            var xyMag = uiPlane.magnitude;
            var maxMag = Mathf.Tan(angle_limit * Mathf.PI / 180) * uiPos.z;
            if (xyMag > maxMag)
            {
                uiPlane = uiPlane.normalized * maxMag;
                uiPos.x = uiPlane.x;
                uiPos.y = uiPlane.y;
            }

            // Push the target outward by however far it was behind the target. This is a bit of a complex operation;
            // the target may be below the viewpoint with the UI panel above, for example, but we don't want to terminate
            // onscreen in a misleading position, or cross the center of the screen.
            //
            // We therefore handle this in two steps: First, find the line crossing the center and the (projected to z-plane)
            // target; then, find the side of that line the UI is on. Then, extend that line by however far behind the viewpoint
            // the target is (note that this assumes an unscaled eyeMat coordinate space).
            if (targetBehindDistance > F_EPSILON)
            {
                var uiFlat = uiPos;
                uiFlat.z = 0;

                var targetAdjustment = target;
                targetAdjustment.z = 0;
                //Debug.Log($"tA: {targetAdjustment * 100}/100, uiFlat: {uiFlat * 100}/100, dot: {Vector3.Dot(targetAdjustment, uiFlat)}");
                if (targetAdjustment.sqrMagnitude < F_EPSILON)
                {
                    targetAdjustment = uiFlat.normalized;
                }

                targetAdjustment *= targetBehindDistance * 20;
                target += targetAdjustment;
                uiPos += targetAdjustment;

                // Re-limit the UI position after this adjustment.
                // We did the clamp before to ensure that the target can drag the ui position, even if it started waaay offscreen.
                uiPlane = new Vector2(uiPos.x, uiPos.y);
                xyMag = uiPlane.magnitude;
                if (xyMag > maxMag)
                {
                    uiPlane = uiPlane.normalized * maxMag;
                    uiPos.x = uiPlane.x;
                    uiPos.y = uiPlane.y;
                }
            }




            // Transform back to world coordinates
            target = eyeMat.MultiplyPoint3x4(target);
            uiPos = eyeMat.MultiplyPoint3x4(uiPos);

            pinStem.position = target;
            pinStem.rotation = Quaternion.LookRotation(uiPos - target) * Quaternion.AngleAxis(90, Vector3.right);
            pinStem.localScale = new Vector3(eyeScale, Vector3.Distance(uiPos, target) / externalScale, eyeScale);

            var fwd = (eyePos - uiPos).normalized;

            Quaternion revRotation = Quaternion.LookRotation(fwd, Vector3.up);
            pinRoot.position = pinStem.position;
            pinRoot.localScale = Vector3.one * eyeScale;
            //Debug.Log($"pinRoot localScale {pinRoot.localScale} {pinRoot.gameObject.name}");
            canvas.position = uiPos;
            canvas.rotation = revRotation;
        }
    }

}
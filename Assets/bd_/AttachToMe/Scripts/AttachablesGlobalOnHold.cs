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
using VRC.Udon.Common;

namespace net.fushizen.attachable
{
    /// <summary>
    /// Controls tutorial activation.
    /// 
    /// I should probably rename this sometime (need to figure out how to make that upgrade path work...)
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class AttachablesGlobalOnHold : UdonSharpBehaviour
    {
        public Animator desktopTutorial;
        public GameObject vrTutorial;

        public TutorialPin pin_selectBone;
        public TutorialPin pin_selectPlayer;
        public TutorialPin pin_pickup;

        public TutorialScaler scaler;

        public AttachablesGlobalTracking globalTracking;

        Attachable activeHeld;
        AttachableBoneData boneData;

        bool tut_boneSelect, tut_playerSelect, tut_pickup;

        public float tutorialPinScale = 3;
        public float tutorialPinOffset = 0.2f;

        void Start()
        {
            tut_boneSelect = tut_playerSelect = tut_pickup = false;

            boneData = GetComponent<AttachableBoneData>();

            ConfigurePin(pin_selectBone);
            ConfigurePin(pin_selectPlayer);
            ConfigurePin(pin_pickup);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (player.isLocal)
            {
                // Wait a bit before committing to VR or non-VR, as IsUserInVR is not immediately correct.
                SendCustomEventDelayedSeconds(nameof(_a_SelectTutorial), 2.0f);
            }
        }

        void ConfigurePin(TutorialPin pin)
        {
            pin.offset = tutorialPinOffset;
            pin.scale = tutorialPinScale;
        }

        public void _a_SelectTutorial()
        {
            if (!vrTutorial.transform.parent.gameObject.activeInHierarchy)
            {
                // Tutorial is disabled, kill the attempted pickup scan
                tut_boneSelect = true;
                tut_playerSelect = true;
                tut_pickup = true;
                return;
            }

            if (Networking.LocalPlayer.IsUserInVR())
            {
                vrTutorial.gameObject.SetActive(true);
                SendCustomEventDelayedSeconds(nameof(_ScanForAttemptedPickup), 1.0f);
            } else
            {
                desktopTutorial.gameObject.SetActive(true);
            }
        }

        int scanIndex = 0;
        public void _ScanForAttemptedPickup()
        {
            if (tut_pickup)
            {
                pin_pickup._a_SetShouldDisplay(false);
                return;
            }

            if (ScanForAttemptedPickupInternal())
            {
                SendCustomEventDelayedFrames(nameof(_ScanForAttemptedPickup), 1);
            } else
            {
                SendCustomEventDelayedSeconds(nameof(_ScanForAttemptedPickup), 1.0f);
            }
        }

        bool ScanForAttemptedPickupInternal() {
            int limit = scanIndex + 8;
            float distLimit = scaler.handScale * 2.0f;

            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player))
            {
                return false;
            }

            var lhPos = player.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            var rhPos = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;

            if (pin_pickup.gameObject.activeSelf)
            {
                // Check if the current attachement is still valid
                if (!pin_pickup.shouldDisplay) return false;
                if (pin_pickup.trackObject == null) return false;
                var target = pin_pickup.trackObject;

                if (target == null) return false;

                float dist = Vector3.Distance(target.position, lhPos);
                dist = Mathf.Min(dist, Vector3.Distance(target.position, rhPos));

                if (dist > distLimit)
                {
                    // Disappear, then maybe reappear somewhere else
                    pin_pickup._a_SetShouldDisplay(false);
                }

                return false;
            }

            var allAttachables = globalTracking._a_GetAllRegistered();
            if (allAttachables == null) return false; // Not yet initialized
            if (limit > allAttachables.Length) limit = allAttachables.Length;

            int myPlayerId = player.playerId;

            for (; scanIndex < limit; scanIndex++)
            {
                var obj = allAttachables[scanIndex];
                if (obj == null)
                {
                    scanIndex = limit;
                    break;
                }

                if (obj.pickup == null || obj.pickup.pickupable || obj.t_attachmentDirection == null) continue;

                int trackingBone = obj._a_GetTrackingBone();

                if (trackingBone < 0)
                {
                    continue;
                }

                float lhDist = Vector3.Distance(obj.t_attachmentDirection.position, lhPos);
                float rhDist = Vector3.Distance(obj.t_attachmentDirection.position, rhPos);
                float dist = Mathf.Min(lhDist, rhDist);

                if (dist < distLimit)
                {
                    if (!obj._a_HasPickupPermissions()) continue;

                    if (obj._a_GetTrackingPlayer() == myPlayerId)
                    {
                        var obj_curHand = boneData._a_GetTrackingHand(trackingBone);
                        if (VRC_Pickup.PickupHand.Left.Equals(obj_curHand) && rhDist >= distLimit) continue;
                        if (VRC_Pickup.PickupHand.Right.Equals(obj_curHand) && lhDist >= distLimit) continue;
                    }

                    pin_pickup.trackObject = obj.t_attachmentDirection;
                    pin_pickup._a_SetShouldDisplay(true);
                    scanIndex = 0;
                    return false;
                }
            }

            if (scanIndex == limit)
            {
                scanIndex = 0;
                // Wait a bit before the next scan
                return false;
            } else
            {
                // Continue the scan on the next frame
                return true;
            }
        }

        public void _a_OnBoneSelect()
        {
            _a_SelectTutorial();
            desktopTutorial.SetBool("SelectBoneDone", true);
            tut_boneSelect = true;
            pin_selectBone._a_SetShouldDisplay(false);
        }

        public void _a_OnPlayerSelect()
        {
            _a_SelectTutorial();
            desktopTutorial.SetBool("SelectPlayerDone", true);
            tut_playerSelect = true;
            pin_selectPlayer._a_SetShouldDisplay(false);
        }

        public void _a_OnAttachedPickup()
        {
            _a_SelectTutorial();
            desktopTutorial.SetBool("AttachedPickupDone", true);
            tut_pickup = true;
        }

        float lastPickup;

        public void _a_OnPickup(Attachable a, VRC_Pickup.PickupHand hand)
        {
            if (activeHeld != null && activeHeld != a) return;

            activeHeld = a;

            _a_SelectTutorial();
            desktopTutorial.SetBool("InHand", true);

            pin_selectBone._a_SetShouldDisplay(!tut_boneSelect);
            pin_selectPlayer._a_SetShouldDisplay(!tut_playerSelect);

            pin_selectBone.trackingHand = hand;
            if (hand == VRC_Pickup.PickupHand.Left)
            {
                pin_selectPlayer.trackingHand = VRC_Pickup.PickupHand.Right;
            } else
            {
                pin_selectPlayer.trackingHand = VRC_Pickup.PickupHand.Left;
            }

            lastPickup = Time.timeSinceLevelLoad;
        }

        public void _a_OnDrop(Attachable a)
        {
            if (activeHeld == a)
            {
                activeHeld = null;

                _a_SelectTutorial();
                desktopTutorial.SetBool("InHand", false);
                pin_selectBone._a_SetShouldDisplay(false);
                pin_selectPlayer._a_SetShouldDisplay(false);
            }
        }
    }
}

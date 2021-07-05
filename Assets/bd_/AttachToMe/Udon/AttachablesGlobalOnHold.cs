
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    /// <summary>
    /// Controls tutorial activation and prevention of multiple pickup activation.
    /// </summary>
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

        bool tut_boneSelect, tut_playerSelect, tut_pickup;

        public float tutorialPinScale = 3;
        public float tutorialPinOffset = 0.2f;

        void Start()
        {
            tut_boneSelect = tut_playerSelect = tut_pickup = false;

            ConfigurePin(pin_selectBone);
            ConfigurePin(pin_selectPlayer);
            ConfigurePin(pin_pickup);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (player.isLocal) SelectTutorial();
        }

        void ConfigurePin(TutorialPin pin)
        {
            pin.offset = tutorialPinOffset;
            pin.scale = tutorialPinScale;
        }

        void SelectTutorial()
        {
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

                if (obj.pickup.pickupable) continue;

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
                    if (obj._a_GetTrackingPlayer() == myPlayerId)
                    {
                        if (obj._a_HeldInLeftHand(trackingBone) && rhDist >= distLimit) continue;
                        if (obj._a_HeldInRightHand(trackingBone) && lhDist >= distLimit) continue;
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
            SelectTutorial();
            desktopTutorial.SetBool("SelectBoneDone", true);
            tut_boneSelect = true;
            pin_selectBone._a_SetShouldDisplay(false);
        }

        public void _a_OnPlayerSelect()
        {
            SelectTutorial();
            desktopTutorial.SetBool("SelectPlayerDone", true);
            tut_playerSelect = true;
            pin_selectPlayer._a_SetShouldDisplay(false);
        }

        public void _a_OnAttachedPickup()
        {
            SelectTutorial();
            desktopTutorial.SetBool("AttachedPickupDone", true);
            tut_pickup = true;
        }

        public bool _a_OnPickup(Attachable a, VRC_Pickup.PickupHand hand)
        {
            if (activeHeld != null && activeHeld != a) return false;

            activeHeld = a;

            SelectTutorial();
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

            return true;
        }

        public void _a_OnDrop(Attachable a)
        {
            if (activeHeld == a)
            {
                activeHeld = null;

                SelectTutorial();
                desktopTutorial.SetBool("InHand", false);
                pin_selectBone._a_SetShouldDisplay(false);
                pin_selectPlayer._a_SetShouldDisplay(false);
            }
        }
    }
}

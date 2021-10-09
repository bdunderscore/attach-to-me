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
    [DefaultExecutionOrder(-10)]
    public class AttachablesGlobalTracking : UdonSharpBehaviour
    {
        readonly float PICKUP_TIMEOUT = 4.0f;
        readonly float REMOVE_ALL_TIMEOUT = 1.0f;
        readonly int REMOVE_ALL_COUNT = 3;

        Attachable[] attachables;
        Attachable[] trackingAttachables;

        bool cur_enabled;

        int nextFreeSlot = 0;
        float pickupsEnabledUntil;

        int nextRegistrationSlot;
        int nextTrackingSlot;

        bool altWasHeld;
        float lastAltPress;
        int altCounter;

        [HideInInspector]
        public AttachableBonePositionReader bonePosReader;

        public GameObject bonePosReaderPrefab;

        private void Start()
        {
            CheckInit(); // Run before Update
        }

        private void CheckInit()
        {
            if (attachables != null) return;

            attachables = new Attachable[16];
            trackingAttachables = new Attachable[16];
        }

        #region Registration logic

        Attachable[] ResizeArray(Attachable[] oldArray, int newSize)
        {
            var newArray = new Attachable[newSize];
            System.Array.Copy(oldArray, newArray, oldArray.Length);
            return newArray;
        }

        public Attachable[] _a_GetAllRegistered()
        {
            return attachables;
        }

        public void _a_Register(Attachable a)
        {
            if (attachables == null || nextFreeSlot >= attachables.Length)
            {
                CheckInit();

                attachables = ResizeArray(attachables, (int)(attachables.Length * 1.5));
                trackingAttachables = ResizeArray(trackingAttachables, attachables.Length);
            }

            attachables[nextFreeSlot++] = a;
        }

        public void _a_Deregister(Attachable a)
        {
            _a_DisableTracking(a);
            int idx = System.Array.IndexOf(attachables, a);

            if (idx >= 0)
            {
                System.Array.Copy(attachables, idx + 1, attachables, idx, nextFreeSlot - idx - 1);
                nextFreeSlot--;
            }
        }

        public void _a_EnableTracking(Attachable a)
        {
            if (System.Array.IndexOf(trackingAttachables, a) >= 0) return;

            // Restore the invariant that the array is ordered from child to parent.
            // We do this by walking up the heirarchy to find any tracking parent, and then place this object
            // immediately before it. Any sub-children must have already been before the parent, so they will
            // remain before this intermediate object as well.

            var parent = a.t_pickup.parent;

            int insertionIndex = nextTrackingSlot;
            while (parent != null)
            {
                // initial filtering step to avoid calling an expensive U#-emulated GetComponent on things that
                // aren't even pickups
                if (parent.GetComponent(typeof(VRC_Pickup)) == null)
                {
                    parent = parent.parent;
                    continue;
                }

                var proxy = parent.GetComponent<AttachableInternalPickupProxy>();
                if (proxy != null)
                {
                    var controller = proxy._attachable;

                    if (controller != null)
                    {
                        var idx = System.Array.IndexOf(trackingAttachables, controller);
                        if (idx >= 0)
                        {
                            insertionIndex = idx;
                            break;
                        }
                    }
                }

                parent = parent.parent;
            }

            System.Array.Copy(trackingAttachables, insertionIndex, trackingAttachables, insertionIndex + 1, nextTrackingSlot - insertionIndex);
            trackingAttachables[insertionIndex] = a;
            nextTrackingSlot++;
        }

        public void _a_DisableTracking(Attachable a)
        {
            int idx = System.Array.IndexOf(trackingAttachables, a);
            if (idx < 0) return;

            System.Array.Copy(trackingAttachables, idx + 1, trackingAttachables, idx, nextTrackingSlot - (idx + 1));

            nextTrackingSlot--;
            trackingAttachables[nextTrackingSlot] = null;
        }

        #endregion

        #region Heap maintenance

        void HeapSwap(int a, int b)
        {
            var n_b = trackingAttachables[a];
            var n_a = trackingAttachables[b];

            trackingAttachables[a] = n_a;
            trackingAttachables[b] = n_b;

            n_a._tracking_index = a;
            n_b._tracking_index = b;
        }

        #endregion

        #region Render trigger

        public override void PostLateUpdate()
        {
            //Debug.Log("=== PostLateUpdate()");
            for (int i = nextTrackingSlot - 1; i >= 0; i--)
            {
                var node = trackingAttachables[i];
                if (node != null)
                {
                    ///Debug.Log($"=== Updating {i}: {node.gameObject.name} ({node._depth})");
                    node._a_UpdateTracking();
                }
            }
            //Debug.Log("=== End PostLateUpdate()");
        }

        #endregion

        #region Bone reader respawn logic

        public void _a_RespawnBoneReader()
        {
            if (bonePosReader == null || bonePosReader._a_Watchdog(Time.frameCount) != Time.frameCount)
            {
                int lastPlayerId = -1;
                if (bonePosReader != null)
                {
                    lastPlayerId = bonePosReader.lastPlayerId;
                    Object.Destroy(bonePosReader.gameObject);
                }

                var gameObject = VRCInstantiate(bonePosReaderPrefab);
                bonePosReader = gameObject.GetComponent<AttachableBonePositionReader>();
                bonePosReader.suppressPlayerId = lastPlayerId;

                SendCustomEventDelayedSeconds(nameof(_a_ClearSuppressedPlayer), 1.0f);
            }
        }

        public void _a_ClearSuppressedPlayer()
        {
            if (bonePosReader != null)
            {
                bonePosReader.suppressPlayerId = -1;
            }
        }

        #endregion

        #region Input handling

        public override void InputLookVertical(float value, UdonInputEventArgs args)
        {
            if (args.handType != HandType.RIGHT || value > -0.7f)
            {
                return;
            }
            if (!Networking.LocalPlayer.IsUserInVR()) return;

            pickupsEnabledUntil = Time.timeSinceLevelLoad + PICKUP_TIMEOUT;
            if (!cur_enabled) nextRegistrationSlot = 0;

            cur_enabled = true;
        }

        private void Update()
        {
            var localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return; // avoid making a lot of noise in editor

            _a_RespawnBoneReader();

            float pos = Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryThumbstickVertical");

            bool alt = false;
            if (!localPlayer.IsUserInVR())
            {
                alt = Input.GetKey(KeyCode.LeftAlt);

                if (alt && !altWasHeld)
                {
                    if (lastAltPress + REMOVE_ALL_TIMEOUT < Time.timeSinceLevelLoad)
                    {
                        lastAltPress = Time.timeSinceLevelLoad;
                        altCounter = 0;
                    }

                    altCounter++;

                    if (altCounter == REMOVE_ALL_COUNT)
                    {
                        for (int i = 0; i < nextFreeSlot; i++) {
                            attachables[i]._a_TryRemoveFromSelf();
                        }
                    }
                }
                altWasHeld = alt;
            }

            bool enablePickup = pos < -0.7f || alt;

            if (enablePickup)
            {
                pickupsEnabledUntil = Time.timeSinceLevelLoad + PICKUP_TIMEOUT;
                if (!cur_enabled) nextRegistrationSlot = 0;

                cur_enabled = true;
            }

            if (cur_enabled && pickupsEnabledUntil < Time.timeSinceLevelLoad)
            {
                nextRegistrationSlot = 0;
                cur_enabled = false;
            }

            if (nextRegistrationSlot < nextFreeSlot)
            {
                int limit = nextRegistrationSlot + 16;
                if (limit > nextFreeSlot) limit = nextFreeSlot;

                for (; nextRegistrationSlot < limit; nextRegistrationSlot++)
                {
                    attachables[nextRegistrationSlot]._a_SetPickupEnabled(cur_enabled);
                }
            }
        }

        #endregion
    }
}
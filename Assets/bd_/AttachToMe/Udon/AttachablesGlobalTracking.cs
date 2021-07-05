﻿/*
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

namespace net.fushizen.attachable
{
    [DefaultExecutionOrder(-1)]
    public class AttachablesGlobalTracking : UdonSharpBehaviour
    {
        readonly float PICKUP_TIMEOUT = 4.0f;
        readonly float REMOVE_ALL_TIMEOUT = 1.0f;
        readonly int REMOVE_ALL_COUNT = 3;

        Attachable[] attachables;
        Attachable[] trackingAttachables;
        Attachable[] updateTrackingAttachables;

        bool cur_enabled;

        int nextFreeSlot = 0;
        float pickupsEnabledUntil;

        int nextEvalSlot;

        int nextTrackingSlot, nextUpdateTrackingSlot;

        int nextTrackingFrame;

        bool altWasHeld;
        float lastAltPress;
        int altCounter;

        [HideInInspector]
        public AttachableBonePositionReader bonePosReader;

        public GameObject bonePosReaderPrefab;

        void Start()
        {
            attachables = new Attachable[16];
            trackingAttachables = new Attachable[16];
            updateTrackingAttachables = new Attachable[16];
        }

        Attachable[] ResizeArray(Attachable[] oldArray, int newSize)
        {
            var newArray = new Attachable[newSize];
            System.Array.Copy(attachables, newArray, oldArray.Length);
            return newArray;
        }

        public Attachable[] _a_GetAllRegistered()
        {
            return attachables;
        }

        public void _a_Register(Attachable a)
        {
            if (nextFreeSlot >= attachables.Length)
            {
                attachables = ResizeArray(attachables, (int)(attachables.Length * 1.5));
                trackingAttachables = ResizeArray(trackingAttachables, attachables.Length);
                updateTrackingAttachables = ResizeArray(updateTrackingAttachables, attachables.Length);
            }

            attachables[nextFreeSlot++] = a;
        }

        public void _a_EnableTracking(Attachable a)
        {
            if (a._tracking_index >= 0) return;

            if (a.trackOnUpdate)
            {
                a._tracking_index = nextUpdateTrackingSlot;
                updateTrackingAttachables[nextUpdateTrackingSlot++] = a;
            } else
            {
                a._tracking_index = nextTrackingSlot;
                trackingAttachables[nextTrackingSlot++] = a;
            }
            
        }

        public void _a_DisableTracking(Attachable a)
        {
            int idx = a._tracking_index;
            if (idx < 0) return;
            a._tracking_index = -1;

            if (a.trackOnUpdate)
            {
                nextUpdateTrackingSlot--;
                if (idx != nextUpdateTrackingSlot)
                {
                    var other = updateTrackingAttachables[nextUpdateTrackingSlot];
                    updateTrackingAttachables[idx] = other;
                    other._tracking_index = idx;
                }
                updateTrackingAttachables[nextUpdateTrackingSlot] = null;
            }
            else
            {
                nextTrackingSlot--;
                if (idx != nextTrackingSlot)
                {
                    var other = trackingAttachables[nextTrackingSlot];
                    trackingAttachables[idx] = other;
                    other._tracking_index = idx;
                }
                trackingAttachables[nextTrackingSlot] = null;
            }
        }

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

        private void Update()
        {
            _a_RespawnBoneReader();

            var localPlayer = Networking.LocalPlayer;
            transform.position = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            for (int i = 0; i < nextUpdateTrackingSlot; i++)
            {
                updateTrackingAttachables[i]._a_UpdateTracking();
            }

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
                if (!cur_enabled) nextEvalSlot = 0;

                cur_enabled = true;
            } else if (cur_enabled && pickupsEnabledUntil < Time.timeSinceLevelLoad)
            {
                nextEvalSlot = 0;
                cur_enabled = false;
            }

            if (nextEvalSlot < nextFreeSlot)
            {
                int limit = nextEvalSlot + 16;
                if (limit > nextFreeSlot) limit = nextFreeSlot;

                for (; nextEvalSlot < limit; nextEvalSlot++)
                {
                    attachables[nextEvalSlot]._a_SetPickupEnabled(cur_enabled);
                }
            }
        }

        private void LateUpdate()
        {
            nextTrackingFrame = Time.frameCount;
        }

        private void OnWillRenderObject()
        {
            if (Time.frameCount != nextTrackingFrame) return;
            nextTrackingFrame = 0;

            for (int i = 0; i < nextTrackingSlot; i++)
            {
                trackingAttachables[i]._a_UpdateTracking();
            }
        }
    }
}
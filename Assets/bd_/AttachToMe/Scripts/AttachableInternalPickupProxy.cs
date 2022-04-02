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

namespace net.fushizen.attachable
{
    [AddComponentMenu("Attachable Internal/PickupProxy")]
    [DefaultExecutionOrder(1)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class AttachableInternalPickupProxy : UdonSharpBehaviour
    {
        [HideInInspector]
        public Attachable _attachable;

        void Start()
        {

        }

        public void _a_SetController(Attachable a)
        {
            this._attachable = a;
        }

        public override void OnDrop()
        {
            if (_attachable != null) _attachable._a_OnDrop();
        }

        public override void OnPickup()
        {
            if (_attachable != null) _attachable._a_OnPickup();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            Networking.SetOwner(player, _attachable.gameObject);
        }
    }
}
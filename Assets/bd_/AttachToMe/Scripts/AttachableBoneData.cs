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
    /// <summary>
    /// Holds static tables of bone relationship data.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AttachableBoneData : UdonSharpBehaviour
    {
        bool initDone = false;

        void Start()
        {
            _a_CheckInit();
            enabled = false;
        }

        public void _a_CheckInit()
        {
            if (!initDone)
            {
                initDone = true;
                InitBoneData();
            }
        }

        [HideInInspector]
[VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */ 
        public object[] bone_targets; // Actually HumanBodyBone, but Udon doesn't support arrays of those
        [HideInInspector]
        public int[] bone_child; // child to use for bone linearization, or -1 for sphere, or -2 for do not target
        [HideInInspector]
        public int[] bone_parent; // parent of bone to use for bone size computation, or -1 for hips

        object[] bone_hand; // associated PickupHand, or null

        public readonly int BONE_LEFT_UPPER_LEG = 1;
        public readonly int BONE_RIGHT_UPPER_LEG = 2;
        public readonly int BONE_SPINE = 7;
        public readonly int BONE_NECK = 9;
        public readonly int BONE_HEAD = 10;
        public readonly int BONE_LEFT_HAND = 17;
        public readonly int BONE_RIGHT_HAND = 18;

        public object _a_GetTrackingHand(int trackingBone)
        {
            if (trackingBone < 0 || trackingBone > bone_hand.Length) return null;

            return bone_hand[trackingBone];
        }

        public void InitBoneData()
        {
            int last = -1;
            bone_targets = new object[52];
            bone_child = new int[bone_targets.Length];
            bone_parent = new int[bone_targets.Length];
            bone_hand = new object[bone_targets.Length];

            for (int i = 0; i < 50; i++)
            {
                bone_child[i] = i + 1;
                bone_parent[i] = i - 1;
            }

            bone_targets[last = 0] = HumanBodyBones.Hips; bone_child[last] = -1; bone_parent[last] = -1;
            bone_targets[last = 1] = HumanBodyBones.LeftUpperLeg; bone_child[last] = last + 2; bone_parent[last] = 0;
            bone_targets[last = 2] = HumanBodyBones.RightUpperLeg; bone_child[last] = last + 2; bone_parent[last] = 0;
            bone_targets[last = 3] = HumanBodyBones.LeftLowerLeg; bone_child[last] = last + 2; bone_parent[last] = last - 2;
            bone_targets[last = 4] = HumanBodyBones.RightLowerLeg; bone_child[last] = last + 2; bone_parent[last] = last - 2;
            bone_targets[last = 5] = HumanBodyBones.LeftFoot; bone_child[last] = 50; bone_parent[last] = last - 2;
            bone_targets[last = 6] = HumanBodyBones.RightFoot; bone_child[last] = 51; bone_parent[last] = last - 2;
            bone_targets[last = 7] = HumanBodyBones.Spine; bone_parent[last] = 0;
            bone_targets[last = 8] = HumanBodyBones.Chest; bone_child[last] = 49;
            bone_targets[last = 9] = HumanBodyBones.Neck;
            bone_targets[last = 10] = HumanBodyBones.Head; bone_child[last] = -1;

            bone_targets[last = 11] = HumanBodyBones.LeftShoulder; bone_parent[last] = 49; bone_child[last] = last + 2;
            bone_hand[last] = VRC_Pickup.PickupHand.Left;
            bone_targets[last = 12] = HumanBodyBones.RightShoulder; bone_parent[last] = 49; bone_child[last] = last + 2;
            bone_hand[last] = VRC_Pickup.PickupHand.Right;

            bone_targets[last = 13] = HumanBodyBones.LeftUpperArm; bone_parent[last] = last - 2; bone_child[last] = last + 2;
            bone_hand[last] = VRC_Pickup.PickupHand.Left;
            bone_targets[last = 14] = HumanBodyBones.RightUpperArm; bone_parent[last] = last - 2; bone_child[last] = last + 2;
            bone_hand[last] = VRC_Pickup.PickupHand.Right;

            bone_targets[last = 15] = HumanBodyBones.LeftLowerArm; bone_parent[last] = last - 2; bone_child[last] = last + 2;
            bone_hand[last] = VRC_Pickup.PickupHand.Left;
            bone_targets[last = 16] = HumanBodyBones.RightLowerArm; bone_parent[last] = last - 2; bone_child[last] = last + 2;
            bone_hand[last] = VRC_Pickup.PickupHand.Right;

            bone_targets[last = 17] = HumanBodyBones.LeftHand; bone_parent[last] = last - 2; bone_child[last] = 25; // middle finger
            bone_hand[last] = VRC_Pickup.PickupHand.Left;
            bone_targets[last = 18] = HumanBodyBones.RightHand; bone_parent[last] = last - 2; bone_child[last] = 40;
            bone_hand[last] = VRC_Pickup.PickupHand.Right;

            bone_targets[last = 19] = HumanBodyBones.LeftThumbProximal; bone_parent[last] = 17;
            bone_targets[last = 20] = HumanBodyBones.LeftThumbIntermediate;
            bone_targets[last = 21] = HumanBodyBones.LeftThumbDistal; bone_child[last] = -1;
            bone_targets[last = 22] = HumanBodyBones.LeftIndexProximal; bone_parent[last] = 17;
            bone_targets[last = 23] = HumanBodyBones.LeftIndexIntermediate;
            bone_targets[last = 24] = HumanBodyBones.LeftIndexDistal; bone_child[last] = -1;
            bone_targets[last = 25] = HumanBodyBones.LeftMiddleProximal; bone_parent[last] = 17;
            bone_targets[last = 26] = HumanBodyBones.LeftMiddleIntermediate;
            bone_targets[last = 27] = HumanBodyBones.LeftMiddleDistal; bone_child[last] = -1;
            bone_targets[last = 28] = HumanBodyBones.LeftRingProximal; bone_parent[last] = 17;
            bone_targets[last = 29] = HumanBodyBones.LeftRingIntermediate;
            bone_targets[last = 30] = HumanBodyBones.LeftRingDistal; bone_child[last] = -1;
            bone_targets[last = 31] = HumanBodyBones.LeftLittleProximal; bone_parent[last] = 17;
            bone_targets[last = 32] = HumanBodyBones.LeftLittleIntermediate;
            bone_targets[last = 33] = HumanBodyBones.LeftLittleDistal; bone_child[last] = -1;

            for (int i = 19; i < 34; i++) bone_hand[i] = VRC_Pickup.PickupHand.Left;

            bone_targets[last = 34] = HumanBodyBones.RightThumbProximal; bone_parent[last] = 18;
            bone_targets[last = 35] = HumanBodyBones.RightThumbIntermediate;
            bone_targets[last = 36] = HumanBodyBones.RightThumbDistal; bone_child[last] = -1;
            bone_targets[last = 37] = HumanBodyBones.RightIndexProximal; bone_parent[last] = 18;
            bone_targets[last = 38] = HumanBodyBones.RightIndexIntermediate;
            bone_targets[last = 39] = HumanBodyBones.RightIndexDistal; bone_child[last] = -1;
            bone_targets[last = 40] = HumanBodyBones.RightMiddleProximal; bone_parent[last] = 18;
            bone_targets[last = 41] = HumanBodyBones.RightMiddleIntermediate;
            bone_targets[last = 42] = HumanBodyBones.RightMiddleDistal; bone_child[last] = -1;
            bone_targets[last = 43] = HumanBodyBones.RightRingProximal; bone_parent[last] = 18;
            bone_targets[last = 44] = HumanBodyBones.RightRingIntermediate;
            bone_targets[last = 45] = HumanBodyBones.RightRingDistal; bone_child[last] = -1;
            bone_targets[last = 46] = HumanBodyBones.RightLittleProximal; bone_parent[last] = 18;
            bone_targets[last = 47] = HumanBodyBones.RightLittleIntermediate;
            bone_targets[last = 48] = HumanBodyBones.RightLittleDistal; bone_child[last] = -1;

            for (int i = 34; i < 49; i++) bone_hand[i] = VRC_Pickup.PickupHand.Right;

            bone_targets[last = 49] = HumanBodyBones.UpperChest; bone_child[last] = 9; bone_parent[last] = 8;
            bone_targets[last = 50] = HumanBodyBones.LeftToes; bone_parent[last] = 5; bone_child[last] = -2;
            bone_targets[last = 51] = HumanBodyBones.RightToes; bone_parent[last] = 5; bone_child[last] = -2;
        }
    }
}
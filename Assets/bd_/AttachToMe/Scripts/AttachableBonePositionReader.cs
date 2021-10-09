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
    /// Sacrificial class to read bone positions. GetBonePosition can throw exceptions that can't be caught, so this U# script
    /// acts as a try-catch block, with AttachablesGlobalTracking respawning it if necessary.
    /// </summary>
    [DefaultExecutionOrder(-99)]
    public class AttachableBonePositionReader : UdonSharpBehaviour
    {
        public Vector3[] positions;
        public Quaternion[] rotations;

        public Vector3 singleBonePos;
        public Quaternion singleBoneRot;

        public bool successful;

        public int suppressPlayerId = -1;
        public int lastPlayerId = -1;

        void Start()
        {
            positions = new Vector3[0];
            rotations = new Quaternion[0];
        }

        public void _a_AcquireSingleBoneData(VRCPlayerApi player, HumanBodyBones bone)
        {
            if (player.playerId == suppressPlayerId) return;

            lastPlayerId = player.playerId;

            singleBonePos = player.GetBonePosition(bone);
            singleBoneRot = player.GetBoneRotation(bone);

            successful = true;
        }

        public void _a_AcquireAllBoneData(VRCPlayerApi player, object[] bones)
        {
            if (player.playerId == suppressPlayerId) return;

            lastPlayerId = player.playerId;

            if (bones.Length > positions.Length)
            {
                positions = new Vector3[bones.Length];
                rotations = new Quaternion[bones.Length];
            }

            for (int i = 0; i < bones.Length; i++)
            {
                var bone = (HumanBodyBones)bones[i];

                positions[i] = player.GetBonePosition(bone);
                rotations[i] = player.GetBoneRotation(bone);
            }

            successful = true;
        }

        public int _a_Watchdog(int val)
        {
            return val;
        }
    }
}

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    /// <summary>
    /// Sacrificial class to read bone positions. GetBonePosition can throw exceptions that can't be caught, so this U# script
    /// </summary>
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
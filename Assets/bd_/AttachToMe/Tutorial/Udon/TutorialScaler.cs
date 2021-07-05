
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    /// <summary>
    /// Adjusts the scale of the tutorial objects based on the player's avatar scale
    /// (specifically, the size of their hand).
    /// </summary>
    public class TutorialScaler : UdonSharpBehaviour
    {
        readonly float F_EPSILON = 0.001f;

        public float handScale;

        void Start()
        {
            SendCustomEventDelayedSeconds(nameof(_CheckAvatarScale), 1.0f);
        }

        public void _CheckAvatarScale()
        {
            SendCustomEventDelayedSeconds(nameof(_CheckAvatarScale), 1.0f);

            var player = Networking.LocalPlayer;

            if (!Utilities.IsValid(player)) return;

            var handRoot = player.GetBonePosition(HumanBodyBones.LeftHand);
            var fingerRoot = player.GetBonePosition(HumanBodyBones.LeftIndexProximal);

            if (handRoot.sqrMagnitude < F_EPSILON || fingerRoot.sqrMagnitude < F_EPSILON) return;

            handScale = Vector3.Distance(handRoot, fingerRoot);

            transform.localScale = Vector3.one * handScale;
        }
    }
}
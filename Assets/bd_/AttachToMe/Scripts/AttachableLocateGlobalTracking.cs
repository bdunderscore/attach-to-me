
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    [DefaultExecutionOrder(-1000)]
    public class AttachableLocateGlobalTracking : UdonSharpBehaviour
    {
        void Start()
        {
            // Construct and save the path to this object in a player tag
            string path = gameObject.name;

            Transform parent = transform.parent;
            while (parent != null)
            {
                path = $"{parent.gameObject.name}/{path}";
                parent = parent.parent;
            }

            Networking.LocalPlayer.SetPlayerTag("net.fushizen.attachable.GlobalTrackingPath", path);
        }
    }
}
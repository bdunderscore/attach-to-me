
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{

    public class AttachableVersion : UdonSharpBehaviour
    {
        readonly string VERSION = "AttachToMe v1.0.0rc1";

        void Start()
        {
            var uiText = GetComponent<UnityEngine.UI.Text>();

            uiText.text = VERSION;

            enabled = false;
        }
    }

}
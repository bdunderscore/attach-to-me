
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{

    public class AttachableVersion : UdonSharpBehaviour
    {
        readonly string VERSION = "AttachToMe v0.9.8alpha";

        void Start()
        {
            var uiText = GetComponent<UnityEngine.UI.Text>();

            uiText.text = VERSION;

            enabled = false;
        }
    }

}
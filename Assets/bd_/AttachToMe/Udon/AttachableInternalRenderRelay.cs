
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace net.fushizen.attachable
{
    [AddComponentMenu("Attachable Internal/RenderRelay")]
    [DefaultExecutionOrder(1)]
    public class AttachableInternalRenderRelay : UdonSharpBehaviour
    {
        [HideInInspector]
        public Attachable parent;

        void Start()
        {

        }

        void OnWillRenderObject()
        {
            parent._a_PreRender();
        }
    }
}
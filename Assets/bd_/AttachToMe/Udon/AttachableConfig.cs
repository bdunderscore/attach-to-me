using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
using UdonSharpEditor;
#endif

namespace net.fushizen.attachable
{
    [RequireComponent(typeof(Attachable))]
    public class AttachableConfig : MonoBehaviour
    {
        [HideInInspector]
        public bool isNewlyCreated = true;

        public Transform t_pickup;
        public Transform t_attachmentDirection;
        public Transform t_support;

        public float range = 2;
        [Range(0, 1)]
        public float directionality = 0;
        public bool preferSelf = true;

        public bool perm_removeTracee = true;
        public bool perm_removeOwner = true;
        public bool perm_removeOther = true;

        public Animator c_animator;
        public string anim_onTrack, anim_onHeld, anim_onTrackLocal, anim_onHeldLocal;

#if UNITY_EDITOR
        private void OnValidate()
        {
            SyncAll();
        }

        public void SyncAll() {
            Attachable attachable = gameObject.GetUdonSharpComponent<Attachable>();

            bool anythingChanged = false;

            syncProp(ref anythingChanged, ref t_pickup, ref attachable.t_pickup);
            syncProp(ref anythingChanged, ref t_attachmentDirection, ref attachable.t_attachmentDirection);
            syncProp(ref anythingChanged, ref t_support, ref attachable.t_support);
            syncProp(ref anythingChanged, ref range, ref attachable.range);
            syncProp(ref anythingChanged, ref directionality, ref attachable.directionality);
            syncProp(ref anythingChanged, ref preferSelf, ref attachable.preferSelf);
            syncProp(ref anythingChanged, ref perm_removeTracee, ref attachable.perm_removeTracee);
            syncProp(ref anythingChanged, ref perm_removeOwner, ref attachable.perm_removeOwner);
            syncProp(ref anythingChanged, ref perm_removeOther, ref attachable.perm_removeOther);

            syncProp(ref anythingChanged, ref c_animator, ref attachable.c_animator);
            syncProp(ref anythingChanged, ref anim_onHeld, ref attachable.anim_onHeld);
            syncProp(ref anythingChanged, ref anim_onHeldLocal, ref attachable.anim_onHeldLocal);
            syncProp(ref anythingChanged, ref anim_onTrack, ref attachable.anim_onTrack);
            syncProp(ref anythingChanged, ref anim_onTrackLocal, ref attachable.anim_onTrackLocal);

            if (anythingChanged)
            {
                Undo.RecordObjects(new UnityEngine.Object[] { attachable, UdonSharpEditorUtility.GetBackingUdonBehaviour(attachable) }, "Sync attachable configuration");
                UdonSharpEditorUtility.CopyProxyToUdon(attachable); 
            }

            isNewlyCreated = false;
        }

        private void syncProp(ref bool anythingChanged, ref string newVal, ref string oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            }
            else if ((newVal == null || oldVal == null) || (newVal != null && !newVal.Equals(oldVal)))
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }

        private void syncProp(ref bool anythingChanged, ref bool newVal, ref bool oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            } else if (newVal != oldVal)
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }

        private void syncProp(ref bool anythingChanged, ref float newVal, ref float oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            }
            else if (newVal != oldVal)
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }

        private void syncProp(ref bool anythingChanged, ref Transform newVal, ref Transform oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            }
            else if (newVal != oldVal)
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }


        private void syncProp(ref bool anythingChanged, ref Animator newVal, ref Animator oldVal)
        {
            if (isNewlyCreated)
            {
                newVal = oldVal;
            }
            else if (newVal != oldVal)
            {
                oldVal = newVal;
                anythingChanged = true;
            }
        }

#endif
    }
}

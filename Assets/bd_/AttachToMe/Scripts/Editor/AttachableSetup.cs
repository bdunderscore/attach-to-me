using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UdonSharpEditor;
using VRC.SDK3.Components;

namespace net.fushizen.attachable {

    public class AttachableSetup : MonoBehaviour
    {
        readonly static string SUPPORT_PREFAB_GUID = "f5b337b5275cec940975dcd0e0d45049";

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }

        [MenuItem("GameObject/[AttachToMe] Make attachable", false, 49)]
        static void GameObjectMenuMakeAttachable(MenuCommand command)
        {
            var target = (GameObject)command.context;


            MakeAttachable(target);
        }

        static void MakeAttachable(GameObject target) {
            var path = AssetDatabase.GUIDToAssetPath(SUPPORT_PREFAB_GUID);
            if (path == null)
            {
                Debug.LogError("Unable to find support prefab");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (path == null)
            {
                Debug.LogError("Unable to load support prefab");
                return;
            }

            Undo.RegisterCompleteObjectUndo(target, "Make pickup attachable");

            var isActive = target.activeSelf;
            target.SetActive(true);

            if (target.GetComponent<VRCObjectSync>() == null)
            {
                Undo.AddComponent<VRCObjectSync>(target);
            }

            if (target.GetUdonSharpComponent<Attachable>())
            {
                return;
            }

            var rigidbody = target.GetComponent<Rigidbody>();
            if (rigidbody == null)
            {
                rigidbody = Undo.AddComponent<Rigidbody>(target);
            } else
            {
                Undo.RegisterCompleteObjectUndo(rigidbody, "Configure rigidbody");
            }
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;

            var pickup = target.GetComponent<VRCPickup>();
            if (pickup == null)
            {
                pickup = Undo.AddComponent<VRCPickup>(target);
            } else
            {
                Undo.RecordObject(pickup, "Configure pickup");
            }
            pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;

            if (target.GetComponent<Collider>() == null)
            {
                Undo.AddComponent<BoxCollider>(target);
            }

            if (target.GetUdonSharpComponent<AttachableInternalPickupProxy>() == null)
            {
                var component = Undo.AddComponent<AttachableInternalPickupProxy>(target);
                UdonSharpEditorUtility.ConvertToUdonBehavioursWithUndo(new UdonSharp.UdonSharpBehaviour[] { component });
            }

            var t_target = target.transform;
            var siblingIndex = target.transform.GetSiblingIndex();
            var parent = target.transform.parent;

            var directionality = new GameObject("Attachment Direction");
            Undo.RegisterCreatedObjectUndo(directionality, "Attachable setup");
            directionality.transform.SetParent(t_target);
            directionality.transform.localPosition = Vector3.zero;
            directionality.transform.localRotation = Quaternion.identity;

            var gameObj = new GameObject();
            Undo.RegisterCreatedObjectUndo(gameObj, "Attachable setup");

            gameObj.name = target.name;

            gameObj.transform.parent = parent;
            gameObj.transform.SetPositionAndRotation(t_target.position, t_target.rotation);
            gameObj.transform.SetSiblingIndex(siblingIndex);

            Undo.SetTransformParent(t_target, gameObj.transform, "Attachable setup");
            target.name = "pickup";

            var attachable = gameObj.AddComponent<Attachable>();

            var updateLoop = gameObj.AddComponent<AttachableInternalUpdateLoop>();
            updateLoop.enabled = false;

            var postLateUpdate = gameObj.AddComponent<AttachableInternalPostLateUpdate>();
            postLateUpdate.enabled = false;

            UdonSharpEditor.UdonSharpEditorUtility.ConvertToUdonBehaviours(new UdonSharp.UdonSharpBehaviour[]
            {
                attachable,
                updateLoop,
                postLateUpdate
            });

            var config = gameObj.AddComponent<AttachableConfig>();

            config.t_pickup = t_target;
            config.t_attachmentDirection = directionality.transform;
            config.SyncAll();

            gameObj.SetActive(isActive);
        }
    }

}
﻿/*
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
            Undo.RegisterCompleteObjectUndo(target, "Make pickup attachable");

            var isActive = target.activeSelf;
            target.SetActive(true);

            var objectSync = target.GetComponent<VRCObjectSync>();
            if (objectSync != null)
            {
                Undo.DestroyObjectImmediate(objectSync);
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

            UdonSharpEditor.UdonSharpEditorUtility.ConvertToUdonBehaviours(new UdonSharp.UdonSharpBehaviour[]
            {
                attachable,
                updateLoop
            });

            var config = gameObj.AddComponent<AttachableConfig>();

            config.t_pickup = t_target;
            config.t_attachmentDirection = directionality.transform;
            config.SyncAll();

            gameObj.SetActive(isActive);
        }
    }

}
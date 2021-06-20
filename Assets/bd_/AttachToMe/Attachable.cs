
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace net.fushizen.attachable
{
    // UI controls
    // Trigger (same hand): Attach (short press), detach (long press), next target (press after attachment)
    // Trigger (other hand): Change target player

    // Player targets are selected based on abs(distance along Z) to capsule center

    // Targets:
    //   For most bones, we imagine a line between the bone's origin and its primary child:
    //     UpperLeg -> LowerLeg
    //     LowerLeg -> Foot
    //     Foot -> Toe
    //     Spine -> Chest -> Neck -> Head
    //     Shoulder -> UpperArm -> LowerArm -> Hand
    //     Finger Proximal -> Intermediate -> Distal
    //
    //     Distance to bone is distance of closest approach between Z+ direction on model and the bone line.
    //
    //   For terminal bones we treat them as spheres of radius determined by nearby bones:
    //     Toe, Eye, Jaw, UpperChest: Not mapped
    //     Head: Distance to neck
    //     Last finger: Distance to prior bone
    //     Hips: Greater of distance to upper leg and distance to spine.
    //
    //     Distance to bone is distance of closest approach between Z+ direction on model and bone sphere

    // We will initially select the closest bone by the above heuristic. We then keep this choice until we find a bone
    // that is X times better. Manual overriding can also select any bone not more than X times worse than the best.

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class Attachable : UdonSharpBehaviour
    {
        #region Editor configuration
        public Transform t_pickup, t_boneProxy;

        public Transform t_boneSelectorModel, t_boneRootModel;
        Material mat_bone;
        MeshRenderer mr_pickup_proxy;

        public float range;

        public bool preferSelf;

        #endregion


        #region Placement state

        readonly int ST_NOT_ATTACHED = 0;
        readonly int ST_ATTACHED = 1;
        readonly int ST_HELD_LOCAL = 2;
        readonly int ST_HELD_REMOTE = 3;

        int state;

        #endregion

        #region Input state

        bool[] trigger_wasHeld;
        float trigger_sameHand_lastChange;

        float lastBoneScan;

        #endregion

        #region Debug

        public Transform markClosestPoint;
        public Transform markBoneTarget;

        #endregion

        #region Bone data

        object[] bone_targets;
        int[] bone_child; // child to use for bone linearization, or -1 for sphere, or -2 for do not target
        int[] bone_parent; // parent of bone to use for bone size computation, or -1 for hips

        void InitBoneData()
        {
            int last = -1;
            bone_targets = new object[52];
            bone_child = new int[bone_targets.Length];
            bone_parent = new int[bone_targets.Length];

            for (int i = 0; i < 50; i++)
            {
                bone_child[i] = i + 1;
                bone_parent[i] = i - 1;
            }

            bone_targets[last = 0] = HumanBodyBones.Hips; bone_child[last] = -1; bone_parent[last] = -1;
            bone_targets[last = 1] = HumanBodyBones.LeftUpperLeg; bone_child[last] = last + 2; bone_parent[last] = 0;
            bone_targets[last = 2] = HumanBodyBones.RightUpperLeg; bone_child[last] = last + 2; bone_parent[last] = 0;
            bone_targets[last = 3] = HumanBodyBones.LeftLowerLeg; bone_child[last] = last + 2; bone_parent[last] = last - 2;
            bone_targets[last = 4] = HumanBodyBones.RightLowerLeg; bone_child[last] = last + 2; bone_parent[last] = last - 2;
            bone_targets[last = 5] = HumanBodyBones.LeftFoot; bone_child[last] = 50; bone_parent[last] = last - 2;
            bone_targets[last = 6] = HumanBodyBones.RightFoot; bone_child[last] = 51; bone_parent[last] = last - 2;
            bone_targets[last = 7] = HumanBodyBones.Spine; bone_parent[last] = 0;
            bone_targets[last = 8] = HumanBodyBones.Chest; bone_child[last] = 49;
            bone_targets[last = 9] = HumanBodyBones.Neck;
            bone_targets[last = 10] = HumanBodyBones.Head; bone_child[last] = -1;
            bone_targets[last = 11] = HumanBodyBones.LeftShoulder; bone_parent[last] = 49; bone_child[last] = last + 2;
            bone_targets[last = 12] = HumanBodyBones.RightShoulder; bone_parent[last] = 49; bone_child[last] = last + 2;
            bone_targets[last = 13] = HumanBodyBones.LeftUpperArm; bone_parent[last] = last - 2; bone_child[last] = last + 2;
            bone_targets[last = 14] = HumanBodyBones.RightUpperArm; bone_parent[last] = last - 2; bone_child[last] = last + 2;
            bone_targets[last = 15] = HumanBodyBones.LeftLowerArm; bone_parent[last] = last - 2; bone_child[last] = last + 2;
            bone_targets[last = 16] = HumanBodyBones.RightLowerArm; bone_parent[last] = last - 2; bone_child[last] = last + 2;
            bone_targets[last = 17] = HumanBodyBones.LeftHand; bone_parent[last] = last - 2; bone_child[last] = 25; // middle finger
            bone_targets[last = 18] = HumanBodyBones.RightHand; bone_parent[last] = last - 2; bone_child[last] = 40;
            bone_targets[last = 19] = HumanBodyBones.LeftThumbProximal; bone_parent[last] = 17;
            bone_targets[last = 20] = HumanBodyBones.LeftThumbIntermediate;
            bone_targets[last = 21] = HumanBodyBones.LeftThumbDistal; bone_child[last] = -1;
            bone_targets[last = 22] = HumanBodyBones.LeftIndexProximal; bone_parent[last] = 17;
            bone_targets[last = 23] = HumanBodyBones.LeftIndexIntermediate;
            bone_targets[last = 24] = HumanBodyBones.LeftIndexDistal; bone_child[last] = -1;
            bone_targets[last = 25] = HumanBodyBones.LeftMiddleProximal; bone_parent[last] = 17;
            bone_targets[last = 26] = HumanBodyBones.LeftMiddleIntermediate;
            bone_targets[last = 27] = HumanBodyBones.LeftMiddleDistal; bone_child[last] = -1;
            bone_targets[last = 28] = HumanBodyBones.LeftRingProximal; bone_parent[last] = 17;
            bone_targets[last = 29] = HumanBodyBones.LeftRingIntermediate;
            bone_targets[last = 30] = HumanBodyBones.LeftRingDistal; bone_child[last] = -1;
            bone_targets[last = 31] = HumanBodyBones.LeftLittleProximal; bone_parent[last] = 17;
            bone_targets[last = 32] = HumanBodyBones.LeftLittleIntermediate;
            bone_targets[last = 33] = HumanBodyBones.LeftLittleDistal; bone_child[last] = -1;
            bone_targets[last = 34] = HumanBodyBones.RightThumbProximal; bone_parent[last] = 18;
            bone_targets[last = 35] = HumanBodyBones.RightThumbIntermediate;
            bone_targets[last = 36] = HumanBodyBones.RightThumbDistal; bone_child[last] = -1;
            bone_targets[last = 37] = HumanBodyBones.RightIndexProximal; bone_parent[last] = 18;
            bone_targets[last = 38] = HumanBodyBones.RightIndexIntermediate;
            bone_targets[last = 39] = HumanBodyBones.RightIndexDistal; bone_child[last] = -1;
            bone_targets[last = 40] = HumanBodyBones.RightMiddleProximal; bone_parent[last] = 18;
            bone_targets[last = 41] = HumanBodyBones.RightMiddleIntermediate;
            bone_targets[last = 42] = HumanBodyBones.RightMiddleDistal; bone_child[last] = -1;
            bone_targets[last = 43] = HumanBodyBones.RightRingProximal; bone_parent[last] = 18;
            bone_targets[last = 44] = HumanBodyBones.RightRingIntermediate;
            bone_targets[last = 45] = HumanBodyBones.RightRingDistal; bone_child[last] = -1;
            bone_targets[last = 46] = HumanBodyBones.RightLittleProximal; bone_parent[last] = 18;
            bone_targets[last = 47] = HumanBodyBones.RightLittleIntermediate;
            bone_targets[last = 48] = HumanBodyBones.RightLittleDistal; bone_child[last] = -1;
            bone_targets[last = 49] = HumanBodyBones.UpperChest; bone_child[last] = 9; bone_parent[last] = 8;
            bone_targets[last = 50] = HumanBodyBones.LeftToes; bone_parent[last] = 5; bone_child[last] = -2;
            bone_targets[last = 51] = HumanBodyBones.RightToes; bone_parent[last] = 5; bone_child[last] = -2;
        }

        // pseudo-ref parameters
        float boneDistance;
        Vector3 nearestPointBone, nearestPointRay, selectedBoneRoot, selectedBoneChildPos;
        float boneLength;
        bool boneHasChild;

        float PointDistanceToInfiniteLine(Vector3 probe, Vector3 raySource, Vector3 rayDirection)
        {
            Vector3 ac = probe - raySource;
            Vector3 ab = rayDirection;

            return Vector3.Dot(ac, ab) / ab.sqrMagnitude;
        }


        Vector3 NearestPointToSphere(Vector3 target, Vector3 raySource, Vector3 rayDirection)
        {
            Vector3 one = new Vector3(1, 1, 1);

            float initialDeriv = Vector3.Dot(raySource, one) - Vector3.Dot(target, one);
            float deltaTime = Vector3.Dot(rayDirection, one);
            float t = initialDeriv / (-deltaTime);

            return raySource + t * rayDirection;
        }

        bool ComputeBonePosition(VRCPlayerApi player, int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= bone_targets.Length) return false;

            HumanBodyBones bone = (HumanBodyBones)bone_targets[targetIndex];
            int child = bone_child[targetIndex];

            if (child == -2) return false;

            selectedBoneRoot = player.GetBonePosition(bone);
            if (selectedBoneRoot.sqrMagnitude < 0.001f) return false;

            // Identify bone child point
            selectedBoneChildPos = Vector3.zero;
            while (child >= 0)
            {
                selectedBoneChildPos = player.GetBonePosition((HumanBodyBones)bone_targets[child]);
                if (selectedBoneChildPos.sqrMagnitude > 0.001) break;
                child = bone_child[child];
            }

            boneHasChild = (child >= 0);

            if (targetIndex == 0)
            {
                // Special handling for hips
                Vector3 leftLeg = player.GetBonePosition(HumanBodyBones.LeftUpperLeg);
                Vector3 rightLeg = player.GetBonePosition(HumanBodyBones.RightUpperLeg);
                Vector3 betweenLegs = Vector3.Lerp(leftLeg, rightLeg, 0.5f);

                if (betweenLegs.sqrMagnitude > 0.001f)
                {
                    selectedBoneRoot = player.GetBonePosition(HumanBodyBones.Spine);
                    Vector3 displacement = betweenLegs - selectedBoneRoot;
                    selectedBoneChildPos = selectedBoneRoot + displacement * 2; // why negative?
                    boneHasChild = true;
                }
            } else if (targetIndex == 10)
            {
                // Special handling for head. Extend the bone vector in whichever _local_ axis is best aligned with the neck-head direction
                Vector3 neck = player.GetBonePosition(HumanBodyBones.Neck);
                Vector3 neckHeadRaw = selectedBoneRoot - neck;
                Vector3 neckHead = neckHeadRaw.normalized;


                Quaternion headRot = player.GetBoneRotation(HumanBodyBones.Head);

                Vector3 bestAxis = headRot * Vector3.up;
                float bestAxisLen = Mathf.Abs(Vector3.Dot(bestAxis, neckHead));

                Vector3 candidate = headRot * Vector3.forward;
                float axisLen = Mathf.Abs(Vector3.Dot(candidate, neckHead));
                if (axisLen > bestAxisLen)
                {
                    bestAxis = candidate;
                    bestAxisLen = axisLen;
                }

                candidate = headRot * Vector3.right;
                axisLen = Mathf.Abs(Vector3.Dot(candidate, neckHead));
                if (axisLen > bestAxisLen)
                {
                    bestAxis = candidate;
                }

                // Fix orientation and scale to more-or-less neck length
                bestAxis *= Vector3.Dot(bestAxis, neckHeadRaw);

                selectedBoneChildPos = selectedBoneRoot + bestAxis;
                boneHasChild = true;
            }

            if (boneHasChild)
            {
                boneLength = (selectedBoneRoot - selectedBoneChildPos).magnitude;
            }

            return true;
        }

        // Returns the estimated distance to a bone, or -1 if not targetable
        bool DistanceToBone(VRCPlayerApi player, Vector3 raySource, Vector3 rayDirection, int targetIndex)
        {
            if (player.isLocal && player.Equals(pickup.currentPlayer))
            {
                // Don't attach to the current hand
                var heldInHand = pickup.currentHand;

                if (heldInHand == VRC_Pickup.PickupHand.Left)
                {
                    if (targetIndex == 11 || targetIndex == 13 || targetIndex == 15 || targetIndex == 17 || (targetIndex >= 19 && targetIndex < 34))
                    {
                        return false;
                    }
                } else if (heldInHand == VRC_Pickup.PickupHand.Right)
                {
                    if (targetIndex == 12 || targetIndex == 14 || targetIndex == 16 || targetIndex == 18 || (targetIndex >= 34 && targetIndex < 49))
                    {
                        return false;
                    }
                }
            }

            if (!ComputeBonePosition(player, targetIndex))
            {
                return false;
            }

            if (!boneHasChild)
            {
                nearestPointRay = raySource;
                nearestPointBone = selectedBoneRoot;
            } else
            {
                Vector3 velo = selectedBoneChildPos - selectedBoneRoot;
                float t = PointDistanceToInfiniteLine(raySource, selectedBoneRoot, velo);

                if (t > 1) t = 1;
                else if (t < 0) t = 0;

                nearestPointBone = selectedBoneRoot + velo * t;
                nearestPointRay = raySource;
            }

            boneDistance = Vector3.Distance(nearestPointRay, nearestPointBone) - Mathf.Abs(Vector3.Dot(nearestPointBone - nearestPointRay, rayDirection.normalized)) * 0.5f;
            
            return true;
        }

        #endregion

        PickupProxy proxy;
        VRC_Pickup pickup;
        UpdateLoop updateLoop;

        void Start()
        {
            InitBoneData();
            mat_bone = t_boneSelectorModel.GetComponent<MeshRenderer>().sharedMaterial;
            trigger_wasHeld = new bool[2];

            proxy = t_pickup.GetComponent<PickupProxy>();
            proxy._a_SetController(this);
            pickup = (VRC_Pickup)t_pickup.GetComponent(typeof(VRC_Pickup));
            mr_pickup_proxy = t_pickup.GetComponent<MeshRenderer>();

            updateLoop = GetComponent<UpdateLoop>();
            updateLoop.enabled = false;

            ApplyState();
        }

        #region Synced data

        [UdonSynced]
        bool tracking, heldSynced;

        // -1 when not tracking
        [UdonSynced]
        int trackingPlayerId;
        [UdonSynced]
        int trackingBoneId;

        // These offsets describe the position and rotation of the model within the t_boneProxy local coordinate space
        [UdonSynced]
        Vector3 positionOffset;
        [UdonSynced]
        Quaternion rotationOffset;

        #endregion

        #region State management and bone position update loop

        public override void OnOwnershipTransferred(VRCPlayerApi newOwner)
        {
            ApplyState();
        }

        bool checkHeldActive;

        public void _CheckHeld()
        {
            checkHeldActive = false;
            ApplyState();
        }

        public void ApplyState() {
            var currentHolder = pickup.currentPlayer;

            bool enable_update;
            bool enable_prerender;

            if (Utilities.IsValid(currentHolder))
            {
                // Do not sync to the bone
                t_pickup.parent = transform;

                if (currentHolder.isLocal)
                {
                    Debug.Log("ApplyState: Active holder is local");
                    enable_update = true;
                } else 
                {
                    Debug.Log("ApplyState: Active holder is remote");
                    t_boneRootModel.gameObject.SetActive(false);
                    markClosestPoint.gameObject.SetActive(false);
                    enable_update = false;

                    if (!checkHeldActive)
                    {
                        checkHeldActive = true;
                        SendCustomEventDelayedSeconds("_CheckHeld", 0.25f);
                    }
                }
                enable_prerender = false;
            } else if (tracking)
            {
                Debug.Log("ApplyState: Tracking");
                // Sync to the bone
                UpdateBoneProxy();

                // Reparent preserving world position
                t_pickup.parent = t_boneProxy;

                // If the synced values are for a non-held state, we can apply them (they should be up to date, or will be soon)
                ApplyOffsets();

                enable_update = true;
                enable_prerender = true;
            } else
            {
                Debug.Log("ApplyState: Not tracking");
                // Not tracking.
                t_pickup.parent = transform;

                enable_update = false;
                enable_prerender = false;
            }
            
            mr_pickup_proxy.enabled = enable_prerender;
            updateLoop.enabled = enable_update;
        }

        public void _a_Update()
        {
            var currentHolder = pickup.currentPlayer;
            if (Utilities.IsValid(currentHolder))
            {
                if (currentHolder.isLocal)
                {
                    UpdateHeld();
                }
            } else if (tracking)
            {
                UpdateBoneProxy();
                ApplyOffsets();
            } else
            {
                // Error state
                ApplyState();
            }
        }

        public void _a_PreRender()
        {
            // Adjust position just before rendering to fix up FinalIK weirdness
            UpdateBoneProxy();
            ApplyOffsets();
        }

        void ApplyOffsets()
        {
            Debug.Log($"ApplyOffsets: HS={heldSynced}");
            if (!heldSynced)
            {
                t_pickup.localPosition = positionOffset;
                t_pickup.localRotation = rotationOffset;
            }
        }

        void UpdateBoneProxy()
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(trackingPlayerId);
            if (player == null || !Utilities.IsValid(player) || trackingBoneId < 0 || trackingBoneId >= bone_targets.Length)
            {
                if (!Networking.IsOwner(gameObject)) return;
                tracking = false;
                RequestSerialization();
                return;
            }

            var bone = (HumanBodyBones)bone_targets[trackingBoneId];
            var bonePos = player.GetBonePosition(bone);

            if (bonePos.sqrMagnitude < 0.001)
            {
                if (!Networking.IsOwner(gameObject)) return;
                tracking = false;
                RequestSerialization();
                return;
            }

            t_boneProxy.position = bonePos;
            t_boneProxy.rotation = player.GetBoneRotation(bone);
        }
        #endregion

        #region Serialization

        public override void OnDeserialization()
        {
            var holder = pickup.currentPlayer;
            var holderName = Utilities.IsValid(holder) ? $"{holder.playerId}" : "<none>";

            Debug.Log($"OnDS: t={tracking} hs={heldSynced} ch={holderName}");
            var curHolder = pickup.currentPlayer;
            if (curHolder != null && curHolder.isLocal)
            {
                // Take back ownership
                _a_OnPickup();
            } else
            {
                ApplyState();
            }
        }

        public override void OnPreSerialization()
        {
            if (heldSynced && !Networking.LocalPlayer.Equals(pickup.currentPlayer))
            {
                // Something has gone terribly wrong, reset.
                heldSynced = tracking = false;
                ApplyState();
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(gameObject)) RequestSerialization();
        }

        #endregion

        // Player search preferences
        // All players with capsule center within range are considered candidates
        // Prefer in order:
        //  1. Last manually selected player
        //  2. Self (if prefer self selected)
        //  3. Nearest other players
        //  4. Self (if prefer self not selected)

        // Last manually selected player is cleared when we search and find them to not be a candidate

        #region Bone search

        // Bone search has two modes:
        // Continuous scan - the initial mode, scans N bones per frame and selects the best continuously
        // Manual selection - Cycles through bones in order of distance to target. We periodically scan to maintain this order.
        //   Bones more than *X distance from the best candidate are rejected

        readonly float secondaryCandidateMult = 3;

        int[] prefBoneIds;
        float[] prefBoneDistances;
        int prefBoneLength;

        void HeapSwap(int a, int b)
        {
            float td = prefBoneDistances[a];
            prefBoneDistances[a] = prefBoneDistances[b];
            prefBoneDistances[b] = td;

            int ti = prefBoneIds[a];
            prefBoneIds[a] = prefBoneIds[b];
            prefBoneIds[b] = ti;
        }

        void HeapPush(int boneId, float dist)
        {
            int i = prefBoneLength++;
            prefBoneIds[i] = boneId;
            prefBoneDistances[i] = dist;

            while (i > 0)
            {
                int parent = (i - 1) >> 1;

                if (dist < prefBoneDistances[parent])
                {
                    HeapSwap(i, parent);
                    i = parent;
                } else
                {
                    break;
                }
            }
        }

        int HeapPop() {

            if (prefBoneLength == 0)
                return -1;

            // pop and swap max to end
            int i = --prefBoneLength;

            HeapSwap(0, i);

            // restore heap
            float d = prefBoneDistances[0];
            for(int j = 0; j < i; )
            {
                int c1 = (j << 1) + 1;
                int c2 = c1 + 1;

                float d1 = c1 < i ? prefBoneDistances[c1] : 99999;
                float d2 = c2 < i ? prefBoneDistances[c2] : 99999;
                    
                if (d1 < d && d1 < d2) // move higher parent to the smaller child
                {
                    HeapSwap(j, c1);
                    j = c1;
                } else if (d2 < d)
                {
                    HeapSwap(j, c2);
                    j = c2;
                } else
                {
                    break;
                }
            }

            return i;
        }

        void BoneScan(VRCPlayerApi player)
        {
            int nBones = bone_targets.Length;

            Vector3 bestBoneRoot = Vector3.zero, bestBoneChild = Vector3.zero;

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            prefBoneIds = new int[bone_targets.Length];
            prefBoneDistances = new float[bone_targets.Length];
            prefBoneLength = 0;
            prefBoneDistances[0] = 99999;

            for (int i = 0; i < nBones; i++)
            {
                boneDistance = 999;
                bool success = DistanceToBone(player, t_pickup.position, t_pickup.TransformDirection(Vector3.forward), i);

                if (success && boneDistance <= prefBoneDistances[0] * secondaryCandidateMult)
                {
                    HeapPush(i, boneDistance);
                }
            }

            if (prefBoneLength > 0)
            {
                closestBoneDistance = prefBoneDistances[0];
            }
        }

        #endregion


        #region Held controls

        float closestBoneDistance;

        public void _a_OnPickup()
        {
            Debug.Log($"OnPickup o={Networking.IsOwner(pickup.gameObject)} t={tracking} cp={Networking.GetOwner(pickup.gameObject).playerId}/{pickup.currentPlayer.playerId}");
            if (Networking.IsOwner(pickup.gameObject))
            {
                heldSynced = true;

                Networking.SetOwner(Networking.LocalPlayer, gameObject);

                if (tracking && TargetPlayerValid())
                {
                    BoneScan(VRCPlayerApi.GetPlayerById(trackingPlayerId));
                }

                RequestSerialization();

                // Suppress the desktop trigger-press-on-pickup
                trigger_wasHeld[0] = trigger_wasHeld[1] = true;
            }

            // Apply locally immediately (offset one frame to avoid breaking pickups) but also RPC it off to everyone else
            // XXX: can this arrive before the pickup state updates? [answer: yes, and it's a problem]
            SendCustomEventDelayedFrames("ApplyState", 1);
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ApplyState");
        }

        public void _a_OnDrop()
        {
            Debug.Log("=== OnDrop");
            if (tracking)
            {
                UpdateBoneProxy(); // update bone proxy

                positionOffset = t_boneProxy.InverseTransformPoint(t_pickup.position);
                rotationOffset = Quaternion.Inverse(t_boneProxy.rotation) * t_pickup.rotation;

                heldSynced = false;
            } else
            {
                trackingPlayerId = -1;
                trackingBoneId = -1;
            }

            if (Networking.IsOwner(pickup.gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                RequestSerialization();
            }

            Debug.Log($"OnDrop: HS={heldSynced}");

            SendCustomEventDelayedFrames("ApplyState", 1);
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "ApplyState");
        }

        void DisplayBoneModel(VRCPlayerApi target)
        {
            string boneTarget = "<invalid>";
            if (ComputeBonePosition(target, trackingBoneId))
            {
                boneTarget = bone_targets[trackingBoneId].ToString();
                t_boneRootModel.position = selectedBoneRoot;

                if (!boneHasChild)
                {
                    t_boneRootModel.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                    t_boneRootModel.rotation = Quaternion.identity;
                    t_boneSelectorModel.gameObject.SetActive(false);
                }
                else
                {
                    t_boneRootModel.localScale = new Vector3(boneLength, boneLength, boneLength);
                    t_boneRootModel.rotation = Quaternion.LookRotation(selectedBoneChildPos - selectedBoneRoot);
                    t_boneSelectorModel.gameObject.SetActive(true);
                    markBoneTarget.position = selectedBoneChildPos;
                }

                t_boneRootModel.gameObject.SetActive(true);
                mat_bone.SetColor("_WireColor", tracking ? Color.green : Color.blue);
            }
            else
            {
                t_boneRootModel.gameObject.SetActive(false);
            }
        }

        bool TargetPlayerValid()
        {
            var player = VRCPlayerApi.GetPlayerById(trackingPlayerId);

            if (!Utilities.IsValid(player)) return false;

            return Vector3.Distance(player.GetPosition(), pickup.transform.position) <= range;
        }

        VRCPlayerApi[] playerArray;

        int FindNextPlayer(int minPlayerId)
        {
            if (playerArray == null || playerArray.Length < 128)
            {
                playerArray = new VRCPlayerApi[128];
            }

            int nPlayers = VRCPlayerApi.GetPlayerCount();
            playerArray = VRCPlayerApi.GetPlayers(playerArray);

            float bestDistance = range;
            int bestPlayerId = -1;

            var pickupPos = pickup.transform.position;

            for (int i = 0; i < nPlayers; i++)
            {
                var player = playerArray[i];
                
                if (!Utilities.IsValid(player)) continue;

                var playerId = player.playerId;

                if (playerId <= minPlayerId) continue;

                float distance = Vector3.Distance(pickupPos, player.GetPosition());


                if (player.isLocal)
                {
                    if (preferSelf)
                    {
                        if (distance < range) {
                            return playerId;
                        }
                    }
                    else if (i < nPlayers - 1)
                    {
                        // Move self to the end of the list to evaluate last
                        var tmp = playerArray[nPlayers - 1];
                        playerArray[nPlayers - 1] = player;
                        playerArray[i] = tmp;
                        i--;
                    } else
                    {
                        // Select only if we have no choice
                        if (bestPlayerId == -1)
                        {
                            bestPlayerId = playerId;
                            bestDistance = distance;
                        }
                    }
                } else
                {
                    if (distance > bestDistance) continue;

                    bestDistance = distance;
                    bestPlayerId = playerId;
                }
            }

            return bestPlayerId;
        }

        VRCPlayerApi UpdateTrackingPlayer()
        {
            // Check if we need to perform a target player search. This happens only when the current player is invalid or out of range.
            if (!TargetPlayerValid())
            {
                tracking = false;
                trackingPlayerId = FindNextPlayer(-1);
                if (trackingPlayerId < 0)
                {
                    if (tracking) RequestSerialization();

                    return null;
                }
            }

            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(trackingPlayerId);
            if (!Utilities.IsValid(target))
            {
                // Should be impossible...?
                if (tracking)
                {
                    tracking = false;
                    trackingPlayerId = -1;
                    RequestSerialization();
                }
                return null;
            }

            return target;
        }

        void UpdateHeld() {
            VRCPlayerApi player = UpdateTrackingPlayer();
            if (player == null)
            {
                Debug.Log("=== No candidate players");
                // No candidate players in range, disable display
                t_boneRootModel.gameObject.SetActive(false);
                return;
            }

            CheckInput();

            // If we've held the trigger for long enough, release tracking
            if (tracking && trigger_wasHeld[0] && trigger_sameHand_lastChange + 3.0f < Time.timeSinceLevelLoad)
            {
                tracking = false;
                RequestSerialization();
            }

            // If locked, check if current bone is a valid target.
            bool needScan = !tracking
                || !DistanceToBone(player, t_pickup.position, t_pickup.TransformDirection(Vector3.forward), trackingBoneId)
                || boneDistance > closestBoneDistance * secondaryCandidateMult;

            if (needScan)
            {
                BoneScan(player);
                int priorId = trackingBoneId;
                trackingBoneId = prefBoneLength > 0 ? prefBoneIds[0] : -1;
                if (priorId != trackingBoneId) RequestSerialization();
            }

            DisplayBoneModel(player);
        }

        void _a_OnTriggerChanged(bool boneSelectTrigger, bool prior)
        {
            Debug.Log($"=== OnTriggerChanges boneSelectTrigger={boneSelectTrigger} prior={prior}");

            if (prior) return; // only trigger on false -> true transition

            if (boneSelectTrigger)
            {
                // Lock to bone
                if (trackingBoneId >= 0)
                {
                    if (tracking)
                    {
                        if (prefBoneLength > 1)
                        {
                            var priorBoneDist = prefBoneDistances[0];
                            // Find next bone
                            if (HeapPop() > -1)
                            {
                                trackingBoneId = prefBoneIds[0];
                                Debug.Log($"Pop: Bone distance {priorBoneDist}=>{prefBoneDistances[0]}@{bone_targets[prefBoneIds[0]]}");
                            } else
                            {
                                trackingBoneId = -1;
                            }
                            
                            RequestSerialization();
                        } else
                        {
                            // Restart scan on next update frame
                            trackingBoneId = -1;
                        }
                    } else
                    {
                        tracking = true;

                        RequestSerialization();
                    }
                }
            } else
            {
                // Change tracking player
                if (trackingPlayerId < 0) return;

                trackingPlayerId = FindNextPlayer(trackingPlayerId);
                tracking = false;
                trackingBoneId = -1;
            }
        }

        void CheckInput()
        {
            bool boneSelectTrigger, userSelectTrigger;

            if (Networking.LocalPlayer.IsUserInVR())
            {
                bool heldInLeft = pickup.currentHand == VRC_Pickup.PickupHand.Left;

                boneSelectTrigger = Input.GetAxis("Oculus_CrossPlatform_PrimaryIndexTrigger") > 0.9;
                userSelectTrigger = Input.GetAxis("Oculus_CrossPlatform_SecondaryIndexTrigger") > 0.9;

                if (!heldInLeft)
                {
                    bool tmp = boneSelectTrigger;
                    boneSelectTrigger = userSelectTrigger;
                    userSelectTrigger = tmp;
                }
            } else
            {
                boneSelectTrigger = Input.GetMouseButton(0);
                userSelectTrigger = Input.GetMouseButton(2);
            }

            if (trigger_wasHeld[0] != boneSelectTrigger)
            {
                _a_OnTriggerChanged(true, trigger_wasHeld[0]);
                trigger_wasHeld[0] = boneSelectTrigger;
                trigger_sameHand_lastChange = Time.timeSinceLevelLoad;
            }

            if (trigger_wasHeld[1] != userSelectTrigger)
            {
                _a_OnTriggerChanged(false, trigger_wasHeld[1]);
                trigger_wasHeld[1] = userSelectTrigger;
            }
        }

        #endregion
    }
}
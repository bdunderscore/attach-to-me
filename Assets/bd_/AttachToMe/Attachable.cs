
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

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
        public Transform t_pickup, t_objectSync, t_boneProxy;

        public Transform t_boneSelectorModel, t_boneRootModel;

        public float range;

        #endregion


        #region Placement state

        readonly int ST_NOT_ATTACHED = 0;
        readonly int ST_ATTACHED = 1;
        readonly int ST_HELD = 2;

        int state;

        #endregion

        #region Input state

        float t_same_hand_held;
        float t_other_hand_held;

        VRC_Pickup.PickupHand holdingHand;

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
            bone_targets[last = 17] = HumanBodyBones.LeftHand; bone_parent[last] = last - 2; bone_child[last] = -2;
            bone_targets[last = 18] = HumanBodyBones.RightHand; bone_parent[last] = last - 2; bone_child[last] = -2;
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

        Vector3 NearestPointToSphere(Vector3 target, Vector3 raySource, Vector3 rayDirection)
        {
            Vector3 one = new Vector3(1, 1, 1);

            float initialDeriv = Vector3.Dot(raySource, one) - Vector3.Dot(target, one);
            float deltaTime = Vector3.Dot(rayDirection, one);
            float t = initialDeriv / (-deltaTime);

            return raySource + t * rayDirection;
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

            HumanBodyBones bone = (HumanBodyBones)bone_targets[targetIndex];
            int child = bone_child[targetIndex];
            int parent = bone_parent[targetIndex];

            if (child == -2) return false;

            Vector3 bonePos = player.GetBonePosition(bone);
            if (bonePos.sqrMagnitude < 0.001f) return false;

            // Identify bone child point
            Vector3 childPos = Vector3.zero;
            while (child != -1)
            {
                childPos = player.GetBonePosition((HumanBodyBones)bone_targets[child]);
                if (childPos.sqrMagnitude > 0.001) break;
                child = bone_child[child];
            }

            if (targetIndex == 0)
            {
                // Special handling for hips
                Vector3 leftLeg = player.GetBonePosition(HumanBodyBones.LeftUpperLeg);
                Vector3 rightLeg = player.GetBonePosition(HumanBodyBones.RightUpperLeg);
                Vector3 betweenLegs = Vector3.Lerp(leftLeg, rightLeg, 0.5f);

                if (betweenLegs.sqrMagnitude > 0.001f)
                {
                    bonePos = player.GetBonePosition(HumanBodyBones.Spine);
                    Vector3 displacement = betweenLegs - bonePos;
                    childPos = bonePos + displacement * 2; // why negative?
                    child = 0;
                }
            }

            if (child == -1)
            {
                nearestPointRay = NearestPointToSphere(bonePos, raySource, rayDirection);
                nearestPointBone = bonePos;
                boneLength = 0;
            } else
            {
                rayDirection = Vector3.Normalize(rayDirection);
                Vector3 boneDirection = Vector3.Normalize(childPos - bonePos);

                float limitHi = Vector3.Distance(childPos, bonePos);

                // https://homepage.univie.ac.at/franz.vesely/notes/hard_sticks/hst/hst.html

                Vector3 r12 = bonePos - raySource;
                float dot_e1 = Vector3.Dot(r12, rayDirection);
                float dot_e2 = Vector3.Dot(r12, boneDirection);
                float dotDirections = Vector3.Dot(rayDirection, boneDirection);
                float divisor = (1 - dotDirections * dotDirections);

                if (divisor < 0.001) return false;

                float lambda = (dot_e1 - dot_e2 * dotDirections) / divisor;
                float mu = -(dot_e2 - dot_e1 * dotDirections) / divisor; // why is this negative?

                if (mu < 0)
                {
                    nearestPointBone = bonePos;
                    nearestPointRay = NearestPointToSphere(nearestPointBone, raySource, rayDirection);
                } else if (mu > limitHi)
                {
                    nearestPointBone = childPos;
                    nearestPointRay = NearestPointToSphere(nearestPointBone, raySource, rayDirection);
                } else
                {
                    nearestPointBone = bonePos + mu * boneDirection;
                    nearestPointRay = raySource + lambda * rayDirection;
                }

                boneLength = limitHi;
            }

            boneDistance = Vector3.Distance(raySource, nearestPointRay) + Vector3.Distance(nearestPointRay, nearestPointBone);
            selectedBoneRoot = bonePos;
            selectedBoneChildPos = childPos;

            return true;
        }

        #endregion

        PickupProxy proxy;
        VRC_Pickup pickup;
        UpdateLoop updateLoop;

        void Start()
        {
            InitBoneData();

            proxy = t_pickup.GetComponent<PickupProxy>();
            proxy._a_SetController(this);
            pickup = (VRC_Pickup)t_pickup.GetComponent(typeof(VRC_Pickup));

            updateLoop = GetComponent<UpdateLoop>();
            updateLoop.enabled = false;

            SendCustomEventDelayedSeconds("_a_PeriodicUpdate", Random.Range(1f, 2f)); // XXX hack

            SetState(ST_NOT_ATTACHED);
        }

        public void _a_PeriodicUpdate()
        {
            _a_Update();
            SendCustomEventDelayedSeconds("_a_PeriodicUpdate", Random.Range(1f, 2f)); // XXX hack
        }

        #region Synced data

        [UdonSynced]
        bool tracking;

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

        void ParentAndZero(Transform parent, Transform child)
        {
            child.SetParent(parent);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
        }

        void SetState(int state)
        {
            this.state = state;

            if (Networking.IsOwner(pickup.gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, t_objectSync.gameObject);
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            // Avoid reparenting pickups while in a pickup callback
            SendCustomEventDelayedFrames("_ApplyState", 1);
        }

        public void _ApplyState() {
            Debug.Log("=== Begin apply ===");
            switch (state)
            {
                case 0: // ST_NOT_ATTACHED

                    updateLoop.enabled = false;
                    t_objectSync.parent = transform;
                    t_objectSync.position = t_pickup.position;

                    ParentAndZero(t_objectSync, t_pickup);

                    break;
                case 1: // ST_ATTACHED
                    updateLoop.enabled = true;
                    _a_UpdateAttached();

                    ParentAndZero(t_boneProxy, t_pickup);
                    t_pickup.localRotation = rotationOffset;
                    t_pickup.localPosition = positionOffset;

                    ParentAndZero(t_pickup, t_objectSync);
                    break;
                case 2: // ST_HELD
                    t_pickup.parent = transform;

                    updateLoop.enabled = true;

                    ParentAndZero(t_pickup, t_objectSync);
                    break;
            }
            Debug.Log("=== End apply ===");
        }

        public void _a_Update()
        {
            switch (state)
            {
                case 0: // ST_NOT_ATTACHED:
                    updateLoop.enabled = false;
                    break;
                case 2: //ST_HELD:
                    _a_UpdateHeld();
                    break;
                case 1: //ST_ATTACHED:
                    _a_UpdateAttached();
                    break;
            }
        }

        public void _a_PreRender()
        {
            // Adjust position just before rendering to fix up FinalIK weirdness
            if (state == ST_ATTACHED) _a_UpdateAttached();
        }

        void _a_UpdateAttached()
        {

            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(trackingPlayerId);
            if (player == null || !Utilities.IsValid(player) || trackingBoneId < 0 || trackingBoneId >= bone_targets.Length)
            {
                if (!Networking.IsOwner(gameObject)) return;
                SetState(ST_NOT_ATTACHED);
                return;
            }

            var bone = (HumanBodyBones)bone_targets[trackingBoneId];
            var bonePos = player.GetBonePosition(bone);

            if (bonePos.sqrMagnitude < 0.001)
            {
                SetState(ST_NOT_ATTACHED);
                return;
            }

            t_boneProxy.position = bonePos;
            t_boneProxy.rotation = player.GetBoneRotation(bone);
        }
        #endregion

        #region Serialization

        public override void OnDeserialization()
        {
            Debug.Log("===== DESERIALIZE =====");
            var curHolder = pickup.currentPlayer;
            if (curHolder != null && curHolder.isLocal)
            {
                // Take back ownership
                _a_OnPickup();
            } else
            {
                if (tracking)
                {
                    t_pickup.localRotation = rotationOffset;
                    t_pickup.localPosition = positionOffset;
                }

                if (tracking != (state == ST_ATTACHED))
                {
                    SetState(tracking ? ST_ATTACHED : ST_NOT_ATTACHED);
                }
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(gameObject)) RequestSerialization();
        }

        #endregion

        #region Interaction

        public void _a_OnPickup()
        {
            Debug.Log("====== PICKUP ======");
            if (!Networking.IsOwner(pickup.gameObject)) return;

            SetState(ST_HELD);

            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            tracking = false;
            RequestSerialization();
        }

        public void _a_OnDrop()
        {
            Debug.Log("====== DROP ======");

            if (!Networking.IsOwner(pickup.gameObject)) return;

            if (state == ST_HELD)
                SetState(ST_NOT_ATTACHED);
        }

        #endregion

        float lastDump = 0;

        public void _a_UpdateHeld()
        {

            var players = VRCPlayerApi.GetPlayers(new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]);
            VRCPlayerApi target = null;

            foreach (var p in players)
            {
                if (p.isLocal) continue;
                target = p;
                break;
            }

            if (target == null)
            {
                target = Networking.LocalPlayer;
            }

            int nBones = bone_targets.Length;
            float bestDistance = range * 2;
            int bestBone = -1;
            bool fullDump = Time.time - lastDump > 5;
            float bestBoneLength = 0;

            Vector3 bestBoneRoot = Vector3.zero, bestBoneChild = Vector3.zero;

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            for (int i = 0; i < nBones; i++)
            {
                boneDistance = 999;
                bool success = DistanceToBone(target, t_pickup.position, t_pickup.TransformDirection(Vector3.forward), i);

                if (success && boneDistance <= bestDistance)
                {
                    bestDistance = boneDistance;
                    //markClosestPoint.position = nearestPointRay;

                    bestBone = i;
                    bestBoneLength = boneLength;
                    bestBoneRoot = selectedBoneRoot;
                    bestBoneChild = selectedBoneChildPos;

                    if (fullDump)
                    {
                        Debug.Log($"Full dump [bone {bone_targets[i]}]: success {success} distance {boneDistance} best {bestDistance}");
                    }

                }
            }

            if (fullDump)
            {
                lastDump = Time.time;
            }

            if (bestBone >= 0)
            {
                var bone = (HumanBodyBones)bone_targets[bestBone];
                t_boneProxy.position = target.GetBonePosition(bone);
                t_boneProxy.rotation = target.GetBoneRotation(bone);

                t_boneRootModel.position = bestBoneRoot;
                t_boneRootModel.rotation = Quaternion.LookRotation(bestBoneChild - bestBoneRoot);

                if (bestBoneLength < 0.001)
                {
                    t_boneRootModel.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                    t_boneSelectorModel.gameObject.SetActive(false);
                } else
                {
                    t_boneRootModel.localScale = new Vector3(bestBoneLength, bestBoneLength, bestBoneLength);
                    t_boneSelectorModel.gameObject.SetActive(true);
                    markBoneTarget.position = bestBoneChild;
                }

                trackingPlayerId = target.playerId;
                trackingBoneId = bestBone;
            } else
            {
                trackingPlayerId = -1;
                trackingBoneId = -1;
            }

            sw.Stop();

            if ((Time.frameCount % 100) == 0)
            {
                var elapsed = (double)sw.ElapsedTicks / (double)System.Diagnostics.Stopwatch.Frequency;

                Debug.Log($"Bone search elapsed: {sw.ElapsedTicks} ticks {sw.Elapsed.TotalMilliseconds:F4} ms {sw.Elapsed.TotalMilliseconds * 1000.0:F6} uS");
            }
        }

        public void _a_Commit()
        {
            Debug.Log("====== COMMIT ======");

            if (trackingPlayerId != -1)
            {
                tracking = true;
                t_pickup.parent = t_boneProxy;
                positionOffset = t_pickup.localPosition;
                rotationOffset = t_pickup.localRotation;

                SetState(ST_ATTACHED);
                RequestSerialization();

                pickup.Drop();
            }
        }
    }
}
/*
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

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.SDK3.Components;

#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
#endif


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
    [DefaultExecutionOrder(0)]
    public class Attachable : UdonSharpBehaviour
    {
        /// <summary>
        /// Additional range (in addition to the bone range) for searching for players; beyond this distance
        /// (to capsule root) we'll skip the expensive bone analysis.
        /// </summary>
        private float PLAYER_LEEWAY = 2.0f;

        #region Inspector fields

        public Transform t_pickup;
        public Transform t_attachmentDirection;
        public AttachablesGlobalTracking globalTracking;

        public float range = 0.5f;
        [Range(0,1)]
        public float directionality = 0.5f;
        public bool preferSelf = true;
        public bool trackOnUpdate = false;
        public bool disableFingerSelection = false;

        public bool perm_removeTracee = true;
        public bool perm_removeOwner = true;
        public bool perm_removeOther = true;

        public Animator c_animator;
        public string anim_onTrack, anim_onHeld, anim_onTrackLocal, anim_onHeldLocal;

        public float respawnTime;
        int respawnTimeMs;

        #endregion

        #region Global tracking object hooks

        [HideInInspector]
        public int _tracking_index = -1;

        private float globalTrackingScale;
        private AttachablesGlobalOnHold onHold;

        #endregion

        #region Support object references

        /// PickupProxy object on the pickup
        AttachableInternalPickupProxy proxy;
        /// Actual VRC_Pickup object
        [HideInInspector] // exposed for tutorial hooks
        public VRC_Pickup pickup;

        /// <summary>
        /// Object Sync component on the pickup
        /// </summary>
        private VRCObjectSync objectSync;

        /// Transform for the beam shown from the pickup to the bone
        private Transform t_traceMarker;

        /// Root transform for the bone display mode
        private Transform t_boneModelRoot;

        /// The gameobject holding the renderer for the prism representing the body of the bone
        private GameObject obj_boneModelBody;

        /// <summary>
        /// Material used to render the bone wireframes
        /// </summary>
        private Material mat_bone;

        /// Disablable update loop component
        AttachableInternalUpdateLoop updateLoop;

        /// <summary>
        ///  Disablable post-late-update loop component
        /// </summary>
        AttachableInternalPostLateUpdate postLateUpdateLoop;

        #endregion

        #region State transition animation data

        Vector3 transitionPosition;
        Quaternion transitionRotation;

        float transitionEndTime;

        readonly float transitionDuration = 0.1f;

        #endregion

        #region Core tracking state state

        // When tracking a bone, contains the position and rotation offset relative to that bone.
        [UdonSynced]
        Vector3 sync_pos;
        [UdonSynced]
        Quaternion sync_rot;

        // When tracking a bone (possibly the holder's hand), contains the target player and bone index.
        // When not tracking, sync_targetPlayer contains -1
        [UdonSynced]
        int sync_targetPlayer, sync_targetBone;

        // Incremented when changes are made to synced state;
        [UdonSynced]
        int sync_seqno;

        // Held remotely
        [UdonSynced]
        bool sync_heldRemote;

        /// <summary>
        /// Time that a respawn is scheduled at (expressed as network time ms). Zero if no respawn is scheduled.
        /// Invalid when tracking (sync_targetBone >= 0) or held (sync_heldRemote).
        /// </summary>
        [UdonSynced]
        int sync_scheduledRespawn;

        // Last applied state sequence number (triggers lerping transition)
        int last_seqno;

        #endregion

        #region Misc state

        bool allowAttachPickup;

        /// <summary>
        ///  We allow pickup for a short time after dropping the object, even if globally pickup of tracked objects is disabled.
        ///  This time is the time at which this exception is no longer active.
        /// </summary>
        float forcePickupEnabledUntil = -999f;

        readonly float PICKUP_GRACETIME = 4.0f;

        #endregion

        #region Bone state

        Vector3 bonePosition;
        Quaternion boneRotation;

        #endregion

        #region Selection state

        /// <summary>
        /// True if held by the local user.
        /// </summary>
        bool isHeldLocally;

        /// <summary>
        /// True if bone/player selection can be performed for this object.
        /// </summary>
        bool isSelectionActive;

        /// <summary>
        /// True if we intend to transition to tracking mode on drop (ie we're locked on)
        /// </summary>
        bool tracking;

        /// <summary>
        /// Local target player
        /// </summary>
        int targetPlayerId;
        /// <summary>
        /// Local target bone
        /// </summary>
        int targetBoneId;

        bool[] trigger_wasHeld;
        float trigger_sameHand_lastChange;

        /// <summary>
        /// Time (since scene load) of the last full scan we performed, when in target-locked mode.
        /// Used to determine if bone selection will do a new full search or not
        /// 
        /// TODO: unused
        /// </summary>
        float lastBoneScan;

        #endregion


        #region Initialization

        void SetupReferences()
        {
            proxy = t_pickup.GetComponent<AttachableInternalPickupProxy>();
            proxy._a_SetController(this);
            pickup = (VRC_Pickup) t_pickup.GetComponent(typeof(VRC_Pickup));
            objectSync = (VRCObjectSync) t_pickup.GetComponent(typeof(VRCObjectSync));

            onHold = globalTracking.GetComponent<AttachablesGlobalOnHold>();

            var t_support = globalTracking.transform;
            globalTrackingScale = t_support.localScale.x;
            t_traceMarker = t_support.Find("TraceMarker");
            t_boneModelRoot = t_support.Find("BoneMarkerRoot");
            obj_boneModelBody = t_boneModelRoot.Find("boneMarker/Bone").gameObject;

            mat_bone = obj_boneModelBody.transform.GetComponent<MeshRenderer>().sharedMaterial;

            updateLoop = GetComponent<AttachableInternalUpdateLoop>();
            postLateUpdateLoop = GetComponent<AttachableInternalPostLateUpdate>();
        }

        void Start()
        {
            SetupReferences();

            globalTracking._a_Register(this);

            trigger_wasHeld = new bool[2];

            InitBoneData();
            InitAnimator();
            InitRespawn();

            ClearTracking();

            updateLoop.enabled = false;
            postLateUpdateLoop.enabled = false;
        }


        #endregion

        #region Respawn logic

        Vector3 initialPosition;
        Quaternion initialRotation;

        /// <summary>
        /// Records the initial position of the pickup. Called in Start.
        /// </summary>
        void InitRespawn()
        {
            initialPosition = t_pickup.localPosition;
            initialRotation = t_pickup.localRotation;
            respawnTimeMs = Mathf.CeilToInt(respawnTime * 1000.0f);
        }

        /// <summary>
        /// Performs a respawn, if appropriate.
        /// </summary>
        void PerformRespawn()
        {
            Networking.SetOwner(Networking.LocalPlayer, t_pickup.gameObject);
            t_pickup.localPosition = initialPosition;
            t_pickup.localRotation = initialRotation;
            objectSync.FlagDiscontinuity();

            ClearRespawnTimer();
            RequestSerialization();
        }

        /// <summary>
        /// Resets the respawn timer to start ticking from the present moment.
        /// </summary>
        void SetRespawnTimer()
        {
            if (Networking.IsOwner(gameObject) && respawnTimeMs > 0)
            {
                sync_scheduledRespawn = Networking.GetServerTimeInMilliseconds() + respawnTimeMs;
                ScheduleRespawnCheck();
            }
        }

        /// <summary>
        /// Clears the respawn timer, when we start tracking or are picked up.
        /// Does not request serialization.
        /// </summary>
        void ClearRespawnTimer()
        {
            sync_scheduledRespawn = 0;
        }

        /// <summary>
        /// True if we've scheduled a respawn check event already.
        /// </summary>
        bool checkRespawnScheduled;

        public void _a_CheckRespawn()
        {
            checkRespawnScheduled = false;

            if (sync_scheduledRespawn == 0) return;
            if (!Networking.IsOwner(gameObject)) return;

            if (sync_targetBone >= 0 || sync_heldRemote)
            {
                // Already tracking, clear the timer. This should have been cleared already, but just to make sure...
                sync_scheduledRespawn = 0;
                RequestSerialization();
            } else if (sync_scheduledRespawn - Networking.GetServerTimeInMilliseconds() <= 0)
            {
                PerformRespawn();
            } else
            {
                ScheduleRespawnCheck();
            }
        }

        void ScheduleRespawnCheck()
        {
            if (Networking.IsOwner(gameObject) && !checkRespawnScheduled && sync_scheduledRespawn != 0)
            {
                int timeRemaining = sync_scheduledRespawn - Networking.GetServerTimeInMilliseconds();

                if (timeRemaining < 0)
                {
                    PerformRespawn();
                    return;
                }

                // Avoid spamming checks in the event of rounding issues by setting a minimum 100ms interval.
                float delay = Mathf.Max(0.1f, timeRemaining / 1000.0f);

                SendCustomEventDelayedSeconds(nameof(_a_CheckRespawn), delay);
                checkRespawnScheduled = true;
            }
        }

        #endregion

        #region Tracking

        public int _a_GetTrackingBone()
        {
            return sync_targetBone;
        }

        public int _a_GetTrackingPlayer()
        {
            return sync_targetPlayer;
        }

        bool wasTracking;
        void SetTrackingEnabled(bool tracking)
        {
            if (wasTracking == tracking) return;
            wasTracking = tracking;

            if (tracking)
            {
                globalTracking._a_EnableTracking(this);
            } else
            {
                globalTracking._a_DisableTracking(this);
            }

            updateLoop.enabled = isHeldLocally || (tracking && trackOnUpdate);
            postLateUpdateLoop.enabled = tracking && !trackOnUpdate;

            if (Networking.IsOwner(gameObject))
            {
                if (tracking || isHeldLocally)
                {
                    ClearRespawnTimer();
                }
                else
                {
                    SetRespawnTimer();
                }
            }
        }

        /// <summary>
        /// Disables tracking in synced data (but does not actually request serialization)
        /// </summary>
        void ClearTracking()
        {
            transitionEndTime = -1;

            sync_targetPlayer = sync_targetBone = -1;

            SetTrackingEnabled(false);
            _a_SetPickupPerms();
            _a_SyncAnimator();

            sync_seqno++;
        }

        /// <summary>
        /// Tracks the specified player and bone, computing the position/rotation offsets based on present world position.
        /// </summary>
        /// <param name="playerId">Target player</param>
        /// <param name="boneId">Target bone</param>
        /// <returns>True if successful, false if we could not track this bone (in which case we will track world position)</returns>
        bool TrackBone(int playerId, int boneId)
        {
            transitionEndTime = -1;

            sync_targetBone = boneId;
            sync_targetPlayer = playerId;
            if (!UpdateBonePosition())
            {
                ClearTracking();
                return false;
            }

            var invRot = Quaternion.Inverse(boneRotation);
            sync_pos = invRot * (t_pickup.position - bonePosition);
            sync_rot = invRot * t_pickup.rotation;

            sync_seqno++;

            SetTrackingEnabled(true);
            _a_SetPickupPerms();
            _a_SyncAnimator();

            return true;
        }

        /// <summary>
        /// Updates the bone position data to match the targeted bone.
        /// </summary>
        /// <returns>True if successful, else false (eg, generic avatar detected or player missing)</returns>
        bool UpdateBonePosition()
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(sync_targetPlayer);
            if (player == null || !Utilities.IsValid(player))
            {
                return false;
            }
            
            if (sync_targetBone < 0 || sync_targetBone >= bone_targets.Length)
                return false;

            var bone = (HumanBodyBones)bone_targets[sync_targetBone];

            var reader = globalTracking.bonePosReader;
            reader.successful = false;
            reader._a_AcquireSingleBoneData(player, bone);
            if (!reader.successful)
            {
                globalTracking._a_RespawnBoneReader(); // respawn it if it's dead.
                // for now keep our last known good position. Once the player suppression is lifted
                // we'll be able to resume querying.
                return true;
            }

            var bonePos = reader.singleBonePos;

            if (bonePos.sqrMagnitude < 0.001)
            {
                // probably a generic avatar with no bones
                return false;
            }

            this.bonePosition = bonePos;
            this.boneRotation = reader.singleBoneRot;

            return true;
        }

        /// <summary>
        /// Updates the pickup position to match tracked data.
        /// </summary>
        /// <returns>True if successful, false if tracking failed for some reason (eg, missing target or missing bone)</returns>
        public void _a_UpdateTracking()
        {
            if (globalTracking.bonePosReader == null) return;

            if (isHeldLocally)
            {
                if (!Networking.IsOwner(gameObject))
                {
                    // Whoops, someone else stole it
                    pickup.Drop();
                }
            }

            var isTracking = (sync_targetPlayer >= 0);

            if (isTracking != (_tracking_index != -1))
            {
                SetTrackingEnabled(isTracking);
            }

            if (!isTracking || isHeldLocally)
            {
                return;
            }

            if (!UpdateBonePosition()) {
                if (Networking.IsOwner(gameObject))
                {
                    ClearTracking();
                    RequestSerialization();
                }
                return;
            }

            var pos = boneRotation * sync_pos + bonePosition;
            var rot = boneRotation * sync_rot;

            if (sync_seqno != last_seqno)
            {
                transitionPosition = t_pickup.position;
                transitionRotation = t_pickup.rotation;
                transitionEndTime = Time.timeSinceLevelLoad + transitionDuration;
                last_seqno = sync_seqno;
                _a_SyncAnimator();
            }

            if (transitionEndTime > Time.timeSinceLevelLoad)
            {
                var blend = (transitionEndTime - Time.timeSinceLevelLoad) / transitionDuration;

                // Lerp from transition position/rotation
                pos = Vector3.Lerp(pos, transitionPosition, blend);
                rot = Quaternion.Lerp(rot, transitionRotation, blend);
            }

            t_pickup.SetPositionAndRotation(pos, rot);
        }

        #endregion

        #region Bone data

        object[] bone_targets;

        // Valid only in same frame as we started the bone scan.
        Vector3[] bone_positions;
        Quaternion[] bone_rotations;
        int lastBonePosLoadFrame;

        int[] bone_child; // child to use for bone linearization, or -1 for sphere, or -2 for do not target
        int[] bone_parent; // parent of bone to use for bone size computation, or -1 for hips

        readonly int BONE_LEFT_UPPER_LEG = 1;
        readonly int BONE_RIGHT_UPPER_LEG = 2;
        readonly int BONE_SPINE = 7;
        readonly int BONE_NECK = 9;
        readonly int BONE_HEAD = 10;

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
        float boneDistance, trueBoneDistance;
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
            if (disableFingerSelection && targetIndex >= 19 && targetIndex < 49) return false;

            HumanBodyBones bone = (HumanBodyBones)bone_targets[targetIndex];
            int child = bone_child[targetIndex];

            if (child == -2) return false;

            selectedBoneRoot = bone_positions[targetIndex];
            if (selectedBoneRoot.sqrMagnitude < 0.001f) return false;

            // Identify bone child point
            selectedBoneChildPos = Vector3.zero;
            while (child >= 0)
            {
                selectedBoneChildPos = bone_positions[child];
                if (selectedBoneChildPos.sqrMagnitude > 0.001) break;
                child = bone_child[child];
            }

            boneHasChild = (child >= 0);

            if (targetIndex == 0)
            {
                // Special handling for hips
                Vector3 leftLeg = bone_positions[BONE_LEFT_UPPER_LEG];
                Vector3 rightLeg = bone_positions[BONE_RIGHT_UPPER_LEG];
                Vector3 betweenLegs = Vector3.Lerp(leftLeg, rightLeg, 0.5f);

                if (betweenLegs.sqrMagnitude > 0.001f)
                {
                    selectedBoneRoot = bone_positions[BONE_SPINE];
                    Vector3 displacement = betweenLegs - selectedBoneRoot;
                    selectedBoneChildPos = selectedBoneRoot + displacement * 2;
                    boneHasChild = true;
                }
            } else if (targetIndex == 10)
            {
                // Special handling for head. Extend the bone vector in whichever _local_ axis is best aligned with the neck-head direction
                Vector3 neck = bone_positions[BONE_NECK];
                Vector3 neckHeadRaw = selectedBoneRoot - neck;
                Vector3 neckHead = neckHeadRaw.normalized;


                Quaternion headRot = bone_rotations[BONE_HEAD];

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

        public bool _a_HeldInLeftHand(int targetIndex)
        {
            return targetIndex == 11 || targetIndex == 13 || targetIndex == 15 || targetIndex == 17 || (targetIndex >= 19 && targetIndex < 34);
        }

        public bool _a_HeldInRightHand(int targetIndex)
        {
            return targetIndex == 12 || targetIndex == 14 || targetIndex == 16 || targetIndex == 18 || (targetIndex >= 34 && targetIndex < 49);
        }

        // Returns the estimated distance to a bone, or -1 if not targetable
        bool DistanceToBone(VRCPlayerApi player, int targetIndex)
        {
            Vector3 raySource = t_attachmentDirection.position;

            if (bone_positions == null) return false;

            if (player.isLocal && player.Equals(pickup.currentPlayer))
            {
                // Don't attach to the current hand
                var heldInHand = pickup.currentHand;

                if (heldInHand == VRC_Pickup.PickupHand.Left)
                {
                    if (_a_HeldInLeftHand(targetIndex))
                    {
                        return false;
                    }
                } else if (heldInHand == VRC_Pickup.PickupHand.Right)
                {
                    if (_a_HeldInRightHand(targetIndex))
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

            var directionalVector = t_attachmentDirection.TransformDirection(Vector3.forward);

            trueBoneDistance = Vector3.Distance(nearestPointRay, nearestPointBone);
            boneDistance = trueBoneDistance - directionality * Mathf.Abs(Vector3.Dot(nearestPointBone - nearestPointRay, directionalVector));
            
            return true;
        }

        #endregion

        #region Held state transition and sync management

        public void _a_OnPickup()
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);

            sync_heldRemote = true;
            isHeldLocally = true;
            isSelectionActive = onHold._a_OnPickup(this, pickup.currentHand);

            // Save target bones
            targetPlayerId = sync_targetPlayer;
            targetBoneId = sync_targetBone;

            ClearTracking();

            RequestSerialization();

            SetTrackingEnabled(false);
            updateLoop.enabled = true; // SetTrackingEnabled won't set this if we weren't already tracking

            tracking = false;
            if (targetPlayerId >= 0 && TargetPlayerValid())
            {
                if (forcePickupEnabledUntil < Time.timeSinceLevelLoad && targetPlayerId >= 0)
                {
                    // Player picked up a pickup locked to another player by pressing alt, clear the tutorial hook
                    onHold._a_OnAttachedPickup();
                }

                if (targetBoneId >= 0)
                {
                    tracking = true;
                }

                if (!tracking || forcePickupEnabledUntil <= Time.timeSinceLevelLoad)
                {
                    // Enable tracking, but restart the bone selection cycle.
                    var player = VRCPlayerApi.GetPlayerById(targetPlayerId);
                    BoneScan(player);
                }
            }

            // Suppress the desktop trigger-press-on-pickup
            trigger_wasHeld[0] = trigger_wasHeld[1] = true;
            trigger_sameHand_lastChange = Time.timeSinceLevelLoad;

            _a_SyncAnimator();
            ClearRespawnTimer();
        }

        public void _a_OnDrop()
        {
            onHold._a_OnDrop(this);

            isHeldLocally = false;
            isSelectionActive = false;

            if (Networking.IsOwner(gameObject))
            {
                sync_heldRemote = false;

                if (tracking)
                {
                    // Confirm that the tracking target is valid.
                    var player = VRCPlayerApi.GetPlayerById(targetPlayerId);

                    tracking = Utilities.IsValid(player) && player != null
                        && DistanceToBone(player, targetBoneId) && trueBoneDistance < range;
                }

                if (tracking) {
                    TrackBone(targetPlayerId, targetBoneId);
                } else {
                    ClearTracking();
                }
                RequestSerialization();
            }

            t_boneModelRoot.gameObject.SetActive(false);
            t_traceMarker.gameObject.SetActive(false);

            forcePickupEnabledUntil = Time.timeSinceLevelLoad + (PICKUP_GRACETIME - 0.1f);
            _a_SetPickupPerms();
            SendCustomEventDelayedSeconds(nameof(_a_SetPickupPerms), PICKUP_GRACETIME);

            // The change in the currently-held-player flag seems to be delayed one frame
            SendCustomEventDelayedFrames(nameof(_a_SyncAnimator), 1);

            if (!tracking)
            {
                SetRespawnTimer();
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi newOwner)
        {
            // This may cancel tracking or force drop as needed
            _a_UpdateTracking();
            if (sync_heldRemote && !isHeldLocally)
            {
                sync_heldRemote = false;
                RequestSerialization();
            }
            _a_SyncAnimator();

            // Clear cached bone-distance data, this will force a rescan the next time we pick it up.
            closestBoneDistance = 0;
        }

        #endregion

        #region Update loop

        public void _a_Update()
        {
            if (isSelectionActive)
            {
                UpdateHeld();
            }
            else
            {
                _a_UpdateTracking();
            }
        }

        #endregion

        #region State management and bone position update loop

        public void _a_SetPickupEnabled(bool allowAttachPickup)
        {
            this.allowAttachPickup = allowAttachPickup;
            _a_SetPickupPerms();
        }

        public void _a_TryRemoveFromSelf()
        {
            if (!isHeldLocally && sync_targetPlayer == Networking.LocalPlayer.playerId && _a_HasPickupPermissions())
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                ClearTracking();
                RequestSerialization();
            }
        }

        public void _a_SetPickupPerms()
        {
            if (isHeldLocally) return;

            if (sync_targetPlayer >= 0 && !allowAttachPickup && Time.timeSinceLevelLoad >= forcePickupEnabledUntil)
            {
                pickup.pickupable = false;
            } else
            {
                pickup.pickupable = _a_HasPickupPermissions();
            }
        }

        public bool _a_HasPickupPermissions()
        {
            if (targetPlayerId == -1 || VRCPlayerApi.GetPlayerCount() == 1)
            {
                return true;
            } else
            {
                bool isTarget = Networking.LocalPlayer.playerId == sync_targetPlayer;
                bool isOwner = Networking.IsOwner(gameObject);
                bool isOther = !(isTarget || isOwner);

                return (perm_removeTracee && isTarget) || (perm_removeOwner && isOwner) || (perm_removeOther && isOther);
            }
        }

        #endregion

        #region Serialization

        public override void OnDeserialization()
        {
            _a_UpdateTracking();
            _a_SetPickupPerms();
            _a_SyncAnimator();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(gameObject)) RequestSerialization();

            // This is mostly just to initialize things once networking is up and running
            _a_UpdateTracking();
            _a_SetPickupPerms();
            _a_SyncAnimator();

            // Workaround dumb vrchat bugs with late joiners :(
            if (Networking.IsOwner(gameObject))
            {
                SendCustomEventDelayedSeconds("_a_RequestSerialization", 1.0f);
                SendCustomEventDelayedSeconds("_a_RequestSerialization", 2.5f);
            }
        }

        public void _a_RequestSerialization()
        {
            RequestSerialization();
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

        bool LoadBoneData(VRCPlayerApi player)
        {
            var bonePosReader = globalTracking.bonePosReader;
            if (bonePosReader == null)
            {
                globalTracking._a_RespawnBoneReader();
            }

            bonePosReader.successful = false;
            bonePosReader._a_AcquireAllBoneData(player, bone_targets);
            if (!bonePosReader.successful)
            {
                // respawn the (potentially dead) bone reader if necessary
                globalTracking._a_RespawnBoneReader();
                // return zero bone candidates, so we'll move on to the next player.
                return false;
            }

            bone_positions = bonePosReader.positions;
            bone_rotations = bonePosReader.rotations;
            lastBonePosLoadFrame = Time.frameCount;

            return true;
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

            float bestPrefDist = 9999;
            float bestTrueDist = 9999;

            if (lastBonePosLoadFrame != Time.frameCount)
            {
                if (!LoadBoneData(player)) return;
            }

            for (int i = 0; i < nBones; i++)
            {
                boneDistance = 999;
                bool success = DistanceToBone(player, i);

                if (boneDistance < bestPrefDist)
                {
                    bestPrefDist = boneDistance;
                    bestTrueDist = trueBoneDistance;
                }

                if (trueBoneDistance > range)
                {
                    continue;
                }

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

        #region Player search
        /// <summary>
        /// Time of last player scan - similarly used to determine if we restart the player search from scratch.
        /// </summary>
        float lastPlayerSearch;
        /// <summary>
        /// Cached player array
        /// </summary>
        VRCPlayerApi[] playerArray;
        /// <summary>
        /// Number of valid players remaining in the array (starting from index 0)
        /// </summary>
        int playerCountInArray;

        bool TargetPlayerValid()
        {
            var player = VRCPlayerApi.GetPlayerById(targetPlayerId);

            if (!Utilities.IsValid(player)) return false;

            return Vector3.Distance(player.GetPosition(), pickup.transform.position) <= range + PLAYER_LEEWAY;
        }

        int FindPlayer()
        {
            var priorSearch = lastPlayerSearch;
            lastPlayerSearch = Time.timeSinceLevelLoad;

            if (playerCountInArray > 0 && priorSearch + 5.0f >= Time.timeSinceLevelLoad)
            {
                int rv = FindNextPlayer();
                if (rv != -1) return rv;
            }

            if (playerArray == null || playerArray.Length < 128)
            {
                playerArray = new VRCPlayerApi[128];
            }

            playerCountInArray = VRCPlayerApi.GetPlayerCount();
            playerArray = VRCPlayerApi.GetPlayers(playerArray);

            return FindNextPlayer();
        }

        int FindNextPlayer()
        {
            // invalidate bone data cache
            lastBonePosLoadFrame = -1;

            float limit = range + PLAYER_LEEWAY;
            float bestDistance = limit;
            int bestIndex = -1;

            var pickupPos = pickup.transform.position;

            for (int i = 0; i < playerCountInArray; i++)
            {
                var player = playerArray[i];

                if (!Utilities.IsValid(player))
                {
                    // Clear this slot and continue
                    playerArray[i] = playerArray[playerCountInArray - 1];
                    i--;
                    continue;
                }

                float distance = Vector3.Distance(pickupPos, player.GetPosition());

                if (player.isLocal)
                {
                    if (preferSelf)
                    {
                        if (distance < limit)
                        {
                            bestIndex = i;
                            break;
                        }
                    }
                    else if (i < playerCountInArray - 1)
                    {
                        // Move self to the end of the list to evaluate last
                        var tmp = playerArray[playerCountInArray - 1];
                        playerArray[playerCountInArray - 1] = player;
                        playerArray[i] = tmp;
                        i--;
                    }
                    else
                    {
                        // Select only if we have no choice
                        if (bestIndex == -1 && distance < limit)
                        {
                            bestIndex = i;
                            bestDistance = distance;
                        }
                    }
                }
                else
                {
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestIndex = i;
                    }
                }
            }

            if (bestIndex != -1)
            {
                // Remove from list
                var bestPlayer = playerArray[bestIndex];

                playerArray[bestIndex] = playerArray[--playerCountInArray];

                return bestPlayer.playerId;
            }

            return -1;
        }

        VRCPlayerApi UpdateTrackingPlayer()
        {
            // Check if we need to perform a target player search. This happens only when the current player is invalid or out of range.
            if (!TargetPlayerValid())
            {
                tracking = false;
                targetPlayerId = FindPlayer();
                if (targetPlayerId < 0)
                {
                    if (tracking) RequestSerialization();

                    return null;
                }
            }

            VRCPlayerApi target = VRCPlayerApi.GetPlayerById(targetPlayerId);
            if (!Utilities.IsValid(target))
            {
                // Should be impossible...?
                if (tracking)
                {
                    tracking = false;
                    targetPlayerId = -1;
                    RequestSerialization();
                }
                return null;
            }

            return target;
        }

        #endregion

        #region Held controls

        float closestBoneDistance;

        void DisplayBoneModel(VRCPlayerApi target)
        {
            string boneTarget = "<invalid>";

            if (bone_positions == null) return; // sanity check

            // Distance is needed to show out-of-range warning
            if (DistanceToBone(target, targetBoneId))
            {
                boneTarget = bone_targets[targetBoneId].ToString();
                t_boneModelRoot.position = selectedBoneRoot;

                if (!boneHasChild)
                {
                    t_boneModelRoot.localScale = new Vector3(0.1f, 0.1f, 0.1f) / globalTrackingScale;
                    t_boneModelRoot.rotation = Quaternion.identity;
                    obj_boneModelBody.SetActive(false);
                }
                else
                {
                    t_boneModelRoot.localScale = new Vector3(boneLength, boneLength, boneLength) / globalTrackingScale;
                    t_boneModelRoot.rotation = Quaternion.LookRotation(selectedBoneChildPos - selectedBoneRoot);
                    obj_boneModelBody.SetActive(true);
                }

                t_boneModelRoot.gameObject.SetActive(true);

                Vector3 traceTarget = boneHasChild ? Vector3.Lerp(selectedBoneRoot, selectedBoneChildPos, 0.5f) : selectedBoneRoot;
                Vector3 traceSource = t_pickup.position;
                t_traceMarker.position = traceSource;
                t_traceMarker.rotation = Quaternion.LookRotation(traceTarget - traceSource);
                t_traceMarker.localScale = 
                    transform.lossyScale.magnitude * Vector3.Distance(traceSource, traceTarget) * new Vector3(0.5f, 0.5f, 0.5f) / globalTrackingScale;
                t_traceMarker.gameObject.SetActive(true);

                Color color = Color.blue;
                if (tracking)
                {
                    if (trueBoneDistance >= range)
                    {
                        color = Color.red;
                    } else
                    {
                        color = Color.green;
                    }
                }

                mat_bone.SetColor("_WireColor", color);
            }
            else
            {
                t_boneModelRoot.gameObject.SetActive(false);
                t_traceMarker.gameObject.SetActive(false);
            }
        }

        void UpdateHeld() {
            VRCPlayerApi player = UpdateTrackingPlayer();
            if (player == null)
            {
                // No candidate players in range, disable display
                t_boneModelRoot.gameObject.SetActive(false);
                t_traceMarker.gameObject.SetActive(false);
                return;
            }

            CheckInput();

            // If we've held the trigger for long enough, release tracking
            if (tracking && trigger_wasHeld[0] && trigger_sameHand_lastChange + 3.0f < Time.timeSinceLevelLoad)
            {
                tracking = false;
                RequestSerialization();
            }

            if (!LoadBoneData(player)) return;

            // If locked, check if current bone is a valid target.
            var boneValid = tracking;
            var dtb = DistanceToBone(player, targetBoneId);
            boneValid = boneValid && dtb;

            bool needScan = !boneValid || boneDistance > closestBoneDistance * secondaryCandidateMult;

            if (needScan)
            {
                var oldDistance = boneDistance;

                BoneScan(player);
                int priorId = targetBoneId;
                var bestBone = prefBoneLength > 0 ? prefBoneIds[0] : -1;

                // closestBoneDistance might have been outdated, check whether we still want to switch bones now
                if (!boneValid || oldDistance > closestBoneDistance * secondaryCandidateMult || bestBone == -1)
                {
                    targetBoneId = bestBone;

                    // Stop tracking, since we're forcing a bone change
                    tracking = false;
                    RequestSerialization();
                }

                if (prefBoneLength == 0)
                {
                    // There were no candidate bones on this player, try the next.
                    targetPlayerId = FindPlayer();
                }
            }

            DisplayBoneModel(player);
        }

        /* BoneScan(player); */
        void NextBone()
        {
            var player = VRCPlayerApi.GetPlayerById(targetPlayerId);
            if (!Utilities.IsValid(player))
            {
                // reset scan
                targetPlayerId = -1;
                targetBoneId = -1;
                return;
            }

            if (trigger_sameHand_lastChange + 5.0f < Time.timeSinceLevelLoad)
            {
                // Perform new scan
                BoneScan(player);

                if (prefBoneLength < 1)
                {
                    tracking = false;
                    targetBoneId = -1;
                } else {
                    targetBoneId = prefBoneIds[0];
                }
                
                return;
            }

            while (prefBoneLength > 1)
            {
                // Find next bone
                if (HeapPop() > -1)
                {
                    targetBoneId = prefBoneIds[0];
                }
                else
                {
                    break;
                }

                // Check that bone is in range
                if (!ComputeBonePosition(player, targetBoneId))
                {
                    continue;
                }

                if (trueBoneDistance >= range)
                {
                    continue;
                }

                // Ok, accept this candidate.
                return;
            }

            // Restart scan on next update frame
            targetBoneId = -1;
            tracking = false;
        }

        void _a_OnTriggerChanged(bool boneSelectTrigger, bool prior)
        {
            if (prior) return; // only trigger on false -> true transition

            if (boneSelectTrigger)
            {
                onHold._a_OnBoneSelect();

                // Lock to bone
                if (targetBoneId >= 0)
                {
                    if (tracking)
                    {
                        NextBone();
                    } else
                    {
                        tracking = true;
                    }
                }
            } else
            {
                if (tracking || targetPlayerId >= 0) onHold._a_OnPlayerSelect();

                tracking = false;
                targetBoneId = -1;

                // Change tracking player
                if (targetPlayerId < 0) return;

                onHold._a_OnPlayerSelect();

                targetPlayerId = FindPlayer();

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

        #region Animator sync

        void InitAnimator()
        {
            if (anim_onTrack.Equals("")) anim_onTrack = null;
            if (anim_onTrackLocal.Equals("")) anim_onTrackLocal = null;
            if (anim_onHeld.Equals("")) anim_onHeld = null;
            if (anim_onHeldLocal.Equals("")) anim_onHeldLocal = null;
        }

        void _a_SyncAnimator()
        {
            if (c_animator == null) return;

            if (anim_onTrack != null) c_animator.SetBool(anim_onTrack, sync_targetPlayer >= 0);
            if (anim_onTrackLocal != null) c_animator.SetBool(anim_onTrackLocal, sync_targetPlayer == Networking.LocalPlayer.playerId);
            if (anim_onHeld != null) c_animator.SetBool(anim_onHeld, sync_heldRemote);
            if (anim_onHeldLocal != null) c_animator.SetBool(anim_onHeldLocal, isHeldLocally);
        }

        #endregion

    }
}

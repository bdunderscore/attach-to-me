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

using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.SDK3.Components;

#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
#endif


namespace net.fushizen.attachable
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [DefaultExecutionOrder(0)]
    public class Attachable : UdonSharpBehaviour
    {
        #region Inspector fields

        public Transform t_pickup;
        public Transform t_attachmentDirection;

        public float range = 0.5f;
        [Range(0,1)]
        public float directionality = 0.5f;
        public bool disableFingerSelection = false;

        /// <summary>
        /// Whether the person the prop is attached to can remove it.
        /// </summary>
        public bool perm_removeTracee = true;
        /// <summary>
        /// Whether the player who attached the prop can remove it.
        /// </summary>
        public bool perm_removeOwner = true;
        /// <summary>
        /// Whether anyone other than the above can remove it.
        /// </summary>
        public bool perm_removeOther = true;
        /// <summary>
        /// Whether the prop can be removed when the player is alone.
        /// </summary>
        public bool perm_fallback = true;

        public Animator c_animator;
        public string anim_onTrack, anim_onHeld, anim_onTrackLocal, anim_onHeldLocal;

        public float respawnTime;
        int respawnTimeMs;

        #endregion

        #region Global tracking object hooks

        [HideInInspector]
        public int _tracking_index = -1;

        [HideInInspector]
        public int _depth = -1;

        private float globalTrackingScale;
        private AttachablesGlobalOnHold onHold;

        #endregion

        #region Support object references

        /// <summary>
        /// Reference to the GlobalTracking component on the controller prefab.
        /// Initialized automagically by spooky action-at-a-distance.
        /// </summary>
        AttachablesGlobalTracking globalTracking;

        /// PickupProxy object on the pickup
        AttachableInternalPickupProxy proxy;

        /// Actual VRC_Pickup object
        [HideInInspector] // exposed for tutorial hooks
        public VRC_Pickup pickup;

        AttachableBoneSelection boneSelection;

        /// <summary>
        /// Material used to render the bone wireframes
        /// </summary>
        private Material mat_bone;

        /// Disablable update loop component
        AttachableInternalUpdateLoop updateLoop;

        #endregion

        #region State transition animation data

        Vector3 transitionPosition;
        Quaternion transitionRotation;

        float transitionEndTime;

        readonly float transitionDuration = 0.1f;

        #endregion

        #region Core tracking state state

        // When tracking a bone, contains the position and rotation offset relative to that bone.
        // When not tracking a bone, contains the local position/rotation of this object
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

        /// <summary>
        /// Display name of player who attached this object. Used for perm_removeOwner.
        /// </summary>
        [UdonSynced]
        string sync_placedBy;

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

        #endregion

        #region Initialization

        void Start()
        {
            InitProxyReference();
            InitAnimator();
            InitRespawn();

            transitionEndTime = -1;

            sync_targetPlayer = sync_targetBone = -1;
            sync_pos = t_pickup.localPosition;
            sync_rot = t_pickup.localRotation;
            sync_placedBy = "";

            _a_SetPickupPerms();
        }

        void InitProxyReference()
        {
            proxy = t_pickup.GetComponent<AttachableInternalPickupProxy>();
            proxy._a_SetController(this);
            pickup = (VRC_Pickup)t_pickup.GetComponent(typeof(VRC_Pickup));
        }

        bool SetupReferences()
        {
            var gtPath = Networking.LocalPlayer.GetPlayerTag("net.fushizen.attachable.GlobalTrackingPath");

            if (gtPath == null)
            {
                Debug.LogError("Attachable: Global tracking object was not found.");
                return false;
            } else
            {
                globalTracking = GameObject.Find(gtPath).GetComponent<AttachablesGlobalTracking>();
            }

            InitProxyReference();

            onHold = globalTracking.GetComponent<AttachablesGlobalOnHold>();
            boneSelection = globalTracking.GetComponent<AttachableBoneSelection>();

            var t_support = globalTracking.transform;
            globalTrackingScale = t_support.localScale.x;

            updateLoop = GetComponent<AttachableInternalUpdateLoop>();

            return true;
        }

        private bool isInitComplete;

        /// <summary>
        /// Checks whether this object has been initialized; if not, attempts initialization.
        /// Initialization may fail if this object receives an event before the AttachableLocateGlobalTracking object.
        /// Due to VRC bugs, we do not rely on DefaultExecutionOrder to sequence this.
        /// </summary>
        /// <returns>True if initialization was successful, false if not</returns>
        private bool CheckInit()
        {
            if (isInitComplete) return true;
            if (!SetupReferences()) return false;

            // We can't fail after this point, and we want to avoid potential recursion in the init logic below.
            isInitComplete = true;

            _depth = 0;
            var p = transform.parent;
            while (p != null)
            {
                _depth++;
                p = p.parent;
            }

            globalTracking._a_Register(this);

            InitBoneData();

            updateLoop.enabled = false;

            sync_pos = t_pickup.localPosition;
            sync_rot = t_pickup.localRotation;

            return true;
        }

        private void OnDestroy()
        {
            if (globalTracking == null) return;
            boneSelection._a_EndSelection(this);
            globalTracking._a_Deregister(this);
        }

        private void OnDisable()
        {
            if (globalTracking == null) return;
            boneSelection._a_EndSelection(this);
        }

        #endregion

        #region Pseudo-object-sync

        /// <summary>
        /// How often to sync the position of the held pickup. This only really affects either generic avatars,
        /// or full-body-tracking avatars whose hip tracker has gone flying off into the distance; in other circumstances,
        /// we're tracking either the head (for desktop) or hand (for VR), which means we'll have up-to-date position information
        /// based on IK.
        /// </summary>
        readonly float heldSyncEvalInterval = 0.1f;


        /// <summary>
        /// Sync data is updated either once per slos interval, or if the error is too high (eg, we're in a generic avatar and can't track a bone)
        /// once per eval interval.
        /// </summary>
        readonly float heldSyncSlowInterval = 0.5f;

        float lastHeldSyncTransmit = -1;

        readonly float heldSyncPosError = 0.02f; // 2cm
        readonly float heldSyncAngError = 5; // degrees

        bool isSyncScheduled;

        /// <summary>
        /// Entry point for periodic held-position-sync activities. This should only be used from the SendCustomEventDelayedSeconds
        /// call in SyncHeldPosition.
        /// </summary>
        public void _a_SyncHeldPosition()
        {
            isSyncScheduled = false;

            SyncHeldPosition();
        }

        /// <summary>
        /// Updates data used to sync the position of the prop while held (assuming object sync is not in use).
        /// This will schedule itself to be re-executed periodically until dropped.
        /// </summary>
        void SyncHeldPosition() {
            if (isHeldLocally)
            {
                var localPlayer = Networking.LocalPlayer;
                var targetBone = BONE_HEAD;

                if (localPlayer.IsUserInVR())
                {
                    targetBone = pickup.currentHand == VRC_Pickup.PickupHand.Left ? BONE_LEFT_HAND : BONE_RIGHT_HAND;
                }

                var priorPos = sync_pos;
                var priorRot = sync_rot;

                var wasTracking = sync_targetBone == targetBone && sync_targetPlayer == localPlayer.playerId;
                var trackSuccess = TrackBoneRaw(localPlayer.playerId, targetBone);
                var isTracking = sync_targetPlayer == localPlayer.playerId;

                var needFastSync = (wasTracking != isTracking) || Vector3.Distance(priorPos, sync_pos) > heldSyncPosError || Quaternion.Angle(priorRot, sync_rot) > heldSyncAngError;

                if (needFastSync || lastHeldSyncTransmit < Time.timeSinceLevelLoad - heldSyncSlowInterval)
                {
                    RequestSerialization();
                    lastHeldSyncTransmit = Time.timeSinceLevelLoad;
                }
            
                SendCustomEventDelayedSeconds(nameof(_a_SyncHeldPosition), heldSyncEvalInterval);
                isSyncScheduled = true;
            }
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
        public void _PerformRespawn()
        {
            Networking.SetOwner(Networking.LocalPlayer, t_pickup.gameObject);
            t_pickup.localPosition = initialPosition;
            t_pickup.localRotation = initialRotation;
            
            ClearTrackingState();
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
                _PerformRespawn();
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
                    _PerformRespawn();
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

            updateLoop.enabled = isHeldLocally;

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

        void ClearTrackingState()
        {
            transitionEndTime = -1;

            sync_targetPlayer = sync_targetBone = -1;
            sync_pos = t_pickup.localPosition;
            sync_rot = t_pickup.localRotation;
            sync_placedBy = "";
        }

        /// <summary>
        /// Disables tracking in synced data (but does not actually request serialization)
        /// </summary>
        void ClearTracking()
        {
            ClearTrackingState();

            SetTrackingEnabled(false);
            _a_SetPickupPerms();
            _a_SyncAnimator();

            sync_seqno++;
        }

        /// <summary>
        /// Tracks the specified player and bone, but does not update pickup state, animator, etc.
        /// </summary>
        /// <param name="playerId">Target player</param>
        /// <param name="boneId">Target bone</param>
        /// <returns>True if successful, false if we could not track this bone (in which case we will track world position)</returns>
        bool TrackBoneRaw(int playerId, int boneId)
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
            sync_placedBy = Networking.LocalPlayer.displayName;

            sync_seqno++;

            return true;
        }


        /// <summary>
        /// Tracks the specified player and bone, computing the position/rotation offsets based on present world position.
        /// </summary>
        /// <param name="playerId">Target player</param>
        /// <param name="boneId">Target bone</param>
        /// <returns>True if successful, false if we could not track this bone (in which case we will track world position)</returns>
        bool TrackBone(int playerId, int boneId)
        {
            bool success = TrackBoneRaw(playerId, boneId);

            SetTrackingEnabled(success);
            _a_SetPickupPerms();
            _a_SyncAnimator();

            return success;
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
            if (globalTracking == null || globalTracking.bonePosReader == null) return;

            if (isHeldLocally)
            {
                if (!Networking.IsOwner(gameObject))
                {
                    // Whoops, someone else stole it
                    pickup.Drop();
                } else
                {
                    return;
                }
            }

            var isTracking = (sync_targetPlayer >= 0);

            SetTrackingEnabled(isTracking || transitionEndTime > Time.timeSinceLevelLoad || sync_seqno != last_seqno);

            if (isTracking && !UpdateBonePosition()) {
                if (Networking.IsOwner(gameObject))
                {
                    ClearTracking();
                    RequestSerialization();
                }
                return;
            }

            // World position and rotation
            Vector3 pos;
            Quaternion rot;

            if (isTracking)
            {
                pos = boneRotation * sync_pos + bonePosition;
                rot = boneRotation * sync_rot;
            } else
            {
                // synced position and rotation are relative to the transform's parent
                var parent = t_pickup.parent;

                if (parent != null)
                {
                    pos = parent.TransformPoint(sync_pos);
                    rot = parent.rotation * sync_rot; 
                } else
                {
                    pos = sync_pos;
                    rot = sync_rot;
                }
            }

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

        // Must match AttachableBoneData
        readonly int BONE_LEFT_UPPER_LEG = 1;
        readonly int BONE_RIGHT_UPPER_LEG = 2;
        readonly int BONE_SPINE = 7;
        readonly int BONE_NECK = 9;
        readonly int BONE_HEAD = 10;
        readonly int BONE_LEFT_HAND = 17;
        readonly int BONE_RIGHT_HAND = 18;

        object[] bone_targets; // Actually HumanBodyBone, but Udon doesn't support arrays of those
        int[] bone_child; // child to use for bone linearization, or -1 for sphere, or -2 for do not target
        int[] bone_parent; // parent of bone to use for bone size computation, or -1 for hips

        void InitBoneData()
        {
            var boneData = globalTracking.GetComponent<AttachableBoneData>();

            bone_targets = boneData.bone_targets;
            bone_child = boneData.bone_child;
            bone_parent = boneData.bone_parent;
        }

        #endregion

        #region Held state transition and sync management

        public void _a_OnPickup()
        {
            if (!CheckInit()) return;

            Networking.SetOwner(Networking.LocalPlayer, gameObject);

            sync_heldRemote = true;
            isHeldLocally = true;
            onHold._a_OnPickup(this, pickup.currentHand);

            if (sync_targetPlayer >= 0)
            {
                if (forcePickupEnabledUntil < Time.timeSinceLevelLoad && sync_targetPlayer >= 0)
                {
                    // Player picked up a pickup locked to another player by pressing alt, clear the tutorial hook
                    onHold._a_OnAttachedPickup();
                }
            }

            // Initiate selection
            boneSelection._a_StartSelection(pickup.currentHand, this, sync_targetPlayer, sync_targetBone);

            _a_SyncAnimator();
            ClearRespawnTimer();
            SyncHeldPosition();
            RequestSerialization();
        }

        public void _a_OnDrop()
        {
            if (!CheckInit()) return;

            onHold._a_OnDrop(this);

            isHeldLocally = false;

            var wasSelectionActive = boneSelection._a_EndSelection(this);

            if (Networking.IsOwner(gameObject))
            {
                sync_heldRemote = false;

                if (wasSelectionActive && boneSelection._a_trackingBone >= 0)
                {
                    TrackBone(boneSelection._a_trackingPlayer, boneSelection._a_trackingBone);
                } else
                {
                    ClearTracking();
                }

                RequestSerialization();
            }

            forcePickupEnabledUntil = Time.timeSinceLevelLoad + (PICKUP_GRACETIME - 0.1f);
            _a_SetPickupPerms();
            SendCustomEventDelayedSeconds(nameof(_a_SetPickupPerms), PICKUP_GRACETIME);

            // The change in the currently-held-player flag seems to be delayed one frame
            SendCustomEventDelayedFrames(nameof(_a_SyncAnimator), 1);

            if (sync_targetBone < 0)
            {
                SetRespawnTimer();
            }
        }

        public override void OnOwnershipTransferred(VRCPlayerApi newOwner)
        {
            if (!CheckInit()) return;

            // This may cancel tracking or force drop as needed
            _a_UpdateTracking();

            if (newOwner.isLocal && sync_heldRemote && !isHeldLocally)
            {
                // Don't inherit bone tracking if we're stealing the pickup.
                ClearTracking();

                sync_heldRemote = false;
                RequestSerialization();
            } else if (isHeldLocally)
            {
                pickup.Drop();
            }

            _a_SyncAnimator();
            _a_SetPickupPerms();
        }

        #endregion

        #region Update loop

        public void _a_Update()
        {
            _a_UpdateTracking();
        }

        #endregion

        #region State management and bone position update loop

        public void _a_SetPickupEnabled(bool allowAttachPickup)
        {
            this.allowAttachPickup = allowAttachPickup;

            if (!CheckInit()) return;

            _a_SetPickupPerms();
        }

        public void _a_TryRemoveFromSelf()
        {
            if (!CheckInit()) return;

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

            if (sync_heldRemote || (sync_targetPlayer >= 0 && !allowAttachPickup && Time.timeSinceLevelLoad >= forcePickupEnabledUntil))
            {
                pickup.pickupable = false;
            } else
            {
                pickup.pickupable = _a_HasPickupPermissions();
            }
        }

        public bool _a_HasPickupPermissions()
        {
            if (sync_targetPlayer == -1 || (perm_fallback && VRCPlayerApi.GetPlayerCount() == 1))
            {
                return true;
            } else
            {
                bool isTarget = Networking.LocalPlayer.playerId == sync_targetPlayer;
                bool isOwner = Networking.LocalPlayer.displayName.Equals(sync_placedBy);
                bool isOther = !(isTarget || isOwner);

                return (perm_removeTracee && isTarget) || (perm_removeOwner && isOwner) || (perm_removeOther && isOther);
            }
        }

        #endregion

        #region Serialization

        public override void OnDeserialization()
        {
            if (!CheckInit())
            {
                SendCustomEventDelayedSeconds(nameof(_a_DelayedOnDeserialization), 1.0f);
            }
            else
            {
                _a_DelayedOnDeserialization();
            }
        }

        public void _a_DelayedOnDeserialization() {
            _a_UpdateTracking();
            _a_SetPickupPerms();
            _a_SyncAnimator();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (Networking.IsOwner(gameObject))
            {
                // Workaround dumb vrchat bugs with late joiners :(
                SendCustomEventDelayedSeconds("_a_RequestSerialization", 2.5f);
            }
        }

        public void _a_RequestSerialization()
        {
            RequestSerialization();
        }

        #endregion

        #region Animator sync

        void InitAnimator()
        {
            if ("".Equals(anim_onTrack)) anim_onTrack = null;
            if ("".Equals(anim_onTrackLocal)) anim_onTrackLocal = null;
            if ("".Equals(anim_onHeld)) anim_onHeld = null;
            if ("".Equals(anim_onHeldLocal)) anim_onHeldLocal = null;
        }

        void _a_SyncAnimator()
        {
            if (c_animator == null) return;

            if (anim_onTrack != null) c_animator.SetBool(anim_onTrack, !sync_heldRemote && sync_targetPlayer >= 0);
            if (anim_onTrackLocal != null) c_animator.SetBool(anim_onTrackLocal, !sync_heldRemote && sync_targetPlayer == Networking.LocalPlayer.playerId);
            if (anim_onHeld != null) c_animator.SetBool(anim_onHeld, sync_heldRemote);
            if (anim_onHeldLocal != null) c_animator.SetBool(anim_onHeldLocal, isHeldLocally);
        }

        #endregion

        #region Editor support
        
        internal void OnValidate()
        {
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!AttachableVersion.IS_USHARP_10) return;

            EditorApplication.delayCall += () =>
            {
                var hideFlags = AttachableConfig.debugComponentsVisibleInInspector
                    ? HideFlags.None
                    : HideFlags.HideInInspector;
                var isDirty = false;

                if (t_pickup != null)
                {
                    var proxy = t_pickup.GetComponent<AttachableInternalPickupProxy>();
                    if (proxy == null)
                    {
                        proxy = t_pickup.gameObject.AddComponent<AttachableInternalPickupProxy>();
                        isDirty = true;
                    }

                    proxy.hideFlags = hideFlags;
                }

                var updateLoop = GetComponent<AttachableInternalUpdateLoop>();
                if (updateLoop == null)
                {
                    isDirty = true;
                    updateLoop = gameObject.AddComponent<AttachableInternalUpdateLoop>();
                }

                updateLoop.enabled = false;
                updateLoop.hideFlags = hideFlags;

                if (isDirty)
                {
                    var stage = PrefabStageUtility.GetCurrentPrefabStage();
                    if (stage != null)
                    {
                        EditorSceneManager.MarkSceneDirty(stage.scene);
                    }
                    else
                    {
                        EditorSceneManager.MarkSceneDirty(gameObject.scene);
                    }
                }
            };
#endif
        }

        #endregion
    }
}

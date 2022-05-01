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

namespace net.fushizen.attachable {
    /// <summary>
    /// Handles searching and selecting candidate bones while an attachable is held.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class AttachableBoneSelection : UdonSharpBehaviour
    {
        /// <summary>
        /// The number of bones to evaluate per frame.
        /// </summary>
#if UNITY_ANDROID
        readonly int BONES_PER_FRAME = 6;
#else
        readonly int BONES_PER_FRAME = 12;
#endif

        /// <summary>
        /// Controls how many seconds to retain bone scan data after dropping.
        /// 
        /// If the same pickup is re-picked-up within this time, we'll continue the bone selection cycle where they
        /// left off instead of re-scanning from scratch.
        /// </summary>
        readonly float RETAIN_CYCLE_TIME = 4.0f;

        /// <summary>
        /// Additional range (in addition to the bone range) for searching for players; beyond this distance
        /// (to capsule root) we'll skip the expensive bone analysis.
        /// </summary>
        private float PLAYER_LEEWAY = 2.0f;

        /// <summary>
        /// Output parameter for _a_EndSelection - indicates the player selected for tracking, or -1 if the pickup should not track.
        /// </summary>
        [HideInInspector]
        public int _a_trackingPlayer;

        /// <summary>
        /// Output parameter for _a_EndSelection - indicates the bone selected for tracking, or -1 if the pickup should not track.
        /// </summary>
        [HideInInspector]
        public int _a_trackingBone;


        bool initDone = false;

        private bool CheckInit() {
            if (initDone) return true;
            SetupReferences();
            
            boneData._a_CheckInit();
            boneHeap._a_CheckInit();

            InitBoneData();

            enabled = false;
            targetPlayerId = targetBoneId = -1;
            initDone = true;

            return true;
        }

#region Global references

        AttachableBoneData boneData;
        AttachablesGlobalOnHold onHold;
        AttachablesGlobalTracking globalTracking;
        AttachableInternalBoneHeap boneHeap;

        /// <summary>
        /// Transform for the beam shown from the pickup to the bone
        /// </summary>
        private Transform t_traceMarker;

        /// <summary>Root transform for the bone display mode</summary>
        private Transform t_boneModelRoot;

        /// <summary>
        /// The gameobject holding the renderer for the prism representing the body of the bone
        /// </summary>
        private GameObject obj_boneModelBody;

        /// <summary>
        /// Material used to render the bone wireframes
        /// </summary>
        private Material mat_bone, mat_sphere;

        void SetupReferences()
        {
            boneData = GetComponent<AttachableBoneData>();
            globalTracking = GetComponent<AttachablesGlobalTracking>();
            onHold = GetComponent<AttachablesGlobalOnHold>();
            boneHeap = GetComponent<AttachableInternalBoneHeap>();

            t_traceMarker = transform.Find("TraceMarker");
            t_boneModelRoot = transform.Find("BoneMarkerRoot");
            obj_boneModelBody = t_boneModelRoot.Find("boneMarker/Bone").gameObject;
            var t_boneModelSphere = t_boneModelRoot.Find("boneMarker/Root");

            mat_bone = obj_boneModelBody.transform.GetComponent<MeshRenderer>().sharedMaterial;
            mat_sphere = t_boneModelSphere.GetComponent<MeshRenderer>().sharedMaterial;
        }

        public override void PostLateUpdate()
        {
            if (activeAttachable == null)
            {
                enabled = false;
                return;
            }

            UpdateHeld();
        }

#endregion

#region Selection state

        /// <summary>
        /// The currently active attachable, or null if we're not selecting currently.
        /// </summary>
        Attachable activeAttachable;

        /// <summary>
        /// The attachable selected previously (or null if we've never picked up anything)
        /// </summary>
        Attachable lastAttachable;

        /// <summary>
        /// Time (since level load) of the last time we dropped a pickup.
        /// </summary>
        float lastDrop;

        /// <summary>
        /// Time (since level load) of the last time we picked up a pickup.
        /// </summary>
        float lastPickup;

        /// <summary>
        /// Time (since level load) of the last time we issued a "next bone" command, or -1 if we haven't done so (recently)
        /// </summary>
        float lastBoneAdvance;

        /// <summary>
        /// Time the trigger was pulled down, or -1 if it's currently up
        /// </summary>
        float triggerDownTime;

        // Values copied from the attachable
        bool disableFingerSelection;
        Transform t_attachmentDirection;
        float range, directionality;
        VRC_Pickup.PickupHand currentHand;

        /// <summary>
        /// The current target player ID; this is the ID of the player that the displayed bone model is tracking,
        /// so it can be set even when not locked.
        /// </summary>
        int targetPlayerId;
        /// <summary>
        /// The current target bone ID; this is the ID of the bone that the displayed bone model is tracking,
        /// so it can be set even when not locked.
        /// </summary>
        int targetBoneId;
        /// <summary>
        /// True if we're locked (bone is green or red) and will attempt to track (if in range) when the pickup is dropped.
        /// </summary>
        bool tracking;

#endregion


#region Bone data and distance computation
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
            var boneData = GetComponent<AttachableBoneData>();

            bone_targets = boneData.bone_targets;
            bone_child = boneData.bone_child;
            bone_parent = boneData.bone_parent;
        }

        // Valid only in same frame as we started the bone scan.
        Vector3[] bone_positions;
        Quaternion[] bone_rotations;
        int lastBonePosLoadFrame;

        // pseudo-ref parameters
        float boneDistance, trueBoneDistance;
        Vector3 nearestPointBone, selectedBoneRoot, selectedBoneChildPos;
        float boneLength;
        bool boneHasChild;

        float PointDistanceToInfiniteLine(Vector3 probe, Vector3 raySource, Vector3 rayDirection)
        {
            Vector3 ac = probe - raySource;
            Vector3 ab = rayDirection;

            return Vector3.Dot(ac, ab) / ab.sqrMagnitude;
        }

        /// <summary>
        /// Computes the position and length of the bone in question.
        /// 
        /// Bone positions must be loaded prior to calling.
        /// 
        /// Output is returned in fields: selectedBoneRoot, selectedBoneChildPos, boneLength, boneHasChild
        /// </summary>
        /// <param name="targetIndex"></param>
        /// <returns></returns>
        bool ComputeBonePosition(int targetIndex)
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

        /// <summary>
        /// Computes the estimated (adjusted) distance to a bone, or -1 if not targetable.
        /// 
        /// Bone positions must be loaded prior to calling.
        /// 
        /// Output is returned in fields: trueBoneDistance holds the raw distance to the bone, while boneDistance has
        /// the distance adjusted for directionality. nearestPointBone has the point on the bone closest to the pickup.
        /// 
        /// This function invokes ComputeBonePosition as a side-effect.
        /// </summary>
        /// <param name="player">Target player</param>
        /// <param name="targetIndex">Target bone index</param>
        /// <returns>True if the bone is a valid candidate, false if not (in which case no guarantees are made about the value of any output fields)</returns>
        bool DistanceToBone(VRCPlayerApi player, int targetIndex)
        {
            Vector3 raySource = t_attachmentDirection.position;

            if (bone_positions == null) return false;

            if (player.isLocal)
            {
                if (currentHand.Equals(boneData._a_GetTrackingHand(targetIndex)))
                {
                    return false;
                }
            }

            if (!ComputeBonePosition(targetIndex))
            {
                return false;
            }

            if (!boneHasChild)
            {
                nearestPointBone = selectedBoneRoot;
            } else
            {
                Vector3 velo = selectedBoneChildPos - selectedBoneRoot;
                float t = PointDistanceToInfiniteLine(raySource, selectedBoneRoot, velo);

                if (t > 1) t = 1;
                else if (t < 0) t = 0;

                nearestPointBone = selectedBoneRoot + velo * t;
            }

            var directionalVector = t_attachmentDirection.TransformDirection(Vector3.forward);

            trueBoneDistance = Vector3.Distance(raySource, nearestPointBone);
            boneDistance = trueBoneDistance - directionality * Mathf.Max(0, Vector3.Dot(nearestPointBone - raySource, directionalVector));
            
            return true;
        }

#endregion

#region Initialization

        /// <summary>
        /// Attempts to start the bone selection process on pickup of the pickup.
        /// </summary>
        /// <param name="hand">The hand holding the pickup</param>
        /// <param name="attachable">The controlling Attachable</param>
        /// <param name="curPlayer">Currently tracked bone (or -1 if not tracking)</param>
        /// <param name="curBone">Currently tracked bone (or -1 if not tracking)</param>
        /// <returns>True if search was initiated, false if another attachable has reserved the selection object</returns>
        public bool _a_StartSelection(VRC_Pickup.PickupHand hand, Attachable attachable, int curPlayer, int curBone)
        {
            if (activeAttachable != null && activeAttachable != attachable) return false;
            if (!CheckInit()) return false;

            var sameAsLast = activeAttachable == lastAttachable;

            if (!sameAsLast || lastDrop <= Time.timeSinceLevelLoad + RETAIN_CYCLE_TIME)
            {
                boneHeap._a_Reset();
            }

            activeAttachable = attachable;
            currentHand = hand;
            disableFingerSelection = attachable.disableFingerSelection;
            enabled = true; // start update loop and input monitoring

            t_attachmentDirection = attachable.t_attachmentDirection;
            this.range = attachable.range;
            this.directionality = attachable.directionality;

            targetPlayerId = curPlayer;
            targetBoneId = curBone;

            tracking = false;
            if (targetPlayerId >= 0 && targetBoneId >= 0)
            {
                tracking = true; // enable tracking, for now...
                if (targetBoneId >= 0)
                {
                    tracking = true;
                }

                if (!tracking || !sameAsLast || lastDrop <= Time.timeSinceLevelLoad + RETAIN_CYCLE_TIME)
                {
                    // Enable tracking, but restart the bone selection cycle.
                    var player = VRCPlayerApi.GetPlayerById(targetPlayerId);
                    finalPlayerId = finalBoneId = -1;

                    //Debug.Log("=== StartSelection: StartBoneScan");
                    StartBoneScan(player);
                }
            }

            // Suppress the desktop trigger-press-on-pickup
            lastPickup = Time.timeSinceLevelLoad;
            triggerDownTime = -1;
            lastBoneAdvance = Time.timeSinceLevelLoad;

            closestBoneDistance = 0;

            return true;
        }


        /// <summary>
        /// Releases the lock on the bone selection system, if and only if the provided attachable matches the lock owner.
        /// 
        /// If the lock was indeed released, the _a_trackingPlayer and _a_trackingBone properties on this object reflect
        /// any selected bone.
        /// </summary>
        /// <param name="attachable">The owning attachable</param>
        /// <returns>true if the passed attachable was holding the lock</returns>
        public bool _a_EndSelection(Attachable attachable)
        {
            if (activeAttachable != attachable) return false;
            if (!CheckInit()) return false;

            if (tracking)
            {
                // Confirm that the tracking target is valid.
                var player = VRCPlayerApi.GetPlayerById(targetPlayerId);

                if (Utilities.IsValid(player) && player != null)
                {
                    var trueDist = boneHeap._a_GetBoneTrueDistance(targetPlayerId, targetBoneId);
                    if (trueDist < 0 || trueDist > range)
                    {
                        tracking = false;
                    }
                } else
                {
                    tracking = false;
                }
            }

            if (!tracking)
            {
                targetPlayerId = -1;
                targetBoneId = -1;
            }

            _a_trackingPlayer = targetPlayerId;
            _a_trackingBone = targetBoneId;

            t_boneModelRoot.gameObject.SetActive(false);
            t_traceMarker.gameObject.SetActive(false);

            lastAttachable = activeAttachable;
            lastDrop = Time.timeSinceLevelLoad;
            activeAttachable = null;
            enabled = false;

            return true;
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

        float[] distanceBuffer;
        Vector3[] boneLengthBuffer;
        float[] boneTrueDistanceBuffer;

        bool LoadBoneData(VRCPlayerApi player)
        {
            var bonePosReader = globalTracking.bonePosReader;
            if (bonePosReader == null)
            {
                globalTracking._a_RespawnBoneReader();
                bonePosReader = globalTracking.bonePosReader;
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

        Vector3 singleBonePos;
        Quaternion singleBoneRot;

        bool LoadSingleBoneData(VRCPlayerApi player, int boneId)
        {
            if (boneId < 0 || boneId >= bone_targets.Length || !Utilities.IsValid(player)) return false;

            var bonePosReader = globalTracking.bonePosReader;
            if (bonePosReader == null)
            {
                globalTracking._a_RespawnBoneReader();
                bonePosReader = globalTracking.bonePosReader;
            }

            bonePosReader.successful = false;
            bonePosReader._a_AcquireSingleBoneData(player, (HumanBodyBones) bone_targets[boneId]);
            if (!bonePosReader.successful)
            {
                // respawn the (potentially dead) bone reader if necessary
                globalTracking._a_RespawnBoneReader();
                return false;
            }

            singleBonePos = bonePosReader.singleBonePos;
            singleBoneRot = bonePosReader.singleBoneRot;

            return true;
        }

        VRCPlayerApi[] boneScanPlayers;
        int nextBoneScanPlayerIndex, boneScanPlayerCount;

        VRCPlayerApi currentlyScanningTarget;

        /// <summary>
        /// This player tag has a nonzero length string if the associated player has data in the bone heap.
        /// </summary>
        readonly string HAS_DATA_FLAG = "net.fushizen.attachable.AttachableBoneSelection.PlayerHasHeapData";

        void ScanNextPlayer()
        {
            //var sw = new System.Diagnostics.Stopwatch();
            //sw.Start();

            if (Utilities.IsValid(currentlyScanningTarget))
            {
                ContinueBoneScan();
                return;
            }

            //Debug.Log("=== ScanNextPlayer: Select next ===");

            VRCPlayerApi player;

            if (boneScanPlayers == null) boneScanPlayers = new VRCPlayerApi[0];

            float maxDistance = range + PLAYER_LEEWAY;
            var pos = t_attachmentDirection.position;

            while (nextBoneScanPlayerIndex < boneScanPlayerCount)
            {
                player = boneScanPlayers[nextBoneScanPlayerIndex++];
                if (!Utilities.IsValid(player)) continue;
                if (Vector3.Distance(player.GetPosition(), pos) <= maxDistance)
                {
                    //Debug.Log("=== ScanNextPlayer: SBS");
                    StartBoneScan(player);
                    return;
                } else {
                    var tag = player.GetPlayerTag(HAS_DATA_FLAG);
                    if (tag != null && tag.Length > 0)
                    {
                        boneHeap._a_ClearPlayer(player);
                        player.SetPlayerTag(HAS_DATA_FLAG, "");
                        // This was an expensive call, so continue on the next frame
                        return;
                    }
                }
            }

            // Restart scan on the next frame
            boneScanPlayerCount = VRCPlayerApi.GetPlayerCount();
            if (boneScanPlayers.Length < boneScanPlayerCount)
            {
                boneScanPlayers = new VRCPlayerApi[boneScanPlayerCount];
            }
            boneScanPlayers = VRCPlayerApi.GetPlayers(boneScanPlayers);
            nextBoneScanPlayerIndex = 0;
        }


        int nextBone = 0;
        void StartBoneScan(VRCPlayerApi player) {
            if (!LoadBoneData(player))
            {
                //Debug.Log("=== StartBoneScan failed to load bone data");
                nextBone = 0;
                currentlyScanningTarget = null;
                return;
            }

            //Debug.Log($"=== StartBoneScan: Init {player.playerId}");

            currentlyScanningTarget = player;
            nextBone = 0;
            currentlyScanningTarget.SetPlayerTag(HAS_DATA_FLAG, "x");

            // On android, we take a break after loading the raw bone data before processing it
#if !UNITY_ANDROID
            ContinueBoneScan();
#endif
        }

        void ContinueBoneScan() {
            //Debug.Log($"=== ContinueBoneScan: {currentlyScanningTarget.playerId}/{nextBone}");
            int lastBone = nextBone + BONES_PER_FRAME;

            if (!Utilities.IsValid(currentlyScanningTarget))
            {
                nextBone = 0;
                currentlyScanningTarget = null;
                //Debug.Log($"=== ContinueBoneScan: no target, reset");
                return;
            }

            int nBones = bone_targets.Length;
            if (lastBone > nBones) lastBone = nBones;

            if (distanceBuffer == null)
            {
                distanceBuffer = new float[nBones];
                boneLengthBuffer = new Vector3[nBones];
                boneTrueDistanceBuffer = new float[nBones];
            }

            float bestPrefDist = boneHeap._a_BestBoneDistance();

            var targetIndex = (targetPlayerId == currentlyScanningTarget.playerId) ? targetBoneId : -1;

            for (int i = nextBone; i < lastBone; i++)
            {
                boneDistance = 999;
                bool success = DistanceToBone(currentlyScanningTarget, i) && (trueBoneDistance <= range || i == targetIndex);

                if (!success)
                {
                    distanceBuffer[i] = -1;
                    continue;
                }

                // Compute bone length vector for display. For now, this is in world coordinates.
                boneLengthBuffer[i] = boneHasChild ? selectedBoneChildPos - selectedBoneRoot : Vector3.zero;
                boneTrueDistanceBuffer[i] = trueBoneDistance;

                distanceBuffer[i] = boneDistance;

                if (boneDistance < bestPrefDist)
                {
                    bestPrefDist = boneDistance;
                }
            }


            for (int i = nextBone; i < lastBone; i++)
            {
                // Always record whatever our current target is, so that we have good data to use for (red) bone model display.
                if (distanceBuffer[i] > bestPrefDist * secondaryCandidateMult && targetIndex != i)
                {
                    distanceBuffer[i] = -1;
                } else
                {
                    // Transform to bone coordinates
                    boneLengthBuffer[i] = Quaternion.Inverse(bone_rotations[i]) * boneLengthBuffer[i];
                }
            }

            boneHeap._a_UpdateBatch(currentlyScanningTarget, nextBone, lastBone, distanceBuffer, boneLengthBuffer, boneTrueDistanceBuffer);

            if (lastBone == nBones)
            {
                currentlyScanningTarget = null;
                nextBone = 0;
                //Debug.Log($"=== ContinueBoneScan: completed");

            }
            else
            {
                //Debug.Log($"=== ContinueBoneScan: nextBone: {nextBone} => {lastBone}");
                nextBone = lastBone;
            }
        }

#endregion

#region Held controls

        float closestBoneDistance;

        void DisplayBoneModel()
        {
            float trueBoneDistance = boneHeap._a_GetBoneTrueDistance(targetPlayerId, targetBoneId);

            if (!LoadSingleBoneData(VRCPlayerApi.GetPlayerById(targetPlayerId), targetBoneId))
            {
                // Invalid selection
                t_boneModelRoot.gameObject.SetActive(false);
                t_traceMarker.gameObject.SetActive(false);
                tracking = false;

                return;
            }

            Vector3 boneLengthVec = boneHeap._a_GetBoneLengthVector(targetPlayerId, targetBoneId);

            // Distance is needed to show out-of-range warning
            t_boneModelRoot.position = singleBonePos;

            Vector3 traceTarget = singleBonePos;

            // yes this is float equality comparison, I know what I'm doing (usually)
            if (boneLengthVec.sqrMagnitude == 0)
            {
                t_boneModelRoot.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                t_boneModelRoot.rotation = Quaternion.identity;
                obj_boneModelBody.SetActive(false);
            }
            else
            {
                boneLengthVec = singleBoneRot * boneLengthVec;
                t_boneModelRoot.localScale = Vector3.one * boneLengthVec.magnitude;
                t_boneModelRoot.rotation = Quaternion.LookRotation(boneLengthVec);
                obj_boneModelBody.SetActive(true);

                traceTarget += boneLengthVec * 0.5f;
            }

            t_boneModelRoot.gameObject.SetActive(true);

            Vector3 traceSource = t_attachmentDirection.position;
            t_traceMarker.position = traceSource;
            t_traceMarker.rotation = Quaternion.LookRotation(traceTarget - traceSource);
            t_traceMarker.localScale =
                transform.lossyScale.magnitude * Vector3.Distance(traceSource, traceTarget) * new Vector3(0.5f, 0.5f, 0.5f);
            t_traceMarker.gameObject.SetActive(true);

            Color color = Color.blue;
            if (tracking)
            {
                if (trueBoneDistance >= range || trueBoneDistance < 0)
                {
                    color = Color.red;
                }
                else
                {
                    color = Color.green;
                }
            }

            mat_bone.SetColor("_Color", color);
            mat_sphere.SetColor("_Color", color);
        }

        int noValidBoneFrames = 0;

        void UpdateHeld()
        {
            CheckInput();

            // If we've held the trigger for long enough, release tracking
            if (tracking && triggerDownTime >= 0 && triggerDownTime + 3.0f < Time.timeSinceLevelLoad)
            {
                tracking = false;
            }

            if (lastBoneAdvance >= 0 && lastBoneAdvance + 5.0f < Time.timeSinceLevelLoad)
            {
                lastBoneAdvance = -1;
                finalBoneId = finalPlayerId = -1;
                boneHeap._a_ClearBlocklist();
            }

            // Perform a bone update step
            ScanNextPlayer();

            // Reset blocklist if we've gone too far without a valid bone
            if (boneHeap._a_ValidBones() == 0)
            {
                noValidBoneFrames++;
                if (noValidBoneFrames > VRCPlayerApi.GetPlayerCount())
                {
                    boneHeap._a_ClearBlocklist();
                    noValidBoneFrames = -999999;
                }
            } else
            {
                noValidBoneFrames = 0;
            }

            if (!tracking)
            {
                targetBoneId = boneHeap.bestBoneId;
                targetPlayerId = boneHeap.bestPlayerId;
            }

            DisplayBoneModel();

        }

        /* BoneScan(player); */
        int finalPlayerId = -1;
        int finalBoneId = -1;

        void NextBone()
        {
            if (targetBoneId == -1) return;

            lastBoneAdvance = Time.timeSinceLevelLoad;

            boneHeap._a_ForbidBone(targetPlayerId, targetBoneId);
            if (finalPlayerId != -1 && finalBoneId != -1 && boneHeap._a_GetBoneTrueDistance(finalPlayerId, finalBoneId) >= 0)
            {
                // We have the last bone left over from the prior scan, select and forbid it
                targetPlayerId = finalPlayerId;
                targetBoneId = finalBoneId;
            } else if (boneHeap.bestBoneId != -1)
            {
                targetPlayerId = boneHeap.bestPlayerId;
                targetBoneId = boneHeap.bestBoneId;
            }

            if (boneHeap._a_ValidBones() < 2)
            {
                // Clear blocklist and let new candidates load while the player decides whether to pull the trigger again
                finalBoneId = boneHeap.bestBoneId;
                finalPlayerId = boneHeap.bestPlayerId;

                boneHeap._a_ClearBlocklist();
            }

            if (targetPlayerId == -1)
            {
                tracking = false;
            }
        }

        void _a_OnTriggerChanged(bool boneSelectTrigger, bool prior)
        {
            if (prior) return; // only trigger on false -> true transition
            if (lastPickup + 0.25f > Time.timeSinceLevelLoad) return; // suppress input for a moment

            if (boneSelectTrigger)
            {
                onHold._a_OnBoneSelect();

                // Lock to bone
                if (targetBoneId >= 0)
                {
                    if (tracking)
                    {
                        NextBone();
                    }
                    else
                    {
                        tracking = true;
                    }
                }
                lastBoneAdvance = Time.timeSinceLevelLoad;
            }
            else
            {
                if (tracking || targetPlayerId >= 0) onHold._a_OnPlayerSelect();

                tracking = false;
                targetBoneId = -1;

                boneHeap._a_ForbidPlayer(targetPlayerId);

                if (boneHeap._a_ValidBones() == 0)
                {
                    boneHeap._a_ClearBlocklist();
                }

                targetPlayerId = -1;
            }
        }

        bool[] wasHeld = new bool[2];
        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (!Networking.LocalPlayer.IsUserInVR()) return;
            if (!CheckInit()) return;

            int index = args.handType == HandType.LEFT ? 0 : 1;
            bool heldInLeft = currentHand == VRC_Pickup.PickupHand.Left;
            bool sameHand = heldInLeft == (args.handType == HandType.LEFT);

            if (lastDrop + 0.25f > Time.timeSinceLevelLoad) return;

            if (activeAttachable != null && wasHeld[index] != value)
            {
                _a_OnTriggerChanged(sameHand, !value);

                if (sameHand)
                {
                    triggerDownTime = value ? Time.timeSinceLevelLoad : -1;
                }
            }

            wasHeld[index] = value;
        }

        // We need to use an input loop as udon input events don't support middle mouse
        bool mouse_boneSelectWasHeld, mouse_userSelectWasHeld;

        void CheckInput()
        {
            bool boneSelectTrigger, userSelectTrigger;

            if (Networking.LocalPlayer.IsUserInVR())
            {
                return;
            }
            else
            {
                boneSelectTrigger = Input.GetMouseButton(0);
                userSelectTrigger = Input.GetMouseButton(2);
            }

            if (mouse_boneSelectWasHeld != boneSelectTrigger)
            {
                _a_OnTriggerChanged(true, mouse_boneSelectWasHeld);
                mouse_boneSelectWasHeld = boneSelectTrigger;
                triggerDownTime = boneSelectTrigger ? Time.timeSinceLevelLoad : -1;
            }

            if (mouse_userSelectWasHeld != userSelectTrigger)
            {
                _a_OnTriggerChanged(false, mouse_userSelectWasHeld);
                mouse_userSelectWasHeld = userSelectTrigger;
            }
        }

#endregion
    }
}
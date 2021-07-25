
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace net.fushizen.attachable {
    /// <summary>
    /// Handles searching and selecting candidate bones while an attachable is held.
    /// </summary>
    [DefaultExecutionOrder(0)]
    public class AttachableBoneSelection : UdonSharpBehaviour
    {
        // TODO: In OnOwnershipTransferred we cleared closestBoneDistance; see if this logic needs forward porting.


        readonly float RETAIN_CYCLE_TIME = 4.0f;

        /// <summary>
        /// Additional range (in addition to the bone range) for searching for players; beyond this distance
        /// (to capsule root) we'll skip the expensive bone analysis.
        /// </summary>
        private float PLAYER_LEEWAY = 2.0f;

        [HideInInspector]
        public int _a_trackingPlayer, _a_trackingBone;

        void Start()
        {
            SetupReferences();
            InitBoneData();

            enabled = false;
            targetPlayerId = targetBoneId = -1;
        }

        #region Global references

        AttachableBoneData boneData;
        AttachablesGlobalOnHold onHold;
        AttachablesGlobalTracking globalTracking;

        float globalTrackingScale;

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

        void SetupReferences()
        {
            boneData = GetComponent<AttachableBoneData>();
            globalTracking = GetComponent<AttachablesGlobalTracking>();
            onHold = GetComponent<AttachablesGlobalOnHold>();

            globalTrackingScale = transform.localScale.x;
            t_traceMarker = transform.Find("TraceMarker");
            t_boneModelRoot = transform.Find("BoneMarkerRoot");
            obj_boneModelBody = t_boneModelRoot.Find("boneMarker/Bone").gameObject;

            mat_bone = obj_boneModelBody.transform.GetComponent<MeshRenderer>().sharedMaterial;
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

        Attachable activeAttachable;
        Attachable lastAttachable;
        float lastPickup;
        float lastBoneAdvance;

        float triggerDownTime;

        // Values copied from the attachable
        bool disableFingerSelection;
        Transform t_attachmentDirection;
        float range, directionality;
        VRC_Pickup.PickupHand currentHand;
        bool preferSelf;

        int targetPlayerId, targetBoneId;
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

        // Returns the estimated distance to a bone, or -1 if not targetable
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

            var sameAsLast = activeAttachable == lastAttachable;

            activeAttachable = attachable;
            currentHand = hand;
            enabled = true; // start update loop and input monitoring

            t_attachmentDirection = attachable.t_attachmentDirection;
            this.range = attachable.range;
            this.directionality = attachable.directionality;
            preferSelf = attachable.preferSelf;

            targetPlayerId = curPlayer;
            targetBoneId = curBone;

            tracking = false;
            if (targetPlayerId >= 0 && TargetPlayerValid())
            {
                if (targetBoneId >= 0)
                {
                    tracking = true;
                }

                if (!tracking || !sameAsLast || lastPickup <= Time.timeSinceLevelLoad + RETAIN_CYCLE_TIME)
                {
                    // Enable tracking, but restart the bone selection cycle.
                    var player = VRCPlayerApi.GetPlayerById(targetPlayerId);
                    BoneScan(player);
                }
            }

            // Suppress the desktop trigger-press-on-pickup
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

            if (tracking)
            {
                // Confirm that the tracking target is valid.
                var player = VRCPlayerApi.GetPlayerById(targetPlayerId);

                tracking = Utilities.IsValid(player) && player != null
                    && DistanceToBone(player, targetBoneId) && trueBoneDistance < range;
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
            lastPickup = Time.timeSinceLevelLoad;
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
                }
                else
                {
                    break;
                }
            }
        }

        int HeapPop()
        {

            if (prefBoneLength == 0)
                return -1;

            // pop and swap max to end
            int i = --prefBoneLength;

            HeapSwap(0, i);

            // restore heap
            float d = prefBoneDistances[0];
            for (int j = 0; j < i;)
            {
                int c1 = (j << 1) + 1;
                int c2 = c1 + 1;

                float d1 = c1 < i ? prefBoneDistances[c1] : 99999;
                float d2 = c2 < i ? prefBoneDistances[c2] : 99999;

                if (d1 < d && d1 < d2) // move higher parent to the smaller child
                {
                    HeapSwap(j, c1);
                    j = c1;
                }
                else if (d2 < d)
                {
                    HeapSwap(j, c2);
                    j = c2;
                }
                else
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

            return Vector3.Distance(player.GetPosition(), t_attachmentDirection.position) <= range + PLAYER_LEEWAY;
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

            var pickupPos = t_attachmentDirection.position;

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
                Vector3 traceSource = t_attachmentDirection.position;
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
                    }
                    else
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

        void UpdateHeld()
        {
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
            if (tracking && triggerDownTime >= 0 && triggerDownTime + 3.0f < Time.timeSinceLevelLoad)
            {
                tracking = false;
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

            if (lastBoneAdvance + 5.0f < Time.timeSinceLevelLoad)
            {
                // Perform new scan
                BoneScan(player);

                if (prefBoneLength < 1)
                {
                    tracking = false;
                    targetBoneId = -1;
                }
                else
                {
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

                // Change tracking player
                if (targetPlayerId < 0) return;

                onHold._a_OnPlayerSelect();

                targetPlayerId = FindPlayer();

            }
        }

        bool[] wasHeld = new bool[2];
        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            if (!Networking.LocalPlayer.IsUserInVR()) return;

            int index = args.handType == HandType.LEFT ? 0 : 1;
            bool heldInLeft = currentHand == VRC_Pickup.PickupHand.Left;
            bool sameHand = heldInLeft == (args.handType == HandType.LEFT);

            if (lastPickup + 0.25f > Time.timeSinceLevelLoad) return;

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
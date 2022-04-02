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

namespace net.fushizen.attachable
{

    /// <summary>
    /// This class implements a tracking datastructure for candidate player/bones tuples.
    /// 
    /// We organize this data as a min-heap by the preference distance metric ("distance" for the purposes of this class).
    /// Each element is associated with an ID (consisting of a combined "player slot ID" and bone ID) and the distance metric.
    /// 
    /// Internally we map VRChat player IDs to a "player slot ID"; this is because we want to reuse indices to avoid the values
    /// growing large and requiring tracking arrays to be similarly large.
    /// 
    /// As the caller scans for bones, it will present them in bulk to update their elements in the heap - potentially
    /// resulting in sift-up or sift-down operations. As bones are selected with trigger, we'll mark those bone IDs as
    /// forbidden and skip them on future updates.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class AttachableInternalBoneHeap : UdonSharpBehaviour
    {
        int boneCount;

        public void _a_CheckInit()
        {
            if (slotToPlayerId != null) return;

            boneCount = GetComponent<AttachableBoneData>().bone_targets.Length;

            slotToPlayerId = new int[4];
            for (int i = 0; i < slotToPlayerId.Length; i++)
            {
                slotToPlayerId[i] = -1;
            }

            _a_Reset();
        }

        private void Start()
        {
            _a_CheckInit();
        }

        #region Public API

        [HideInInspector]
        public int bestPlayerId, bestBoneId;

        public void _a_Reset()
        {
            FilterReset();
            HeapReset();

            bestBoneId = bestPlayerId = -1;
        }
        
        public void _a_ClearBlocklist()
        {
            FilterReset();
        }

        public void _a_UpdateBatch(VRCPlayerApi player, int startBone, int endBone, float[] boneDistances, Vector3[] boneLengthVectors, float[] boneTrueDistances)
        {
            int slot = PlayerToSlot(player);
            if (slot < 0)
            {
                Debug.Log("No slot assigned for player");
            }

            EnsureCapacity(slot);

            int startIndex = slot * boneCount;
            int count = boneDistances.Length;

            int nDel = 0, nIns = 0;

            for (int i = startBone; i < endBone; i++)
            {
                int selector = startIndex + i;
                var distance = boneDistances[i];

                if (distance < 0 || (boneIsFiltered.Length > selector && boneIsFiltered[selector]))
                {
                    HeapDelete(selector);
                    nDel++;
                } else {
                    HeapInsert(selector, distance);
                    nIns++;
                }
                //CheckHeapIntegrity();
            }

            int nBones = endBone - startBone;
            System.Array.Copy(boneLengthVectors, startBone, selectorToBoneLength, startIndex + startBone, nBones);
            System.Array.Copy(boneTrueDistances, startBone, selectorToBoneTrueDistance, startIndex + startBone, nBones);

            UpdateBestResult();
        }

        void CheckHeapIntegrity()
        {
            bool fault = false;

            bool[] selectorsUsed = new bool[selectorToHeapIndex.Length];

            // Verify cross-reference integrity
            for (int i = 0; i < maxAllocatedElement; i++)
            {
                if (elementIds[i] < 0 || elementIds[i] >= selectorToHeapIndex.Length)
                {
                    Debug.LogWarning($"elementIds[{i}] = {elementIds[i]} which is out of range 0..{selectorToHeapIndex.Length - 1}");
                    fault = true;
                    continue;
                }

                if (selectorsUsed[elementIds[i]])
                {
                    Debug.LogWarning($"Multiple references to selector {elementIds[i]}");
                    fault = true;
                }
                selectorsUsed[elementIds[i]] = true;

                var selIndex = selectorToHeapIndex[elementIds[i]];

                if (selIndex != i + 1)
                {
                    Debug.LogWarning($"Cross-reference incorrect: elementId[{i}] => selectorToHeapIndex[{elementIds[i]}] = {selectorToHeapIndex[elementIds[i]]} but should be {i + 1}"); ;
                    fault = true;
                }
            }

            // Verify all unused selectors are unmapped
            for (int i = 0; i < selectorToHeapIndex.Length; i++)
            {
                if (selectorsUsed[i] != (selectorToHeapIndex[i] != 0))
                {
                    Debug.LogWarning($"Selector unused flag mismatch: selectorsUsed[i]={selectorsUsed[i]} STHI[{i}]={selectorToHeapIndex[i]}");
                    var index = selectorToHeapIndex[i] - 1;
                    if (index >= 0 && index < maxAllocatedElement)
                    {
                        Debug.LogWarning($"  => Backreference: elementIds[{index}] = elementIds[{index}]");
                    }

                    fault = true;
                }
            }

            // Verify heap is in min-heap order
            for (int i = 1; i < maxAllocatedElement; i++)
            {
                int parent = (i - 1) >> 1;
                float childDist = elementDistance[i];
                float parentDist = elementDistance[parent];

                if (parentDist > childDist)
                {
                    Debug.LogWarning($"Heap condition violated: dist[{parent}] = {parentDist:F2} > dist[{i}] = {childDist:F2}");
                    fault = true;
                }
            }

            if (fault)
            {
                Debug.Log($"Halting execution; maxAllocatedElement={maxAllocatedElement}");

                // Halt execution
                elementIds[-1] = 0;
            }
        }

        public void _a_ForbidBone(int playerId, int boneId)
        {
            int slot = PlayerIdToSlot(playerId);
            if (slot < 0) return;

            int selector = slot * boneCount + boneId;

            if (selector < selectorToBoneLength.Length)
            {
                HeapDelete(selector);
            }
            BlockBone(selector);

            Debug.Log($"After forbidding selector {selector}, remaining count is {maxAllocatedElement} and best is {elementIds[0]}");

            UpdateBestResult();
        }

        public void _a_ForbidPlayer(int playerId)
        {
            int slot = PlayerIdToSlot(playerId);
            if (slot < 0) return;

            int baseSelector = slot * boneCount;
            EnsureCapacity(slot);

            for (int i = 0; i < boneCount; i++)
            {
                int selector = baseSelector + i;
                HeapDelete(selector);
                BlockBone(selector);
            }

            UpdateBestResult();
        }

        public void _a_ClearPlayer(VRCPlayerApi player)
        {
            int slot = PlayerIdToSlot(player.playerId);
            if (slot < 0) return;

            int baseSelector = slot * boneCount;
            EnsureCapacity(slot);

            for (int i = 0; i < boneCount; i++)
            {
                int selector = baseSelector + i;
                HeapDelete(selector);

                if (selector < boneIsFiltered.Length)
                {
                    boneIsFiltered[selector] = false;
                }
            }
        }

        public float _a_GetBoneDistance(int playerId, int boneId)
        {
            int slot = PlayerIdToSlot(playerId);
            if (slot < 0) return -1;

            int selector = slot * boneCount + boneId;

            if (selector >= selectorToHeapIndex.Length) return -1;

            int heapIndex = selectorToHeapIndex[selector];
            if (heapIndex == 0) return -1;

            return elementDistance[heapIndex];
        }

        public Vector3 _a_GetBoneLengthVector(int playerId, int boneId)
        {
            int slot = PlayerIdToSlot(playerId);
            if (slot < 0) return Vector3.zero;

            int selector = slot * boneCount + boneId;

            if (selector >= selectorToHeapIndex.Length) return Vector3.zero;

            return selectorToBoneLength[selector];
        }

        public float _a_GetBoneTrueDistance(int playerId, int boneId)
        {
            int slot = PlayerIdToSlot(playerId);
            if (slot < 0) return -1;

            int selector = slot * boneCount + boneId;

            if (selector >= selectorToHeapIndex.Length || selectorToHeapIndex[selector] == 0) return -1;

            return selectorToBoneTrueDistance[selector];
        }

        public float _a_BestBoneDistance()
        {
            if (maxAllocatedElement == 0) return 99999;
            return elementDistance[0];
        }

        public int _a_ValidBones()
        {
            return maxAllocatedElement;
        }

        void UpdateBestResult()
        {
            if (maxAllocatedElement == 0)
            {
                bestBoneId = bestPlayerId = -1;
            }

            int selector = elementIds[0];
            int bone = selector % boneCount;
            int playerSlot = selector / boneCount;

            VRCPlayerApi player = SlotToPlayer(playerSlot);

            if (player == null)
            {
                // Whoops, player is gone...
                bestBoneId = bestPlayerId = -1;
            }
            else
            {
                bestBoneId = bone;
                bestPlayerId = player.playerId;
            }
        }

        #endregion

        #region Bone filter

        bool[] boneIsFiltered;

        void FilterReset()
        {
            boneIsFiltered = new bool[0];
        }

        void BlockBone(int selector)
        {
            int newCount = boneIsFiltered.Length;
            if (newCount == 0) newCount = boneCount;

            while (selector >= newCount)
            {
                newCount = (int)(newCount * 1.5);
            }

            if (newCount != boneIsFiltered.Length)
            {
                var newArray = new bool[newCount];
                System.Array.Copy(boneIsFiltered, newArray, boneIsFiltered.Length);

                boneIsFiltered = newArray;
            }

            boneIsFiltered[selector] = true;
        }

        #endregion

        #region Player ID mapping

        readonly string SLOT_TAG = "net.fushizen.attachables.AttachableInternalBoneHeap.PlayerSlot";

        int[] slotToPlayerId;

        int FindOpenSlot()
        {
            for (int i = 0; i < slotToPlayerId.Length; i++)
            {
                if (slotToPlayerId[i] == -1)
                {
                    return i;
                }
            }

            // Allocate more space
            var newArray = new int[(int)(slotToPlayerId.Length * 1.5)];

            System.Array.Copy(slotToPlayerId, newArray, slotToPlayerId.Length);

            var newIndex = slotToPlayerId.Length;

            for (int i = newIndex; i < newArray.Length; i++)
            {
                newArray[i] = -1;
            }

            slotToPlayerId = newArray;

            return newIndex;
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            _a_CheckInit();

            // Map the new player to a slot
            int newPlayerSlot = FindOpenSlot();

            // Write this to a player tag
            // This string building process is slow but only needed on player join...
            char[] c = new char[] { (char)newPlayerSlot };
            string s = new string(c);

            player.SetPlayerTag(SLOT_TAG, s);
            slotToPlayerId[newPlayerSlot] = player.playerId;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (slotToPlayerId == null || !Utilities.IsValid(player)) return;

            // Player tags are cleared before entering this function, so search by ID instead...
            int slot = System.Array.IndexOf(slotToPlayerId, player.playerId);

            if (slot >= 0)
            {
                slotToPlayerId[slot] = -1;

                int selectorBase = slot * boneCount;
                for (int i = 0; i < boneCount; i++)
                {
                    var selector = selectorBase + i;

                    if (selector < selectorToHeapIndex.Length)
                    {
                        HeapDelete(selector);
                    }

                    if (selector < boneIsFiltered.Length)
                    {
                        boneIsFiltered[selector] = false;
                    }
                }
            }
        }

        int PlayerIdToSlot(int playerId)
        {
            var player = VRCPlayerApi.GetPlayerById(playerId);

            if (player == null || !Utilities.IsValid(player)) return -1;

            return PlayerToSlot(player);
        }

        int PlayerToSlot(VRCPlayerApi player)
        {
            string s = player.GetPlayerTag(SLOT_TAG);

            if (s.Length == 1)
            {
                return (int)s[0];
            }
            else
            {
                return -1;
            }
        }

        VRCPlayerApi SlotToPlayer(int slot)
        {
            if (slot < 0 || slot >= slotToPlayerId.Length) return null;

            int playerId = slotToPlayerId[slot];

            if (playerId < 0) return null;

            return VRCPlayerApi.GetPlayerById(playerId);
        }

        #endregion

        #region Heap operations 
        
        int[] elementIds;
        float[] elementDistance;
        int maxAllocatedElement;

        /// <summary>
        /// Maps from a selector (playerSlot * boneCount +_boneIndex) to the index in elementIds corresponding to that selector _plus one_.
        /// Zero indicates a selector not present in the heap.
        /// 
        /// </summary>
        int[] selectorToHeapIndex;

        /// <summary>
        /// Contains "bone length vectors" for all selectors in the heap. These are vectors, in the bone's local translation and rotation space,
        /// pointing to the child bone (or zero length if there is no child bone), and are used to render the bone selection model.
        /// 
        /// The contents of slots not in the heap are not guaranteed to be valid.
        /// </summary>
        Vector3[] selectorToBoneLength;

        /// <summary>
        /// Contains the true (unadjusted) distance to each inserted selector.
        /// 
        /// The contents of slots not in the heap are not guaranteed to be valid.
        /// </summary>
        float[] selectorToBoneTrueDistance;

        void HeapReset()
        {
            elementIds = new int[1];
            elementDistance = new float[1];
            maxAllocatedElement = 0;
            selectorToHeapIndex = new int[0];
            selectorToBoneLength = new Vector3[1];
            selectorToBoneTrueDistance = new float[1];
        }

        void EnsureCapacity(int playerSlot)
        {
            int maxSelectorIndex = (playerSlot + 1) * boneCount;

            if (selectorToHeapIndex.Length < maxSelectorIndex)
            {
                var newArray = new int[maxSelectorIndex];
                System.Array.Copy(selectorToHeapIndex, newArray, selectorToHeapIndex.Length);
                selectorToHeapIndex = newArray;

                var newBoneLengthArray = new Vector3[maxSelectorIndex];
                System.Array.Copy(selectorToBoneLength, newBoneLengthArray, selectorToBoneLength.Length);
                selectorToBoneLength = newBoneLengthArray;


                var newBoneTrueDistArray = new float[maxSelectorIndex];
                System.Array.Copy(selectorToBoneTrueDistance, newBoneTrueDistArray, selectorToBoneTrueDistance.Length);
                selectorToBoneTrueDistance = newBoneTrueDistArray;
            }

            if (maxAllocatedElement + boneCount > elementIds.Length)
            {
                var newArray = new int[maxAllocatedElement + boneCount * 2];
                System.Array.Copy(elementIds, newArray, elementIds.Length);

                var newDistArray = new float[newArray.Length];
                System.Array.Copy(elementDistance, newDistArray, elementDistance.Length);

                elementIds = newArray;
                elementDistance = newDistArray;

            }
        }

        /// <summary>
        /// Inserts a single element, overwriting it if already present, then repairs the heap condition.
        /// Does not consider the bone blocklist.
        /// </summary>
        /// <param name="selector">Player+bone selector</param>
        /// <param name="distance">Distance</param>
        void HeapInsert(int selector, float distance)
        {
            var heapIndex = selectorToHeapIndex[selector];
            if (heapIndex == 0)
            {
                heapIndex = maxAllocatedElement++;
                elementIds[heapIndex] = selector;
                selectorToHeapIndex[selector] = heapIndex + 1;
            }
            else
            {
                heapIndex--; // selectorToHeapIndex is offset by one
            }

            elementDistance[heapIndex] = distance;
            RepairHeap(heapIndex);
            return;
        }

        void HeapDelete(int selector)
        {
            var heapIndex = selectorToHeapIndex[selector];

            if (heapIndex == 0) return;

            heapIndex -= 1;

            // Swap with final element
            int lastElement = --maxAllocatedElement;
            if (lastElement != heapIndex)
            {
                HeapSwap(lastElement, heapIndex);
                RepairHeap(heapIndex);
            }

            selectorToHeapIndex[selector] = 0;
        }
        void HeapSwap(int a, int b)
        {
            var elemA = elementIds[a];
            var distA = elementDistance[a];
            var elemB = elementIds[b];

            selectorToHeapIndex[elemA] = b + 1;
            selectorToHeapIndex[elemB] = a + 1;

            elementIds[a] = elemB;
            elementDistance[a] = elementDistance[b];

            elementIds[b] = elemA;
            elementDistance[b] = distA;
        }

        void RepairHeap(int heapIndex)
        {
            // parent: (heapIndex - 1) >> 1
            // child: (heapIndex << 1) + 1/2

            if (heapIndex > 0)
            {
                // Check parent
                int parent = (heapIndex - 1) >> 1;

                if (elementDistance[parent] > elementDistance[heapIndex])
                {
                    SiftUp(heapIndex);
                    return;
                }
            }

            SiftDown(heapIndex);
        }

        void SiftUp(int heapIndex)
        {
            while (heapIndex > 0)
            {
                int parent = (heapIndex - 1) >> 1;

                if (elementDistance[parent] > elementDistance[heapIndex])
                {
                    HeapSwap(heapIndex, parent);
                    heapIndex = parent;
                }
                else
                {
                    return;
                }
            }
        }

        void SiftDown(int heapIndex)
        {
            while (true)
            {
                int child1 = (heapIndex << 1) + 1;
                int child2 = child1 + 1;

                if (child1 >= maxAllocatedElement) return;

                var distance = elementDistance[heapIndex];
                var dist1 = elementDistance[child1];

                if (dist1 < distance)
                {
                    if (child2 >= maxAllocatedElement || dist1 < elementDistance[child2])
                    {
                        // child1 has the smallest element
                        HeapSwap(child1, heapIndex);
                        heapIndex = child1;
                    }
                    else
                    {
                        // child2 has the smallest element
                        HeapSwap(child2, heapIndex);
                        heapIndex = child2;
                    }
                }
                else if (child2 < maxAllocatedElement && elementDistance[child2] < distance)
                {
                    HeapSwap(child2, heapIndex);
                    heapIndex = child2;
                }
                else
                {
                    return;
                }
            }
        }

        #endregion
    }
}
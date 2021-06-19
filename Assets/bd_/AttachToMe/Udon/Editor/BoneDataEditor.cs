using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Assets.bd_.AttachToMe.Udon
{
    class BoneDataEditor : MonoBehaviour
    {
        [MenuItem("bd_/DumpBones")]
        static void dumpBones()
        {
            HumanBodyBones[] bones = (HumanBodyBones[])System.Enum.GetValues(typeof(HumanBodyBones));

            string s = "";
            int j = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                if (i >= 19 && i < 24) continue;
                s += $"bone_targets[last = {j++}] = HumanBodyBones.{bones[i]}\n";
            }

            Debug.Log(s);
        }
    }
}

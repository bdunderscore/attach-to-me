using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class EditorTest : MonoBehaviour
{
    public Transform ray;
    public Transform target;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    // pseudo-ref parameters
    float boneDistance;
    Vector3 nearestPointBone, nearestPointRay;

    // Returns the estimated distance to a bone, or -1 if not targetable
    internal bool DistanceToBone(Vector3 raySource, Vector3 rayDirection, int targetIndex)
    {
        Vector3 one = new Vector3(1, 1, 1);

        // r2, e2
        Vector3 bonePos = target.position;
        Vector3 boneDirection = target.TransformDirection(Vector3.forward);

        // https://homepage.univie.ac.at/franz.vesely/notes/hard_sticks/hst/hst.html

        Vector3 r12 = bonePos - raySource;
        float dot_e1 = Vector3.Dot(r12, rayDirection);
        float dot_e2 = Vector3.Dot(r12, boneDirection);
        float dotDirections = Vector3.Dot(rayDirection, boneDirection);
        float divisor = (1 - dotDirections * dotDirections);

        if (divisor < 0.001) return false;

        float lambda = (dot_e1 - dot_e2 * dotDirections) / divisor;
        float mu = (dot_e2 - dot_e1 * dotDirections) / divisor;

        nearestPointBone = bonePos - mu * boneDirection; // why is this negative???
        nearestPointRay = raySource + lambda * rayDirection;

        boneDistance = Vector3.Distance(nearestPointRay, nearestPointBone);

        return true;
    }

    private void OnDrawGizmos()
    {
        if (target == null || ray == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawLine(target.position, target.TransformPoint(Vector3.forward));
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(ray.position, ray.TransformDirection(Vector3.forward));

        if (DistanceToBone(ray.position, ray.TransformVector(Vector3.forward), 0)) {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(nearestPointRay, nearestPointBone);

            Debug.Log($"Distance: {boneDistance}");
        }
    }
}

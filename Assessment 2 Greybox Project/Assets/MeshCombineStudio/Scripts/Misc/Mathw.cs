using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshCombineStudio
{
    public class AABB3
    {
        public Vector3 min;
        public Vector3 max;

        public AABB3(Vector3 min, Vector3 max)
        {
            this.min = min;
            this.max = max;
        }
    }

    public class Sphere3
    {
        public Vector3 center;
        public float radius;

        public Sphere3() { }

        public Sphere3(Vector3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }
    }

    static public class Mathw
    {
        static public bool IntersectAABB3Sphere3(AABB3 box, Sphere3 sphere)
        {
            Vector3 center = sphere.center;
            Vector3 min = box.min;
            Vector3 max = box.max;
            float totalDistance = 0f;
            float distance;
            if (center.x < min.x)
            {
                distance = center.x - min.x;
                totalDistance += distance * distance;
            }
            else if (center.x > max.x)
            {
                distance = center.x - max.x;
                totalDistance += distance * distance;
            }
            if (center.y < min.y)
            {
                distance = center.y - min.y;
                totalDistance += distance * distance;
            }
            else if (center.y > max.y)
            {
                distance = center.y - max.y;
                totalDistance += distance * distance;
            }
            if (center.z < min.z)
            {
                distance = center.z - min.z;
                totalDistance += distance * distance;
            }
            else if (center.z > max.z)
            {
                distance = center.z - max.z;
                totalDistance += distance * distance;
            }
            return totalDistance <= sphere.radius * sphere.radius;
        }
    }
}
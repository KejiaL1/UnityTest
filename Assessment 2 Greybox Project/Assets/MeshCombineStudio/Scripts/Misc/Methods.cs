using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Reflection;

namespace MeshCombineStudio
{
    static public class Methods
    {
        static public Vector3 Clamp(Vector3 v, float min, float max)
        {
            if (v.x < min) v.x = min;
            else if (v.x > max) v.x = max;
            if (v.y < min) v.y = min;
            else if (v.y > max) v.y = max;
            if (v.z < min) v.z = min;
            else if (v.z > max) v.z = max;
            return v;
        }

        static public Vector3 FloatToVector3(float v)
        {
            return new Vector3(v, v, v);
        }

        static public float SinDeg(float angle)
        {
            return Mathf.Sin(angle * Mathf.Deg2Rad);// * Mathf.Rad2Deg;
        }
       
        static public void SetTag(GameObject go, string tag)
        {
            Transform[] tArray = go.GetComponentsInChildren<Transform>();
            for (int i = 0; i < tArray.Length; i++) { tArray[i].tag = tag; }
        }

        static public void SetTagWhenCollider(GameObject go, string tag)
        {
            Transform[] tArray = go.GetComponentsInChildren<Transform>();
            for (int i = 0; i < tArray.Length; i++)
            {
                if (tArray[i].GetComponent<Collider>() != null) tArray[i].tag = tag;
            }
        }

        static public void SetTagAndLayer(GameObject go, string tag, int layer)
        {
            // Debug.Log("Layer " + layer);
            Transform[] tArray = go.GetComponentsInChildren<Transform>();
            for (int i = 0; i < tArray.Length; i++) { tArray[i].tag = tag; tArray[i].gameObject.layer = layer; }
        }

        static public void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            Transform[] tArray = go.GetComponentsInChildren<Transform>();
            for (int i = 0; i < tArray.Length; i++) tArray[i].gameObject.layer = layer;
        }

        static public bool Contains(string compare, string name)
        {
            List<string> cuts = new List<string>();
            int index;

            do
            {
                index = name.IndexOf("*");

                if (index != -1)
                {
                    if (index != 0) { cuts.Add(name.Substring(0, index)); }
                    if (index != name.Length - 1) { name = name.Substring(index + 1); }
                    else break;
                }
            }
            while (index != -1);

            cuts.Add(name);

            for (int i = 0; i < cuts.Count; i++)
            {
                //Debug.Log(cuts.items[i] +" " + compare);
                if (!compare.Contains(cuts[i])) return false;
            }
            //Debug.Log("Passed");
            return true;
        }

        static public T[] Search<T>(GameObject parentGO = null)
        {
            GameObject[] gos = null;
            if (parentGO == null) {
                #if !UNITY_5_1 && !UNITY_5_2
                gos = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
                #endif
            }

            else gos = new GameObject[] { parentGO };

            if (gos == null) return null;

            if (typeof(T) == typeof(GameObject))
            {
                List<GameObject> list = new List<GameObject>();
                for (int i = 0; i < gos.Length; i++)
                {
                    Transform[] transforms = gos[i].GetComponentsInChildren<Transform>(true);
                    for (int j = 0; j < transforms.Length; j++) list.Add(transforms[j].gameObject);
                }
                return list.ToArray() as T[];
            }
            else
            {
                if (parentGO == null)
                {
                    List<T> list = new List<T>();
                    for (int i = 0; i < gos.Length; i++)
                    {
                        list.AddRange(gos[i].GetComponentsInChildren<T>(true));
                    }
                    return list.ToArray();
                }
                else return parentGO.GetComponentsInChildren<T>(true);
            }
        }

        static public T Find<T>(GameObject parentGO, string name) where T : UnityEngine.Object
        {
            T[] gos = Search<T>(parentGO);

            for (int i = 0; i < gos.Length; i++)
            {
                if (gos[i].name == name) return gos[i];
            }
            return null;
        }

        static public void SetCollidersActive(Collider[] colliders, bool active, string[] nameList)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                for (int j = 0; j < nameList.Length; j++)
                {
                    if (colliders[i].name.Contains(nameList[j])) colliders[i].enabled = active;
                }
            }
        }

        static public void SelectChildrenWithMeshRenderer(Transform t)
        {
            #if UNITY_EDITOR
            MeshRenderer[] mrs = t.GetComponentsInChildren<MeshRenderer>();

            GameObject[] gos = new GameObject[mrs.Length];

            for (int i = 0; i < mrs.Length; i++) gos[i] = mrs[i].gameObject;

            UnityEditor.Selection.objects = gos;
            #endif
        }

        static public void DestroyChildren(Transform t)
        {
            while (t.childCount > 0)
            {
                Transform child = t.GetChild(0);
                child.parent = null;
                GameObject.DestroyImmediate(child.gameObject);
            }
        }

        static public void Destroy(GameObject go)
        {
            if (go == null) return;

            #if UNITY_EDITOR
                GameObject.DestroyImmediate(go);
            #else
                GameObject.Destroy(go);
            #endif
        }

        static public void SetChildrenActive(Transform t, bool active)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                child.gameObject.SetActive(active);
            }
        }

        static public float GetMax(Vector3 v)
        {
            float max = v.x;
            if (v.y > max) max = v.y;
            if (v.z > max) max = v.z;
            return max;
        }

        static public Vector3 SetMin(Vector3 v, float min)
        {
            if (v.x < min) v.x = min;
            if (v.y < min) v.y = min;
            if (v.z < min) v.z = min;
            return v;
        }

        static public Vector3 Snap(Vector3 v, float snapSize)
        {
            v.x = Mathf.Floor(v.x / snapSize) * snapSize;
            v.y = Mathf.Floor(v.y / snapSize) * snapSize;
            v.z = Mathf.Floor(v.z / snapSize) * snapSize;

            return v;
        }

        static public Vector3 Abs(Vector3 v)
        {
            return new Vector3(v.x < 0 ? -v.x : v.x, v.y < 0 ? -v.y : v.y, v.z < 0 ? -v.z : v.z);
        }

        static public void SnapBoundsAndPreserveArea(ref Bounds bounds, float snapSize, Vector3 offset)
        {
            Vector3 newCenter = Snap(bounds.center, snapSize) + offset;
            bounds.size += Abs(newCenter - bounds.center) * 2;
            bounds.center = newCenter;
        }

        static public void ListRemoveAt<T>(List<T> list, int index)
        {
            list[index] = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
        }

        static public void CopyComponent(Component component, GameObject target)
        {
            Type type = component.GetType();
            target.AddComponent(type);
            PropertyInfo[] propInfo = type.GetProperties(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
            foreach (var property in propInfo)
            {
                property.SetValue(target.GetComponent(type), property.GetValue(component, null), null);
            }
        }
    }
}
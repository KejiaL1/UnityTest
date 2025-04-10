using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;

namespace MeshCombineStudio
{
    [ExecuteInEditMode]
    public class MeshCombineJobManager : MonoBehaviour
    {
        static public MeshCombineJobManager instance;

        public JobSettings jobSettings = new JobSettings();

        [Serializable]
        public class JobSettings
        {
            public CombineJobMode combineJobMode;
            public ThreadAmountMode threadAmountMode;
            public int combineMeshesPerFrame = 4;

            public bool useMultiThreading = true;
            public bool useMainThread = true;
            public int customThreadAmount = 1;
            public bool showStats;

            public void CopySettings(JobSettings source)
            {
                combineJobMode = source.combineJobMode;
                threadAmountMode = source.threadAmountMode;
                combineMeshesPerFrame = source.combineMeshesPerFrame;
                useMultiThreading = source.useMultiThreading;
                useMainThread = source.useMainThread;
                customThreadAmount = source.customThreadAmount;
            }

            public void ReportStatus()
            {
                Debug.Log("---------------------");
                Debug.Log("combineJobMode " + combineJobMode);
                Debug.Log("threadAmountMode " + threadAmountMode);
                Debug.Log("combineMeshesPerFrame " + combineMeshesPerFrame);
                Debug.Log("useMultiThreading " + useMultiThreading);
                Debug.Log("useMainThread " + useMainThread);
                Debug.Log("customThreadAmount " + customThreadAmount);
            }
        }

        public enum CombineJobMode { CombineAtOnce, CombinePerFrame };
        public enum ThreadAmountMode { AllThreads, HalfThreads, Custom };
        public enum ThreadState { isReady, isRunning, hasError };

        [NonSerialized] public List<NewMeshObject> newMeshObjectsPool = new List<NewMeshObject>();
        public Dictionary<Mesh, MeshCache> meshCacheDictionary = new Dictionary<Mesh, MeshCache>();
        
        [NonSerialized] public int totalNewMeshObjects;
        
        public Queue<NewMeshObject> newMeshObjectsDone = new Queue<NewMeshObject>();
        public Queue<NewMeshObject> newMeshObjectsDoneThread = new Queue<NewMeshObject>();
        public Queue<MeshCombineJob> meshCombineJobs = new Queue<MeshCombineJob>(); 

        public MeshCombineJobsThread[] meshCombineJobsThreads;

        public int cores;
        public int threadAmount;
        public int startThreadId, endThreadId;

        public bool abort;

        MeshCache.SubMeshCache tempMeshCache;

        Ray ray = new Ray(Vector3.zero, Vector3.down);
        RaycastHit hitInfo;

        static public MeshCombineJobManager CreateInstance(MeshCombiner meshCombiner, GameObject instantiatePrefab)
        {
            if (instance != null) return instance;

            GameObject go = new GameObject("MCS Job Manager");
            
            instance = go.AddComponent<MeshCombineJobManager>();
            instance.SetJobMode(meshCombiner.jobSettings);
            
            return instance;
        }

        void Awake()
        {
            instance = this;
        }

        void OnEnable()
        {
            instance = this;
            gameObject.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
            Init();
            
            #if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.EditorApplication.update += MyUpdate;
            #endif
        }

        public void Init() 
        {
            // Debug.Log("Init");

            cores = Environment.ProcessorCount;

            if (meshCombineJobsThreads == null || meshCombineJobsThreads.Length != cores)
            {
                meshCombineJobsThreads = new MeshCombineJobsThread[cores];
                for (int i = 0; i < meshCombineJobsThreads.Length; i++) meshCombineJobsThreads[i] = new MeshCombineJobsThread(i);
            }
        }

        void OnDisable()
        {
            // Debug.Log("Disable");
            #if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.update -= MyUpdate;
            #endif
        }

        void OnDestroy()
        {
            AbortJobs();
            instance = null;
        }

        void Update()
        {
            if (Application.isPlaying) MyUpdate();
        }

        void MyUpdate()
        {
            ExecuteJobs();
            if (newMeshObjectsDone.Count > 0 || newMeshObjectsDoneThread.Count > 0)
            {
                CombineMeshesDone();
            }
        }

        public void SetJobMode(JobSettings newJobSettings)
        {
            if (newJobSettings.combineMeshesPerFrame < 1)
            {
                Debug.LogError("MCS Job Manager -> CombineMeshesPerFrame is " + newJobSettings.combineMeshesPerFrame + " and should be 1 or higher.");
                return;
            }
            else if (newJobSettings.combineMeshesPerFrame > 128)
            {
                Debug.LogError("MCS Job Manager -> CombineMeshesPerFrame is " + newJobSettings.combineMeshesPerFrame + " and should be 128 or lower.");
                return;
            }
            if (newJobSettings.customThreadAmount < 1)
            {
                Debug.LogError("MCS Job Manager -> customThreadAmount is " + newJobSettings.combineMeshesPerFrame + " and should be 1 or higher.");
                return;
            }
            else if (newJobSettings.customThreadAmount > cores) newJobSettings.customThreadAmount = cores;

            jobSettings.CopySettings(newJobSettings);

            if (jobSettings.useMultiThreading)
            {
                startThreadId = jobSettings.useMainThread ? 0 : 1;

                if (jobSettings.threadAmountMode == ThreadAmountMode.Custom)
                {
                    if (jobSettings.customThreadAmount > cores - startThreadId) jobSettings.customThreadAmount = cores - startThreadId;
                    threadAmount = jobSettings.customThreadAmount;
                }
                else
                {
                    if (jobSettings.threadAmountMode == ThreadAmountMode.AllThreads) threadAmount = cores;
                    else threadAmount = cores / 2;
                    threadAmount -= startThreadId;
                }

                endThreadId = startThreadId + threadAmount;
            }
            else
            {
                startThreadId = 0;
                endThreadId = 1;
                threadAmount = 1;
            }

            int totalNewCacheMeshesNeeded;
            if (jobSettings.combineJobMode == CombineJobMode.CombinePerFrame) totalNewCacheMeshesNeeded = jobSettings.combineMeshesPerFrame;
            else totalNewCacheMeshesNeeded = threadAmount;

            while (newMeshObjectsPool.Count > totalNewCacheMeshesNeeded)
            {
                newMeshObjectsPool.RemoveAt(newMeshObjectsPool.Count - 1);
                // Debug.LogError("Remove!!");
            }
        }
        
        public void AddJob(MeshCombiner meshCombiner, MeshObjectsHolder meshObjectsHolder, Transform parent, Vector3 position)
        {
            List<MeshObject> meshObjects = meshObjectsHolder.meshObjects;
            
            int totalVertices = 0, totalTriangles = 0;
            int startIndex = 0;
            int length = 0;
            bool firstMesh = true;
            bool intersectsSurface = false;

            Mesh meshOld = null;
            MeshCache meshCache = null;

            int maxVertices = meshCombiner.useVertexOutputLimit ? meshCombiner.vertexOutputLimit : 65534;
            
            for (int i = 0; i < meshObjects.Count; i++)
            {
                MeshObject meshObject = meshObjects[i];
                meshObject.skip = false;

                meshCombiner.originalDrawCalls++;

                Mesh mesh = meshObject.cachedGO.mesh;
                
                if (mesh != meshOld)
                {
                    if (!meshCacheDictionary.TryGetValue(mesh, out meshCache))
                    {
                        meshCache = new MeshCache(mesh);
                        meshCacheDictionary.Add(mesh, meshCache);
                    }
                }
                meshOld = mesh; 

                meshObject.meshCache = meshCache;

                int vertexCount = meshCache.subMeshCache[meshObject.subMeshIndex].vertexCount;
                int triangleCount = meshCache.subMeshCache[meshObject.subMeshIndex].triangleCount;

                meshCombiner.originalTotalVertices += vertexCount;
                meshCombiner.originalTotalTriangles += triangleCount;

                if (totalVertices + vertexCount > maxVertices) 
                {
                    // Debug.Log(">AddJob StartIndex " + startIndex + " length " + length);
                    meshCombineJobs.Enqueue(new MeshCombineJob(meshCombiner, meshObjectsHolder, parent, position, startIndex, length, firstMesh, intersectsSurface));
                    firstMesh = intersectsSurface = false;
                    totalVertices = totalTriangles = length = 0;
                    startIndex = i;
                }

                if (meshCombiner.removeTrianglesBelowSurface)
                {
                    int intersect = MeshIntersectsSurface(meshCombiner, meshObject.cachedGO);

                    if (intersect == 0)
                    {
                        meshObject.intersectsSurface = intersectsSurface = true;
                        meshObject.startNewTriangleIndex = totalTriangles;
                        meshObject.newTriangleCount = triangleCount;
                        meshObject.skip = false;
                    }
                    else
                    {
                        meshObject.intersectsSurface = false;
                        if (intersect == -1) { meshObject.skip = true; ++length; continue; }
                        else meshObject.skip = false;
                    }
                }
                
                totalVertices += vertexCount;
                totalTriangles += triangleCount; 

                ++length;
            }

            if (totalVertices > 0)
            {
                // Debug.Log("*AddJob2 StartIndex " + startIndex + " length " + length);
                meshCombineJobs.Enqueue(new MeshCombineJob(meshCombiner, meshObjectsHolder, parent, position, startIndex, length, firstMesh, intersectsSurface));
            }
        }
        
        public int MeshIntersectsSurface(MeshCombiner meshCombiner, CachedGameObject cachedGO)
        {
            // -1 = below, 0 = intersect, 1 = above 
            MeshRenderer mr = cachedGO.mr;
            LayerMask terrainLayerMask = meshCombiner.surfaceLayerMask;
            int maxTerrainHeigt = meshCombiner.maxSurfaceHeight;

            #if UNITY_5_1 || UNITY_5_2
            if (Physics.CheckSphere(mr.bounds.center, Methods.GetMax(mr.bounds.extents), terrainLayerMask)) return 0;
            #else
            if (Physics.CheckBox(mr.bounds.center, mr.bounds.extents, Quaternion.identity, terrainLayerMask)) return 0;
            #endif

            Vector3 pos = mr.bounds.min;
            float rayLength = meshCombiner.maxSurfaceHeight - pos.y;

            ray.origin = new Vector3(pos.x, maxTerrainHeigt, pos.z);
            if (Physics.Raycast(ray, out hitInfo, rayLength, terrainLayerMask)) if (pos.y < hitInfo.point.y) return -1;

            return 1;
        }

        public void AbortJobs()
        {
            foreach (MeshCombineJob meshCombineJob in meshCombineJobs)
            {
                meshCombineJob.meshCombiner.jobCount = 0;
            }
            meshCombineJobs.Clear();
            
            for (int i = 0; i < meshCombineJobsThreads.Length; i++)
            {
                MeshCombineJobsThread meshCombineJobsThread = meshCombineJobsThreads[i];
                lock (meshCombineJobsThread.meshCombineJobs)
                {
                    meshCombineJobsThread.meshCombineJobs.Clear();
                } 
            }

            newMeshObjectsPool.Clear();
            totalNewMeshObjects = 0;
            abort = true;
        }

        public void ExecuteJobs()
        {
            while (meshCombineJobs.Count > 0)
            {
                int min = 999999;
                int threadId = 0;

                for (int i = startThreadId; i < endThreadId; i++)
                {
                    int count = meshCombineJobsThreads[i].meshCombineJobs.Count;

                    if (count < min)
                    {
                        threadId = i;
                        min = count;
                        if (min == 0) break;
                    }
                }

                lock (meshCombineJobsThreads[threadId].meshCombineJobs)
                {
                    meshCombineJobsThreads[threadId].meshCombineJobs.Enqueue(meshCombineJobs.Dequeue());
                }
            }

            bool jobsPending;

            try
            {
                do
                {
                    jobsPending = false;
                    
                    if (jobSettings.useMultiThreading)
                    {
                        for (int i = 1; i < endThreadId; i++)
                        {
                            MeshCombineJobsThread meshCombineJobThread = meshCombineJobsThreads[i];

                            bool hasJobCount = false;

                            lock (meshCombineJobThread.meshCombineJobs)
                            {
                                if (meshCombineJobThread.meshCombineJobs.Count > 0) hasJobCount = true;
                            }

                            if (hasJobCount)
                            {
                                jobsPending = true;
                                if (meshCombineJobThread.threadState == ThreadState.isReady) ThreadPool.QueueUserWorkItem(meshCombineJobThread.ExecuteJobsThread);
                                else if (meshCombineJobThread.threadState == ThreadState.hasError) { AbortJobs(); goto exitLoop; }
                            }
                        }
                    }

                    if (!jobSettings.useMultiThreading || jobSettings.useMainThread)
                    {
                        if (meshCombineJobsThreads[0].meshCombineJobs.Count > 0)
                        {
                            jobsPending = true;
                            meshCombineJobsThreads[0].ExecuteJobsThread(null);
                        }
                    }
                    if (jobSettings.combineJobMode == CombineJobMode.CombineAtOnce) CombineMeshesDone();
                }
                while (jobSettings.combineJobMode == CombineJobMode.CombineAtOnce && jobsPending);

                exitLoop:;
            }
            catch (Exception e)
            {
                Debug.LogError("Mesh Combine Studio error -> " + e.ToString());
                AbortJobs();
            }
        }
    
        public void CombineMeshesDone()
        {
            lock (newMeshObjectsDoneThread)
            {
                while(newMeshObjectsDoneThread.Count > 0) newMeshObjectsDone.Enqueue(newMeshObjectsDoneThread.Dequeue());
            }

            int count = 0;
            
            while(newMeshObjectsDone.Count > 0)
            {
                NewMeshObject newMeshObject = newMeshObjectsDone.Dequeue();
                
                if (!abort && !newMeshObject.allSkipped) newMeshObject.CreateMesh();

                MeshCombiner meshCombiner = newMeshObject.meshCombineJob.meshCombiner;
                if (--meshCombiner.jobCount == 0) meshCombiner.ExecuteOnCombiningReady();
                
                lock (newMeshObjectsPool)
                {
                    newMeshObjectsPool.Add(newMeshObject);
                }
                Interlocked.Decrement(ref totalNewMeshObjects);

                if (jobSettings.combineJobMode == CombineJobMode.CombinePerFrame && ++count > jobSettings.combineMeshesPerFrame && !abort)
                {
                    break;
                }
            }

            abort = false;
        }

        public class MeshCombineJobsThread
        {
            public int threadId;
            public ThreadState threadState;
            public Queue<MeshCombineJob> meshCombineJobs = new Queue<MeshCombineJob>();

            public MeshCombineJobsThread(int threadId)
            {
                this.threadId = threadId;
            }

            public void ExecuteJobsThread(object state)
            {
                threadState = ThreadState.isRunning;
                NewMeshObject newMeshObject = null;

                try
                {
                    while (true)
                    {
                        newMeshObject = null;

                        bool doBreak = false;

                        int totalNewMeshObjects = Interlocked.Increment(ref instance.totalNewMeshObjects);
                        
                        if (instance.jobSettings.combineJobMode == CombineJobMode.CombinePerFrame)
                        {
                            if (totalNewMeshObjects > instance.jobSettings.combineMeshesPerFrame) doBreak = true;
                        }
                        else
                        {
                            if (totalNewMeshObjects > instance.threadAmount) doBreak = true;
                        }

                        if (doBreak)
                        {
                            Interlocked.Decrement(ref instance.totalNewMeshObjects);
                            break;
                        }
                        
                        MeshCombineJob meshCombineJob;
                        
                        lock (meshCombineJobs)
                        {
                            if (meshCombineJobs.Count == 0) break;
                            meshCombineJob = meshCombineJobs.Dequeue();
                        }
                        
                        lock (instance.newMeshObjectsPool)
                        {
                            if (instance.newMeshObjectsPool.Count == 0)
                            {
                                newMeshObject = new NewMeshObject();
                                // Debug.Log("Create newMeshObject " + totalNewMeshObjects);
                            }
                            else
                            {
                                newMeshObject = instance.newMeshObjectsPool[instance.newMeshObjectsPool.Count - 1];
                                instance.newMeshObjectsPool.RemoveAt(instance.newMeshObjectsPool.Count - 1);
                            }
                        }

                        newMeshObject.newPosition = meshCombineJob.position;
                        newMeshObject.Combine(meshCombineJob);
                        
                        lock (instance.newMeshObjectsDoneThread)
                        {
                            instance.newMeshObjectsDoneThread.Enqueue(newMeshObject);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (newMeshObject != null)
                    {
                        lock (instance.newMeshObjectsPool)
                        {
                            instance.newMeshObjectsPool.Add(newMeshObject);
                        }
                        Interlocked.Decrement(ref instance.totalNewMeshObjects);
                    }
                    lock (meshCombineJobs)
                    {
                        meshCombineJobs.Clear();
                    }
                    Interlocked.Decrement(ref instance.totalNewMeshObjects);
                    Debug.LogError("Mesh Combine Studio thread error -> " + e.ToString()); 
                    threadState = ThreadState.hasError;
                    return;
                }

                threadState = ThreadState.isReady;
            }
        }

        public class MeshCombineJob
        {
            public MeshCombiner meshCombiner;
            public MeshObjectsHolder meshObjectsHolder;
            public Transform parent;
            public Vector3 position;
            public int startIndex;
            public int endIndex;
            public bool firstMesh;
            public bool intersectsSurface;
            public int backFaceTrianglesRemoved, trianglesRemoved;
            
            public MeshCombineJob(MeshCombiner meshCombiner, MeshObjectsHolder meshObjectsHolder, Transform parent, Vector3 position, int startIndex, int length, bool firstMesh, bool intersectsSurface)
            {
                this.meshCombiner = meshCombiner;
                this.meshObjectsHolder = meshObjectsHolder;
                this.parent = parent;
                this.position = position;
                this.startIndex = startIndex;
                this.firstMesh = firstMesh;
                this.intersectsSurface = intersectsSurface;
                endIndex = startIndex + length;
                meshObjectsHolder.lodParent.jobsPending++;
                meshCombiner.jobCount++;
            }
        }
        
        public class NewMeshObject
        {
            public MeshCombineJob meshCombineJob;
            public MeshCache.SubMeshCache newMeshCache = new MeshCache.SubMeshCache();
            public bool allSkipped;
            public Vector3 newPosition;
            
            byte[] vertexIsBelow;
            
            readonly byte belowSurface = 1, aboveSurface = 2;
            
            public NewMeshObject()
            {
                newMeshCache.Init();
            }
            
            public void Combine(MeshCombineJob meshCombineJob)
            {
                this.meshCombineJob = meshCombineJob;
                int startIndex = meshCombineJob.startIndex;
                int endIndex = meshCombineJob.endIndex;
                List<MeshObject> meshObjects = meshCombineJob.meshObjectsHolder.meshObjects;

                newMeshCache.ResetHasBooleans();

                int totalVertices = 0, totalTriangles = 0;
                int meshCount = endIndex - startIndex;

                MeshCombiner meshCombiner = meshCombineJob.meshCombiner;

                bool copyBakedLighting = meshCombiner.validCopyBakedLighting;
                bool rebakeLighting = meshCombiner.validRebakeLighting;
                bool regenarateLightingUvs = meshCombiner.rebakeLightingMode == MeshCombiner.RebakeLightingMode.RegenarateLightmapUvs;

                int tiles = 0;
                int tileX = 0;
                int tileY = 0;
                float tilesInv = 0;
                
                if (rebakeLighting)
                {
                    tiles = Mathf.CeilToInt(Mathf.Sqrt(meshCount));
                    tilesInv = 1.0f / tiles;
                }

                allSkipped = true;
                
                for (int i = startIndex; i < endIndex; i++)
                {
                    MeshObject meshObject = meshObjects[i];
                    if (meshObject.skip) continue;
                    
                    allSkipped = false;

                    MeshCache meshCache = meshObject.meshCache;

                    int subMeshIndex = meshObject.subMeshIndex;
                    MeshCache.SubMeshCache subMeshCache = meshCache.subMeshCache[subMeshIndex];

                    Vector3 position = meshObject.position - meshCombineJob.position;
                    Quaternion rotation = meshObject.rotation;
                    Vector3 scale = meshObject.scale;
                   
                    bool flipTriangles = false;

                    if (scale.x < 0) flipTriangles = !flipTriangles;
                    if (scale.y < 0) flipTriangles = !flipTriangles;
                    if (scale.z < 0) flipTriangles = !flipTriangles;
                    
                    Vector3[] vertices = subMeshCache.vertices;
                    Vector3[] normals = subMeshCache.normals;
                    Vector4[] tangents = subMeshCache.tangents;

                    Vector2[] uv = subMeshCache.uv;
                    Vector2[] uv2 = subMeshCache.uv2;
                    Vector2[] uv3 = subMeshCache.uv3;
                    Vector2[] uv4 = subMeshCache.uv4;
                    Color32[] colors32 = subMeshCache.colors32;

                    int[] triangles = subMeshCache.triangles;
                    
                    int vertexCount = subMeshCache.vertexCount;
                    
                    int[] newTriangles = newMeshCache.triangles;

                    HasArray(ref newMeshCache.hasNormals, subMeshCache.hasNormals, ref newMeshCache.normals, normals, vertexCount, totalVertices);
                    HasArray(ref newMeshCache.hasTangents, subMeshCache.hasTangents, ref newMeshCache.tangents, tangents, vertexCount, totalVertices);

                    HasArray(ref newMeshCache.hasUv, subMeshCache.hasUv, ref newMeshCache.uv, uv, vertexCount, totalVertices);
                    HasArray(ref newMeshCache.hasUv2, subMeshCache.hasUv2, ref newMeshCache.uv2, uv2, vertexCount, totalVertices);
                    HasArray(ref newMeshCache.hasUv3, subMeshCache.hasUv3, ref newMeshCache.uv3, uv3, vertexCount, totalVertices);
                    HasArray(ref newMeshCache.hasUv4, subMeshCache.hasUv4, ref newMeshCache.uv4, uv4, vertexCount, totalVertices);
                    HasArray(ref newMeshCache.hasColors, subMeshCache.hasColors, ref newMeshCache.colors32, colors32, vertexCount, totalVertices);

                    Vector3[] newVertices = newMeshCache.vertices;
                    Vector3[] newNormals = newMeshCache.normals;
                    Vector4[] newTangents = newMeshCache.tangents;
                    Vector2[] newUv = newMeshCache.uv;
                    Vector2[] newUv2 = newMeshCache.uv2;
                    Vector2[] newUv3 = newMeshCache.uv3;
                    Vector2[] newUv4 = newMeshCache.uv4;
                    Color32[] newColors32 = newMeshCache.colors32;

                    bool hasNormals = subMeshCache.hasNormals;
                    bool hasTangents = subMeshCache.hasTangents;

                    for (int j = 0; j < vertices.Length; j++)
                    {
                        int vertexIndex = j + totalVertices;
                        newVertices[vertexIndex] = (rotation * new Vector3(vertices[j].x * scale.x, vertices[j].y * scale.y, vertices[j].z * scale.z)) + position;
                        if (hasNormals) newNormals[vertexIndex] = rotation * normals[j];
                        if (hasTangents)
                        {
                            newTangents[vertexIndex] = rotation * tangents[j];
                            newTangents[vertexIndex].w = tangents[j].w;
                        }
                    }

                    if (subMeshCache.hasUv) Array.Copy(uv, 0, newUv, totalVertices, vertexCount);
                    if (subMeshCache.hasUv2)
                    {
                        if (copyBakedLighting)
                        {
                            Vector4 lightmapScaleOffset = meshObject.lightmapScaleOffset;
                            Vector2 uvOffset = new Vector2(lightmapScaleOffset.z, lightmapScaleOffset.w);
                            Vector2 uvScale = new Vector2(lightmapScaleOffset.x, lightmapScaleOffset.y);

                            for (int j = 0; j < vertices.Length; j++)
                            {
                                int vertexIndex = j + totalVertices;
                                newUv2[vertexIndex] = new Vector2(uv2[j].x * uvScale.x , uv2[j].y * uvScale.y) + uvOffset;
                            }
                        }
                        else if (rebakeLighting)
                        {
                            if (!regenarateLightingUvs)
                            {
                                Vector2 uvOffset = new Vector2(tilesInv * tileX, tilesInv * tileY);

                                for (int j = 0; j < vertices.Length; j++)
                                {
                                    int vertexIndex = j + totalVertices;
                                    newUv2[vertexIndex] = (uv2[j] * tilesInv) + uvOffset;
                                }
                            }
                        }
                        else Array.Copy(uv2, 0, newUv2, totalVertices, vertexCount);
                    }
                    if (subMeshCache.hasUv3) Array.Copy(uv3, 0, newUv3, totalVertices, vertexCount);
                    if (subMeshCache.hasUv4) Array.Copy(uv4, 0, newUv4, totalVertices, vertexCount);
                    if (subMeshCache.hasColors) Array.Copy(colors32, 0, newColors32, totalVertices, vertexCount);
                    
                    if (flipTriangles)
                    {
                        for (int j = 0; j < triangles.Length; j += 3)
                        {
                            newTriangles[j + totalTriangles] = triangles[j + 2] + totalVertices;
                            newTriangles[j + totalTriangles + 1] = triangles[j + 1] + totalVertices;
                            newTriangles[j + totalTriangles + 2] = triangles[j] + totalVertices;
                        }
                    }
                    else
                    {
                        for (int j = 0; j < triangles.Length; j++)
                        {
                            newTriangles[j + totalTriangles] = triangles[j] + totalVertices;
                        }
                    }
                    
                    totalVertices += vertexCount;
                    totalTriangles += triangles.Length;
                    if (++tileX >= tiles) { tileX = 0; ++tileY; }
                }

                newMeshCache.vertexCount = totalVertices;
                newMeshCache.triangleCount = totalTriangles;

                if (meshCombiner.removeBackFaceTriangles) RemoveBackFaceTriangles();
            }
            
            void HasArray<T>(ref bool hasNewArray, bool hasArray, ref T[] newArray, Array array, int vertexCount, int totalVertices)
            {
                if (hasArray)
                { 
                    if (!hasNewArray)
                    {
                        if (newArray == null) newArray = new T[65534]; 
                        else Array.Clear(newArray, 0, totalVertices);
                    }
                    hasNewArray = true;
                }
                else if (hasNewArray) Array.Clear(newArray, totalVertices, vertexCount); 
            }

            public void RemoveTrianglesBelowSurface(Transform t, MeshCombineJob meshCombineJob)
            {
                if (vertexIsBelow == null) vertexIsBelow = new byte[65534];
                
                Ray ray = instance.ray;
                RaycastHit hitInfo = instance.hitInfo;
                Vector3 pos = Vector3.zero;
                int layerMask = meshCombineJob.meshCombiner.surfaceLayerMask;
                float rayHeight = meshCombineJob.meshCombiner.maxSurfaceHeight;
                
                Vector3[] newVertices = newMeshCache.vertices;
                int[] newTriangles = newMeshCache.triangles;
                
                List<MeshObject> meshObjects = meshCombineJob.meshObjectsHolder.meshObjects;

                int startIndex = meshCombineJob.startIndex;
                int endIndex = meshCombineJob.endIndex;

                for (int i = startIndex; i < endIndex; i++)
                {
                    MeshObject meshObject = meshObjects[i];
                    if (!meshObject.intersectsSurface) continue;
                    
                    int startTriangleIndex = meshObject.startNewTriangleIndex;
                    int endTriangleIndex = meshObject.newTriangleCount + startTriangleIndex;
                    
                    for (int j = startTriangleIndex; j < endTriangleIndex; j += 3)
                    {
                        bool isAboveSurface = false;

                        for (int k = 0; k < 3; k++)
                        {
                            int verticeIndex = newTriangles[j + k];
                            if (verticeIndex == -1) continue;
                            byte isBelow = vertexIsBelow[verticeIndex];

                            if (isBelow == 0)
                            {
                                pos = t.TransformPoint(newVertices[verticeIndex]);
                                ray.origin = new Vector3(pos.x, rayHeight, pos.z);

                                if (Physics.Raycast(ray, out hitInfo, rayHeight - pos.y, layerMask)) { isBelow = pos.y < hitInfo.point.y ? belowSurface : aboveSurface; vertexIsBelow[verticeIndex] = isBelow; }// hitPoints.Add(hitInfo.point); }
                                else { vertexIsBelow[verticeIndex] = aboveSurface; isAboveSurface = true; break; }
                            }

                            if (isBelow != belowSurface) { isAboveSurface = true; break; }
                        }
                        if (!isAboveSurface)
                        {
                            meshCombineJob.trianglesRemoved += 3;
                            newTriangles[j] = -1;
                        }
                    }
                }
                
                Array.Clear(vertexIsBelow, 0, newMeshCache.vertexCount);
            }

            public void RemoveBackFaceTriangles()
            {
                int[] newTriangles = newMeshCache.triangles;
                Vector3[] newNormals = newMeshCache.normals;
                int totalTriangles = newMeshCache.triangleCount;

                MeshCombiner meshCombiner = meshCombineJob.meshCombiner;
                bool useBackFaceDirection = (meshCombiner.backFaceTriangleMode == MeshCombiner.BackFaceTriangleMode.Direction);

                Bounds backFaceBounds = meshCombiner.backFaceBounds;
                Vector3 backFaceBoundsMin = backFaceBounds.min;
                Vector3 backFaceBoundsMax = backFaceBounds.max;

                Vector3[] newVertices = newMeshCache.vertices;
                
                Vector3 backFaceDirection = Quaternion.Euler(meshCombiner.backFaceDirection) * Vector3.forward;
                
                for (int i = 0; i < totalTriangles; i += 3)
                {
                    Vector3 normal = Vector3.zero;
                    Vector3 vertexPosition = Vector3.zero;

                    for (int j = 0; j < 3; j++)
                    {
                        int vertexIndex = newTriangles[i + j];
                        vertexPosition += newVertices[vertexIndex];
                        normal += newNormals[vertexIndex];
                    }
                    vertexPosition /= 3;
                    normal /= 3;
                    
                    if (!useBackFaceDirection)
                    {
                        Vector3 outerPosition;
                        outerPosition.x = (normal.x > 0 ? backFaceBoundsMax.x : backFaceBoundsMin.x);
                        outerPosition.y = (normal.y > 0 ? backFaceBoundsMax.y : backFaceBoundsMin.y);
                        outerPosition.z = (normal.z > 0 ? backFaceBoundsMax.z : backFaceBoundsMin.z);
                    
                        backFaceDirection = ((newPosition + vertexPosition) - outerPosition);
                    }

                    if (Vector3.Dot(backFaceDirection, normal) >= 0)
                    {
                        newTriangles[i] = -1;
                        meshCombineJob.backFaceTrianglesRemoved += 3;
                    }
                }
            }

            void ArrangeTriangles()
            {
                int totalTriangles = newMeshCache.triangleCount;
                int[] newTriangles = newMeshCache.triangles;
                
                for (int i = 0; i < totalTriangles; i += 3)
                {
                    if (newTriangles[i] == -1)
                    {
                        newTriangles[i] = newTriangles[totalTriangles - 3];
                        newTriangles[i + 1] = newTriangles[totalTriangles - 2];
                        newTriangles[i + 2] = newTriangles[totalTriangles - 1];
                        i -= 3;
                        totalTriangles -= 3;
                    }
                }
                
                newMeshCache.triangleCount = totalTriangles;
            }

            public void CreateMesh()
            {
                MeshCombiner meshCombiner = meshCombineJob.meshCombiner;

                if (meshCombiner.instantiatePrefab == null)
                {
                    Debug.LogError("Mesh Combine Studio -> Instantiate Prefab = null");
                    return;
                }

                MeshObjectsHolder meshObjectHolder = meshCombineJob.meshObjectsHolder;

                #if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3
                    GameObject go = (GameObject)GameObject.Instantiate(meshCombiner.instantiatePrefab, newPosition, Quaternion.identity);
                    go.transform.parent = meshCombineJob.parent;
                #else
                    GameObject go = (GameObject)GameObject.Instantiate(meshCombiner.instantiatePrefab, newPosition, Quaternion.identity, meshCombineJob.parent);
                #endif
                
                string name = meshCombineJob.meshObjectsHolder.mat.name;
                go.name = name;
                go.layer = meshCombiner.outputLayer;
                
                Mesh mesh = new Mesh();
                mesh.name = name;

                if (meshCombineJob.intersectsSurface)
                {
                    RemoveTrianglesBelowSurface(go.transform, meshCombineJob);
                }

                if (meshCombineJob.trianglesRemoved > 0 || meshCombineJob.backFaceTrianglesRemoved > 0)
                {
                    ArrangeTriangles();

                    if (instance.tempMeshCache == null)
                    {
                        instance.tempMeshCache = new MeshCache.SubMeshCache();
                        instance.tempMeshCache.Init(false);
                    }
                    instance.tempMeshCache.CopySubMeshCache(newMeshCache);
                    newMeshCache.RebuildVertexBuffer(instance.tempMeshCache, false);
                }
                
                int totalVertices = newMeshCache.vertexCount;
                int totalTriangles = newMeshCache.triangleCount;

                meshCombiner.totalVertices += totalVertices;
                meshCombiner.totalTriangles += totalTriangles;

                MeshExtension.ApplyVertices(mesh, newMeshCache.vertices, totalVertices);
                MeshExtension.ApplyTriangles(mesh, newMeshCache.triangles, totalTriangles);
                if (newMeshCache.hasNormals) MeshExtension.ApplyNormals(mesh, newMeshCache.normals, totalVertices);
                if (newMeshCache.hasTangents) MeshExtension.ApplyTangents(mesh, newMeshCache.tangents, totalVertices);

                if (newMeshCache.hasUv) MeshExtension.ApplyUvs(mesh, newMeshCache.uv, 0, totalVertices);
                if (newMeshCache.hasUv2) MeshExtension.ApplyUvs(mesh, newMeshCache.uv2, 1, totalVertices);
                if (newMeshCache.hasUv3) MeshExtension.ApplyUvs(mesh, newMeshCache.uv3, 2, totalVertices);
                if (newMeshCache.hasUv4) MeshExtension.ApplyUvs(mesh, newMeshCache.uv4, 3, totalVertices);
                if (newMeshCache.hasColors) MeshExtension.ApplyColors32(mesh, newMeshCache.colors32, totalVertices);
                
                CachedComponents cachedComponents = go.GetComponent<CachedComponents>();

                #if UNITY_EDITOR
                if (meshCombiner.validRebakeLighting)
                {
                    if (meshCombiner.rebakeLightingMode == MeshCombiner.RebakeLightingMode.RegenarateLightmapUvs)
                    {
                        UnityEditor.UnwrapParam unwrapParam = new UnityEditor.UnwrapParam();
                        UnityEditor.UnwrapParam.SetDefaults(out unwrapParam);
                        UnityEditor.Unwrapping.GenerateSecondaryUVSet(mesh, unwrapParam);
                    }

                    UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(cachedComponents.mr);
                    so.FindProperty("m_ScaleInLightmap").floatValue = meshCombiner.scaleInLightmap;
                    so.ApplyModifiedProperties();
                }
                UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, ~UnityEditor.StaticEditorFlags.BatchingStatic);
                #endif
                
                if (meshCombiner.addMeshColliders)
                {
                    MeshCollider meshCollider = go.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = mesh;
                }

                if (meshCombiner.makeMeshesUnreadable) mesh.UploadMeshData(true);

                meshCombiner.newDrawCalls++;
                
                cachedComponents.mr.sharedMaterial = meshCombineJob.meshObjectsHolder.mat;
                cachedComponents.mf.sharedMesh = mesh;
                cachedComponents.mr.lightmapIndex = meshObjectHolder.lightmapIndex;
                cachedComponents.garbageCollectMesh.mesh = mesh;
                
                if (meshCombineJob.meshObjectsHolder.shadowCastingModeTwoSided || (meshCombiner.twoSidedShadows && meshCombineJob.backFaceTrianglesRemoved > 0)) cachedComponents.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

                if (meshObjectHolder.newCachedGOs == null) meshObjectHolder.newCachedGOs = new List<CachedGameObject>();
                meshObjectHolder.newCachedGOs.Add(new CachedGameObject(cachedComponents));

                meshObjectHolder.lodParent.lodLevels[meshObjectHolder.lodLevel].newMeshRenderers.Add(cachedComponents.mr);
                if (--meshObjectHolder.lodParent.jobsPending == 0) meshObjectHolder.lodParent.AssignLODGroup(meshCombiner);
            }
        }
    }
    
    public class MeshCache
    {
        public Mesh mesh;
        public SubMeshCache[] subMeshCache;
        public int subMeshCount;
        
        public MeshCache(Mesh mesh)
        {
            this.mesh = mesh;
            subMeshCount = mesh.subMeshCount;

            subMeshCache = new SubMeshCache[subMeshCount];

            if (subMeshCount == 1) subMeshCache[0] = new SubMeshCache(mesh, true);
            else
            {
                SubMeshCache tempMeshCache = new SubMeshCache(mesh, false);

                for (int i = 0; i < subMeshCache.Length; i++)
                {
                    subMeshCache[i] = new SubMeshCache(mesh, i);
                    subMeshCache[i].RebuildVertexBuffer(tempMeshCache, true);
                }
            }
        } 

        public class SubMeshCache
        {
            public Vector3[] vertices, normals;
            public Vector4[] tangents;
            public Vector2[] uv, uv2, uv3, uv4;
            public Color32[] colors32;
            public int[] triangles;

            public bool hasNormals, hasTangents, hasUv, hasUv2, hasUv3, hasUv4, hasColors;

            public int vertexCount;
            public int triangleCount;

            public SubMeshCache() { }
            
            public void CopySubMeshCache(SubMeshCache source)
            {
                vertexCount = source.vertexCount;
                
                Array.Copy(source.vertices, 0, vertices, 0, vertexCount);

                hasNormals = source.hasNormals; hasTangents = source.hasTangents; hasColors = source.hasColors;
                hasUv = source.hasUv; hasUv2 = source.hasUv2; hasUv3 = source.hasUv3; hasUv4 = source.hasUv4;

                if (source.hasNormals) CopyArray(source.normals, ref normals, vertexCount);
                if (source.hasTangents) CopyArray(source.tangents, ref tangents, vertexCount);

                if (source.hasUv) CopyArray(source.uv, ref uv, vertexCount);
                if (source.hasUv2) CopyArray(source.uv2, ref uv2, vertexCount);
                if (source.hasUv3) CopyArray(source.uv3, ref uv3, vertexCount);
                if (source.hasUv4) CopyArray(source.uv4, ref uv4, vertexCount);
                if (source.hasColors) CopyArray(source.colors32, ref colors32, vertexCount);
            }

            public void CopyArray<T>(Array sourceArray, ref T[] destinationArray, int vertexCount)
            {
                if (destinationArray == null) destinationArray = new T[65534];
                Array.Copy(sourceArray, 0, destinationArray, 0, vertexCount);
            }

            public SubMeshCache(Mesh mesh, int subMeshIndex)
            {
                triangles = mesh.GetTriangles(subMeshIndex);
                triangleCount = triangles.Length;
            }
            
            public SubMeshCache(Mesh mesh, bool assignTriangles)
            {
                vertices = mesh.vertices;
                normals = mesh.normals;
                tangents = mesh.tangents;
                uv = mesh.uv;
                uv2 = mesh.uv2;
                uv3 = mesh.uv3;
                uv4 = mesh.uv4;
                colors32 = mesh.colors32;

                if (assignTriangles)
                {
                    triangles = mesh.triangles;
                    triangleCount = triangles.Length;
                }

                CheckHasArrays();

                vertexCount = vertices.Length;
            }

            public void CheckHasArrays()
            {
                if (normals != null && normals.Length > 0) hasNormals = true;
                if (tangents != null && tangents.Length > 0) hasTangents = true;
                if (uv != null && uv.Length > 0) hasUv = true;
                if (uv2 != null && uv2.Length > 0) hasUv2 = true;
                if (uv3 != null && uv3.Length > 0) hasUv3 = true;
                if (uv4 != null && uv4.Length > 0) hasUv4 = true;
                if (colors32 != null && colors32.Length > 0) hasColors = true;
            }

            public void ResetHasBooleans()
            {
                hasNormals = hasTangents = hasUv = hasUv2 = hasUv3 = hasUv4 = hasColors = false;
            }

            public void Init(bool initTriangles = true)
            {
                vertices = new Vector3[65534];
                if (initTriangles) triangles = new int[786408];
            }
            
            // TODO make it possible to do on multi thread
            public void RebuildVertexBuffer(SubMeshCache sub, bool resizeArrays)
            {
                int[] usedVertices = new int[sub.vertices.Length];
                int[] subVertexIndices = new int[usedVertices.Length];

                vertexCount = 0;
                
                for (int i = 0; i < triangleCount; i++)
                {
                    int vertexIndex = triangles[i];

                    if (usedVertices[vertexIndex] == 0)
                    {
                        usedVertices[vertexIndex] = vertexCount + 1;
                        subVertexIndices[vertexCount] = vertexIndex;
                        triangles[i] = vertexCount;
                        ++vertexCount;
                    }
                    else triangles[i] = usedVertices[vertexIndex] - 1;
                }

                if (resizeArrays) vertices = new Vector3[vertexCount];

                hasNormals = sub.hasNormals; hasTangents = sub.hasTangents; hasColors = sub.hasColors;
                hasUv = sub.hasUv; hasUv2 = sub.hasUv2; hasUv3 = sub.hasUv3; hasUv4 = sub.hasUv4;

                if (resizeArrays)
                {
                    if (hasNormals) normals = new Vector3[vertexCount];
                    if (hasTangents) tangents = new Vector4[vertexCount];
                    if (hasUv) uv = new Vector2[vertexCount];
                    if (hasUv2) uv2 = new Vector2[vertexCount];
                    if (hasUv3) uv3 = new Vector2[vertexCount];
                    if (hasUv4) uv4 = new Vector2[vertexCount];
                    if (hasColors) colors32 = new Color32[vertexCount];
                }
                
                for (int i = 0; i < vertexCount; i++)
                {
                    int vertexIndex = subVertexIndices[i];

                    vertices[i] = sub.vertices[vertexIndex];

                    if (hasNormals) normals[i] = sub.normals[vertexIndex];
                    if (hasTangents) tangents[i] = sub.tangents[vertexIndex];

                    if (hasUv) uv[i] = sub.uv[vertexIndex];
                    if (hasUv2) uv2[i] = sub.uv2[vertexIndex];
                    if (hasUv3) uv3[i] = sub.uv3[vertexIndex];
                    if (hasUv4) uv4[i] = sub.uv4[vertexIndex];
                    if (hasColors) colors32[i] = sub.colors32[vertexIndex];
                }
            }
        }
    }

    static public class MeshExtensionAlloc
    {
        static public void ApplyVertices(Mesh mesh, Vector3[] vertices, int length)
        {
            Vector3[] newVertices = new Vector3[length];
            Array.Copy(vertices, newVertices, length);
            mesh.vertices = newVertices;
        }

        static public void ApplyNormals(Mesh mesh, Vector3[] normals, int length)
        {
            Vector3[] newNormals = new Vector3[length];
            Array.Copy(normals, newNormals, length);
            mesh.normals = newNormals;
        }

        static public void ApplyTangents(Mesh mesh, Vector4[] tangents, int length)
        {
            Vector4[] newTangents = new Vector4[length];
            Array.Copy(tangents, newTangents, length);
            mesh.tangents = newTangents;
        }

        static public void ApplyUvs(Mesh mesh, int channel, Vector2[] uvs, int length)
        {
            Vector2[] newUvs = new Vector2[length];
            Array.Copy(uvs, newUvs, length);
            if (channel == 0) mesh.uv = newUvs;
            else if (channel == 1) mesh.uv2 = newUvs;
            else if (channel == 2) mesh.uv3 = newUvs;
            else mesh.uv4 = newUvs;
        }

        static public void ApplyColors32(Mesh mesh, Color32[] colors, int length)
        {
            Color32[] newColors = new Color32[length];
            Array.Copy(colors, newColors, length);
            mesh.colors32 = newColors;
        }

        static public void ApplyTriangles(Mesh mesh, int[] triangles, int length)
        {
            int[] newTriangles = new int[length];
            Array.Copy(triangles, newTriangles, length);
            mesh.triangles = newTriangles;
        }
    }
}



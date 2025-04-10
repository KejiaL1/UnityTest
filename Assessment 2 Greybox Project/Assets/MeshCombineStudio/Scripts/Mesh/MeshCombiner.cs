using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

namespace MeshCombineStudio
{
    [ExecuteInEditMode]
    public class MeshCombiner : MonoBehaviour
    {
        static public List<MeshCombiner> instances = new List<MeshCombiner>();

        public enum HandleComponent { Disable, Destroy };
        public enum ObjectCenter { BoundsCenter, TransformPosition };
        public enum BackFaceTriangleMode { Box, Direction }
        public delegate void DefaultMethod();

        public event DefaultMethod OnCombiningReady;

        public MeshCombineJobManager.JobSettings jobSettings = new MeshCombineJobManager.JobSettings(); 
        public LODGroupSettings[] lodGroupsSettings;

        public GameObject instantiatePrefab;
        public const int maxLodCount = 8;
        public string saveMeshesFolder;

        public ObjectOctree.Cell octree;
        public List<ObjectOctree.MaxCell> changedCells;
        [NonSerialized] public bool octreeContainsObjects;

        // Output Settings
        public bool useCells = true;
        public int cellSize = 32;
        public Vector3 cellOffset;
        
        public bool useVertexOutputLimit;
        public int vertexOutputLimit = 65534;
        public enum RebakeLightingMode { CopyLightmapUvs, RegenarateLightmapUvs };
        public RebakeLightingMode rebakeLightingMode;
        public bool copyBakedLighting, validCopyBakedLighting;
        public bool rebakeLighting, validRebakeLighting;
        public int outputLayer = 0;
        public float scaleInLightmap = 1;
        public bool addMeshColliders = false;
        public bool makeMeshesUnreadable = true;

        public bool removeTrianglesBelowSurface;
        public LayerMask surfaceLayerMask;
        public int maxSurfaceHeight = 1000;

        public bool removeBackFaceTriangles;
        public BackFaceTriangleMode backFaceTriangleMode;
        public Vector3 backFaceDirection;
        public Bounds backFaceBounds;
        public bool twoSidedShadows = true;
        
        // Runtime Settings
        public bool combineInRuntime;
        public bool combineOnStart = true;
        public bool useCombineSwapKey;
        public KeyCode combineSwapKey = KeyCode.Tab;
        public HandleComponent originalMeshRenderers = HandleComponent.Disable;
        public HandleComponent originalLODGroups = HandleComponent.Disable;
        
        public SearchOptions searchOptions;

        public Vector3 oldPosition, oldScale;
        public LodParentHolder[] lodParentHolders = new LodParentHolder[maxLodCount];

        [NonSerialized] public List<CachedGameObject> foundObjects = new List<CachedGameObject>();
        [NonSerialized] public List<CachedLodGameObject> foundLodObjects = new List<CachedLodGameObject>();
        HashSet<Transform> uniqueLodObjects = new HashSet<Transform>();
        HashSet<LODGroup> foundLodGroups = new HashSet<LODGroup>();

        public List<Mesh> unreadableMeshes = new List<Mesh>();

        public int mrDisabledCount = 0;
        
        public bool combined = false;
        public bool activeOriginal = true;

        public bool combinedActive;
        public bool drawGizmos = true;
        public bool drawMeshBounds = true;
        
        public int originalTotalVertices, originalTotalTriangles;
        public int totalVertices, totalTriangles;
        public int originalDrawCalls, newDrawCalls;

        [NonSerialized] MeshCombiner thisInstance;
        [NonSerialized] public int jobCount;

        bool hasFoundFirstObject;
        Bounds bounds;
        
        [Serializable]
        public class SearchOptions
        {
            public enum ComponentCondition { And, Or };
            public GameObject parent;
            public ObjectCenter objectCenter;
            public bool useSearchBox = false;
            public Bounds searchBoxBounds;
            public bool searchBoxSquare;
            public Vector3 searchBoxPivot;
            public Vector3 searchBoxSize = new Vector3(25, 25, 25); 
            public bool useMaxBoundsFactor = true;
            public float maxBoundsFactor = 1.5f;  
            public bool useVertexInputLimit = true;
            public int vertexInputLimit = 5000;
            public bool useVertexInputLimitLod = true;
            public int vertexInputLimitLod = 10000;

            public bool useLayerMask;
            public LayerMask layerMask = ~0;
            public bool useTag;
            public string tag;
            public bool useNameContains;
            public List<string> nameContainList = new List<string>();
            public bool onlyActive = true;
            public bool onlyStatic = true;
            public bool useComponentsFilter;
            public ComponentCondition componentCondition;
            public List<string> componentNameList = new List<string>();

            public SearchOptions(GameObject parent)
            {
                this.parent = parent;
            }

            public void GetSearchBoxBounds()
            {
                searchBoxBounds = new Bounds(searchBoxPivot + new Vector3(0, searchBoxSize.y * 0.5f, 0), searchBoxSize);
            }
        }
        
        public void ExecuteOnCombiningReady()
        {
            // Debug.Log("Combining ready");
            if (OnCombiningReady != null) OnCombiningReady();
        }
        
        void Awake()
        {
            instances.Add(this);
            thisInstance = this;
        }

        void OnEnable()
        {
            if (thisInstance == null)
            {
                thisInstance = this;
                instances.Add(this);
            }
        }

        void Start()
        {
            InitMeshCombineJobManager();
            if (instances[0] == this)
                MeshCombineJobManager.instance.SetJobMode(jobSettings);

            if (!Application.isPlaying && Application.isEditor) return;

            // Debug.Log("Start");
            StartRuntime();
        } 
        // ==========================================================================================================================

        void OnDestroy()
        {
            thisInstance = null;
            instances.Remove(this);

            // if (!Application.isPlaying && Application.isEditor) return;

            if (instances.Count == 0 && MeshCombineJobManager.instance != null)
            {
                Methods.Destroy(MeshCombineJobManager.instance.gameObject);
                MeshCombineJobManager.instance = null;
            }
        }

        static public MeshCombiner GetInstance(string name)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i].gameObject.name == name) return instances[i];
            }
            return null;
        }

        public void CopyJobSettingsToAllInstances()
        {
            for (int i = 0; i < instances.Count; i++) instances[i].jobSettings.CopySettings(jobSettings);
        }

        public void InitMeshCombineJobManager()
        {
            if (MeshCombineJobManager.instance == null)
            {
                MeshCombineJobManager.CreateInstance(this, instantiatePrefab);
            }
        }

        public void CreateLodGroupsSettings()
        {
            lodGroupsSettings = new LODGroupSettings[maxLodCount];
            for (int i = 0; i < lodGroupsSettings.Length; i++) lodGroupsSettings[i] = new LODGroupSettings(i);
        }

        private void StartRuntime() 
        {
            if (combineInRuntime)
            {
                if (combineOnStart) CombineAll();
                if (useCombineSwapKey && originalMeshRenderers == HandleComponent.Disable && originalLODGroups == HandleComponent.Disable)
                {
                    if (SwapCombineKey.instance == null) gameObject.AddComponent<SwapCombineKey>(); else SwapCombineKey.instance.meshCombinerList.Add(this);
                }
            }
        } 
        // ==========================================================================================================================

        public void DestroyCombinedObjects()
        {
            RestoreOriginalRenderersAndLODGroups();
            Methods.DestroyChildren(transform);
            combined = false;
        }

        private void Reset()
        {
            // Debug.Log("Add to Octree");
            RestoreOriginalRenderersAndLODGroups();

            foundObjects.Clear();
            uniqueLodObjects.Clear();
            foundLodGroups.Clear();
            foundLodObjects.Clear();
            unreadableMeshes.Clear();

            ResetOctree();

            hasFoundFirstObject = false;

            bounds.center = bounds.size = Vector3.zero;

            if (searchOptions.useSearchBox) searchOptions.GetSearchBoxBounds();

            InitAndResetLodParentsCount();
        }

        public void AddObjects(List<Transform> transforms, bool useSearchOptions, bool checkForLODGroups = true)
        {
            List<LODGroup> lodGroups = new List<LODGroup>();

            if (checkForLODGroups)
            {
                for (int i = 0; i < transforms.Count; i++)
                {
                    LODGroup lodGroup = transforms[i].GetComponent<LODGroup>();
                    if (lodGroup != null) lodGroups.Add(lodGroup);
                }

                if (lodGroups.Count > 0) AddLodGroups(lodGroups.ToArray(), useSearchOptions);
            }

            AddTransforms(transforms.ToArray(), useSearchOptions);
        }

        public void AddObjectsAutomatically()
        {
            Reset();
            AddObjectsFromSearchParent();
            AddFoundObjectsToOctree();
            if (octreeContainsObjects) octree.SortObjects(this);

            if (Console.instance != null) LogOctreeInfo();
        }

        public void AddFoundObjectsToOctree()
        {
            if (foundObjects.Count > 0 || foundLodObjects.Count > 0) octreeContainsObjects = true;
            else
            {
                Debug.Log("No matching GameObjects with chosen search options are found for combining.");
                return;
            }

            CalcOctreeSize(bounds);
            
            ObjectOctree.MaxCell.maxCellCount = 0;
            
            for (int i = 0; i < foundObjects.Count; i++) octree.AddObject(this, foundObjects[i], 0, 0);
            for (int i = 0; i < foundLodObjects.Count; i++)
            {
                CachedLodGameObject cachedLodGO = foundLodObjects[i];
                octree.AddObject(this, cachedLodGO, cachedLodGO.lodCount, cachedLodGO.lodLevel);
            }
        } 
        // ==========================================================================================================================
        
        public void ResetOctree()
        {
            // Debug.Log("ResetOctree");
            octreeContainsObjects = false;

            if (octree == null) { octree = new ObjectOctree.Cell(); return; }

            BaseOctree.Cell[] cells = octree.cells;
            octree.Reset(ref cells);
        } 
        // ==========================================================================================================================

        public void CalcOctreeSize(Bounds bounds)
        {
            float size;
            int levels;

            Methods.SnapBoundsAndPreserveArea(ref bounds, cellSize, useCells ? cellOffset : Vector3.zero);
            
            if (useCells)
            {
                float areaSize = Mathf.Max(Methods.GetMax(bounds.size), cellSize);
                levels = Mathf.CeilToInt(Mathf.Log(areaSize / cellSize, 2));
                size = (int)Mathf.Pow(2, levels) * cellSize;
            }
            else
            {
                size = Methods.GetMax(bounds.size);
                levels = 0;
            }
            
            if (levels == 0 && octree is ObjectOctree.Cell) octree = new ObjectOctree.MaxCell();
            else if (levels > 0 && octree is ObjectOctree.MaxCell) octree = new ObjectOctree.Cell();

            octree.maxLevels = levels;
            octree.bounds.center = bounds.center;
            octree.bounds.size = new Vector3(size, size, size);

            // Debug.Log("size " + size + " levels " + levels);
        } 
        // ==========================================================================================================================
        
        public void ApplyChanges()
        {
            validRebakeLighting = rebakeLighting && !validCopyBakedLighting && !Application.isPlaying && Application.isEditor;

            for (int i = 0; i < changedCells.Count; i++)
            {
                ObjectOctree.MaxCell maxCell = changedCells[i];
                maxCell.hasChanged = false;

                maxCell.ApplyChanges(this);
            }

            changedCells.Clear();
        }
        
        public void CombineAll()
        {
            // System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            // stopwatch.Start();

            DestroyCombinedObjects();
            
            if (!octreeContainsObjects) AddObjectsAutomatically();
            if (!octreeContainsObjects) return;

            validRebakeLighting = rebakeLighting && !validCopyBakedLighting && !Application.isPlaying && Application.isEditor;
           
            totalVertices = totalTriangles = originalTotalVertices = originalTotalTriangles = originalDrawCalls = newDrawCalls = 0;

            for (int i = 0; i < lodParentHolders.Length; i++)
            {
                LodParentHolder lodParentHolder = lodParentHolders[i];
                if (!lodParentHolder.found) continue;

                if (lodParentHolder.go == null) lodParentHolder.Create(this, i);
                
                octree.CombineMeshes(this, i);
            }

            if (MeshCombineJobManager.instance.jobSettings.combineJobMode == MeshCombineJobManager.CombineJobMode.CombineAtOnce) MeshCombineJobManager.instance.ExecuteJobs();

            combinedActive = true;
            combined = true;

            activeOriginal = false;
            ExecuteHandleObjects(activeOriginal, HandleComponent.Disable, HandleComponent.Disable);

            // stopwatch.Stop();
            // Debug.Log("Combine time " + stopwatch.ElapsedMilliseconds);

        } 
        // ==========================================================================================================================
        
        void InitAndResetLodParentsCount()
        {
            for (int i = 0; i < lodParentHolders.Length; i++)
            {
                if (lodParentHolders[i] == null) lodParentHolders[i] = new LodParentHolder(i + 1);
                else lodParentHolders[i].Reset();
            }
        }

        public void AddObjectsFromSearchParent()
        {
            if (searchOptions.parent == null)
            {
                Debug.Log("You need to assign a 'Parent' GameObject in which meshes will be searched");
                return;
            }
            
            LODGroup[] lodGroups = searchOptions.parent.GetComponentsInChildren<LODGroup>(true);
            AddLodGroups(lodGroups);
            
            Transform[] transforms = searchOptions.parent.GetComponentsInChildren<Transform>(true);
            AddTransforms(transforms);
        } 
        // ==========================================================================================================================

        void AddLodGroups(LODGroup[] lodGroups, bool useSearchOptions = true)
        {
            List<CachedLodGameObject> cachedLodRenderers = new List<CachedLodGameObject>();

            for (int i = 0; i < lodGroups.Length; i++)
            {
                LODGroup lodGroup = lodGroups[i];

                if (searchOptions.onlyActive && !lodGroup.gameObject.activeInHierarchy) continue;

                LOD[] lods = lodGroup.GetLODs();
                int lodParentIndex = lods.Length - 1;

                if (lodParentIndex <= 0) continue;
                // Debug.Log(lods.Length);

                for (int j = 0; j < lods.Length; j++)
                {
                    LOD lod = lods[j];

                    for (int k = 0; k < lod.renderers.Length; k++)
                    {
                        Renderer r = lod.renderers[k];

                        if (r == null) { cachedLodRenderers.Clear(); goto breakLoop; }

                        Transform lodT = r.transform;

                        uniqueLodObjects.Add(lodT);
                        CachedGameObject cachedGO = ValidObject(lodT, true, useSearchOptions);

                        if (cachedGO == null) { cachedLodRenderers.Clear(); goto breakLoop; }
                        else
                        {
                            cachedLodRenderers.Add(new CachedLodGameObject(cachedGO, lodParentIndex, j));
                            foundLodGroups.Add(lodGroup);
                        }
                    }
                }

                breakLoop:

                for (int j = 0; j < cachedLodRenderers.Count; j++)
                {
                    CachedLodGameObject cachedLodGO = cachedLodRenderers[j];
                    if (!hasFoundFirstObject) { bounds.center = cachedLodGO.mr.bounds.center; hasFoundFirstObject = true; }
                    bounds.Encapsulate(cachedLodGO.mr.bounds);
                    foundLodObjects.Add(cachedLodGO);
                    lodParentHolders[lodParentIndex].found = true;
                    lodParentHolders[lodParentIndex].lods[cachedLodGO.lodLevel]++;
                }

                cachedLodRenderers.Clear();
            }
        }
        
        void AddTransforms(Transform[] transforms, bool useSearchOptions = true)
        {
            int uniqueLodObjectsCount = uniqueLodObjects.Count;
            
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform t = transforms[i];
                
                if (uniqueLodObjectsCount > 0 && uniqueLodObjects.Contains(t)) continue;
                
                CachedGameObject cachedGO = ValidObject(t, false, useSearchOptions);
                
                if (cachedGO != null)
                {
                    if (!hasFoundFirstObject) { bounds.center = cachedGO.mr.bounds.center; hasFoundFirstObject = true; }
                    bounds.Encapsulate(cachedGO.mr.bounds);
                    foundObjects.Add(cachedGO);
                    lodParentHolders[0].lods[0]++;
                }
            }
            
            if (foundObjects.Count > 0) lodParentHolders[0].found = true;
            // Debug.Log("Count " + count);
            // Debug.Log(foundObjects.Count);
        } 
        // ==========================================================================================================================

        CachedGameObject ValidObject(Transform t, bool isLodObject, bool useSearchOptions = true)
        {
            GameObject go = t.gameObject;
            
            MeshRenderer mr = t.GetComponent<MeshRenderer>();
            if (mr == null) return null;

            MeshFilter mf = t.GetComponent<MeshFilter>();
            if (mf == null) return null;
            
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) return null;

            if (!mesh.isReadable)
            {
                Debug.LogError("Mesh Combine Studio -> Read/Write is disabled on the mesh on GameObject " + go.name + " and can't be combined. Click the 'Make Meshes Readable' in the MCS Inspector to make it automatically readable in the mesh import settings.");
                unreadableMeshes.Add(mesh);
                return null;
            }

            if (useSearchOptions)
            {
                if (searchOptions.onlyActive && !go.activeInHierarchy) return null;

                if (searchOptions.useLayerMask)
                {
                    int layer = 1 << t.gameObject.layer;
                    if ((searchOptions.layerMask.value & layer) != layer) return null;
                }

                if (searchOptions.useTag)
                {
                    if (!t.CompareTag(searchOptions.tag)) return null;
                }

                if (searchOptions.useComponentsFilter)
                {
                    if (searchOptions.componentCondition == SearchOptions.ComponentCondition.And)
                    {
                        bool pass = true;
                        for (int j = 0; j < searchOptions.componentNameList.Count; j++)
                        {
                            if (t.GetComponent(searchOptions.componentNameList[j]) == null) { pass = false; break; }
                        }
                        if (!pass) return null;
                    }
                    else
                    {
                        bool pass = false;
                        for (int j = 0; j < searchOptions.componentNameList.Count; j++)
                        {
                            if (t.GetComponent(searchOptions.componentNameList[j]) != null) { pass = true; break; }
                        }
                        if (!pass) return null;
                    }
                }

                if (searchOptions.useSearchBox)
                {
                    if (searchOptions.objectCenter == ObjectCenter.BoundsCenter)
                    {
                        if (!searchOptions.searchBoxBounds.Contains(mr.bounds.center)) return null;
                    }
                    else if (!searchOptions.searchBoxBounds.Contains(t.position)) return null;
                }

                if (searchOptions.onlyStatic && !go.isStatic) return null;

                if (isLodObject)
                {
                    if (searchOptions.useVertexInputLimitLod && mesh.vertexCount > searchOptions.vertexInputLimitLod) return null;
                }
                else
                {
                    if (searchOptions.useVertexInputLimit && mesh.vertexCount > searchOptions.vertexInputLimit) return null;
                }
                if (useVertexOutputLimit && mesh.vertexCount > vertexOutputLimit) return null;


                if (searchOptions.useMaxBoundsFactor && useCells)
                {
                    if (Methods.GetMax(mr.bounds.size) > cellSize * searchOptions.maxBoundsFactor) return null;
                }

                if (searchOptions.useNameContains)
                {
                    bool found = false;
                    for (int k = 0; k < searchOptions.nameContainList.Count; k++)
                    {
                        if (Methods.Contains(t.name, searchOptions.nameContainList[k])) { found = true; break; }
                    }
                    if (!found) return null;
                }
            }
            
            return new CachedGameObject(go, t, mr, mf, mesh);
        }

        public void RestoreOriginalRenderersAndLODGroups()
        {
            if (activeOriginal) return;
            activeOriginal = true;
            ExecuteHandleObjects(activeOriginal, HandleComponent.Disable, HandleComponent.Disable);
        }

        public void GetOriginalRenderersAndLODGroups()
        {
            if (foundObjects.Count != 0 || foundLodObjects.Count != 0 || foundLodGroups.Count != 0) return;

            foundObjects.Clear();
            foundLodObjects.Clear();

            DisabledMeshRenderer[] disabledMrs = FindObjectsOfType<DisabledMeshRenderer>();

            for (int i = 0; i < disabledMrs.Length; i++)
            {
                DisabledMeshRenderer disabledMr = disabledMrs[i];
                if (disabledMr.meshCombiner == this) foundObjects.Add(disabledMr.cachedGO);
            }

            DisabledLodMeshRender[] disabledLodMrs = FindObjectsOfType<DisabledLodMeshRender>();

            for (int i = 0; i < disabledLodMrs.Length; i++)
            {
                DisabledLodMeshRender disabledLodMr = disabledLodMrs[i];
                if (disabledLodMrs[i].meshCombiner == this) foundLodObjects.Add(disabledLodMr.cachedLodGO);
            }

            DisabledLODGroup[] disabledLodGroups = FindObjectsOfType<DisabledLODGroup>();

            for (int i = 0; i < disabledLodGroups.Length; i++)
            {
                DisabledLODGroup disabledLodGroup = disabledLodGroups[i];
                if (disabledLodGroup.meshCombiner == this) foundLodGroups.Add(disabledLodGroup.lodGroup);
            }

            foundLodGroups.Clear();
        }

        public void SwapCombine()
        {
            if (!combined) { CombineAll(); }

            combinedActive = !combinedActive;
            
            ExecuteHandleObjects(!combinedActive, originalMeshRenderers, originalLODGroups);
        }

        public void ExecuteHandleObjects(bool active, HandleComponent handleOriginalObjects, HandleComponent handleOriginalLodGroups)
        {
            Methods.SetChildrenActive(transform, !active);
            
            if (handleOriginalObjects == HandleComponent.Disable)
            {
                for (int i = 0; i < foundObjects.Count; i++)
                {
                    CachedGameObject cachedGO = foundObjects[i];
                    #if UNITY_EDITOR
                    if (!active) AddDisabledMeshRenderer(cachedGO);
                    else RemoveDisabledMeshRenderer(cachedGO);
                    #endif
                    if (cachedGO.mr != null) cachedGO.mr.enabled = active;
                    else Methods.ListRemoveAt(foundObjects, i--);
                }
                for (int i = 0; i < foundLodObjects.Count; i++)
                {
                    CachedLodGameObject cachedLodGO = foundLodObjects[i];
                    #if UNITY_EDITOR
                    if (!active) AddDisabledLodMeshRenderer(cachedLodGO);
                    else RemoveDisabledLodMeshRenderer(cachedLodGO);
                    #endif
                    if (cachedLodGO.mr != null) cachedLodGO.mr.enabled = active;
                    else Methods.ListRemoveAt(foundLodObjects, i--);
                }
            }
            if (handleOriginalObjects == HandleComponent.Destroy)
            {
                for (int i = 0; i < foundObjects.Count; i++)
                {
                    bool remove = false;
                    CachedGameObject cachedGO = foundObjects[i];
                    if (cachedGO.mf != null) Destroy(cachedGO.mf);
                    else remove = true;

                    if (cachedGO.mr != null) Destroy(cachedGO.mr);
                    else remove = true;

                    if (remove) Methods.ListRemoveAt(foundObjects, i--);
                }

                for (int i = 0; i < foundLodObjects.Count; i++)
                {
                    bool remove = false;
                    CachedGameObject cachedGO = foundLodObjects[i];
                    if (cachedGO.mf != null) Destroy(cachedGO.mf);
                    else remove = true;

                    if (cachedGO.mr != null) Destroy(cachedGO.mr);
                    else remove = true;

                    if (remove) Methods.ListRemoveAt(foundLodObjects, i--);
                }
            }
            
            if (handleOriginalLodGroups == HandleComponent.Disable)
            {
                foreach (LODGroup lodGroup in foundLodGroups)
                {
                    if (lodGroup != null)
                    {
                        #if UNITY_EDITOR
                        DisabledLODGroup disabledLODGroup = lodGroup.GetComponent<DisabledLODGroup>();

                        if (!active)
                        {
                            if (disabledLODGroup == null)
                            {
                                disabledLODGroup = lodGroup.gameObject.AddComponent<DisabledLODGroup>();
                                disabledLODGroup.hideFlags = HideFlags.HideInInspector;
                                disabledLODGroup.lodGroup = lodGroup;
                            }
                            disabledLODGroup.meshCombiner = this;
                        }
                        else
                        {
                            if (disabledLODGroup != null) DestroyImmediate(disabledLODGroup);
                        }
                        #endif
                        lodGroup.enabled = active;
                    }
                }
            }
            else if (handleOriginalLodGroups == HandleComponent.Destroy)
            {
                foreach (LODGroup lodGroup in foundLodGroups)
                {
                    if (lodGroup != null) Destroy(lodGroup);
                }
            }
        }

        void AddDisabledMeshRenderer(CachedGameObject cachedGO)
        {
            if (cachedGO.go == null) return;

            DisabledMeshRenderer disabledMr = cachedGO.go.GetComponent<DisabledMeshRenderer>();
            if (disabledMr == null)
            {
                disabledMr = cachedGO.go.AddComponent<DisabledMeshRenderer>();
                disabledMr.hideFlags = HideFlags.HideInInspector;
            }
            disabledMr.meshCombiner = this;
            disabledMr.cachedGO = cachedGO;
        }

        void AddDisabledLodMeshRenderer(CachedLodGameObject cachedLodGO)
        {
            if (cachedLodGO.go == null) return;

            DisabledLodMeshRender disabledLodMr = cachedLodGO.go.GetComponent<DisabledLodMeshRender>();
            if (disabledLodMr == null)
            {
                disabledLodMr = cachedLodGO.go.AddComponent<DisabledLodMeshRender>();
                disabledLodMr.hideFlags = HideFlags.HideInInspector;
            }
            disabledLodMr.meshCombiner = this;
            disabledLodMr.cachedLodGO = cachedLodGO;
        }

        void RemoveDisabledMeshRenderer(CachedGameObject cachedGO)
        {
            if (cachedGO.go == null) return;

            DisabledMeshRenderer disabledMr = cachedGO.go.GetComponent<DisabledMeshRenderer>();
            if (disabledMr != null)
            { 
                if (disabledMr.meshCombiner == this) DestroyImmediate(disabledMr);
            }
        }

        void RemoveDisabledLodMeshRenderer(CachedLodGameObject cachedLodGO)
        {
            if (cachedLodGO.go == null) return;

            DisabledLodMeshRender disabledLodMr = cachedLodGO.go.GetComponent<DisabledLodMeshRender>();
            if (disabledLodMr != null)
            {
                if (disabledLodMr.meshCombiner == this) DestroyImmediate(disabledLodMr);
            }
        }

        public void MakeMeshesReadableInImportSettings()
        {
            #if UNITY_EDITOR
            for (int i = 0; i < unreadableMeshes.Count; i++)
            {
                Mesh mesh = unreadableMeshes[i];

                string path = UnityEditor.AssetDatabase.GetAssetPath(mesh);
                if (path.Length > 0)
                {
                    UnityEditor.ModelImporter modelImporter = (UnityEditor.ModelImporter)UnityEditor.ModelImporter.GetAtPath(path);
                    modelImporter.isReadable = true;
                    modelImporter.SaveAndReimport();
                    Debug.Log("Read/Write Enabled on " + path);
                }
            }
            unreadableMeshes.Clear();
            #endif
        }

        void OnDrawGizmosSelected()
        {
            if (removeBackFaceTriangles)
            {
                if (backFaceTriangleMode == BackFaceTriangleMode.Box) Gizmos.DrawWireCube(backFaceBounds.center, backFaceBounds.size);
            }

            if (!drawGizmos) return;
            
            if (octree != null && octreeContainsObjects)
            {
                octree.Draw(this, true, !searchOptions.useSearchBox);
            }
            
            if (searchOptions.useSearchBox)
            {
                searchOptions.GetSearchBoxBounds();

                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(searchOptions.searchBoxBounds.center, searchOptions.searchBoxBounds.size);
                Gizmos.color = Color.white;
            }
        }
        // ==========================================================================================================================

        void LogOctreeInfo()
        {
            Console.Log("Cells " + ObjectOctree.MaxCell.maxCellCount + " -> Found Objects: ");
            
            LodParentHolder[] lodParentsCount = lodParentHolders;

            if (lodParentsCount == null || lodParentsCount.Length == 0) return;

            for (int i = 0; i < lodParentsCount.Length; i++)
            {
                LodParentHolder lodParentCount = lodParentsCount[i];
                if (!lodParentCount.found) continue;

                string text = "";
                text = "LOD Group " + (i + 1) + " |";

                int[] lods = lodParentCount.lods;

                for (int j = 0; j < lods.Length; j++)
                {
                    text += " " + lods[j].ToString() + " |";
                }
                Console.Log(text);
            }
        }
        
        [Serializable]
        public class LODGroupSettings
        {
            public LODSettings[] lodSettings;

            public LODGroupSettings(int lodParentIndex)
            {
                int lodCount = lodParentIndex + 1;
                lodSettings = new LODSettings[lodCount];
                float percentage = 1.0f / lodCount;

                for (int i = 0; i < lodSettings.Length; i++)
                {
                    lodSettings[i] = new LODSettings(1 - (percentage * (i + 1)));
                }
            }
        }

        [Serializable]
        public class LODSettings
        {
            public float screenRelativeTransitionHeight;
            public float fadeTransitionWidth;

            public LODSettings(float screenRelativeTransitionHeight)
            {
                this.screenRelativeTransitionHeight = screenRelativeTransitionHeight;
            }
        }

        public class LodParentHolder
        {
            public GameObject go;
            public Transform t;

            public bool found;
            public int[] lods;

            public LodParentHolder(int lodCount)
            {
                lods = new int[lodCount];
            }
            
            public void Create(MeshCombiner meshCombiner, int lodParentIndex)
            {
                go = new GameObject("LODGroup " + (lodParentIndex + 1));

                LODGroupSetup lodGroupSetup = go.AddComponent<LODGroupSetup>();
                lodGroupSetup.Init(meshCombiner, lodParentIndex);
                t = go.transform;

                Transform parentT = t.transform;
                parentT.parent = meshCombiner.transform;
            }

            public void Reset()
            {
                found = false;
                Array.Clear(lods, 0, lods.Length);
            }
        }
    }
}
﻿using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using UnityEngine.Networking;
using System.Linq;
#if C3D_TMPRO
using TMPro;
#endif

//an interface for exporting/decimating and uploading scenes and dynamic objects

namespace Cognitive3D
{
    using Debug = UnityEngine.Debug;
    using Path = System.IO.Path;
    //temporary data about a skinned mesh/canvas/terrain to be baked to mesh and exported in GLTF
    public class BakeableMesh
    {
        public GameObject tempGo;
        public MeshRenderer meshRenderer;
        public MeshFilter meshFilter;
        public bool useOriginalscale;
        public Vector3 originalScale;
    }

    //returned from get scene version. contains info about all versions of a single scene
    public class SceneVersionCollection
    {
        public long createdAt;
        public long updatedAt;
        public string id;
        public List<SceneVersion> versions = new List<SceneVersion>();
        public string customerId;
        public string sceneName;
        public bool isPublic;

        public SceneVersion GetLatestVersion()
        {
            int latest = 0;
            SceneVersion latestscene = null;
            if (versions == null) { Debug.LogError("SceneVersionCollection versions is null!"); return null; }
            for (int i = 0; i < versions.Count; i++)
            {
                if (versions[i].versionNumber > latest)
                {
                    latest = versions[i].versionNumber;
                    latestscene = versions[i];
                }
            }
            return latestscene;
        }

        public SceneVersion GetVersion(int versionnumber)
        {
            var sceneversion = versions.Find(delegate (SceneVersion obj) { return obj.versionNumber == versionnumber; });
            return sceneversion;
        }
    }

    //a specific version of a scene
    [System.Serializable]
    public class SceneVersion
    {
        public long createdAt;
        public long updatedAt;
        public int id;
        public int versionNumber;
        public float scale;
        public string sdkVersion;
        public int sessionCount;
    }

    public static class ExportUtility
    {
        private enum ExportQuadType
        {
            Canvas = 0,
            TMPro = 1,
            SpriteRenderer = 2
        };

        #region Export Scene

        /// <summary>
        /// skip exporting any geometry from the scene. just generate the folder and json file
        /// will delete any files in the export directory
        /// </summary>
        public static void ExportSceneAR()
        {
            string fullName = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name;
            string objPath = EditorCore.GetSubDirectoryPath(fullName);

            //if folder exists, delete mtl, obj, png and json contents
            if (Directory.Exists(objPath))
            {
                var files = Directory.GetFiles(objPath);
                for (int i = 0; i < files.Length; i++)
                {
                    File.Delete(files[i]);
                }
            }

            //write json settings file
            string jsonSettingsContents = "{ \"scale\":1,\"sceneName\":\"" + fullName + "\",\"sdkVersion\":\"" + Cognitive3D_Manager.SDK_VERSION + "\"}";
            File.WriteAllText(objPath + "settings.json", jsonSettingsContents);

            string debugContent = DebugInformationWindow.GetDebugContents();
            File.WriteAllText(objPath + "debug.log", debugContent);
        }

        static List<string> customTextureExports;

        /// <summary>
        /// Deletes all the temporary files exported in the active scene directory
        /// </summary>
        public static void ClearActiveSceneDirectory()
        {
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            string path = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + activeScene.name;
            if (!Directory.Exists(path))
            {
                Debug.LogWarning("Directory doesn't exist! " + path);
                return;
            }
            var files = Directory.GetFiles(path);
            Debug.Log("Delete Files:");
            foreach (var v in files)
            {
                Debug.Log(v);
            }
        }

        /// <summary>
        /// Deletes all the temporary files exported in scene directory
        /// </summary>
        /// <param name="sceneName"></param>
        public static void ClearSceneDirectory(string sceneName)
        {
            string path = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName;
            if (!Directory.Exists(path))
            {
                Debug.LogWarning("Directory doesn't exist! " + path);
                return;
            }
            var files = Directory.GetFiles(path);
            Debug.Log("Delete Files:");
            foreach (var v in files)
            {
                Debug.Log(v);
            }
        }

        /// <summary>
        /// Upload temporary geometry from the active scene to the dashboard. This handles new scene versions and entirely new scenes
        /// </summary>
        public static void UploadActiveSceneGeometry()
        {
            //refresh scene versions post upload
            System.Action<int> refreshSceneVersion = delegate (int responseCode)
            {
                if (responseCode == 200 || responseCode == 201)
                {
                    Util.logDebug("scene upload complete, refresh scene version");
                    EditorCore.RefreshSceneVersion(null); //likely completed in previous step, but just in case
                }
                else
                {
                    Util.logDebug("scene upload failed - response code " + responseCode);
                }
            };

            //upload scene geometry
            System.Action uploadScene = delegate
            {
                Cognitive3D_Preferences.SceneSettings current = Cognitive3D_Preferences.FindCurrentScene();
                if (current == null)
                {
                    Util.logError("Trying to upload to a scene with no settings! Upload cancelled");
                    return;
                }

                if (string.IsNullOrEmpty(current.SceneId))
                {
                    //new scene
                    ExportUtility.UploadDecimatedScene(current, refreshSceneVersion, null);
                }
                else
                {
                    //new version
                    ExportUtility.UploadDecimatedScene(current, refreshSceneVersion, null);
                }
            };

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                Util.logError("GetActiveScene returned an invalid scene. Upload cancelled");
                return;
            }

            Cognitive3D_Preferences.AddSceneSettings(activeScene);

            //first refresh scene version
            if (string.IsNullOrEmpty(activeScene.name))
            {
                Util.logError("Cannot Upload scenes that have not been saved yet! Upload cancelled");
            }
            else
            {
                EditorCore.RefreshSceneVersion(uploadScene);
            }
        }

        /// <summary>
        /// export all geometry for the active scene. will NOT delete existing files in this directory
        /// </summary>
        public static void ExportGLTFScene()
        {
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            List<GameObject> allRootObjects = new List<GameObject>();
            List<BakeableMesh> temp = new List<BakeableMesh>();
            string path = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + activeScene.name;

            EditorUtility.DisplayProgressBar("Export GLTF", "Bake Nonstandard Renderers", 0.10f); //generate meshes from terrain/canvas/skeletal meshes
            customTextureExports = new List<string>();
            BakeNonstandardRenderers(null, temp, path); //needs to happen before scene root transforms are grabbed - terrain spawns tree prefabs
            for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++)
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    allRootObjects.AddRange(scene.GetRootGameObjects());
                }
            }

            List<Transform> t = new List<Transform>();
            foreach (var v in allRootObjects)
            {
                if (v.GetComponent<MeshFilter>() != null && v.GetComponent<MeshFilter>().sharedMesh == null) { continue; } //skip procedural meshes
                if (v.GetComponent<CustomRenderExporter>() != null) { continue; } //skip mesh that uses custom render
                if (v.activeInHierarchy) { t.Add(v.transform); }
                //check for mesh renderers here, before nodes are constructed for invalid objects?
#if C3D_TMPRO
                if (v.GetComponent<MeshRenderer>() && v.GetComponent<TextMeshPro>()) { continue; } // skip MeshRenderer that has TMPro; otherwise you get an blank mesh
#endif
            }
            try
            {
                var exporter = new UnityGLTF.GLTFSceneExporter(t.ToArray(), null);
                exporter.SetNonStandardOverrides(temp);
                Directory.CreateDirectory(path);

                EditorUtility.DisplayProgressBar("Export GLTF", "Save GLTF and Bin", 0.50f); //export all all mesh renderers to gltf
                exporter.SaveGLTFandBin(path, "scene", customTextureExports);

                EditorUtility.DisplayProgressBar("Export GLTF", "Resize Textures", 0.75f); //resize each texture from export directory
                ResizeQueue.Enqueue(path);
                EditorApplication.update -= UpdateResize;
                EditorApplication.update += UpdateResize;
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(e);
                Debug.LogError("Could not complete GLTF Export");
            }
            finally
            {
                for (int i = 0; i < temp.Count; i++) //delete temporary generated meshes
                {
                    if (temp[i].useOriginalscale)
                    {
                        temp[i].meshRenderer.transform.localScale = temp[i].originalScale;
                    }
                    UnityEngine.Object.DestroyImmediate(temp[i].meshFilter);
                    UnityEngine.Object.DestroyImmediate(temp[i].meshRenderer);
                    if (temp[i].tempGo != null)
                        UnityEngine.Object.DestroyImmediate(temp[i].tempGo);
                }

                foreach (var tempgameobject in deleteCustomRenders)
                {
                    UnityEngine.Object.DestroyImmediate(tempgameobject);
                }
            }
        }

        internal static void GenerateSettingsFile(string path, string sceneName)
        {
            string jsonSettingsContents = "{ \"scale\":1,\"sceneName\":\"" + sceneName + "\",\"sdkVersion\":\"" + Cognitive3D_Manager.SDK_VERSION + "\"}";
            System.IO.File.WriteAllText(path + "settings.json", jsonSettingsContents);
        }

        /// <summary>
        /// used by scene and dynamics to queue resizing textures
        /// wait for update message from the editor - reading files without this delay has issues reading data
        /// </summary>
        static Queue<string> ResizeQueue = new Queue<string>();
        static void UpdateResize()
        {
            while (ResizeQueue.Count > 0)
            {
                ResizeTexturesInExportFolder(ResizeQueue.Dequeue());
            }

            EditorApplication.update -= UpdateResize;
            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        /// loads textures from file, reduces their size and saves
        /// </summary>
        static void ResizeTexturesInExportFolder(string folderpath)
        {
            var textureDivisor = Cognitive3D_Preferences.Instance.TextureResize;

            if (textureDivisor == 1) { return; }
            Texture2D texture = new Texture2D(2, 2);
            var files = Directory.GetFiles(folderpath);

            int minTextureSize = Mathf.NextPowerOfTwo(textureDivisor);

            foreach (var file in files)
            {
                if (!file.EndsWith(".png")) { continue; }

                //skip thumbnails
                if (file.EndsWith("cvr_object_thumbnail.png")) { continue; }

                string path = file;

                texture.LoadImage(File.ReadAllBytes(file));

                if (texture.width < minTextureSize && texture.height < minTextureSize) { continue; }

                var newWidth = Mathf.Max(1, Mathf.NextPowerOfTwo(texture.width) / textureDivisor);
                var newHeight = Mathf.Max(1, Mathf.NextPowerOfTwo(texture.height) / textureDivisor);

                TextureScale.Bilinear(texture, newWidth, newHeight);
                byte[] bytes = texture.EncodeToPNG();
                File.WriteAllBytes(path, bytes);
            }
        }

        #endregion

        #region Upload Scene
        static System.Action<int> UploadComplete;
        //displays popup window confirming upload, then uploads the files

        /// <summary>
        /// displays confirmation popup
        /// reads files from export directory and sends POST request to backend
        /// invokes uploadComplete if upload actually starts and PostSceneUploadResponse callback gets 200/201 responsecode
        /// </summary>
        public static void UploadDecimatedScene(Cognitive3D_Preferences.SceneSettings settings, System.Action<int> uploadComplete, System.Action<float> progressCallback)
        {
            //if uploadNewScene POST
            //else PUT to sceneexplorer/sceneid

            if (settings == null) { UploadSceneSettings = null; return; }

            UploadSceneSettings = settings;

            bool hasExistingSceneId = settings != null && !string.IsNullOrEmpty(settings.SceneId);

            bool uploadConfirmed = false;
            string sceneName = settings.SceneName;
            string[] filePaths = new string[] { };

            string sceneExportDirectory = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + settings.SceneName + Path.DirectorySeparatorChar;
            var SceneExportDirExists = Directory.Exists(sceneExportDirectory);

            if (SceneExportDirExists)
            {
                filePaths = Directory.GetFiles(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar);
            }

            //custom confirm upload popup windows
            if ((!SceneExportDirExists || filePaths.Length <= 1))
            {
                if (EditorUtility.DisplayDialog("Upload Scene", "Scene " + settings.SceneName + " has no exported geometry. Upload anyway?", "Yes", "No"))
                {
                    uploadConfirmed = true;
                    //create a json.settings file in the directory
                    string objPath = EditorCore.GetSubDirectoryPath(sceneName);

                    Directory.CreateDirectory(objPath);

                    string jsonSettingsContents = "{ \"scale\":1, \"sceneName\":\"" + settings.SceneName + "\",\"sdkVersion\":\"" + Cognitive3D_Manager.SDK_VERSION + "\"}";
                    File.WriteAllText(objPath + "settings.json", jsonSettingsContents);

                    string debugContent = DebugInformationWindow.GetDebugContents();
                    File.WriteAllText(objPath + "debug.log", debugContent);
                }
            }
            else
            {
                uploadConfirmed = true;
            }

            if (!uploadConfirmed)
            {
                UploadSceneSettings = null;
                return; //just exit now
            }

            //after confirmation because uploading an empty scene creates a settings.json file
            if (Directory.Exists(sceneExportDirectory))
            {
                filePaths = Directory.GetFiles(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar);
            }

            string[] screenshotPath = new string[0];
            if (Directory.Exists(sceneExportDirectory + "screenshot"))
            {
                screenshotPath = Directory.GetFiles(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + sceneName + Path.DirectorySeparatorChar + "screenshot");
            }
            else
            {
                Debug.Log("SceneExportWindow Upload can't find directory to screenshot");
            }

            string fileList = "Upload Files:\n";
            WWWForm wwwForm = new WWWForm();
            foreach (var f in filePaths)
            {
                if (f.ToLower().EndsWith(".ds_store"))
                {
                    Debug.Log("skip file " + f);
                    continue;
                }

                fileList += f + "\n";

                var data = File.ReadAllBytes(f);
                wwwForm.AddBinaryData("file", data, Path.GetFileName(f));
            }

            Debug.Log(fileList);

            if (screenshotPath.Length == 0)
            {
                Debug.Log("SceneExportWindow Upload can't find files in screenshot directory");
            }
            else
            {
                wwwForm.AddBinaryData("screenshot", File.ReadAllBytes(screenshotPath[0]), "screenshot.png");
            }

            if (hasExistingSceneId) //upload new verison of existing scene
            {
                Dictionary<string, string> headers = new Dictionary<string, string>();
                if (EditorCore.IsDeveloperKeyValid)
                {
                    headers.Add("Authorization", "APIKEY:DEVELOPER " + EditorCore.DeveloperKey);
                    foreach (var v in wwwForm.headers)
                    {
                        headers[v.Key] = v.Value;
                    }
                }
                EditorNetwork.Post(CognitiveStatics.PostUpdateScene(settings.SceneId), wwwForm.data, PostSceneUploadResponse, headers, true, "Upload", "Uploading new version of scene", progressCallback);//AUTH
            }
            else //upload as new scene
            {
                Dictionary<string, string> headers = new Dictionary<string, string>();
                if (EditorCore.IsDeveloperKeyValid)
                {
                    headers.Add("Authorization", "APIKEY:DEVELOPER " + EditorCore.DeveloperKey);
                    foreach (var v in wwwForm.headers)
                    {
                        headers[v.Key] = v.Value;
                    }
                }
                EditorNetwork.Post(CognitiveStatics.PostNewScene(), wwwForm.data, PostSceneUploadResponse, headers, true, "Upload", "Uploading new scene", progressCallback);//AUTH
            }

            UploadComplete = uploadComplete;
        }

        /// <summary>
        /// callback from UploadDecimatedScene
        /// </summary>
        static void PostSceneUploadResponse(int responseCode, string error, string text)
        {
            Debug.Log("UploadScene Response. [RESPONSE CODE] " + responseCode
                + (!string.IsNullOrEmpty(error) ? " [ERROR] " + error : "")
                + (!string.IsNullOrEmpty(text) ? " [TEXT] " + text : ""));

            if (responseCode != 200 && responseCode != 201)
            {
                Debug.LogError("Scene Upload Error " + error);
                SegmentAnalytics.TrackEvent("UploadingSceneError" + responseCode + "_SceneUploadPage", "SceneSetupSceneUploadPage");
                if (responseCode != 100) //ie user cancelled upload
                {
                    EditorUtility.DisplayDialog("Error Uploading Scene", "There was an error uploading the scene. Response code was " + responseCode + ".\n\nSee Console for more details", "Ok");
                }
                UploadComplete.Invoke(responseCode);
                UploadSceneSettings = null;
                UploadComplete = null;
                return;
            }

            //response can be <!DOCTYPE html><html lang=en><head><meta charset=utf-8><title>Error</title></head><body><pre>Internal Server Error</pre></body></html>
            if (text.Contains("Internal Server Error") || text.Contains("Bad Request"))
            {
                Debug.LogError("Scene Upload Error:" + text);
                EditorUtility.DisplayDialog("Error Uploading Scene", "There was an internal error uploading the scene. \n\nSee Console for more details", "Ok");
                UploadComplete.Invoke(responseCode);
                UploadSceneSettings = null;
                UploadComplete = null;
                return;
            }

            string responseText = text.Replace("\"", "");
            if (!string.IsNullOrEmpty(responseText)) //uploading a new version returns empty. uploading a new scene returns sceneid
            {
                EditorUtility.SetDirty(Cognitive3D_Preferences.Instance);
                UploadSceneSettings.SceneId = responseText;
                AssetDatabase.SaveAssets();
            }

            UploadSceneSettings.LastRevision = System.DateTime.UtcNow.ToString(System.Globalization.CultureInfo.InvariantCulture);
            GUI.FocusControl("NULL");
            EditorUtility.SetDirty(Cognitive3D_Preferences.Instance);
            AssetDatabase.SaveAssets();

            if (UploadComplete != null)
            {
                UploadComplete.Invoke(responseCode);
            }
            UploadComplete = null;

            Debug.Log("<color=green>Scene Upload Complete!</color>");
            SegmentAnalytics.SegmentProperties props = new SegmentAnalytics.SegmentProperties();
            props.buttonName = "SceneSetupSceneUploadPage";
            props.SetProperty("sceneVersion", UploadSceneSettings.VersionNumber+1);
            SegmentAnalytics.TrackEvent("UploadingSceneComplete_SceneUploadPage", props);
        }

        static Cognitive3D_Preferences.SceneSettings UploadSceneSettings;
        /// <summary>
        /// SceneSettings for the currently uploading scene
        /// </summary>
        public static void ClearUploadSceneSettings() //sometimes not set to null when init window quits
        {
            UploadSceneSettings = null;
        }

        #endregion

        #region Bake Renderers

        static List<GameObject> deleteCustomRenders;

        /// <summary>
        /// find all skeletal meshes, terrain and canvases in scene
        /// </summary>
        /// <param name="rootDynamic">if rootDynamic != null, limits baking to only child objects of that dynamic</param>
        /// <param name="meshes">returned list that hold reference to temporary mesh renderers to be deleted after export</param>
        /// <param name="path">used to bake terrain texture to file</param>
        static void BakeNonstandardRenderers(DynamicObject rootDynamic, List<BakeableMesh> meshes, string path)
        {
            SkinnedMeshRenderer[] SkinnedMeshes = UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>();
            Terrain[] Terrains = UnityEngine.Object.FindObjectsOfType<Terrain>();
            Canvas[] Canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            SpriteRenderer[] spriteRenderers= UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
            List<MeshFilter> ProceduralMeshFilters = new List<MeshFilter>();
            CustomRenderExporter[] CustomRenders = UnityEngine.Object.FindObjectsOfType<CustomRenderExporter>();
#if C3D_TMPRO
            TextMeshPro[] TextMeshPros = UnityEngine.Object.FindObjectsOfType<TextMeshPro>();
#endif
            deleteCustomRenders = new List<GameObject>();

            if (rootDynamic != null)
            {
                SkinnedMeshes = rootDynamic.GetComponentsInChildren<SkinnedMeshRenderer>();
                Terrains = rootDynamic.GetComponentsInChildren<Terrain>();
                Canvases = rootDynamic.GetComponentsInChildren<Canvas>();
                spriteRenderers = rootDynamic.GetComponentsInChildren<SpriteRenderer>();
                foreach (var mf in rootDynamic.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh != null && string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(mf.sharedMesh)))
                    {
                        ProceduralMeshFilters.Add(mf);
                    }
                }
#if C3D_TMPRO
                TextMeshPros = rootDynamic.GetComponentsInChildren<TextMeshPro>();
#endif
            }
            else
            {
                var meshfilters = UnityEngine.Object.FindObjectsOfType<MeshFilter>();
                foreach (var mf in meshfilters)
                {
                    if (mf.sharedMesh != null && string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(mf.sharedMesh)))
                    {
                        ProceduralMeshFilters.Add(mf);
                    }
                }
            }

            //count custom render and terrain separately - much heavier
            int numberOfSmallTasks = CountValidSmallTasks(SkinnedMeshes, ProceduralMeshFilters, Canvases, spriteRenderers);
            float progressPerSmallTask = 0.1f / numberOfSmallTasks;

            int numberOfLargeTasks = CountValidLargeTasks(CustomRenders, Terrains);
            float progressPerLargeTask = 0.3f / numberOfLargeTasks;

            //int numberOfTasks = numberOfSmallTasks + numberOfLargeTasks;

            float currentProgress = 0.1f;
            int currentTask = 0;

            foreach (var customRender in CustomRenders)
            {
                if (rootDynamic == null && customRender.GetComponentInParent<DynamicObject>() != null) { continue; }
                else if (rootDynamic != null && customRender.GetComponentInParent<DynamicObject>() != rootDynamic)
                {
                    //exporting dynamic, found skinned mesh in some other dynamic
                    continue;
                }
                currentProgress += progressPerLargeTask;
                currentTask++;
                EditorUtility.DisplayProgressBar("Export GLTF", "Bake Custom Render Exporters " + currentTask + "/" + CustomRenders.Length, currentProgress);

                if (!customRender.gameObject.activeInHierarchy) { continue; }
                var AllData = customRender.RenderMeshCustom();
                if (AllData == null) { continue; }

                foreach (var data in AllData)
                {
                    BakeableMesh bm = new BakeableMesh();
                    bm.tempGo = new GameObject(data.name + "BAKEABLE MESH");
                    bm.tempGo.transform.parent = data.transform;
                    bm.tempGo.transform.localRotation = Quaternion.identity;
                    bm.tempGo.transform.localPosition = Vector3.zero;
                    bm.tempGo.transform.localScale = Vector3.one;

                    bm.meshRenderer = bm.tempGo.AddComponent<MeshRenderer>();
                    bm.meshRenderer.sharedMaterial = data.material;
                    bm.meshFilter = bm.tempGo.AddComponent<MeshFilter>();

                    bm.meshFilter.sharedMesh = data.meshdata;
                    meshes.Add(bm);
                    ProceduralMeshFilters.Add(data.tempGameObject.GetComponent<MeshFilter>());
                    deleteCustomRenders.Add(data.tempGameObject);

                    string finalPath = UnityGLTF.GLTFSceneExporter.ConstructImageFilenamePath((Texture2D)data.material.mainTexture, UnityGLTF.GLTFSceneExporter.TextureMapType.Main, path);
                    //put together a list of textures to be skipped based on path
                    customTextureExports.Add(finalPath);

                    //save out the texture here, instead of keeping it in memory
                    System.IO.File.WriteAllBytes(finalPath, ((Texture2D)data.material.mainTexture).EncodeToPNG());
                }
            }

            currentTask = 0;
            foreach (var skinnedMeshRenderer in SkinnedMeshes)
            {
                currentProgress += progressPerSmallTask;
                currentTask++;
                EditorUtility.DisplayProgressBar("Export GLTF", "Bake Skinned Meshes " + currentTask + "/" + SkinnedMeshes.Length, currentProgress);
                if (!skinnedMeshRenderer.gameObject.activeInHierarchy) { continue; }
                if (rootDynamic == null && skinnedMeshRenderer.GetComponentInParent<DynamicObject>() != null)
                {
                    //skinned mesh as child of dynamic when exporting scene
                    continue;
                }
                else if (rootDynamic != null && skinnedMeshRenderer.GetComponentInParent<DynamicObject>() != rootDynamic)
                {
                    //exporting dynamic, found skinned mesh in some other dynamic
                    continue;
                }
                if (skinnedMeshRenderer.sharedMesh == null)
                {
                    continue;
                }
                BakeableMesh bm = new BakeableMesh();

                bm.tempGo = new GameObject(skinnedMeshRenderer.gameObject.name);
                bm.tempGo.transform.parent = skinnedMeshRenderer.transform;
                bm.tempGo.transform.localRotation = Quaternion.identity;
                bm.tempGo.transform.localPosition = Vector3.zero;

                bm.meshRenderer = bm.tempGo.AddComponent<MeshRenderer>();
                bm.meshRenderer.sharedMaterials = skinnedMeshRenderer.sharedMaterials;
                bm.meshFilter = bm.tempGo.AddComponent<MeshFilter>();
                var m = new Mesh();
                m.name = skinnedMeshRenderer.sharedMesh.name;
                skinnedMeshRenderer.BakeMesh(m);
                bm.meshFilter.sharedMesh = m;
                meshes.Add(bm);
            }

            currentTask = 0;
            foreach (var meshFilter in ProceduralMeshFilters)
            {
                currentProgress += progressPerSmallTask;
                currentTask++;
                EditorUtility.DisplayProgressBar("Export GLTF", "Bake Procedural Meshes " + currentTask + "/" + ProceduralMeshFilters.Count, currentProgress);
                if (meshFilter.gameObject == null) { continue; }
                if (!meshFilter.gameObject.activeInHierarchy) { continue; }
                var mr = meshFilter.GetComponent<MeshRenderer>();
                if (mr == null) { continue; }
                if (!mr.enabled) { continue; }

                if (rootDynamic == null && meshFilter.GetComponentInParent<DynamicObject>() != null)
                {
                    //skinned mesh as child of dynamic when exporting scene
                    continue;
                }
                else if (rootDynamic != null && meshFilter.GetComponentInParent<DynamicObject>() != rootDynamic)
                {
                    //exporting dynamic, found skinned mesh in some other dynamic
                    continue;
                }

                BakeableMesh bm = new BakeableMesh();
                bm.tempGo = new GameObject(meshFilter.gameObject.name);
                bm.tempGo.transform.parent = meshFilter.transform;
                bm.tempGo.transform.localRotation = Quaternion.identity;
                bm.tempGo.transform.localPosition = Vector3.zero;
                bm.tempGo.transform.localScale = Vector3.one;

                bm.meshRenderer = bm.tempGo.AddComponent<MeshRenderer>();
                bm.meshRenderer.sharedMaterials = mr.sharedMaterials;
                bm.meshFilter = bm.tempGo.AddComponent<MeshFilter>();
                var m = new Mesh();
                m.name = meshFilter.sharedMesh.name;
                m = meshFilter.sharedMesh;
                bm.meshFilter.sharedMesh = m;
                meshes.Add(bm);
            }

            currentTask = 0;
            //TODO ignore parent rotation and scale
            foreach (var v in Terrains)
            {
                currentProgress += progressPerLargeTask;
                currentTask++;
                EditorUtility.DisplayProgressBar("Export GLTF", "Bake Terrains " + currentTask + "/" + Terrains.Length, currentProgress);
                if (!v.isActiveAndEnabled) { continue; }
                if (rootDynamic == null && v.GetComponentInParent<DynamicObject>() != null)
                {
                    //terrain as child of dynamic when exporting scene
                    continue;
                }
                else if (rootDynamic != null && v.GetComponentInParent<DynamicObject>() != rootDynamic)
                {
                    //exporting dynamic, found terrain in some other dynamic
                    continue;
                }

                BakeableMesh bm = new BakeableMesh();

                bm.tempGo = new GameObject(v.gameObject.name);
                bm.tempGo.transform.parent = v.transform;
                bm.tempGo.transform.localPosition = Vector3.zero;

                //generate mesh from heightmap
                bm.meshRenderer = bm.tempGo.AddComponent<MeshRenderer>();
                bm.meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
                if (v.materialTemplate != null && v.materialTemplate.shader.name.Contains("CTS"))
                {
                    //check if CTS material
                    bm.meshRenderer.sharedMaterial.mainTexture = BakeCTSTerrainTexture(v.terrainData, v.materialTemplate);
                    bm.meshRenderer.sharedMaterial.SetFloat("_Glossiness", 0);
                }
                else
                {
                    bm.meshRenderer.sharedMaterial.mainTexture = BakeTerrainTexture(v.terrainData);
                    bm.meshRenderer.sharedMaterial.SetFloat("_Glossiness", 0);
                }
                bm.meshFilter = bm.tempGo.AddComponent<MeshFilter>();
                bm.meshFilter.sharedMesh = GenerateTerrainMesh(v);
                meshes.Add(bm);

                //trees
                foreach (var treedata in v.terrainData.treeInstances)
                {
                    var treeposition = new Vector3(treedata.position.x * v.terrainData.size.x, 0, treedata.position.z * v.terrainData.size.z) + v.transform.position;

                    //also accounts for terrain position
                    treeposition.y = v.SampleHeight(treeposition);
                    var prototype = v.terrainData.treePrototypes[treedata.prototypeIndex];

                    //instantiate tree prefabs, export everything, destroy tree prefabs
                    BakeableMesh tbm = new BakeableMesh();
                    tbm.tempGo = GameObject.Instantiate(prototype.prefab, treeposition, Quaternion.identity);

                    //scale and rotation
                    tbm.tempGo.transform.localScale = new Vector3(treedata.widthScale * tbm.tempGo.transform.localScale.x, treedata.heightScale * tbm.tempGo.transform.localScale.y, treedata.widthScale * tbm.tempGo.transform.localScale.z);
                    tbm.tempGo.transform.rotation = Quaternion.Euler(0, treedata.rotation * Mathf.Rad2Deg, 0);
                    meshes.Add(tbm);
                }
            }

#if C3D_TMPRO
            foreach (var v in TextMeshPros)
            {
                if (v.GetComponent<DynamicObject>() ==  null) // Dynamic Objects are handled separately in `ExportDynamicObects()`
                {
                    BakeQuadGameObject(v.gameObject, meshes, ExportQuadType.TMPro, false);
                }
            }
#endif
            currentTask = 0;
            foreach (var v in spriteRenderers)
            {
                currentProgress += progressPerSmallTask;
                currentTask++;
                EditorUtility.DisplayProgressBar("Export GLTF", "Bake Sprites " + currentTask + "/" + spriteRenderers.Length, currentProgress);
                //if (!v.isActiveAndEnabled) { continue; }
                if (rootDynamic == null && v.GetComponentInParent<DynamicObject>() != null)
                {
                    //spriteRenderers as child of dynamic when exporting scene
                    continue;
                }
                else if (rootDynamic != null && v.GetComponentInParent<DynamicObject>() != rootDynamic)
                {
                    //exporting dynamic, found spriteRenderers in some other dynamic
                    continue;
                }
                
                BakeQuadGameObject(v.gameObject, meshes, ExportQuadType.SpriteRenderer, false);
            }

            currentTask = 0;
            foreach (var v in Canvases)
            {
                if (v.renderMode == RenderMode.ScreenSpaceOverlay || v.renderMode == RenderMode.ScreenSpaceCamera)
                {
                    continue;
                }
                currentProgress += progressPerSmallTask;
                currentTask++;
                EditorUtility.DisplayProgressBar("Export GLTF", "Bake Canvases " + currentTask + "/" + Canvases.Length, currentProgress);
                if (!v.isActiveAndEnabled) { continue; }
                if (v.renderMode != RenderMode.WorldSpace) { continue; }
                if (rootDynamic == null && v.GetComponentInParent<DynamicObject>() != null)
                {
                    //canvas as child of dynamic when exporting scene
                    continue;
                }
                else if (rootDynamic != null && v.GetComponentInParent<DynamicObject>() != rootDynamic)
                {
                    //exporting dynamic, found canvas in some other dynamic
                    continue;
                }

                BakeCanvasGameObject(v.gameObject, meshes);
            }
        }

        private static GameObject BakeCanvasGameObject(GameObject v, List<BakeableMesh> meshes)
        {
            BakeableMesh bm = new BakeableMesh();
            bm.tempGo = new GameObject(v.gameObject.name);

            bm.tempGo.transform.parent = v.transform;
            bm.tempGo.transform.position = v.transform.position;
            bm.tempGo.transform.localRotation = Quaternion.identity;
            bm.tempGo.transform.localScale = Vector3.one;

            //remove transform scale
            float width = 0;
            float height = 0;

            var rt = v.GetComponent<RectTransform>();
            width = rt.sizeDelta.x;
            height = rt.sizeDelta.y;

            bm.meshRenderer = bm.tempGo.AddComponent<MeshRenderer>();
            bm.meshRenderer.sharedMaterial = new Material(Shader.Find("Hidden/Cognitive/Canvas Export Shader")); //2 sided transparent diffuse
            Texture2D screenshot;
            //bake texture from render

            screenshot = TextureBakeCanvas(v.transform, width, height);
            screenshot.name = v.gameObject.GetInstanceID().ToString(); //use a unqiue texture name for each canvas - unlikely two will be identical
            bm.meshRenderer.sharedMaterial.mainTexture = screenshot;
            bm.meshFilter = bm.tempGo.AddComponent<MeshFilter>();
            Mesh mesh;
            
            //write simple quad
            mesh = GenerateQuadMesh(v.gameObject.name, Mathf.Max(width, height), Mathf.Max(width, height));
            bm.meshFilter.sharedMesh = mesh;
            meshes.Add(bm);

            return bm.tempGo;
        }

        private static GameObject BakeQuadGameObject(GameObject v, List<BakeableMesh> meshes, ExportQuadType type, bool dyn)
        {
            BakeableMesh bm = new BakeableMesh();
            bm.tempGo = new GameObject(v.gameObject.name);
            if (type != ExportQuadType.TMPro)
            {
                bm.tempGo.transform.parent = v.transform;
            }
            bm.tempGo.transform.localScale = Vector3.one;
            bm.tempGo.transform.localRotation = Quaternion.identity;

            //remove transform scale
            float width = 0;
            float height = 0;
            bm.tempGo.transform.position = v.transform.position;
            if (type == ExportQuadType.Canvas)
            {
                var rt = v.GetComponent<RectTransform>();
                width = rt.sizeDelta.x;
                height = rt.sizeDelta.y;
                if (Mathf.Approximately(width, height))
                {
                    //centered
                }
                else if (height > width) //tall
                {
                    //half of the difference between width and height
                    bm.tempGo.transform.position += (bm.tempGo.transform.right) * (height - width) / 2;
                }
                else //wide
                {
                    //half of the difference between width and height
                    bm.tempGo.transform.position += (bm.tempGo.transform.up) * (width - height) / 2;
                }
            }
            else if (type == ExportQuadType.TMPro)
            {
                MeshRenderer mr = v.GetComponent<MeshRenderer>();
                width = mr.bounds.size.x;
                height = mr.bounds.size.y;
                bm.tempGo.transform.position = mr.bounds.center;
            }
            else if (type == ExportQuadType.SpriteRenderer)
            {
                SpriteRenderer sr = v.GetComponent<SpriteRenderer>();
                width = (sr.bounds.extents.x * 2) / sr.transform.localScale.x;
                height = (sr.bounds.extents.y * 2) / sr.transform.localScale.y;
                bm.tempGo.transform.position = sr.bounds.center;
            }

            bm.meshRenderer = bm.tempGo.AddComponent<MeshRenderer>();
            bm.meshRenderer.sharedMaterial = new Material(Shader.Find("Hidden/Cognitive/Canvas Export Shader")); //2 sided transparent diffuse
            Texture2D screenshot;
            //bake texture from render
            if (type == ExportQuadType.SpriteRenderer)
            {
                SpriteRenderer sr = v.GetComponent<SpriteRenderer>();
                screenshot = TextureBake(v.transform, type, sr.bounds.extents.x*2, sr.bounds.extents.y*2);
                screenshot.name = AssetDatabase.GetAssetPath(sr.sprite).GetHashCode().ToString();
            }
            else //text mesh pro. canvas should be handled in a different function
            {
                screenshot = TextureBake(v.transform, type, width, height);
                screenshot.name = v.gameObject.GetInstanceID().ToString();
            }
            
            bm.meshRenderer.sharedMaterial.mainTexture = screenshot;
            bm.meshFilter = bm.tempGo.AddComponent<MeshFilter>();
            Mesh mesh;
            //write simple quad
            if (dyn)
            {
                mesh = GenerateQuadMesh(v.gameObject.name + type.ToString(), Mathf.Max(width, height) / v.transform.lossyScale.x, Mathf.Max(width, height) / v.transform.lossyScale.y);
            }
            else
            {
                mesh = GenerateQuadMesh(v.gameObject.name + type.ToString(), Mathf.Max(width, height), Mathf.Max(width, height));
            }
            bm.meshFilter.sharedMesh = mesh;
            meshes.Add(bm);
            if (dyn)
            {
                bm.tempGo.AddComponent<DynamicObject>();
            }

            return bm.tempGo;
        }

        private static int CountValidSmallTasks(SkinnedMeshRenderer[] skinnedMeshes, List<MeshFilter> proceduralMeshFilters, Canvas[] canvases, SpriteRenderer[] spriteRenderers)
        {
            int number = 0;

            foreach (var skinnedMeshRenderer in skinnedMeshes)
            {
                if (!skinnedMeshRenderer.gameObject.activeInHierarchy) { continue; }
                if (skinnedMeshRenderer.sharedMesh == null)
                {
                    continue;
                }
                number++;
            }

            foreach (var meshFilter in proceduralMeshFilters)
            {
                if (meshFilter.gameObject == null) { continue; }
                if (!meshFilter.gameObject.activeInHierarchy) { continue; }
                var mr = meshFilter.GetComponent<MeshRenderer>();
                if (mr == null) { continue; }
                if (!mr.enabled) { continue; }
                number++;
            }

            foreach (var v in canvases)
            {
                if (!v.isActiveAndEnabled) { continue; }
                if (v.renderMode != RenderMode.WorldSpace) { continue; }
                number++;
            }

            foreach (var v in spriteRenderers)
            {
                if (!v.enabled) { continue; }
                number++;
            }
            return number;
        }

        private static int CountValidLargeTasks(CustomRenderExporter[] customRenderExporters, Terrain[] terrains)
        {
            int number = 0;

            foreach (var v in customRenderExporters)
            {
                if (!v.gameObject.activeInHierarchy) { continue; }
                number++;
            }

            foreach (var v in terrains)
            {
                if (!v.isActiveAndEnabled) { continue; }
                number++;
            }

            return number;
        }

        /// <summary>
        /// returns a low resolution mesh based on the heightmap of a terrain
        /// </summary>
        /// <param name="terrain">the terrain to bake</param>
        public static Mesh GenerateTerrainMesh(Terrain terrain)
        {
            //CONSIDER splitting terrain into different mesh. too many polygons causes issues?
            float downsample = terrain.terrainData.heightmapResolution / 256f;
            downsample = Mathf.Max(downsample, 1);
            
            //sample counts > 256x256 will cause issues because Unity doesn't handle meshes with vertex count > 65536
            //to automatically work around this, the downsample size is calculated so the resulting mesh will have 65536 vertices

            Mesh mesh = new Mesh();
            mesh.name = "temp";

            //distance between each sample point
            var widthSamplesCount = (int)(terrain.terrainData.heightmapResolution / downsample);
            var heightSamplesCount = (int)(terrain.terrainData.heightmapResolution / downsample);

            Vector3[] vertices = new Vector3[widthSamplesCount * heightSamplesCount];
            Vector2[] uv = new Vector2[widthSamplesCount * heightSamplesCount];
            Vector4[] tangents = new Vector4[widthSamplesCount * heightSamplesCount];
            Vector2 uvScale = new Vector2(1.0f / (widthSamplesCount - 1), 1.0f / (widthSamplesCount - 1));
            Vector3 sizeScale = new Vector3(terrain.terrainData.size.x / (widthSamplesCount), 1/*terrain.terrainData.size.y*/, terrain.terrainData.size.z / (heightSamplesCount));

            //generate mesh strips + Assign them to the mesh
            for (int y = 0; y < heightSamplesCount; y++)
            {
                for (int x = 0; x < widthSamplesCount; x++)
                {
                    float pixelHeight = terrain.terrainData.GetHeight((int)(x * downsample), (int)(y * downsample));
                    Vector3 vertex = new Vector3(x, pixelHeight, y);
                    vertices[y * widthSamplesCount + x] = Vector3.Scale(sizeScale, vertex);
                    uv[y * widthSamplesCount + x] = Vector2.Scale(new Vector2(x, y), uvScale);
                    tangents[y * widthSamplesCount + x] = new Vector4(1, 1, 1, -1.0f);
                    //Debug.DrawRay(vertices[y * widthSamplesCount + x], Vector3.up * 10, Color.white, 20);
                }
            }
            mesh.vertices = vertices;
            mesh.uv = uv;

            int[] triangles = new int[(heightSamplesCount - 1) * (widthSamplesCount - 1) * 6];
            int index = 0;
            for (int y = 0; y < heightSamplesCount - 1; y++)
            {
                for (int x = 0; x < widthSamplesCount - 1; x++)
                {
                    triangles[index++] = (y * widthSamplesCount) + x;
                    triangles[index++] = ((y + 1) * widthSamplesCount) + x;
                    triangles[index++] = (y * widthSamplesCount) + x + 1;
                    triangles[index++] = ((y + 1) * widthSamplesCount) + x;
                    triangles[index++] = ((y + 1) * widthSamplesCount) + x + 1;
                    triangles[index++] = (y * widthSamplesCount) + x + 1;
                }
            }
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.tangents = tangents;
            return mesh;
        }

        public static Texture2D BakeCTSTerrainTexture(TerrainData data, Material material)
        {
            if (data == null) return new Texture2D(1, 1);
            
            //read splatmaps and texture arrays
            var t = material.GetTexture("_Texture_Array_Albedo");
            var albedos = t as Texture2DArray;

            //get albedo indexes
            int[] albedoIndex = new int[6];
            albedoIndex[0] = (int)material.GetFloat("_Texture_1_Albedo_Index");
            albedoIndex[1] = (int)material.GetFloat("_Texture_2_Albedo_Index");
            albedoIndex[2] = (int)material.GetFloat("_Texture_3_Albedo_Index");
            albedoIndex[3] = (int)material.GetFloat("_Texture_4_Albedo_Index");
            albedoIndex[4] = (int)material.GetFloat("_Texture_5_Albedo_Index");
            albedoIndex[5] = (int)material.GetFloat("_Texture_6_Albedo_Index");

            //get colour values
            Vector4[] colors = new Vector4[6];
            colors[0] = material.GetVector("_Texture_1_Color");
            colors[1] = material.GetVector("_Texture_2_Color");
            colors[2] = material.GetVector("_Texture_3_Color");
            colors[3] = material.GetVector("_Texture_4_Color");
            colors[4] = material.GetVector("_Texture_5_Color");
            colors[5] = material.GetVector("_Texture_6_Color");

            Texture2D s1 = material.GetTexture("_Texture_Splat_1") as Texture2D;
            Texture2D s2 = material.GetTexture("_Texture_Splat_2") as Texture2D;

            Texture2D[] albedoTextures = new Texture2D[6];
            for (int i = 0; i < albedoIndex.Length; i++)
            {
                if (albedoIndex[i] < 0) albedoIndex[i] = 0;
                Color32[] pixels = albedos.GetPixels32(albedoIndex[i], 0);
                albedoTextures[i] = new Texture2D(albedos.width, albedos.height);
                albedoTextures[i].SetPixels32(pixels);
                albedoTextures[i].Apply();
            }

            Texture2D finalTexture = new Texture2D((int)data.size.x, (int)data.size.z);
            Color c = Color.white;

            int textureResolution = albedos.width;

            float upscalewidth = (float)finalTexture.width / (float)data.alphamapWidth;
            float upscaleheight = (float)finalTexture.height / (float)data.alphamapHeight;

            //go through each pixel in the splatmap?
            for (int x = 0; x < finalTexture.width; x++)
            {
                for (int y = 0; y < finalTexture.height; y++)
                {
                    var p1 = s1.GetPixel((int)(x / upscalewidth), (int)(y / upscaleheight));
                    var p2 = s2.GetPixel((int)(x / upscalewidth), (int)(y / upscaleheight));

                    int selectedIndex = 1;
                    if (p1.b > 0.2f)
                        selectedIndex = 2;
                    if (p2.g > 0.2f)
                        selectedIndex = 5;
                    if (p2.r > 0.4f)
                        selectedIndex = 4;
                    if (p1.a > 0.1f)
                        selectedIndex = 3;

                    var texture = albedoTextures[selectedIndex];
                    //get pixel (ignore scaling) modulo to stay within bounds and multiply color
                    c = texture.GetPixel(x % textureResolution, y % textureResolution) * (Color)colors[selectedIndex];
                    c.a = 1;

                    //grid
                    //Color c = white;
                    //if (x % 10 == 0 || y % 10 == 0)
                    //  c = grey;

                    finalTexture.SetPixel(x, y, c);
                }
            }

            finalTexture.Apply();

            return finalTexture;
        }

        public static Texture2D BakeGridTerrainTexture(TerrainData data, Material material)
        {
            if (data == null) return new Texture2D(1, 1);

            Texture2D finalTexture = new Texture2D((int)data.size.x, (int)data.size.z);
            Color c = Color.white;
            Color white = Color.white;
            Color grey = Color.grey;

            //go through each pixel in the splatmap?
            for (int x = 0; x < finalTexture.width; x++)
            {
                for (int y = 0; y < finalTexture.height; y++)
                {
                    //grid
                    c = white;
                    if (x % 10 == 0 || y % 10 == 0)
                        c = grey;

                    finalTexture.SetPixel(x, y, c);
                }
            }

            finalTexture.Apply();

            return finalTexture;
        }

        /// <summary>
        /// read terrain data and sample texture from splatmaps
        /// returns texture2d from the baked data
        /// </summary>
        public static Texture2D BakeTerrainTexture(TerrainData data)
        {
            if (data == null) return new Texture2D(1, 1);
            
            float[,,] maps = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);

            //LIMIT to 6 layers for now! rbga + black + transparency?
            int layerCount = Mathf.Min(maps.GetLength(2), 6);

            bool[] textureReadable = new bool[layerCount]; //set terrain textures to readable
            for (int i = 0; i < layerCount; i++)
            {
                try
                {
                    if (GetTextureImportFormat(data.terrainLayers[i].diffuseTexture, out textureReadable[i]))
                    {
                        Texture2D originalTexture = data.terrainLayers[i].diffuseTexture as Texture2D;
                        SetTextureImporterFormat(originalTexture, true);
                    }
                }
                catch { }
            }

            Texture2D outTex = new Texture2D((int)data.size.x, (int)data.size.z);
            outTex.name = data.name.Replace(' ', '_');

            //these are used because texturemap = 512 and resolution = 513
            float upscalewidth = (float)outTex.width / (float)data.alphamapWidth;
            float upscaleheight = (float)outTex.height / (float)data.alphamapHeight;

            float[] colorAtLayer = new float[layerCount];

            Vector2 TerrainSize = new Vector2(data.size.x, data.size.z);
            TerrainLayer[] layers = data.terrainLayers;
            //get highest value splatmap at point and write terrain texture to baked texture
            for (int y = 0; y < outTex.height; y++)
            {
                for (int x = 0; x < outTex.width; x++)
                {
                    for (int i = 0; i < colorAtLayer.Length; i++)
                    {
                        colorAtLayer[i] = maps[(int)(y / upscaleheight), (int)(x / upscalewidth), i];
                    }
                    //highest value splat
                    int highestMap = 0;
                    float highestMapValue = 0;
                    for (int i = 0; i < colorAtLayer.Length; i++)
                    {
                        if (colorAtLayer[i] > highestMapValue)
                        {
                            highestMapValue = colorAtLayer[i];
                            highestMap = i;
                        }
                    }
                    //write terrain texture to baked texture
                    if (layers.Length > 0 && layers[highestMap].diffuseTexture != null)
                    {
                        Vector2 imageSize = new Vector2(layers[highestMap].diffuseTexture.width, layers[highestMap].diffuseTexture.height);
                        Vector2 tileSize = layers[highestMap].tileSize;
                        Vector2 imageScaling = TerrainSize / layers[highestMap].tileSize;
                        Vector2 imageOffset = layers[highestMap].tileOffset;

                        Color color = layers[highestMap].diffuseTexture.GetPixel(
                            (int)((x + imageOffset.x) * imageSize.x / TerrainSize.x * (TerrainSize.x / tileSize.x)),
                            (int)((y + imageOffset.y) * imageSize.y / TerrainSize.y * (TerrainSize.y / tileSize.y)));
                        outTex.SetPixel(x, y, color);
                    }
                    else
                    {
                        outTex.SetPixel(x, y, Color.red);
                    }
                }
            }
            outTex.Apply();

            //terrain texture importer to original read/write settings
            for (int i = 0; i < layerCount; i++)
            {
                try
                {
                    bool ignored;
                    if (GetTextureImportFormat(data.terrainLayers[i].diffuseTexture, out ignored))
                    {
                        Texture2D originalTexture = data.terrainLayers[i].diffuseTexture as Texture2D;
                        SetTextureImporterFormat(originalTexture, textureReadable[i]);
                    }
                }
                catch { }
            }

            return outTex;
        }

        /// <summary>
        /// returns true if texture read/writable
        /// </summary>
        public static bool GetTextureImportFormat(Texture2D texture, out bool isReadable)
        {
            isReadable = false;
            if (null == texture) { return false; }
            string assetPath = AssetDatabase.GetAssetPath(texture);
            var tImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (tImporter != null)
            {
                tImporter.textureType = TextureImporterType.Default;
                isReadable = tImporter.isReadable;
                return true;
            }
            return false;
        }

        /// <summary>
        /// sets texture to be read/writable
        /// </summary>
        public static void SetTextureImporterFormat(Texture2D texture, bool isReadable)
        {
            if (null == texture) return;
            string assetPath = AssetDatabase.GetAssetPath(texture);
            var tImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (tImporter != null)
            {
                tImporter.textureType = TextureImporterType.Default;
                tImporter.isReadable = isReadable;
                AssetDatabase.ImportAsset(assetPath);
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// return a mesh quad with certain dimensions. used to bake canvases
        /// </summary>
        /// <param name="meshName"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public static Mesh GenerateQuadMesh(string meshName, float width, float height)
        {
            Vector3 size = new Vector3(width, height, 0);
            Vector3 pivot = size / 2;

            Mesh m = new Mesh();
            m.name = meshName;

            var verts = new Vector3[4];
            verts[0] = new Vector3(0, 0, 0) - pivot;
            verts[1] = new Vector3(width, 0, 0) - pivot;
            verts[2] = new Vector3(0, height, 0) - pivot;
            verts[3] = new Vector3(width, height, 0) - pivot;
            m.vertices = verts;

            var tris = new int[6];
            tris[0] = 0;
            tris[1] = 2;
            tris[2] = 1;
            tris[3] = 2;
            tris[4] = 3;
            tris[5] = 1;
            m.triangles = tris;

            var norms = new Vector3[4];
            norms[0] = -Vector3.forward;
            norms[1] = -Vector3.forward;
            norms[2] = -Vector3.forward;
            norms[3] = -Vector3.forward;
            m.normals = norms;

            //if width == height, 0-1 uvs are fine
            if (Mathf.Approximately(width, height))
            {
                var uvs = new Vector2[4];
                uvs[0] = new Vector2(0, 0);
                uvs[1] = new Vector2(1, 0);
                uvs[2] = new Vector2(0, 1);
                uvs[3] = new Vector2(1, 1);
                m.uv = uvs;
            }
            else if (height > width)
            {
                var uvs = new Vector2[4];
                uvs[0] = new Vector2(0, 0);
                uvs[1] = new Vector2(width / height, 0);
                uvs[2] = new Vector2(0, 1);
                uvs[3] = new Vector2(width / height, 1);
                m.uv = uvs;
            }
            else
            {
                var uvs = new Vector2[4];
                uvs[0] = new Vector2(0, 0);
                uvs[1] = new Vector2(1, 0);
                uvs[2] = new Vector2(0, height / width);
                uvs[3] = new Vector2(1, height / width);
                m.uv = uvs;
            }

            return m;
        }

        /// <summary>
        /// returns texture2d baked from canvas target
        /// </summary>
        private static Texture2D TextureBake(Transform target, ExportQuadType type, float width, float height, int resolution = 512)
        {
            GameObject cameraGo = new GameObject("Temp_Camera " + target.gameObject.name);
            Camera cam = cameraGo.AddComponent<Camera>();

            //snap camera to canvas position
            cameraGo.transform.rotation = target.rotation;
            cameraGo.transform.position = target.position - target.forward * 0.05f;

            if (type == ExportQuadType.Canvas) // use rect bounds for canvas
            {
                if (Mathf.Approximately(width, height))
                {
                    //centered
                }
                else if (height > width) //tall
                {
                    //half of the difference between width and height
                    cameraGo.transform.position += (cameraGo.transform.right) * (height - width) / 2;
                }
                else //wide
                {
                    //half of the difference between width and height
                    cameraGo.transform.position += (cameraGo.transform.up) * (width - height) / 2;
                }
            }
            else if (type == ExportQuadType.TMPro)
            {
                cameraGo.transform.position = target.gameObject.GetComponent<MeshRenderer>().bounds.center - target.transform.forward * 0.05f;
            }
            else if (type == ExportQuadType.SpriteRenderer)
            {
                cameraGo.transform.position = target.gameObject.GetComponent<SpriteRenderer>().bounds.center - target.transform.forward * 0.05f;
            }
            cam.nearClipPlane = 0.04f;
            cam.farClipPlane = 0.06f;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(width, height) / 2;
            cam.clearFlags = CameraClearFlags.Color; //WANT TO CLEAR EVERYTHING FROM THIS CAMERA
            cam.backgroundColor = Color.clear;

            //create render texture and assign to camera
            RenderTexture rt = RenderTexture.GetTemporary(resolution, resolution, 16);
            RenderTexture.active = rt;
            cam.targetTexture = rt;

            Dictionary<GameObject, int> originallayers = new Dictionary<GameObject, int>();
            List<Transform> children = new List<Transform>();
            EditorCore.RecursivelyGetChildren(children, target);

            //set camera to render unassigned layer
            int layer = EditorCore.FindUnusedLayer();
            if (layer == -1) { Debug.LogWarning("TextureBake couldn't find unused layer, texture generation might be incorrect"); }

            if (layer != -1)
            {
                cam.cullingMask = 1 << layer;
            }

            //save all canvas layers. put on unassigned layer then render
            try
            {
                if (layer != -1)
                {
                    foreach (var v in children)
                    {
                        originallayers.Add(v.gameObject, v.gameObject.layer);
                        v.gameObject.layer = layer;
                    }
                }
                //render to texture
                cam.Render();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            //reset dynamic object layers
            if (layer != -1)
            {
                foreach (var v in originallayers)
                {
                    v.Key.layer = v.Value;
                }
            }

            //write rendertexture to png
            Texture2D tex = new Texture2D(resolution, resolution);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            //delete temporary camera
            UnityEngine.Object.DestroyImmediate(cameraGo);

            return tex;
        }

        private static Texture2D TextureBakeCanvas(Transform target, float width, float height, int resolution = 512)
        {
            GameObject cameraGo = new GameObject("Temp_Camera " + target.gameObject.name);
            Camera cam = cameraGo.AddComponent<Camera>();

            //snap camera to canvas position
            cameraGo.transform.rotation = target.rotation;
            cameraGo.transform.position = target.position - target.forward * 2f;

            cam.nearClipPlane = 1f;
            cam.farClipPlane = 4f;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(width * target.lossyScale.x, height * target.lossyScale.z) / 2;
            cam.clearFlags = CameraClearFlags.Color; //WANT TO CLEAR EVERYTHING FROM THIS CAMERA
            cam.backgroundColor = Color.clear;

            //create render texture and assign to camera
            RenderTexture rt = RenderTexture.GetTemporary(resolution, resolution, 16);
            RenderTexture.active = rt;
            cam.targetTexture = rt;

            Dictionary<GameObject, int> originallayers = new Dictionary<GameObject, int>();
            List<Transform> children = new List<Transform>();
            EditorCore.RecursivelyGetChildren(children, target);

            //set camera to render unassigned layer
            int layer = EditorCore.FindUnusedLayer();
            if (layer == -1) { Debug.LogWarning("TextureBake couldn't find unused layer, texture generation might be incorrect"); }

            if (layer != -1)
            {
                cam.cullingMask = 1 << layer;
            }

            //save all canvas layers. put on unassigned layer then render
            try
            {
                if (layer != -1)
                {
                    foreach (var v in children)
                    {
                        originallayers.Add(v.gameObject, v.gameObject.layer);
                        v.gameObject.layer = layer;
                    }
                }
                //render to texture
                cam.Render();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            //reset dynamic object layers
            if (layer != -1)
            {
                foreach (var v in originallayers)
                {
                    v.Key.layer = v.Value;
                }
            }

            //write rendertexture to png
            Texture2D tex = new Texture2D(resolution, resolution);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            //delete temporary camera
            UnityEngine.Object.DestroyImmediate(cameraGo);

            return tex;
        }

        #endregion

        #region Export Dynamic Objects
        /// <summary>
        /// export all dynamic objects in scene. skip prefabs
        /// </summary>
        /// <returns>true if any dynamics are exported</returns>
        public static bool ExportAllDynamicsInScene()
        {
            var dynamics = UnityEngine.Object.FindObjectsOfType<DynamicObject>();
            List<GameObject> gos = new List<GameObject>();
            foreach (var v in dynamics)
                gos.Add(v.gameObject);

            Selection.objects = gos.ToArray();

            return ExportAllSelectedDynamicObjects();
        }

        /// <summary>
        /// adds all dynamics in children to list, starting from transform
        /// </summary>
        static void RecurseThroughChildren(Transform t, List<DynamicObject> dynamics)
        {
            var d = t.GetComponent<DynamicObject>();
            if (d != null)
            {
                dynamics.Add(d);
            }
            for (int i = 0; i < t.childCount; i++)
            {
                RecurseThroughChildren(t.GetChild(i), dynamics);
            }
        }

        static List<TransformHolder> GetParentTransformList(List<TransformHolder> list, Transform owner)
        {
            if (owner.parent != null)
            {
                var th = new TransformHolder() { target = owner.parent, localScale = owner.parent.localScale };
                list.Add(th);
                GetParentTransformList(list, owner.parent);
            }

            return list;
        }

        class TransformHolder
        {
            public Transform target;
            public Vector3 localScale;
        }

        /// <summary>
        /// export a gameobject, temporarily spawn them in the scene if they are prefabs selected in the project window
        /// </summary>
        public static void ExportDynamicObjects(List<DynamicObject> dynamicObjects, bool displayPopup = false)
        {
            List <BakeableMesh> temporaryDynamicMeshes = new List <BakeableMesh>();
            //export as a list. skip dynamics already exported in this collection
            HashSet<string> exportedMeshNames = new HashSet<string>();           
            ExportDynamicObjectList(exportedMeshNames, dynamicObjects, temporaryDynamicMeshes);
            
            if (displayPopup)
            {
                EditorUtility.DisplayDialog("Dynamic Object Export", "Successfully exported 1 Dynamic Object mesh", "Ok");
            }
            //return true;

            foreach (BakeableMesh bm in temporaryDynamicMeshes)
            {
                GameObject.DestroyImmediate(bm.tempGo);
            }
        }

        static void ExportDynamicObjectList(HashSet<string> exportedMeshNames, List<DynamicObject> dynamicObjects, List<BakeableMesh> temporaryDynamicMeshes)
        {
            foreach (var dynamicObject in dynamicObjects)
            {
                DynamicObject temporaryDynamic = dynamicObject;
                if (exportedMeshNames.Contains(dynamicObject.MeshName)) { continue; }

                //setup
                //if (dynamicObject == null) { return false; }
                if (dynamicObject == null) { continue; }

#if C3D_TMPRO
                if (dynamicObject.GetComponent<TextMeshPro>() != null)
                {
                    temporaryDynamic = BakeQuadGameObject(dynamicObject.gameObject, temporaryDynamicMeshes, ExportQuadType.TMPro, true).GetComponent<DynamicObject>();
                    temporaryDynamic.MeshName = dynamicObject.MeshName;
                }
#endif
                //skip exporting common meshes
                if (!dynamicObject.UseCustomMesh) { continue; }
                //skip empty mesh names
                if (string.IsNullOrEmpty(dynamicObject.MeshName)) { Debug.LogError(dynamicObject.gameObject.name + " Skipping export because of null/empty mesh name", dynamicObject.gameObject); continue; }
                GameObject prefabInScene = null;
                if (!dynamicObject.gameObject.scene.IsValid())
                {
                    prefabInScene = GameObject.Instantiate(dynamicObject.gameObject);
                    temporaryDynamic = prefabInScene.GetComponent<DynamicObject>();
                }

                exportedMeshNames.Add(temporaryDynamic.MeshName);
                string path = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + "Dynamic" + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(path + temporaryDynamic.MeshName + Path.DirectorySeparatorChar);

                //normalize the dynamic object (and all it's parents) so scale is not doubly applied in OE/SE since manifest data / session data also holds LOSSYscale
                Vector3 originalOffset = temporaryDynamic.transform.localPosition;
                temporaryDynamic.transform.localPosition = Vector3.zero;
                Quaternion originalRot = temporaryDynamic.transform.localRotation;
                temporaryDynamic.transform.localRotation = Quaternion.identity;

                var list = new List<TransformHolder>();
                list.Add(new TransformHolder() { target = temporaryDynamic.transform, localScale = temporaryDynamic.transform.localScale });
                GetParentTransformList(list, temporaryDynamic.transform);
                foreach (var v in list)
                {
                    v.target.transform.localScale = Vector3.one;
                }

                List<BakeableMesh> temp = new List<BakeableMesh>();
                customTextureExports = new List<string>();
                BakeNonstandardRenderers(temporaryDynamic, temp, path + temporaryDynamic.MeshName + Path.DirectorySeparatorChar);

                //export as gltf
                try
                {
                    var exporter = new UnityGLTF.GLTFSceneExporter(new Transform[] { temporaryDynamic.transform }, temporaryDynamic);
                    exporter.SetNonStandardOverrides(temp);
                    exporter.SaveGLTFandBin(path + temporaryDynamic.MeshName + Path.DirectorySeparatorChar, temporaryDynamic.MeshName, customTextureExports);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                //destroy bakeable meshes from non-standard renderers
                for (int i = 0; i < temp.Count; i++)
                {
                    if (temp[i].useOriginalscale)
                        temp[i].meshRenderer.transform.localScale = temp[i].originalScale;
                    UnityEngine.Object.DestroyImmediate(temp[i].meshFilter);
                    UnityEngine.Object.DestroyImmediate(temp[i].meshRenderer);
                    if (temp[i].tempGo != null)
                        UnityEngine.Object.DestroyImmediate(temp[i].tempGo);
                }

                //reset transform
                temporaryDynamic.transform.localPosition = originalOffset;
                temporaryDynamic.transform.localRotation = originalRot;
                foreach (var v in list)
                {
                    v.target.transform.localScale = v.localScale;
                }

                EditorCore.SaveDynamicThumbnailAutomatic(dynamicObject.gameObject);

                //queue resize texture
                ResizeQueue.Enqueue(path + temporaryDynamic.MeshName + Path.DirectorySeparatorChar);
                EditorApplication.update -= UpdateResize;
                EditorApplication.update += UpdateResize;

                //clean up
                if (prefabInScene != null)
                {
                    GameObject.DestroyImmediate(prefabInScene);
                }
            }
        }

        /// <summary>
        /// small wrapper to pass all selected gameobjects into ExportDynamicObject
        /// </summary>
        /// <returns></returns>
        public static bool ExportAllSelectedDynamicObjects()
        {
            //get all dynamics in selection
            List<Transform> entireSelection = new List<Transform>();
            entireSelection.AddRange(Selection.GetTransforms(SelectionMode.Editable));
            if (entireSelection.Count == 0)
            {
                EditorUtility.DisplayDialog("Dynamic Object Export", "No Dynamic Objects selected", "Ok");
                return false;
            }

            List<Transform> sceneObjects = new List<Transform>();
            sceneObjects.AddRange(Selection.GetTransforms(SelectionMode.Editable | SelectionMode.ExcludePrefab));
            List<Transform> prefabsToSpawn = new List<Transform>();

            //add prefab objects to a list
            foreach (var v in entireSelection)
            {
                if (!sceneObjects.Contains(v))
                {
                    prefabsToSpawn.Add(v);
                }
            }

            //spawn prefabs
            var temporarySpawnedPrefabs = new List<GameObject>();
            foreach (var v in prefabsToSpawn)
            {
                var newPrefab = GameObject.Instantiate(v.gameObject);
                temporarySpawnedPrefabs.Add(newPrefab);
                sceneObjects.Add(newPrefab.transform);
            }

            //recursively get all dynamic objects to export
            List<DynamicObject> AllDynamics = new List<DynamicObject>();
            foreach (var selected in sceneObjects)
            {
                RecurseThroughChildren(selected.transform, AllDynamics);
            }

            //export all
            ExportDynamicObjects(AllDynamics);

            //remove spawned prefabs
            foreach (var v in temporarySpawnedPrefabs)
            {
                GameObject.DestroyImmediate(v);
            }

            return true;
        }
#endregion

        #region Upload Dynamic Objects

        static Queue<DynamicObjectForm> dynamicObjectForms = new Queue<DynamicObjectForm>();
        static string currentDynamicUploadName;

        /// <summary>
        /// returns true if successfully uploaded dynamics
        /// </summary>
        public static bool UploadSelectedDynamicObjectMeshes(List<GameObject> uploadList, bool ShowPopupWindow = false)
        {
            List<string> dynamicMeshNames = new List<string>();
            foreach (var v in uploadList)
            {
                var dyn = v.GetComponent<DynamicObject>();
                if (dyn == null) { continue; }
                dynamicMeshNames.Add(dyn.MeshName);
            }
            if (dynamicMeshNames.Count == 0) { return false; }
            return UploadDynamicObjects(dynamicMeshNames, ShowPopupWindow);
        }

        /// <summary>
        /// returns true if successfully started uploading dynamics
        /// </summary>
        public static bool UploadAllDynamicObjectMeshes(bool ShowPopupWindow = false)
        {
            if (!Directory.Exists(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + "Dynamic"))
            {
                Debug.Log("Skip uploading dynamic objects, folder doesn't exist");
                return false;
            }
            List<string> dynamicMeshNames = new List<string>();
            string path = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + "Dynamic";
            var subdirectories = Directory.GetDirectories(path);
            foreach (var v in subdirectories)
            {
                var split = v.Split(Path.DirectorySeparatorChar);
                dynamicMeshNames.Add(split[split.Length - 1]);
            }
            return UploadDynamicObjects(dynamicMeshNames, ShowPopupWindow);
        }

        /// <summary>
        /// search through files for list of dynamic object meshes. if dynamics.name contains exported folder, upload
        /// can display popup warning. returns false if cancelled
        /// </summary>
        internal static bool UploadDynamicObjects(List<string> dynamicMeshNames, bool ShowPopupWindow = false)
        {
            string fileList = "Upload Files:\n";

            var settings = Cognitive3D_Preferences.FindCurrentScene();
            if (settings == null)
            {
                string s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name;
                if (string.IsNullOrEmpty(s))
                {
                    s = "Unknown Scene";
                }
                EditorUtility.DisplayDialog("Dynamic Object Upload Failed", "Could not find the Scene Settings for \"" + s + "\". Are you sure you've saved, exported and uploaded this scene to SceneExplorer?", "Ok");
                return false;
            }

            //cancel if active scene has not been uploaded
            string sceneid = settings.SceneId;
            if (string.IsNullOrEmpty(sceneid))
            {
                EditorUtility.DisplayDialog("Dynamic Object Upload Failed", "Could not find the SceneId for \"" + settings.SceneName + "\". Are you sure you've exported and uploaded this scene to SceneExplorer?", "Ok");
                return false;
            }

            //get list of export full directory names
            string path = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "Cognitive3D_SceneExplorerExport" + Path.DirectorySeparatorChar + "Dynamic";
            var subdirectories = Directory.GetDirectories(path);
            List<string> exportDirectories = new List<string>();
            foreach (var v in subdirectories)
            {
                var split = v.Split(Path.DirectorySeparatorChar);
                if (dynamicMeshNames.Contains(split[split.Length - 1]))
                {
                    exportDirectories.Add(v);
                }
            }

            //optional confirmation window
            if (ShowPopupWindow)
            {
                int option = EditorUtility.DisplayDialogComplex("Upload Dynamic Objects", "Do you want to upload " + exportDirectories.Count + " Objects to \"" + settings.SceneName + "\" (" + settings.SceneId + " Version:" + settings.VersionNumber + ")?", "Ok", "Cancel", "Open Directory");
                if (option == 0) { } //ok
                else if (option == 1) { return false; } //cancel
                else
                {
#if UNITY_EDITOR_WIN
                    //open directory
                    System.Diagnostics.Process.Start("explorer.exe", path);
                    return false;
#elif UNITY_EDITOR_OSX
                    System.Diagnostics.Process.Start("open", path);
                    return false;
#endif
                }
            }

            //for each dynamic object mesh directory, create a 'dynamic object form' and add it to a queue
            foreach (var subdir in exportDirectories)
            {
                var filePaths = Directory.GetFiles(subdir);

                WWWForm wwwForm = new WWWForm();
                foreach (var f in filePaths)
                {
                    if (f.ToLower().EndsWith(".ds_store"))
                    {
                        Debug.Log("skip file " + f);
                        continue;
                    }

                    fileList += f + "\n";
                    var data = File.ReadAllBytes(f);
                    wwwForm.AddBinaryData("file", data, Path.GetFileName(f));
                }

                var dirname = new DirectoryInfo(subdir).Name;
                string uploadUrl = CognitiveStatics.PostDynamicObjectData(settings.SceneId, settings.VersionNumber, dirname);

                Dictionary<string, string> headers = new Dictionary<string, string>();
                if (EditorCore.IsDeveloperKeyValid)
                {
                    headers.Add("Authorization", "APIKEY:DEVELOPER " + EditorCore.DeveloperKey);
                    foreach (var v in wwwForm.headers)
                    {
                        headers[v.Key] = v.Value;
                    }
                }

                dynamicObjectForms.Enqueue(new DynamicObjectForm(uploadUrl, wwwForm, dirname, headers)); //AUTH
            }

            if (dynamicObjectForms.Count > 0)
            {
                DynamicUploadTotal = dynamicObjectForms.Count;
                DynamicUploadSuccess = 0;
                EditorApplication.update += UpdateUploadDynamics;
            }
            return true;
        }

        /// <summary>
        /// holds info about what to post to scene. mesh, url, headers
        /// </summary>
        class DynamicObjectForm
        {
            public string Url;
            public WWWForm Form;
            public string Name;
            public Dictionary<string, string> Headers;

            public DynamicObjectForm(string url, WWWForm form, string name, Dictionary<string, string> headers)
            {
                Url = url;
                Form = form;
                Name = name;
                Headers = headers;
            }
        }

        static int DynamicUploadTotal;
        static int DynamicUploadSuccess;

        static UnityWebRequest dynamicUploadWWW;
        /// <summary>
        /// attached to editor update to go through dynamic object form queue
        /// </summary>
        static void UpdateUploadDynamics()
        {
            if (dynamicUploadWWW == null)
            {
                //get the next dynamic object to upload from forms
                if (dynamicObjectForms.Count == 0)
                {
                    Debug.Log("Dynamic Object uploads complete. " + DynamicUploadSuccess + "/" + DynamicUploadTotal + " succeeded");
                    EditorApplication.update -= UpdateUploadDynamics;
                    EditorUtility.ClearProgressBar();
                    currentDynamicUploadName = string.Empty;
                    return;
                }
                else
                {
                    var form = dynamicObjectForms.Dequeue();
                    dynamicUploadWWW = UnityWebRequest.Post(form.Url, form.Form);
                    foreach (var v in form.Headers)
                        dynamicUploadWWW.SetRequestHeader(v.Key, v.Value);
                    dynamicUploadWWW.SendWebRequest();
                    currentDynamicUploadName = form.Name;
                }
            }

            if (EditorUtility.DisplayCancelableProgressBar("Upload Dynamic Object", currentDynamicUploadName, dynamicUploadWWW.uploadProgress))
            {
                Debug.Log("Cancelled upload of dynamic object: " + currentDynamicUploadName);
                dynamicUploadWWW.Abort();
                EditorUtility.ClearProgressBar();
            }
      

            if (!dynamicUploadWWW.isDone) { return; }
            if (!string.IsNullOrEmpty(dynamicUploadWWW.error))
            {
                Debug.LogError(dynamicUploadWWW.responseCode + " " + dynamicUploadWWW.error);
            }
            else
            {
                DynamicUploadSuccess++;
            }
            Debug.Log("Finished uploading Dynamic Object mesh: " + currentDynamicUploadName);
            dynamicUploadWWW = null;
        }
        #endregion
    }
}
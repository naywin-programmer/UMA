using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
using UMAEditor;
#endif

namespace UMA
{

	/// <summary>
	/// Contains Refrences for all UMA Assets that you want to be included in the build and accessed by any Libraries in your project. Also includes a list of AssetBundle assets, BUT THESE ARE NOT INCLUDED IN YOUR BUILD.
	/// The list is just here for reference in the editor. But It could potentially in future be used to assign things to bundles (watch this space)
	/// </summary>
	public partial class UMAAssetIndex : ScriptableObject
	{


#if UNITY_EDITOR
		//UMAAssetIndex creates this itself if Instance is null and no Index exists (or was deleted) in Resources
		public static void CreateUMAAssetIndex()
		{
			if (Resources.Load("UMAAssetIndex-DONOTDELETE") != null)
			{
				return;
			}
			//This needs to be created in our internal data store folder (so *hopefully* people dont delete it)
			UMAEditor.CustomAssetUtility.CreateAsset<UMAAssetIndex>(Path.Combine(UMA.FileUtils.GetInternalDataStoreFolder(false, false), "UMAAssetIndex-DONOTDELETE.asset"), false);
		}
#endif
		static UMAAssetIndex _instance;

#if UNITY_EDITOR
		[System.NonSerialized]
		bool initialized = false;
#endif

		[Tooltip("Set the types you wish to track in the index in here.")]
		public List<string> typesToIndex = new List<string>() { "SlotDataAsset", "OverlayDataAsset", "RaceData", "UMATextRecipe", "UMAWardrobeRecipe", "UMAWardrobeCollection", "RuntimeAnimatorController", "DynamicUMADnaAsset" };
		//the index of all the assets you have selected to include
		//This is the only index that gets 'Saved' into the game
		[SerializeField]
		private UMAAssetIndexData _buildIndex = new UMAAssetIndexData();

#if UNITY_EDITOR
		//the index of all the possible assets you could have (excluding those in AssetBundles)
		//Used to generate the list in the Editor where you assign/unassign assets to the serialized _buildIndex
		private UMAAssetIndexData _fullIndex = new UMAAssetIndexData();
		//A non serialized index of assets in the project that are assigned to AssetBundles
		//In future we could use this when we simulateAssetBundles in the editor (because it would be quicker)
		private UMAAssetIndexData _assetBundleIndex = new UMAAssetIndexData();

		//UMA Asset Modification Processor Lists
		//These lists are populated each time AssetModificationProcessor does anything with any assets
		List<string> AMPDeletedAssets = new List<string>();

		List<string> AMPCreatedAssets = new List<string>();

		List<string> AMPSavedAssets = new List<string>();

		//moved paths need a special Type
		private class AMPMovedAsset
		{
			public string prevPath = "";
			public string newPath = "";

			public AMPMovedAsset() { }
			public AMPMovedAsset(string _prevPath, string _newPath)
			{
				prevPath = _prevPath;
				newPath = _newPath;
			}
		}

		List<AMPMovedAsset> AMPMovedAssets = new List<AMPMovedAsset>();

		string folderToSelectAfterMove = "";

		//The windowInstance is assigned when the index is viewed in a window, this is so we can refresh the view when this script modifies the index
		[System.NonSerialized]
		public EditorWindow windowInstance = null;
#endif

		/// <summary>
		/// Gets or creates the UMAAssetIndex Instance. If not Scriptable Object asset has yet been created, it creates one and stores it in UMAInternalStorage/InGame/Resources
		/// </summary>
		public static UMAAssetIndex Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = (UMAAssetIndex)Resources.Load("UMAAssetIndex-DONOTDELETE");
#if UNITY_EDITOR
					if (_instance == null)
					{
						//Sometimes when the editor is compiling and this is missing multiple versions of this get generated so dont do anything while that is happening
						if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
						{
							CreateUMAAssetIndex();
							_instance = (UMAAssetIndex)Resources.Load("UMAAssetIndex-DONOTDELETE");
							_instance.GenerateLists();
						}
					}
					else
					{
						_instance.Initialize();
					}
#endif
				}
#if UNITY_EDITOR
				else if (!_instance.initialized)
				{
					_instance.Initialize();
				}
#endif
				return _instance;
			}
		}

#if UNITY_EDITOR
		private void Initialize()
		{
			if (initialized)
				return;
			if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
			{
				if (_buildIndex.Count() > 0 && !initialized)
				{
					ValidateBuildIndex();
				}
				else if (!initialized)
				{
					GenerateLists();
				}
			}
		}
#endif

		/// <summary>
		/// The Build Index contains refrences to all the assets you have made live in the game using the UMAAssetIndex, 
		/// including those in Resources folders
		/// </summary>
		public UMAAssetIndexData BuildIndex
		{
			get { return _buildIndex; }
		}

#if UNITY_EDITOR
		/// <summary>
		/// The Full Index contains a list of all the assets you *could* make live in your game (but not refrences) 
		/// this is not serialized and should only be used to find assets to add to Build Index
		/// </summary>
		public UMAAssetIndexData FullIndex
		{
			get { return _fullIndex; }
		}

		/// <summary>
		/// The AssetBundleIndex contains a list of assets that are currently assigned to an asset bundle (but not refrences)
		/// This is not serliaized but can be used to check if a given asset is in an asset bundle inside the editor
		/// </summary>
		public UMAAssetIndexData AssetBundleIndex
		{
			get { return _assetBundleIndex; }
		}
#endif

		public UMAAssetIndex()
		{
			
		}


#if UNITY_EDITOR

		#region INITIALIZATION AND VALIDATION
		/// <summary>
		/// Generates the initial lists when an UMAAssetIndex scriptableObject is created for the first time
		/// </summary>
		private void GenerateLists()
		{
			if (AddTypesToIndex(typesToIndex))
			{
				EditorUtility.SetDirty(this);
				AssetDatabase.SaveAssets();
			}
			SortIndexes();
			CheckAndUpdateWindow();
			Resources.UnloadUnusedAssets();
			initialized = true;
		}

		/// <summary>
		/// Generates the full Index when Unity is started and the UMAAssetIndex scriptable object exists. Validates its existing BuildIndex against the assets that are available in the project
		/// </summary>
		private void ValidateBuildIndex()
		{
			//we need the _fullIndex to compare against
			var changed = AddTypesToIndex(typesToIndex);

			if (_buildIndex.Count() == 0)
				return;

			var pathsToRemove = new List<string>();
			bool hadInvalidEntries = false;
			//we need to know if any anything has changed since the index was last open in Unity
			for (int ti = 0; ti < _buildIndex.data.Length; ti++)
			{
				for (int i = 0; i < _buildIndex.data[ti].typeIndex.Length; i++)
				{
					//if any indexes have 0 or -1 as their hash or an empty fullPath flag for UMAAssetIndexData to scan and remove them
					if ((_buildIndex.data[ti].typeIndex[i].nameHash == 0 || _buildIndex.data[ti].typeIndex[i].nameHash == -1) || String.IsNullOrEmpty(_buildIndex.data[ti].typeIndex[i].fullPath))
					{
						hadInvalidEntries = true;
						continue;
					}
					//if this item fullpath is not in _fullIndex
					if (!_fullIndex.Contains(_buildIndex.data[ti].typeIndex[i].fullPath))
					{
						//then the asset has been moved outside of Unity
						//does it still exist?
						var fullIndexItem = _fullIndex.GetEntryFromNameHash(_buildIndex.data[ti].typeIndex[i].nameHash);
						//if not remove it from the buildIndex and delete the reference asset if there was one
						if (fullIndexItem == null)
						{
							pathsToRemove.Add(_buildIndex.data[ti].typeIndex[i].fullPath);
						}
						else
						{
							//if it was in Resources we need to know if it still is
							if (InResources(_buildIndex.data[ti].typeIndex[i].fullPath))
							{
								//the asset still exists but in a different place
								//if the asset is still in Resources we need to update the full path
								if (InResources(fullIndexItem.fullPath))
									_buildIndex.data[ti].typeIndex[i].fullPath = fullIndexItem.fullPath;
								else
									//the asset is no longer in resources so we need to remove it from the index
									pathsToRemove.Add(_buildIndex.data[ti].typeIndex[i].fullPath);
							}
							else
							{
								//if it wasn't in Resources it will only be here because it was manually made live
								//in that case if it is still NOT in Resources we dont need to do anything (update the path)
								if (!InResources(fullIndexItem.fullPath))
								{
									_buildIndex.data[ti].typeIndex[i].fullPath = fullIndexItem.fullPath;
								}
								else
								{
									//it is now in Resources so we no longer need the 'little asset' and need to update the path
									if (!String.IsNullOrEmpty(_buildIndex.data[ti].typeIndex[i].fileRefPath))
										_buildIndex.data[ti].typeIndex[i].TheFileReference = null;
									_buildIndex.data[ti].typeIndex[i].fullPath = fullIndexItem.fullPath;
								}
							}
						}
					}
					//Lastly remove any 'live assets' had their refrence objects deleted
					if (!String.IsNullOrEmpty(_buildIndex.data[ti].typeIndex[i].fileRefPath))
					{
						var thisFileRef = _buildIndex.data[ti].typeIndex[i].TheFileReference;//getting the file ref also makes it null if it no longer exists
						if (thisFileRef == null)
						{
							if (!pathsToRemove.Contains(_buildIndex.data[ti].typeIndex[i].fullPath))
								pathsToRemove.Add(_buildIndex.data[ti].typeIndex[i].fullPath);
						}
					}
				}
			}
			//loop over any removed paths and remove them
			if (pathsToRemove.Count > 0)
			{
				Debug.Log("UMA Global Library had " + pathsToRemove.Count + " entries for assets that no longer exist. Removing...");
				for (int i = 0; i < pathsToRemove.Count; i++)
				{
					if (_buildIndex.RemovePath(pathsToRemove[i]))
					{
						Debug.Log("UMA Global Library removed index for " + pathsToRemove[i]);
						changed = true;
					}
					else
					{
						Debug.Log("UMA Global Library failed to removed index for " + pathsToRemove[i]);
					}
				}
				pathsToRemove.Clear();
			}
			//Removes any entries that have 0 or -1 as their nameHash and or an empty filePath entry
			if (hadInvalidEntries)
			{
				Debug.Log("UMA Global Library had some invalid entries. Removing...");
				_buildIndex.RemoveInvalidEntries();
				Debug.Log("Removed :)");
			}
			SortIndexes();
			CheckAndUpdateWindow();
			if (changed)
			{
				EditorUtility.SetDirty(this);
				AssetDatabase.SaveAssets();
			}
			Resources.UnloadUnusedAssets();
			initialized = true;
		}

		#endregion

		#region ASSET MODIFICATION PROCESSOR CALLBACKS

		public void OnCreateAsset(string createdAsset)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;
			if (PathIsValid(createdAsset) && !AMPCreatedAssets.Contains(createdAsset))
			{
				AMPCreatedAssets.Add(createdAsset);
			}
			if (AMPCreatedAssets.Count > 0)
			{
				EditorApplication.update -= DoCreatedAsset;
				EditorApplication.update += DoCreatedAsset;
			}
		}

		/// <summary>
		/// Callback for UMAAssetPostProcessor that is triggered when assets are imported OR when an asset is Duplucated using "Edit/Duplicate".
		/// Adds the assets to the AMPCreatedAssets list and and sets up DoCreatedAsset to process the list when the assetPostProcessor is finished
		/// </summary>
		public void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
		{
			if (BuildPipeline.isBuildingPlayer || UnityEditorInternal.InternalEditorUtility.inBatchMode || Application.isPlaying)
				return;

			//RENAMED assets are considered Moved AND are then also subsequently sent as saved
			bool addedMovedAssets = false;
			if (movedAssets.Length > 0)
			{
				for (int i = 0; i < movedAssets.Length; i++)
				{
					if (PathIsValid(movedFromAssetPaths[i]) && !AMPMovedAssetsContains(movedFromAssetPaths[i], movedAssets[i]) && !AMPSavedAssets.Contains(movedAssets[i]))
					{
						AMPMovedAssets.Add(new AMPMovedAsset(movedFromAssetPaths[i], movedAssets[i]));
						addedMovedAssets = true;
					}
				}
				if (AMPMovedAssets.Count > 0 && addedMovedAssets)
				{
					MakeAssetsMovingIntoResourcesNotLive();
					EditorApplication.update -= DoMovedAssets;
					EditorApplication.update += DoMovedAssets;
				}

			}
			//EDITED assets (internal values changes) are ONLY sent as imported
			//RENAMED assets are FIRST sent as moved and then sent as imported
			//DUPLICATED assets are snt as imported and have a corresponding 'deleted' asset for its __DELETED_GUID_Trash
			bool addedCreatedAssets = false;
			bool addedSavedAssets = false;
			if (importedAssets.Length > 0)
			{
				for (int i = 0; i < importedAssets.Length; i++)
				{
					if (PathIsValid(importedAssets[i]) && !AMPCreatedAssets.Contains(importedAssets[i]) && !AMPSavedAssets.Contains(importedAssets[i]) && !AMPMovedAssetsContains("", importedAssets[i]))
					{
						//if the path does not already exist in _fullIndex its a created asset- but it could also be an asset whose name has changed
						//check what happens there
						//If an asset is DUPLICATED it will also be an entry for __DELETED_GUID_Trash in the deletedAssets array (so we can know it was CREATED)
						if (deletedAssets.Length > i)
						{
							if (deletedAssets[i] != null)
							{
								AMPCreatedAssets.Add(importedAssets[i]);
								addedCreatedAssets = true;
							}
							else
							{
								AMPSavedAssets.Add(importedAssets[i]);
								addedSavedAssets = true;
							}
						}
						else
						{
							AMPSavedAssets.Add(importedAssets[i]);
							addedSavedAssets = true;
						}
					}
				}
				if (AMPCreatedAssets.Count > 0 && addedCreatedAssets)
				{
					EditorApplication.update -= DoEditorDuplicatedAssets;
					EditorApplication.update += DoEditorDuplicatedAssets;
				}
				if (AMPSavedAssets.Count > 0 && addedSavedAssets)
				{
					EditorApplication.update -= DoSavedAssets;
					EditorApplication.update += DoSavedAssets;
				}
			}
			bool addedDeletedAssets = false;
			if (deletedAssets.Length > 0)
			{
				for (int i = 0; i < deletedAssets.Length; i++)
				{
					if (PathIsValid(deletedAssets[i]) && !AMPDeletedAssets.Contains(deletedAssets[i]))
					{
						AMPDeletedAssets.Add(deletedAssets[i]);
						addedDeletedAssets = true;
					}
				}
				if (AMPDeletedAssets.Count > 0 && addedDeletedAssets)
				{
					EditorApplication.update -= DoDeletedAsset;
					EditorApplication.update += DoDeletedAsset;
				}
			}
		}

		private bool PathIsValid(string path)
		{
			var extension = Path.GetExtension(path);
			if (extension == ".meta" || extension == ".cs" || extension == "" || extension == ".js")
				return false;
			if (path.IndexOf("ProjectSettings.asset") > -1 || path.IndexOf("-fileRef.asset") > -1 || path.IndexOf("__DELETED_GUID_Trash") > -1 || path.IndexOf("UMAAssetIndex-DONOTDELETE") > -1 || path.IndexOf("Assets/") == -1)
				return false;
			return true;
		}
		#endregion

		#region ASSET MODIFICATION PROCESSOR METHODS

		private void MakeAssetsMovingIntoResourcesNotLive()
		{
			foreach (AMPMovedAsset path in AMPMovedAssets)
			{
				//If the asset has moved into Resources
				if (!InResources(path.prevPath) && InResources(path.newPath))
				{
					//And was previously manually live
					if (_buildIndex.Contains(path.prevPath))
					{
						_buildIndex.RemovePath(path.prevPath);
					}
				}
			}
		}
		/// <summary>
		/// Processers the AMPMovedAssets after AssetModificationProcessor has finished moving assets
		/// </summary>
		private void DoMovedAssets()
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating || !initialized)
				return;

			EditorApplication.update -= DoMovedAssets;

			for (int i = 0; i < AMPMovedAssets.Count; i++)
			{
				//if the asset is NOT slot/overlay/race its asset hash may have also changed (because its previous hash was based on its asset name and that might be what has changed)
				//But to determine that we need to load the asset, because we dont know if its slot/overlay/race until we do
				var thisAsset = AssetDatabase.LoadMainAssetAtPath(AMPMovedAssets[i].newPath);
				if (thisAsset)
				{
					if (!IsAssetATrackedType(thisAsset))
						continue;

					int thisAssetHash = -1;
					string thisAssetName = "";
					GetAssetHashAndNames(thisAsset, ref thisAssetHash, ref thisAssetName);
					if ((thisAssetHash == 0 || thisAssetHash == -1) || thisAssetName == "")
						continue;
					//find the asset in the index using the prev path
					var fullIndexData = _fullIndex.GetEntryFromPath(AMPMovedAssets[i].prevPath);
					if (fullIndexData != null)
					{
						//If the asset has been moved INTO an assetbundle, we need to remove it from the FullIndex and the Build Index (if it was in there)
						//and add the new path to the assetBundleIndex
						if (InAssetBundle(AMPMovedAssets[i].newPath, thisAsset.name))
						{
							_fullIndex.RemovePath(AMPMovedAssets[i].prevPath);
							if (_buildIndex.Contains(AMPMovedAssets[i].prevPath))
								_buildIndex.RemovePath(AMPMovedAssets[i].prevPath);
							_assetBundleIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
						}
						else
						{
							//set the path for the asset to be the new path
							//and update the name/hash stuff
							fullIndexData.fullPath = AMPMovedAssets[i].newPath;
							fullIndexData.name = thisAssetName;
							fullIndexData.nameHash = thisAssetHash;
							//THEN check if the asset was in the BuildList
							var buildIndexData = _buildIndex.GetEntryFromPath(AMPMovedAssets[i].prevPath);
							if (buildIndexData != null)
							{
								buildIndexData.UpdateIndexData(fullIndexData.nameHash, fullIndexData.fullPath, fullIndexData.name);
							}
							//Then check if it was moved in or out of Resources
							if (InResources(AMPMovedAssets[i].prevPath) && !InResources(AMPMovedAssets[i].newPath))
							{
								//remove from build index
								MakeAssetNotLive(fullIndexData, thisAsset.GetType().ToString());
							}
							else if (!InResources(AMPMovedAssets[i].prevPath) && InResources(AMPMovedAssets[i].newPath))
							{
								//add to build index
								MakeAssetLive(fullIndexData, thisAsset.GetType().ToString());
							}
						}
					}
					else
					{
						//if fullIndexData is null it will mean the asset was previously in an asset bundle but now is not
						if (!InAssetBundle(AMPMovedAssets[i].newPath, thisAsset.name))
						{
							_fullIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
							//Automatically add anything in a Resources folder because it will already be included in the game
							if (InResources(AMPMovedAssets[i].newPath))
								_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, false);
						}
					}
					var assetBundleIndexData = _assetBundleIndex.GetEntryFromPath(AMPMovedAssets[i].prevPath);
					if (assetBundleIndexData != null)
					{
						if (InAssetBundle(AMPMovedAssets[i].newPath, thisAsset.name))
						{
							assetBundleIndexData.UpdateIndexData(thisAssetHash, AMPMovedAssets[i].newPath, thisAssetName);
						}
						else
						{
							//otherwise remove it from the assetBundle list
							_assetBundleIndex.RemovePath(assetBundleIndexData.fullPath);
						}
					}
				}
			}
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
			AMPMovedAssets.Clear();
			SortIndexes();
			CheckAndUpdateWindow();
			EditorApplication.delayCall -= CleanUnusedAssets;
			EditorApplication.delayCall += CleanUnusedAssets;
			//sort out the selected object
			if (folderToSelectAfterMove != "")
			{
				var folderObject = AssetDatabase.LoadAssetAtPath(folderToSelectAfterMove, typeof(UnityEngine.Object));
				if (folderObject != null)
					Selection.activeObject = folderObject;
				folderToSelectAfterMove = "";
			}
		}

		/// <summary>
		/// Processers the AMPDeletedAssets after AssetModificationProcessor has finished moving assets 
		/// </summary>
		private void DoDeletedAsset()
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating || !initialized)
				return;
			EditorApplication.update -= DoDeletedAsset;

			//Remove the asset from all indexes
			foreach (string path in AMPDeletedAssets)
			{
				_fullIndex.RemovePath(path);
				_buildIndex.RemovePath(path);
				_assetBundleIndex.RemovePath(path);
			}
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
			AMPDeletedAssets.Clear();
			CheckAndUpdateWindow();
			EditorApplication.delayCall -= CleanUnusedAssets;
			EditorApplication.delayCall += CleanUnusedAssets;
		}

		private void DoEditorDuplicatedAssets()
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating || !initialized)
				return;
			EditorApplication.update -= DoEditorDuplicatedAssets;
			DoCreatedAsset();
		}

		/// <summary>
		/// Processers the AMPCreatedAssets after AssetModificationProcessor has finished moving assets or AssetPostProcessor has finished creating or importing
		/// </summary>
		private void DoCreatedAsset()
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating || !initialized)
				return;

			//created happens BEFORE deleted when the item that was created came via AssetModificationProcessor so if the deleted list is not clear wait until it is
			if (AMPDeletedAssets.Count > 0)
				return;
			EditorApplication.update -= DoCreatedAsset;

			foreach (string path in AMPCreatedAssets)
			{
				var thisAsset = AssetDatabase.LoadMainAssetAtPath(path);
				if (!IsAssetATrackedType(thisAsset))
					continue;
				//then add it
				int thisAssetHash = -1;
				string thisAssetName = "";
				GetAssetHashAndNames(thisAsset, ref thisAssetHash, ref thisAssetName);
				if ((thisAssetHash == 0 || thisAssetHash == -1) || thisAssetName == "")
					continue;

				if (InAssetBundle(path, thisAsset.name))
				{
					_assetBundleIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
				}
				else
				{
					_fullIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
					//Automatically add anything in a Resources folder because it will already be included in the game
					//else make assets the user has created live by default
					if (InResources(path))
						_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, false);
					else
						_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, true);
				}
			}
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
			AMPCreatedAssets.Clear();
			SortIndexes();
			CheckAndUpdateWindow();
			EditorApplication.delayCall -= CleanUnusedAssets;
			EditorApplication.delayCall += CleanUnusedAssets;
		}

		/// <summary>
		/// Processers the AMPSavedAssets after AssetModificationProcessor has finished saving assets. 
		/// This also updates the Asset Names and hashes refrenced in the index (in the case of slot/overlay/race these are the slotname/overlayname/racename) 
		/// </summary>
		private void DoSavedAssets()
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating || !initialized)
				return;
			EditorApplication.update -= DoSavedAssets;

			//assets were saved
			foreach (string path in AMPSavedAssets)
			{
				var thisAsset = AssetDatabase.LoadMainAssetAtPath(path);

				if (!IsAssetATrackedType(thisAsset))
					continue;

				int thisAssetHash = -1;
				string thisAssetName = "";
				bool existed = false;
				if (thisAsset)
				{
					GetAssetHashAndNames(thisAsset, ref thisAssetHash, ref thisAssetName);
					if ((thisAssetHash == 0 || thisAssetHash == -1) || thisAssetName == "")
						continue;
					var fullIndexData = _fullIndex.GetEntryFromPath(path);
					if (fullIndexData != null)
					{
						existed = true;
						fullIndexData.name = thisAssetName;
						fullIndexData.nameHash = thisAssetHash;
						//it will only be in the build index if its also in Full Index
						var buildIndexData = _buildIndex.GetEntryFromPath(path);
						if (buildIndexData != null)
						{
							buildIndexData.name = thisAssetName;
							buildIndexData.nameHash = thisAssetHash;
						}
					}
					var assetBundleIndexData = _assetBundleIndex.GetEntryFromPath(path);
					if (assetBundleIndexData != null)
					{
						existed = true;
						assetBundleIndexData.name = thisAssetName;
						assetBundleIndexData.nameHash = thisAssetHash;
					}
					if (!existed)
					{
						//Unity considers assets pasted in from outside Unity to be 'saved' rather than created (wtf!!??)
						//so
						GetAssetHashAndNames(thisAsset, ref thisAssetHash, ref thisAssetName);
						if ((thisAssetHash == 0 || thisAssetHash == -1) || thisAssetName == "")
							continue;
						if (InAssetBundle(path, thisAsset.name))
						{
							_assetBundleIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
						}
						else
						{
							_fullIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
							//Automatically add anything in a Resources folder because it will already be included in the game
							//else make assets the user has created live by default
							if (InResources(path))
								_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, false);
						}
					}
				}
			}
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
			AMPSavedAssets.Clear();
			CheckAndUpdateWindow();

			EditorApplication.delayCall -= CleanUnusedAssets;
			EditorApplication.delayCall += CleanUnusedAssets;
		}

		private void CleanUnusedAssets()
		{
			EditorApplication.delayCall -= CleanUnusedAssets;
			//Debug.Log("CleanUnusedAssets");
			//AssetDatabase.Refresh();//apparently this also calls the following
			//Resources.UnloadUnusedAssets();
			//we seem to actually need to use this
			EditorUtility.UnloadUnusedAssetsImmediate(true);
		}
		#endregion

		#region ADD/REMOVE ASSETS FROM INDEXES METHODS

		/// <summary>
		/// Called by the inspector to forcefully refresh the Indexes 
		/// </summary>
		/// <param name="clearBuildIndex">If true clears the build index as well (only assets in Resources will be live after this)</param>
		public void ClearAndReIndex(bool clearBuildIndex = false)
		{
			_fullIndex = new UMAAssetIndexData();
			if (clearBuildIndex)
			{
				for(int ti = 0; ti < _buildIndex.data.Length; ti++)
				{
					for(int i = 0; i < _buildIndex.data[ti].typeIndex.Length; i++)
					{
						_buildIndex.data[ti].typeIndex[i].TheFileReference = null;
                    }
				}
				_buildIndex = new UMAAssetIndexData();
			}
			_assetBundleIndex = new UMAAssetIndexData();
			GenerateLists();//also does save
		}

		/// <summary>
		/// Called by the inspector when the Types To Index list is modified. 
		/// Finds the assets for newly added types removes the assets for any types that are no longer tracked
		/// </summary>
		/// <param name="newTypesToIndex"></param>
		/// <param name="doUpdate">If true saves the index when the operation is complete</param>
		public void UpdateIndexedTypes(List<string> newTypesToIndex, bool doUpdate = true)
		{
			List<string> typesToAdd = new List<string>();
			List<string> typesToRemove = new List<string>();
			bool buildIndexModified = false;
			//add types that are not there
			for (int i = 0; i < newTypesToIndex.Count; i++)
			{
				if (!typesToIndex.Contains(newTypesToIndex[i]))
					typesToAdd.Add(newTypesToIndex[i]);
			}
			//remove types that are no longer there
			for (int i = 0; i < typesToIndex.Count; i++)
			{
				if (!newTypesToIndex.Contains(typesToIndex[i]))
					typesToRemove.Add(typesToIndex[i]);
			}
			if (typesToAdd.Count > 0)
			{
				if (AddTypesToIndex(typesToAdd))
                    buildIndexModified = true;
            }
			if (typesToRemove.Count > 0)
			{
				if(RemoveTypesFromIndex(typesToRemove))
					buildIndexModified = true;
			}
			typesToIndex = new List<string>(newTypesToIndex);
			if (buildIndexModified && doUpdate)
			{
				EditorUtility.SetDirty(this);
				AssetDatabase.SaveAssets();
				SortIndexes();
				//update the window
				CheckAndUpdateWindow();
				Resources.UnloadUnusedAssets();
			}
		}
		/// <summary>
		/// Adds assets for the given types to the indexes
		/// </summary>
		/// <param name="typesToAdd">a list of types to add to the index</param>
		/// <param name="compareFullPaths">If true, the path of an asset will also be taken into consideration when the index decides if a given asset is already indexed. 
		/// Setting this to FALSE prevents duplicate assets from being added BUT usually you DO want them added so the user can see them and fix them</param>
		/// <returns>true if anything was added to the BuildIndex. false otherwise</returns>
		private bool AddTypesToIndex(List<string> typesToAdd, bool compareFullPaths = true)
		{
			bool buildIndexModified = false;
			for (int i = 0; i < typesToAdd.Count; i++)
			{
				//with this search when it is t:UMATextRecipe it DOES load Child classes too
				var typeGUIDs = AssetDatabase.FindAssets("t:" + typesToAdd[i]);
				for (int tid = 0; tid < typeGUIDs.Length; tid++)
				{
					UnityEngine.Object thisAsset = null;
					var thisPath = AssetDatabase.GUIDToAssetPath(typeGUIDs[tid]);
					//TextAsset gets scripts and Shaders too we dont want that
					if(typesToAdd[i] == "TextAsset")
					{
						var extension = Path.GetExtension(thisPath);
						if (extension == ".meta" || extension == ".cs" || extension == "" || extension == ".js" || extension == ".shader" || thisPath.IndexOf(".cs.txt") > -1)
							continue;
					}
					thisAsset = AssetDatabase.LoadAssetAtPath(thisPath, Type.GetType(typesToAdd[i], false, true));
					//if we didn't get anything its probably because Type.GetType didn't return anything because it wants the assembly qualified name
					if (thisAsset == null)
					{
						var typeToFind = GetTypeByName(typesToAdd[i]);
						if (typeToFind != null)
							thisAsset = AssetDatabase.LoadAssetAtPath(thisPath, typeToFind);
						else
							Debug.LogWarning("[UMAAssetIndex] Could not determine the System Type for the given type name " + typesToIndex[i]);
					}
					if (thisAsset)
					{
						int thisAssetHash = -1;
						string thisAssetName = "";
						GetAssetHashAndNames(thisAsset, ref thisAssetHash, ref thisAssetName);
						if ((thisAssetHash == 0 || thisAssetHash == -1) || thisAssetName == "")
							continue;
						if (InAssetBundle(thisPath, thisAsset.name))
						{
							_assetBundleIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
						}
						else
						{
							_fullIndex.AddPath(thisAsset, thisAssetHash, thisAssetName);
							//Automatically add anything in a Resources folder because it will already be included in the game
							if (InResources(thisPath))
							{
								//Index assets in resources but without adding a refrence to the asset
								if(_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, false, compareFullPaths))
									buildIndexModified = true;
							}
						}
					}
				}
			}
			return buildIndexModified;
		}

		/// <summary>
		/// Removes Assets of the given types from the indexes
		/// </summary>
		/// <returns>true if anything was removed from the BuildIndex. false otherwise</returns>
		private bool RemoveTypesFromIndex(List<string> typesToRemove)
		{
			bool buildIndexChanged = false;
			for (int i = 0; i < typesToRemove.Count; i++)
			{
				Type thisType = Type.GetType(typesToRemove[i]);
				if (thisType == null)
				{
					thisType = GetTypeByName(typesToRemove[i]);
				}
				if (thisType == null)
				{
					Debug.LogWarning("[UMAAssetIndex] could not find the requested type using type name " + typesToRemove[i] + ". Did you enter the name correctly?");
					continue;
				}
				if (_fullIndex.ContainsType(thisType.ToString()))
				{
					_fullIndex.RemoveType(thisType);
				}
				if (_buildIndex.ContainsType(thisType.ToString()))
				{
					_buildIndex.RemoveType(thisType);
					buildIndexChanged = true;
                }
				if (_assetBundleIndex.ContainsType(thisType.ToString()))
				{
					_assetBundleIndex.RemoveType(thisType);
				}
			}
			return buildIndexChanged;
		}

		public void ToggleFolderAssets(string path, string assetType, bool live)
		{
			for (int ti = 0; ti < _fullIndex.data.Length; ti++)
			{
				if (_fullIndex.data[ti].type == assetType)
				{
					for (int i = 0; i < _fullIndex.data[ti].typeIndex.Length; i++)
					{
						if (path == Path.GetDirectoryName(_fullIndex.data[ti].typeIndex[i].fullPath))
						{
							if (live)
								MakeAssetLive(_fullIndex.data[ti].typeIndex[i], assetType, false);
							else
								MakeAssetNotLive(_fullIndex.data[ti].typeIndex[i], assetType, false);
						}
					}
				}
			}
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
			CheckAndUpdateWindow();
			EditorApplication.delayCall -= CleanUnusedAssets;
			EditorApplication.delayCall += CleanUnusedAssets;
		}
		/// <summary>
		/// Adds the given indexed item to the 'BuildIndex' by finding its referenced asset and adding an entry to the Build index that references this asset.
		/// </summary>
		/// <param name="fullIndexData"></param>
		/// <param name="assetType"></param>
		public void MakeAssetLive(UMAAssetIndexData.IndexData fullIndexData, string assetType, bool andUpdate = true)
		{
			if (fullIndexData != null)
			{
				UnityEngine.Object thisAsset = null;
				thisAsset = AssetDatabase.LoadAssetAtPath(fullIndexData.fullPath, Type.GetType(assetType, false, true));
				//if we didn't get anything its probably because Type.GetType didn't return anything because it wants the assembly qualified name
				if (thisAsset == null)
				{
					var typeToFind = GetTypeByName(assetType);
					if (typeToFind != null)
						thisAsset = AssetDatabase.LoadAssetAtPath(fullIndexData.fullPath, typeToFind);
					else
						Debug.LogWarning("[UMAAssetIndex] Could not determine the System Type for the given type name " + assetType);
				}
				if (thisAsset)
				{
					int thisAssetHash = -1;
					string thisAssetName = "";
					GetAssetHashAndNames(thisAsset, ref thisAssetHash, ref thisAssetName);
					if ((thisAssetHash == 0 || thisAssetHash == -1) || thisAssetName == "")
						return;
					//we dont want a file ref if the object is live because it was moved into Resources
					if (InResources(fullIndexData.fullPath))
						_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, false);
					else
						_buildIndex.AddPath(thisAsset, thisAssetHash, thisAssetName, true);
				}
			}
			if (andUpdate)
			{
				CheckAndUpdateWindow();
				EditorApplication.delayCall -= CleanUnusedAssets;
				EditorApplication.delayCall += CleanUnusedAssets;
			}
		}

		/// <summary>
		/// Removes a previously indexed asset from the BuildIndex so its asset is no longer referenced
		/// </summary>
		/// <param name="fullIndexData"></param>
		/// <param name="assetType"></param>
		public void MakeAssetNotLive(UMAAssetIndexData.IndexData fullIndexData, string assetType, bool andUpdate = true)
		{
			_buildIndex.RemovePath(fullIndexData.fullPath);
			if (andUpdate)
			{
				CheckAndUpdateWindow();
				EditorApplication.delayCall -= CleanUnusedAssets;
				EditorApplication.delayCall += CleanUnusedAssets;
			}
		}

		#endregion

		#region INDEX MODIFICATION HELPER METHODS

		private bool AMPMovedAssetsContains(string ppath = "", string npath = "")
		{
			for (int i = 0; i < AMPMovedAssets.Count; i++)
			{
				if (ppath != "" && npath != "")
				{
					if (AMPMovedAssets[i].prevPath == ppath && AMPMovedAssets[i].newPath == npath)
						return true;
				}
				else if (ppath != "")
				{
					if (AMPMovedAssets[i].prevPath == ppath)
						return true;
				}
				else if (npath != "")
				{
					if (AMPMovedAssets[i].newPath == npath)
						return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Checks whether the given Unity Object is one of the types we are tracking
		/// </summary>
		private bool IsAssetATrackedType(UnityEngine.Object assetToTest)
		{
			if (assetToTest == null)
				return false;
			bool isTypeTracked = false;
			for (int sti = 0; sti < typesToIndex.Count; sti++)
			{
				Type foundType = Type.GetType(typesToIndex[sti], false, true);
				if (foundType == null)
				{
					foundType = GetTypeByName(typesToIndex[sti]);
					if (foundType == null)
						Debug.LogWarning("Type not found in types to index for " + typesToIndex[sti]);
				}
				if (foundType != null)
				{
					Type typeToCompare = assetToTest.GetType();
					//sortout animation controller funkiness
					if (typeToCompare == typeof(UnityEditor.Animations.AnimatorController))
					{
						typeToCompare = typeof(UnityEngine.RuntimeAnimatorController);
					}
					if (typeToCompare == foundType)
					{
						isTypeTracked = true;
						break;
					}
				}
			}
			return isTypeTracked;
		}

		/// <summary>
		/// Sorts all the indexes by folder name
		/// </summary>
		private void SortIndexes()
		{
			for (int ti = 0; ti < _fullIndex.data.Length; ti++)
				Array.Sort(_fullIndex.data[ti].typeIndex, CompareByFolderName);
		}

		/// <summary>
		/// Comparer for SortIndexes above
		/// </summary>
		private int CompareByFolderName(UMAAssetIndexData.IndexData obj1, UMAAssetIndexData.IndexData obj2)
		{
			if (obj1 == null)
			{
				if (obj2 == null) return 0;
				else return -1;
			}
			else
			{
				if (obj2 == null)
				{
					return 1;
				}
				else
				{
					string folder1 = System.IO.Path.GetDirectoryName(obj1.fullPath);
					string folder2 = System.IO.Path.GetDirectoryName(obj2.fullPath);
					int folderCompare = String.Compare(folder1, folder2);
					if (folderCompare != 0) return folderCompare;
					string file1 = System.IO.Path.GetFileName(obj1.fullPath);
					string file2 = System.IO.Path.GetFileName(obj2.fullPath);
					int fileCompare = String.Compare(file1, file2);
					return fileCompare;
				}
			}
		}

		public void CheckAndUpdateWindow()
		{
			if (windowInstance != null)
				windowInstance.Repaint();
		}

		private void GetAssetHashAndNames(UnityEngine.Object obj, ref int assetHash, ref string assetName)
		{
			assetName = obj.name;
			assetHash = UMAUtils.StringToHash(obj.name);
			if (obj is SlotDataAsset)
			{
				assetName = (obj as SlotDataAsset).slotName;
				assetHash = (obj as SlotDataAsset).nameHash;
			}
			else if (obj is OverlayDataAsset)
			{
				assetName = (obj as OverlayDataAsset).overlayName;
				assetHash = (obj as OverlayDataAsset).nameHash;
			}
			else if (obj is RaceData)
			{
				assetName = (obj as RaceData).raceName;
				assetHash = UMAUtils.StringToHash((obj as RaceData).raceName);
			}
		}

		private Type GetTypeByName(string name)
		{
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (Type type in assembly.GetTypes())
				{
					if (type.Name == name)
						return type;
				}
			}

			return null;
		}

		public bool InResources(string path)
		{
			return (path.IndexOf("/Resources/") > -1 || path.IndexOf("\\Resources\\") > -1);
		}

		public bool InAssetBundle(string path, string assetName)
		{
			string[] assetBundleNames = AssetDatabase.GetAllAssetBundleNames();
			List<string> pathsInBundle;
			for (int i = 0; i < assetBundleNames.Length; i++)
			{
				pathsInBundle = new List<string>(AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(assetBundleNames[i], assetName));
				if (pathsInBundle.Contains(path))
					return true;
			}
			return false;
		}

		#endregion

#endif

		#region INDEX FIND ASSETS METHODS

		/// <summary>
		/// Returns a List of the given Type containing all the assets that have been made Live in the BuildIndex, the list is empty if not assets of the requested type are found.
		/// </summary>
		public List<T> LoadAllAssetsOfType<T>(string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			return _buildIndex.GetAll<T>(foldersToSearch);
		}
		/// <summary>
		/// Returns the asset of the given Type and uma name if it has been added to the BuildIndex, null if not found
		/// </summary>
		public UnityEngine.Object LoadAsset<T>(string umaName, string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			return _buildIndex.Get<T>(umaName, foldersToSearch);
		}
		/// <summary>
		/// Returns the asset of the given Type and uma nameHash if it has been added to the BuildIndex, null if not found 
		/// </summary>
		public UnityEngine.Object LoadAsset<T>(int umaNameHash, string[] foldersToSearch = null) where T : UnityEngine.Object
		{
			return _buildIndex.Get<T>(umaNameHash, foldersToSearch);
		}
		/// <summary>
		/// Loads an asset at the given path from the BuildIndex and returns it as UnityEngine.Object. Null if no object can be found.
		/// </summary>
		public UnityEngine.Object LoadAssetAtPath(string path)
		{
			UnityEngine.Object foundObj = null;
			for (int i = 0; i < _buildIndex.data.Length; i++)
			{
				for (int ti = 0; ti < _buildIndex.data[i].typeIndex.Length; ti++)
				{
					if (_buildIndex.data[i].typeIndex[ti].fullPath == path)
						return _buildIndex.data[i].typeIndex[ti].TheFileReference;
				}
			}
			return foundObj;
		}

		#endregion
	}
}

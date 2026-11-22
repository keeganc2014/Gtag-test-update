using UnityEditor;

// Import settings for Assets/Resources/Rig/GorillaRig.fbx (the Gorilla IK rig exported from Blender):
// keep the bones as a Generic rig (the game drives them from code), no colliders/cameras/lights.
public class RigImportSettings : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.Contains("Resources/Rig/")) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.addCollider = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = false;
        importer.optimizeGameObjects = false;   // bones must stay as GameObjects
    }
}

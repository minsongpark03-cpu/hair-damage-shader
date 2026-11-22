using UnityEditor;
using UnityEngine;

namespace HairDemo.EditorTools
{
    /// <summary>
    /// 헤어 데모용 에셋의 임포트 설정을 자동으로 맞춘다.
    ///  - lookup1/lookup2 LUT 텍스처: 색(sRGB)이 아니라 "수치 테이블"이라 선형으로 읽고,
    ///    압축/밉맵/반복 없이 그대로 샘플링해야 프로토타입과 같은 값이 나온다.
    ///  - head_model.obj: 코드에서 정점/삼각형을 읽어 노멀을 다시 계산하므로 Read/Write를 켠다.
    /// </summary>
    public class HairDemoImportSettings : AssetPostprocessor
    {
        const string Folder = "Assets/Resources/HairDemo/";

        // 에셋이 이 스크립트보다 먼저 임포트됐을 수 있으니, 컴파일 직후 설정이 틀리면 다시 임포트한다.
        [InitializeOnLoadMethod]
        static void EnsureSettings()
        {
            EditorApplication.delayCall += () =>
            {
                foreach (var file in new[] { "lookup1.png", "lookup2.png" })
                {
                    string path = Folder + file;
                    var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti != null && (ti.sRGBTexture || ti.mipmapEnabled ||
                        ti.textureCompression != TextureImporterCompression.Uncompressed ||
                        ti.wrapMode != TextureWrapMode.Clamp))
                    {
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    }
                }

                foreach (var objPath in new[] { Folder + "head_model.obj", Folder + "Heads/head_female.obj" })
                {
                    var mi = AssetImporter.GetAtPath(objPath) as ModelImporter;
                    if (mi != null && !mi.isReadable)
                    {
                        AssetDatabase.ImportAsset(objPath, ImportAssetOptions.ForceUpdate);
                    }
                }
            };
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder + "lookup")) return;

            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.maxTextureSize = 1024;
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder + "head_model") && !assetPath.StartsWith(Folder + "Heads/")) return;

            var mi = (ModelImporter)assetImporter;
            mi.isReadable = true;
            mi.importNormals = ModelImporterNormals.Calculate; // OBJ에 노멀이 없어서 임포터가 계산하게 한다(경고 방지)
            mi.importCameras = false;
            mi.importLights = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.indexFormat = ModelImporterIndexFormat.UInt32;
        }
    }
}

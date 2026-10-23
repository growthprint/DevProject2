#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 장면 전환이 풀 굽기를 부르므로 풀 코드 전체를 게임 어셈블리에 둔다.
namespace Game.App
{
    // 원본 TgsForUnityTerrain의 Play Mode 경로다. 층마다 지형을 덩어리로 나눠 굽기 재료를 넘기고, 덩어리는 레벨이 끝날 때까지 든다.
    // 켜고 끌 때는 그리기만 켜고 끈다. 실제 굽기와 그리기는 OriginalGrassRenderer가 한다.
    [DisallowMultipleComponent, RequireComponent(typeof(Terrain))]
    public sealed class OriginalTerrainGrass : MonoBehaviour
    {
        public enum TerrainLayerDistribution
        {
            Fill,
            TastyGrassShaderPaintTool,
            ByTerrainLayer,
            ByCustomTexture
        }

        // 원본 TgsTerrainLayer다. 제작 도구가 원본 값을 그대로 옮기도록 직렬화 필드는 원본 이름 앞에 _를 붙이고 원본 순서를 지킨다.
        // 덩어리 목록은 직렬화하지 않고 켜져 있는 동안만 있다.
        [Serializable]
        public sealed class GrassLayer
        {
            [SerializeField] bool _hide;
            [SerializeField] OriginalGrassPreset.Settings _settings = OriginalGrassPreset.Settings.GetDefault();
            [SerializeField] TerrainLayerDistribution _distribution;
            [SerializeField] Texture2D? _paintedDensityMapStorage;
            [SerializeField] int _targetTerrainLayer;
            [SerializeField] Texture2D? _distributionTexture;
            [SerializeField] Vector2 _scaling = Vector2.one;
            [SerializeField] Vector2 _offset;
            [SerializeField] Color _colorMask = Color.white;
            [SerializeField, Min(8f)] int _chunkSize = 32;

            // 지형 스플랫맵 한 장은 RGBA 채널마다 지형 층 하나를 담는다.
            const int _channelsPerAlphamap = 4;

            readonly List<OriginalGrassChunk> _chunks = new List<OriginalGrassChunk>();

            // 원본 CheckForChange가 새 덩어리에 하는 일이다. 덩어리 크기를 높이맵 픽셀 수로 바꿔 지형을 나누고,
            // 덩어리마다 분포 텍스처와 지면 색 텍스처를 붙인 굽기 재료를 넘긴다. 원본처럼 픽셀이 없는 가장자리 덩어리도 만든다.
            internal void CreateChunks(Transform terrainTransform, TerrainData terrainData, Texture2D[] densityMaps, TerrainLayer[] terrainLayers,
                int unityLayer, OriginalGrassWind? windSettings, ComputeShader compute, Material material)
            {
                if (_settings._preset == null)
                    return;

                Vector3 position = terrainTransform.position;
                Vector3 terrainSize = terrainData.size;
                // 원본과 같은 순서로 Bounds의 min과 max를 넣어 경계 값의 부동소수 반올림까지 원본과 같게 한다.
                var terrainBounds = new Bounds { min = position };
                terrainBounds.max = terrainBounds.min + terrainSize;
                Vector3 heightmapScale = terrainData.heightmapScale;
                RenderTexture heightmap = terrainData.heightmapTexture;
                var heightmapSize = new Vector2Int(heightmap.width, heightmap.height);
                var chunkPixels = new Vector2Int((int)OriginalGrassChunk.CeilingDivisionFloat(_chunkSize, heightmapScale.x),
                    (int)OriginalGrassChunk.CeilingDivisionFloat(_chunkSize, heightmapScale.z));
                var chunkCount = new Vector2Int(OriginalGrassChunk.CeilingDivision(heightmapSize.x, chunkPixels.x),
                    OriginalGrassChunk.CeilingDivision(heightmapSize.y, chunkPixels.y));
                int total = chunkCount.x * chunkCount.y;

                // 지형 층 분포는 스플랫맵 한 장에 층 네 개가 RGBA로 들어 있으므로 맵 번호와 채널 마스크를 고른다.
                Texture terrainLayerDensity = Texture2D.whiteTexture;
                Vector4 terrainLayerChannel = Vector4.zero;
                if (_distribution == TerrainLayerDistribution.ByTerrainLayer)
                {
                    int mapIndex = _targetTerrainLayer / _channelsPerAlphamap;
                    if (mapIndex < densityMaps.Length)
                        terrainLayerDensity = densityMaps[mapIndex];
                    int channel = _targetTerrainLayer % _channelsPerAlphamap;
                    terrainLayerChannel = new Vector4(channel == 0 ? 1f : 0f, channel == 1 ? 1f : 0f,
                        channel == 2 ? 1f : 0f, channel == 3 ? 1f : 0f);
                }
                // 원본은 칠하기 도구 지도가 없으면 검은 렌더 텍스처를 읽어 새 지도로 쓴다. 결과가 같도록 검은 텍스처를 쓴다.
                Texture paintedDensity = _paintedDensityMapStorage != null ? _paintedDensityMapStorage : Texture2D.blackTexture;

                bool hasCamouflage = _targetTerrainLayer < terrainLayers.Length;
                Texture? camouflageTexture = null;
                Vector4 camouflageScaleOffset = Vector4.zero;
                if (hasCamouflage)
                {
                    TerrainLayer camouflageLayer = terrainLayers[_targetTerrainLayer];
                    camouflageTexture = camouflageLayer.diffuseTexture;
                    // 원본은 오프셋 w에 position.z가 아니라 position.y를 넣는다. 지면 색 무늬 위치가 원본과 같도록 그대로 둔다.
                    camouflageScaleOffset = new Vector4(1f / camouflageLayer.tileSize.x, 1f / camouflageLayer.tileSize.y, position.x, position.y);
                }

                Matrix4x4 localToWorld = terrainTransform.localToWorldMatrix;
                for (int i = 0; i < total; i++)
                {
                    var chunk = new OriginalGrassChunk(compute, material)
                    {
                        Hide = _hide,
                        UnityLayer = unityLayer,
                        UsedWindSettings = windSettings
                    };
                    // 원본처럼 만들자마자 굽기 대기열에 올린다. 렌더러가 프레임당 정해진 수만큼 만든 순서대로 굽는다.
                    // 등록과 해제는 덩어리 수명을 가진 지형 층이 맡아 덩어리가 렌더러를 거꾸로 부르지 않게 한다.
                    OriginalGrassRenderer.Add(chunk);
                    _chunks.Add(chunk);

                    // 원본처럼 번호를 x 칸 수로 나눈 몫과 나머지로 칸을 정하고, 끝 칸은 높이맵 마지막 픽셀에서 자른다.
                    var cell = new Vector2Int(i / chunkCount.x, i % chunkCount.x);
                    Vector2Int min = cell * chunkPixels;
                    Vector2Int max = Vector2Int.Min(heightmapSize - Vector2Int.one, (cell + Vector2Int.one) * chunkPixels);
                    Vector2Int pixelSize = max - min;
                    if (pixelSize.x <= 0 || pixelSize.y <= 0)
                        continue;

                    var chunkBounds = new Bounds
                    {
                        min = terrainBounds.min + new Vector3(min.x * heightmapScale.x, 0f, min.y * heightmapScale.z),
                        max = terrainBounds.min + new Vector3(max.x * heightmapScale.x, terrainSize.y, max.y * heightmapScale.z)
                    };
                    var recipe = OriginalGrassChunk.Recipe.BakeFromHeightmap(localToWorld, _settings, heightmap, chunkBounds, min, pixelSize);
                    // 지형 층과 칠하기 도구 분포는 지형 전체를 덮는 맵이라 UV 변환 없이 읽고, 칠하기 지도는 빨강 채널만 쓴다.
                    switch (_distribution)
                    {
                        case TerrainLayerDistribution.ByTerrainLayer:
                            recipe.SetupDistributionByTexture(terrainLayerDensity, terrainLayerChannel, OriginalGrassChunk.Recipe._identityScaleOffset);
                            break;
                        case TerrainLayerDistribution.TastyGrassShaderPaintTool:
                            recipe.SetupDistributionByTexture(paintedDensity, OriginalGrassChunk.Recipe._redChannelMask,
                                OriginalGrassChunk.Recipe._identityScaleOffset);
                            break;
                        case TerrainLayerDistribution.ByCustomTexture:
                            recipe.SetupDistributionByTexture(_distributionTexture, _colorMask,
                                new Vector4(_scaling.x, _scaling.y, _offset.x, _offset.y));
                            break;
                    }
                    if (hasCamouflage)
                        recipe.SetupCamouflage(camouflageTexture, camouflageScaleOffset, _settings._camouflage);
                    chunk.SetBakeParameters(recipe);
                }
            }

            // 렌더러 목록 끝에서부터 빠지도록 만든 순서의 반대로 놓는다. 원본처럼 GPU 버퍼를 놓은 뒤 목록에서 뺀다.
            internal void Release()
            {
                for (int i = _chunks.Count - 1; i >= 0; i--)
                {
                    OriginalGrassChunk chunk = _chunks[i];
                    chunk.Release();
                    OriginalGrassRenderer.Remove(chunk);
                }
                _chunks.Clear();
            }

            internal void SetShown(bool shown)
            {
                for (int i = 0; i < _chunks.Count; i++)
                    _chunks[i].Shown = shown;
            }
        }

        // 덩어리를 가진 지형이다. 한 번도 켜지지 않은 하위 씬의 지형은 OnDestroy를 받지 못하므로 레벨 정리가 이 목록으로 놓는다.
        static readonly List<OriginalTerrainGrass> _withChunks = new List<OriginalTerrainGrass>();

        [SerializeField] List<GrassLayer> _layers = new List<GrassLayer>();
        [SerializeField] OriginalGrassWind? _windSettings;
        // 원본은 계산 셰이더와 풀 재질을 Resources에서 찾는다. 여기서는 제작 도구가 늘 연결하고 덩어리가 굽기와 그리기에 쓴다.
        // 제품 씬의 연결은 RuntimeAuthoringContractTests가 확인하므로 실행 중에는 검사하지 않는다.
        [SerializeField] ComputeShader _compute;
        [SerializeField] Material _material;
        bool _hasChunks;
        Scene _chunkScene;

        // 레벨 초기화가 부른다. 꺼진 하위 씬 아래 지형까지 덩어리를 만들어 장면 진입의 검은 화면 아래에서 모두 굽게 한다.
        // 원작 차이: 원작은 하위 씬을 불러올 때마다 덩어리를 새로 만들어 화면이 보인 뒤에 굽는다.
        internal static void CreateSceneChunks(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                foreach (OriginalTerrainGrass grass in roots[i].GetComponentsInChildren<OriginalTerrainGrass>(true))
                    grass.CreateChunks();
            }
        }

        // 레벨 정리가 씬을 내리기 전에 부른다. 다른 씬의 지형은 건드리지 않는다.
        internal static void ReleaseSceneChunks(Scene scene)
        {
            for (int i = _withChunks.Count - 1; i >= 0; i--)
            {
                if (_withChunks[i]._chunkScene == scene)
                    _withChunks[i].ReleaseChunks();
            }
        }

        // 로드 때 켜져 있는 지형은 레벨 초기화보다 먼저 여기서 덩어리를 만든다. 이미 있으면 그리기만 다시 켠다.
        void OnEnable()
        {
            CreateChunks();
            SetShown(true);
        }

        // 하위 씬 루트가 꺼져도 덩어리를 놓지 않고 그리기만 멈춘다. 다시 켤 때 굽기가 화면에 보이지 않게 하려는 것이다.
        void OnDisable() => SetShown(false);

        // 레벨 정리 없이 파괴될 때의 백업이다. 이미 놓았으면 아무것도 하지 않는다.
        void OnDestroy() => ReleaseChunks();

        // 지형 위치, 행렬, 레이어를 덩어리를 만들 때 한 번 읽어 경계와 그리기 레이어로 쓰고 변경은 감시하지 않는다.
        // 덩어리는 레벨이 끝날 때까지 유지되므로, 그동안 지형을 옮기거나 레이어를 바꿔도 반영되지 않는다.
        void CreateChunks()
        {
            if (_hasChunks)
                return;
            _hasChunks = true;
            _chunkScene = gameObject.scene;
            _withChunks.Add(this);
            TerrainData terrainData = GetComponent<Terrain>().terrainData;
            Texture2D[] densityMaps = terrainData.alphamapTextures;
            TerrainLayer[] terrainLayers = terrainData.terrainLayers;
            Transform terrainTransform = transform;
            int unityLayer = gameObject.layer;
            for (int i = 0; i < _layers.Count; i++)
            {
                _layers[i].CreateChunks(terrainTransform, terrainData, densityMaps, terrainLayers, unityLayer, _windSettings,
                    _compute, _material);
            }
            SetShown(isActiveAndEnabled);
        }

        void ReleaseChunks()
        {
            if (!_hasChunks)
                return;
            _hasChunks = false;
            _withChunks.Remove(this);
            for (int i = 0; i < _layers.Count; i++)
                _layers[i].Release();
        }

        void SetShown(bool shown)
        {
            for (int i = 0; i < _layers.Count; i++)
                _layers[i].SetShown(shown);
        }
    }
}

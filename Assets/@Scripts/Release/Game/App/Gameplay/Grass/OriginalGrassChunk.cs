#nullable enable annotations
using System;
using Game.Core;
using UnityEngine;
using UnityEngine.Rendering;

// 장면 전환이 풀 굽기를 부르므로 풀 코드 전체를 게임 어셈블리에 둔다.
namespace Game.App
{
    // 원본 TgsInstance의 높이맵 경로다. 지형 한 덩어리를 TerrainPass로 배치 삼각형으로 바꾸고 BakePass로 풀잎을 굽는다.
    // 구운 버퍼는 계속 들고 있다가 카메라마다 LOD로 정한 앞쪽 풀잎만 그린다. 커널, 버퍼 보폭, 셰이더 이름은 계산 셰이더와 맞춘 원본 값이다.
    // 메타데이터, 풀잎 수, 배치 삼각형, 노이즈 버퍼는 굽기 묶음(OriginalGrassBakeBatch)이 가진다. 덩어리는 구운 풀잎 버퍼만 가진다.
    internal sealed class OriginalGrassChunk
    {
        // _MetaDataRW의 32바이트 원소다. TerrainPass가 원자 연산으로 풀잎 수 추정치, 배치 삼각형 수, 압축 경계를 채운다.
        // 굽기 묶음이 덩어리 번호 칸에 모아 한 번에 읽으므로 묶음도 쓴다.
        internal struct InstanceMetaDataGpu
        {
            public uint _estMaxBlades;
            public uint _placementTriangles;
            public uint _boundsMinX;
            public uint _boundsMinY;
            public uint _boundsMinZ;
            public uint _boundsMaxX;
            public uint _boundsMaxY;
            public uint _boundsMaxZ;

            // 원본 SetupBuffersForCompute의 시작값이다. 최소 경계는 원자 최소 연산을 위해 가장 큰 값으로 시작한다.
            internal static InstanceMetaDataGpu Empty => new InstanceMetaDataGpu
            {
                _boundsMinX = uint.MaxValue,
                _boundsMinY = uint.MaxValue,
                _boundsMinZ = uint.MaxValue
            };
        }

        // 원본 TgsInstanceRecipe의 높이맵 굽기 값이다. 지형 층이 덩어리를 만들 때 채우고 덩어리가 구울 때 읽는다.
        internal struct Recipe
        {
            // 원본 분포 기본값과 칠하기 도구 지도가 쓰는 빨강 채널 마스크, 배율 1과 오프셋 0인 UV 변환이다.
            internal static readonly Vector4 _redChannelMask = new Vector4(1f, 0f, 0f, 0f);
            internal static readonly Vector4 _identityScaleOffset = new Vector4(1f, 1f, 0f, 0f);

            internal OriginalGrassPreset.Settings _settings;
            internal Matrix4x4 _localToWorldMatrix;
            internal Bounds _worldSpaceBounds;
            internal bool _distributionByTextureEnabled;
            internal Texture _distributionTexture;
            internal Vector4 _distributionTextureChannelMask;
            internal Vector4 _distributionTextureScaleOffset;
            internal float _camouflageFactor;
            internal Texture _camouflageTexture;
            internal Vector4 _camouflageTextureScaleOffset;
            internal Texture _heightmapTexture;
            internal Vector2Int _heightmapChunkPixelOffset;
            internal Vector2Int _chunkPixelSize;

            // 원본 기본값처럼 분포는 흰 텍스처의 빨강 채널이고 지면 색은 흰 텍스처이며 섞기는 0이다.
            internal static Recipe BakeFromHeightmap(Matrix4x4 localToWorldMatrix, OriginalGrassPreset.Settings settings,
                Texture heightmapTexture, Bounds heightmapChunkBounds, Vector2Int heightmapChunkPixelOffset, Vector2Int chunkPixelSize)
            {
                return new Recipe
                {
                    _settings = settings,
                    _localToWorldMatrix = localToWorldMatrix,
                    _worldSpaceBounds = heightmapChunkBounds,
                    _distributionTexture = Texture2D.whiteTexture,
                    _distributionTextureChannelMask = _redChannelMask,
                    _camouflageTexture = Texture2D.whiteTexture,
                    _heightmapTexture = heightmapTexture,
                    _heightmapChunkPixelOffset = heightmapChunkPixelOffset,
                    _chunkPixelSize = chunkPixelSize
                };
            }

            internal void SetupDistributionByTexture(Texture? densityTexture, Vector4 channelMask, Vector4 scaleOffset)
            {
                _distributionByTextureEnabled = true;
                _distributionTexture = densityTexture != null ? densityTexture : Texture2D.whiteTexture;
                _distributionTextureChannelMask = channelMask;
                _distributionTextureScaleOffset = scaleOffset;
            }

            internal void SetupCamouflage(Texture? colorMap, Vector4 colorMapScaleOffset, float blendFactor)
            {
                _camouflageTexture = colorMap != null ? colorMap : Texture2D.whiteTexture;
                _camouflageTextureScaleOffset = colorMapScaleOffset;
                _camouflageFactor = blendFactor;
            }
        }

        const string _terrainPassKernel = "TerrainPass";
        const string _bakePassKernel = "BakePass";
        const string _copyPrimitivesKernel = "CopyPrimitives";
        internal const int _metaDataStride = 32;
        const int _grassNodeStride = 16;
        internal const int _sizeofPlacementTriangle = 104;
        // 원본은 이 한도로 나눈 횟수만큼 디스패치하면서도 매번 전체 배치 삼각형 수로 디스패치한다. 결과가 같도록 그대로 따른다.
        const int _maxThreadGroupsPerDispatch = 65535;
        // 원본 MaxGrassRootOffset이다. 풀잎 끝이 뿌리에서 벗어날 수 있는 거리만큼 촘촘한 경계를 넓혀 컬링과 압축 범위로 쓴다.
        const float _maxGrassRootOffset = 4f;

        static readonly int _placementTrianglesAppendId = Shader.PropertyToID("_PlacementTrianglesAppend");
        static readonly int _grassFieldPrimitivesAppendId = Shader.PropertyToID("_GrassFieldPrimitivesAppend");
        static readonly int _grassFieldPrimitivesId = Shader.PropertyToID("_GrassFieldPrimitives");
        static readonly int _positionBoundMinId = Shader.PropertyToID("_PositionBoundMin");
        static readonly int _positionBoundMaxId = Shader.PropertyToID("_PositionBoundMax");
        static readonly int _objectToWorldId = Shader.PropertyToID("_ObjectToWorld");
        static readonly int _heightmapId = Shader.PropertyToID("_Heightmap");
        static readonly int _heightmapResolutionXyId = Shader.PropertyToID("_HeightmapResolutionXy");
        static readonly int _heightmapChunkOffsetSizeId = Shader.PropertyToID("_HeightmapChunkOffsetSize");
        static readonly int _densityUseChannelMaskId = Shader.PropertyToID("_DensityUseChannelMask");
        static readonly int _densityMapChannelMaskId = Shader.PropertyToID("_DensityMapChannelMask");
        static readonly int _densityMapId = Shader.PropertyToID("_DensityMap");
        static readonly int _densityMapUvFromHeightmapIdxId = Shader.PropertyToID("_DensityMapUvFromHeightmapIdx");
        static readonly int _noiseParamsId = Shader.PropertyToID("_NoiseParams");
        static readonly int _sphereColliderId = Shader.PropertyToID("_SphereCollider");
        static readonly int _sphereColliderCountId = Shader.PropertyToID("_SphereColliderCount");
        static readonly int _placementTrianglesRId = Shader.PropertyToID("_PlacementTrianglesR");
        static readonly int _placementTriangleCountId = Shader.PropertyToID("_PlacementTriangleCount");
        static readonly int _usedPlacementTriangleCountId = Shader.PropertyToID("_UsedPlacementTriangleCount");
        static readonly int _metaDataRwId = Shader.PropertyToID("_MetaDataRW");
        static readonly int _placementTriangleOffsetId = Shader.PropertyToID("_PlacementTriangleOffset");
        static readonly int _colorMapId = Shader.PropertyToID("_ColorMap");
        static readonly int _colorMapBlendId = Shader.PropertyToID("_ColorMapBlend");
        static readonly int _colorMapStId = Shader.PropertyToID("_ColorMapST");
        static readonly int _metaDataIndexId = Shader.PropertyToID("_MetaDataIndex");
        static readonly int _primitivesCopySourceId = Shader.PropertyToID("_PrimitivesCopySource");
        static readonly int _primitivesCopyDestinationId = Shader.PropertyToID("_PrimitivesCopyDestination");
        static readonly int _primitivesCopyCountId = Shader.PropertyToID("_PrimitivesCopyCount");
        static readonly int _primitivesCopyOffsetId = Shader.PropertyToID("_PrimitivesCopyOffset");

        // GPU 읽기 실패는 덩어리가 수천 개라 한 번만 기록한다.
        static bool _readbackFailureLogged;

        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        readonly ComputeShader _compute;
        ComputeBuffer? _bakeOutputBuffer;
        // 굽는 동안만 잡는 묶음의 버퍼다. 묶음이 만들고 놓으므로 덩어리는 놓지 않고 BakePass를 넣은 뒤 참조만 지운다.
        ComputeBuffer? _grassNoiseLayers;
        ComputeBuffer? _instanceMetaData;
        ComputeBuffer? _placementTriangleBuffer;
        // 지금 굽는 묶음과 그 묶음의 메타데이터·풀잎 수 칸 번호다. 다른 묶음의 읽기 결과는 버린다.
        OriginalGrassBakeBatch? _batch;
        int _metaDataIndex;
        // GPU 읽기가 한 번 실패한 덩어리다. 한 번은 다시 굽고, 또 실패하면 풀잎 없이 끝낸다.
        bool _retried;
        Recipe _recipe;
        bool _hasRecipe;
        // 원본 activeTgsInstanceRecipe의 프리셋이다. 구운 뒤에만 생기며, LOD 배율과 그림자, 모양 값을 그릴 때마다 여기서 읽는다.
        OriginalGrassPreset? _activePreset;
        bool _isMaterialDirty = true;
        // 굽기 단계다. GPU 결과는 AsyncGPUReadback으로 받으므로, 메인 스레드는 GPU를 기다리지 않고 결과가 오면 다음 단계를 잇는다.
        BakeStage _bakeStage;
        // 해제 뒤에 도착한 읽기 결과는 버린다. 지형 층은 해제한 뒤에 렌더러 목록에서 빼므로, 해제 여부를 단계와 따로 둔다.
        bool _released;
        // TerrainPass 배치 삼각형 버퍼의 크기다. BakePass를 디스패치하기 직전에 공유 계산 셰이더 값을 다시 넣을 때 쓴다.
        int _placementTriangleCapacity;

        enum BakeStage
        {
            Waiting,
            ReadingMetaData,
            ReadingBladeCount,
            Baked
        }

        internal OriginalGrassChunk(ComputeShader compute, Material material)
        {
            _compute = compute;
            Material = material;
        }

        internal Material Material { get; }

        // 지형 층의 _hide를 덩어리를 만들 때 복사한 값이라 Play Mode 중 인스펙터 편집은 반영되지 않는다.
        internal bool Hide { get; set; }

        // 지형이 켜져 있는 동안만 그린다. 꺼진 하위 씬의 덩어리도 구워서 들고 있어, 다시 켤 때 굽지 않는다.
        internal bool Shown { get; set; }

        internal int UnityLayer { get; set; }

        internal OriginalGrassWind? UsedWindSettings { get; set; }

        // 아직 굽기를 시작하지 않았다.
        internal bool IsWaitingForBake => _bakeStage == BakeStage.Waiting;

        // 굽기가 끝나지 않았다. 시작한 뒤 GPU 결과를 기다리는 동안도 포함한다.
        internal bool IsGeometryDirty => _bakeStage != BakeStage.Baked;

        internal int BladeCount { get; private set; }

        internal Bounds TightBounds { get; private set; }

        internal float LodBias => _activePreset != null ? _activePreset.BaseLodFactor : 0f;

        // 굽기 재료가 있어 GPU로 구워야 한다. 렌더러가 이 덩어리를 굽기 묶음에 넣을지 정한다.
        internal bool HasBakeParameters => _hasRecipe;

        // 원본처럼 높이맵 픽셀 하나가 삼각형 둘이고, TerrainPass는 삼각형마다 스레드 하나를 쓴다. 묶음이 이 크기의 배치 삼각형 버퍼를 빌려준다.
        internal int PlacementTriangleCapacity => _recipe._chunkPixelSize.x * _recipe._chunkPixelSize.y * 2;

        // 묶음이 같은 설정의 노이즈 버퍼를 함께 쓰도록 굽기 설정을 보여 준다.
        internal OriginalGrassPreset.Settings BakeSettings => _recipe._settings;

        internal void SetBakeParameters(Recipe recipe)
        {
            _recipe = recipe;
            _hasRecipe = true;
        }

        // 원본 BakeNextRecipe에서 재료가 없는 경우다. 높이맵 픽셀이 없어 재료를 받지 못한 가장자리 덩어리도 원본처럼 굽기 차례 하나를 쓰고
        // 아무것도 만들지 않는다.
        internal void SkipBake() => _bakeStage = BakeStage.Baked;

        // 원본 BakeFromHeightmap의 앞부분이다. 배치 삼각형을 만드는 TerrainPass를 디스패치한다. 촘촘한 경계와 풀잎 버퍼 크기를 담은
        // 메타데이터는 묶음이 다른 덩어리 것과 함께 비동기로 읽는다. 원본은 여기서 결과를 동기로 두 번 읽는다.
        // 지형 층은 프리셋이 있을 때만 덩어리를 만들므로 재료가 있으면 프리셋도 있다.
        internal void DispatchTerrainPass(OriginalGrassBakeBatch batch, int metaDataIndex, ComputeBuffer metaData, ComputeBuffer placementTriangles,
            ComputeBuffer noiseLayers)
        {
            _batch = batch;
            _metaDataIndex = metaDataIndex;
            _instanceMetaData = metaData;
            _placementTriangleBuffer = placementTriangles;
            _grassNoiseLayers = noiseLayers;
            _placementTriangleCapacity = PlacementTriangleCapacity;
            _bakeStage = BakeStage.ReadingMetaData;
            int kernel = ApplyTerrainPassValues();
            ApplyBakePassValues(_recipe._settings._preset!);
            _compute.GetKernelThreadGroupSizes(kernel, out uint groupSizeX, out _, out _);
            _compute.Dispatch(kernel, CeilingDivision(_placementTriangleCapacity, (int)groupSizeX), 1, 1);
        }

        // 메타데이터가 오면 원본 순서대로 경계를 풀고 BakePass를 디스패치한다. 계산 셰이더는 덩어리끼리 함께 쓰므로, 그 사이 다른 덩어리가
        // 바꾼 값을 TerrainPass 때와 같게 다시 넣는다. 그러면 원본이 동기로 읽고 바로 디스패치할 때와 같은 값으로 BakePass가 돈다.
        // 실제 풀잎 수는 계수기를 묶음의 풀잎 수 버퍼 칸에 복사해 묶음이 읽는다. 그 수를 기다리면 true다.
        internal bool OnMetaDataRead(OriginalGrassBakeBatch batch, InstanceMetaDataGpu metaData, ComputeBuffer bladeCounts)
        {
            if (!IsCurrentRead(batch, BakeStage.ReadingMetaData))
                return false;
            OriginalGrassPreset preset = _recipe._settings._preset!;
            ApplyTerrainPassValues();
            ApplyBakePassValues(preset);
            UnpackAndApplyTightBounds(metaData, _recipe._worldSpaceBounds);
            bool dispatched = DispatchBakePass((int)metaData._placementTriangles, (int)metaData._estMaxBlades, bladeCounts);
            // BakePass까지 넣었으므로 묶음 버퍼를 더 쓰지 않는다. 묶음이 이어서 돌려주거나 놓는다.
            _instanceMetaData = null;
            _placementTriangleBuffer = null;
            _grassNoiseLayers = null;
            if (!dispatched)
                FinishBake(0);
            return dispatched;
        }

        // 풀잎 수가 오면 굽기를 끝낸다.
        internal void OnBladeCountRead(OriginalGrassBakeBatch batch, int bladeCount)
        {
            if (IsCurrentRead(batch, BakeStage.ReadingBladeCount))
                FinishBake(bladeCount);
        }

        // 묶음의 GPU 읽기가 실패하면 한 번은 굽기 대기로 돌려 렌더러가 다시 굽게 한다. 두 번째 실패는 이 덩어리만 풀잎 없이 끝내,
        // 장면 전환이 남은 굽기를 영원히 기다리지 않게 한다.
        internal void OnReadFailed(OriginalGrassBakeBatch batch)
        {
            if (_released || _batch != batch)
                return;
            ReleaseBuffer(ref _bakeOutputBuffer);
            if (_retried)
            {
                FailBake();
                return;
            }
            _retried = true;
            DetachBatch();
            _bakeStage = BakeStage.Waiting;
        }

        // 원본처럼 바람 설정이 없으면 그리지 않는다. 렌더러는 풀잎 수가 양수인 덩어리만 부르고, 그 수는 풀잎 버퍼를 만든 뒤에만 생긴다.
        // 충돌체와 바람 값은 카메라마다 블록에 다시 넣는다.
        internal void Draw(Camera camera, int vertexCount, Vector4[] colliders, int colliderCount)
        {
            if (UsedWindSettings == null)
                return;
            // 그릴 풀잎은 프리셋으로 구운 덩어리에만 있으므로 프리셋이 늘 있다.
            OriginalGrassPreset preset = _activePreset!;

            // 원본 SetInt는 값을 실수로 저장하므로 같은 SetFloat로 넣는다.
            _block.SetFloat(_sphereColliderCountId, colliderCount);
            if (colliderCount > 0)
                _block.SetVectorArray(_sphereColliderId, colliders);
            if (_isMaterialDirty)
            {
                _isMaterialDirty = false;
                preset.ApplyToMaterialPropertyBlock(_block);
            }
            UsedWindSettings.ApplyToMaterialPropertyBlock(_block);

            // 원본 DrawProcedural을 대신한다. 그 카메라에만, 풀잎 하나를 삼각형 하나로 그린다.
            // 광원 프로브와 반사 프로브는 메시 그리기의 기본값인 BlendProbes를 쓴다.
            var renderParams = new RenderParams(Material)
            {
                worldBounds = TightBounds,
                camera = camera,
                matProps = _block,
                shadowCastingMode = preset.CastShadows ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off,
                receiveShadows = true,
                layer = UnityLayer,
                lightProbeUsage = LightProbeUsage.BlendProbes,
                reflectionProbeUsage = ReflectionProbeUsage.BlendProbes
            };
            Graphics.RenderPrimitives(renderParams, MeshTopology.Triangles, vertexCount);
        }

        // 원본 Release처럼 덩어리의 GPU 버퍼를 놓는다. 굽는 동안 잡은 묶음 버퍼는 묶음이 놓는다. 렌더러 목록에서 빼는 일은 덩어리를 만든 지형 층이 한다.
        // 굽는 도중이면 아직 오지 않은 읽기 결과를 버리도록 해제 표시를 남긴다.
        internal void Release()
        {
            _released = true;
            ReleaseBuffer(ref _bakeOutputBuffer);
            DetachBatch();
        }

        internal static float ComputeCameraFovScalingFactor(Camera camera)
            => 2f * Mathf.Tan(camera.fieldOfView * (MathF.PI / 180f) * .5f);

        internal static int CeilingDivision(int lhs, int rhs) => (lhs + rhs - 1) / rhs;

        internal static float CeilingDivisionFloat(float lhs, float rhs) => (lhs + rhs - 1f) / rhs;

        // 읽기 결과가 이 덩어리가 지금 기다리는 읽기인지 본다. 해제했거나, 다른 묶음으로 넘어갔거나, 다른 단계면 늦게 온 결과라 버린다.
        bool IsCurrentRead(OriginalGrassBakeBatch batch, BakeStage stage) => !_released && _batch == batch && _bakeStage == stage;

        // 원본처럼 구운 뒤에야 프리셋을 잡아, 굽는 동안 그리기와 LOD가 이 덩어리를 건너뛴다.
        // 덩어리는 한 번만 구우므로 그리기에 쓰는 풀잎 버퍼만 실제 풀잎 수로 줄여 남긴다. 풀잎이 없으면 버퍼도 놓는다.
        void FinishBake(int bladeCount)
        {
            BladeCount = bladeCount;
            if (bladeCount > 0 && _bakeOutputBuffer != null)
                TrimBakeOutput(bladeCount);
            else
                ReleaseBuffer(ref _bakeOutputBuffer);
            _activePreset = _recipe._settings._preset;
            _bakeStage = BakeStage.Baked;
            DetachBatch();
        }

        // 묶음 버퍼의 참조를 지운다. 버퍼는 묶음이 놓으므로 여기서 놓지 않는다.
        void DetachBatch()
        {
            _batch = null;
            _instanceMetaData = null;
            _placementTriangleBuffer = null;
            _grassNoiseLayers = null;
        }

        // 원작 차이: 원본은 추정치 크기의 풀잎 버퍼를 그대로 그린다. 여기서는 앞쪽 실제 풀잎을 같은 순서로 딱 맞는 버퍼에 옮겨 VRAM을 줄인다.
        // 그리기는 LOD로 정한 앞쪽 풀잎만 읽고 그 수는 실제 풀잎 수를 넘지 않으므로 보이는 결과가 같다.
        // 옛 버퍼는 복사를 넣은 직후 놓는다. Unity는 GPU가 아직 쓰는 버퍼를 그 일이 끝난 뒤에 지운다.
        void TrimBakeOutput(int bladeCount)
        {
            var trimmed = new ComputeBuffer(bladeCount, _grassNodeStride);
            int kernel = _compute.FindKernel(_copyPrimitivesKernel);
            _compute.SetBuffer(kernel, _primitivesCopySourceId, _bakeOutputBuffer);
            _compute.SetBuffer(kernel, _primitivesCopyDestinationId, trimmed);
            _compute.SetInt(_primitivesCopyCountId, bladeCount);
            _compute.GetKernelThreadGroupSizes(kernel, out uint groupSizeX, out _, out _);
            int groupCount = CeilingDivision(bladeCount, (int)groupSizeX);
            for (int offset = 0; offset < groupCount; offset += _maxThreadGroupsPerDispatch)
            {
                _compute.SetInt(_primitivesCopyOffsetId, offset * (int)groupSizeX);
                _compute.Dispatch(kernel, Mathf.Min(groupCount - offset, _maxThreadGroupsPerDispatch), 1, 1);
            }
            ReleaseBuffer(ref _bakeOutputBuffer);
            _bakeOutputBuffer = trimmed;
            _block.SetBuffer(_grassFieldPrimitivesId, trimmed);
        }

        // 다시 구워도 GPU 결과를 읽지 못한 덩어리는 풀잎 없이 끝낸다.
        void FailBake()
        {
            if (!_readbackFailureLogged)
            {
                _readbackFailureLogged = true;
                Log.Instance.Error("풀 덩어리의 GPU 결과를 다시 굽고도 읽지 못해 그 덩어리를 그리지 않습니다.");
            }
            FinishBake(0);
        }

        // TerrainPass가 읽는 계산 셰이더 값을 넣고 커널 번호를 돌려준다. 위치 경계는 덩어리의 느슨한 경계다.
        // 메타데이터는 묶음 버퍼에서 이 덩어리의 칸에 쓴다.
        int ApplyTerrainPassValues()
        {
            Texture heightmap = _recipe._heightmapTexture;
            Texture distribution = _recipe._distributionTexture;
            Bounds chunkBounds = _recipe._worldSpaceBounds;
            Vector2Int pixelOffset = _recipe._heightmapChunkPixelOffset;
            Vector2Int pixelSize = _recipe._chunkPixelSize;

            int kernel = _compute.FindKernel(_terrainPassKernel);
            _compute.SetTexture(kernel, _heightmapId, heightmap);
            _compute.SetVector(_heightmapResolutionXyId, new Vector2(heightmap.width, heightmap.height));
            _compute.SetTexture(kernel, _densityMapId, distribution);
            _compute.SetVector(_densityMapChannelMaskId, _recipe._distributionTextureChannelMask);
            _compute.SetInt(_densityUseChannelMaskId, _recipe._distributionByTextureEnabled ? 1 : 0);
            // 원본 식을 그대로 둔다. 분포 텍스처 크기가 약분되어 높이맵 픽셀 번호를 0~1 UV로 바꾸는 배율이 된다.
            float uvScaleX = (float)distribution.width / heightmap.width / distribution.width;
            float uvScaleY = (float)distribution.height / heightmap.height / distribution.height;
            Vector4 scaleOffset = _recipe._distributionTextureScaleOffset;
            _compute.SetVector(_densityMapUvFromHeightmapIdxId,
                new Vector4(uvScaleX * scaleOffset.x, uvScaleY * scaleOffset.y, scaleOffset.z, scaleOffset.w));
            _compute.SetVector(_heightmapChunkOffsetSizeId, new Vector4(pixelOffset.x, pixelOffset.y, pixelSize.x, pixelSize.y));
            _compute.SetVector(_positionBoundMinId, chunkBounds.min);
            _compute.SetVector(_positionBoundMaxId, chunkBounds.max);
            _compute.SetBuffer(kernel, _placementTrianglesAppendId, _placementTriangleBuffer);
            _compute.SetBuffer(kernel, _metaDataRwId, _instanceMetaData);
            _compute.SetInt(_metaDataIndexId, _metaDataIndex);
            _compute.SetMatrix(_objectToWorldId, _recipe._localToWorldMatrix);
            return kernel;
        }

        // 원본 SetupBuffersForCompute의 BakePass 값 넣기다. 원본은 TerrainPass 전에 한 번 넣지만, 여기서는 BakePass 직전에도 다시 넣는다.
        void ApplyBakePassValues(OriginalGrassPreset preset)
        {
            int bakeKernel = _compute.FindKernel(_bakePassKernel);
            _compute.SetBuffer(bakeKernel, _noiseParamsId, _grassNoiseLayers);
            _compute.SetBuffer(bakeKernel, _placementTrianglesRId, _placementTriangleBuffer);
            _compute.SetInt(_placementTriangleCountId, _placementTriangleCapacity);
            preset.ApplyToComputeShader(_compute, _recipe._settings, bakeKernel);
            _compute.SetTexture(bakeKernel, _colorMapId, _recipe._camouflageTexture);
            _compute.SetFloat(_colorMapBlendId, _recipe._camouflageFactor);
            _compute.SetVector(_colorMapStId, _recipe._camouflageTextureScaleOffset);
        }

        // 원본 UnpackAndApplyTightBounds다. 32비트로 압축한 경계를 덩어리 경계 안에서 풀고, 풀잎이 벗어날 거리만큼 넓힌다.
        // BakePass는 이 경계 안에서 풀잎 위치를 압축하고, 풀 셰이더는 같은 경계로 푼다.
        void UnpackAndApplyTightBounds(InstanceMetaDataGpu metaData, Bounds looseBounds)
        {
            Vector3 looseMin = looseBounds.min;
            Vector3 looseMax = looseBounds.max;
            Vector3 min = UnpackVector3_32Bit(metaData._boundsMinX, metaData._boundsMinY, metaData._boundsMinZ, looseMin, looseMax);
            Vector3 max = UnpackVector3_32Bit(metaData._boundsMaxX, metaData._boundsMaxY, metaData._boundsMaxZ, looseMin, looseMax);
            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            bounds.Expand(_maxGrassRootOffset);
            TightBounds = bounds;
            _block.SetVector(_positionBoundMinId, bounds.min);
            _block.SetVector(_positionBoundMaxId, bounds.max);
            _compute.SetVector(_positionBoundMinId, bounds.min);
            _compute.SetVector(_positionBoundMaxId, bounds.max);
        }

        // 원본 BakeFromPlacementBufferOrSetNull이다. 풀잎 버퍼는 TerrainPass의 최대 추정치로 잡고, 실제 수는 계수기를 묶음 버퍼의
        // 이 덩어리 칸에 복사해 묶음이 비동기로 읽는다. 배치 삼각형이나 추정치가 없으면 버퍼를 만들지 않고 false를 돌려준다.
        // 그러면 풀잎 수가 0으로 끝나 그리기를 건너뛴다.
        bool DispatchBakePass(int placementTriangleCount, int estMaxBlades, ComputeBuffer bladeCounts)
        {
            if (placementTriangleCount <= 0 || estMaxBlades <= 0)
                return false;

            _bakeOutputBuffer = new ComputeBuffer(estMaxBlades, _grassNodeStride, ComputeBufferType.Append);
            _bakeOutputBuffer.SetCounterValue(0u);

            int kernel = _compute.FindKernel(_bakePassKernel);
            _compute.SetBuffer(kernel, _placementTrianglesRId, _placementTriangleBuffer);
            _compute.SetBuffer(kernel, _grassFieldPrimitivesAppendId, _bakeOutputBuffer);
            _compute.SetInt(_usedPlacementTriangleCountId, placementTriangleCount);
            int dispatchCount = CeilingDivision(placementTriangleCount, _maxThreadGroupsPerDispatch);
            for (int i = 0; i < dispatchCount; i++)
            {
                _compute.SetInt(_placementTriangleOffsetId, i * _maxThreadGroupsPerDispatch);
                _compute.Dispatch(kernel, placementTriangleCount, 1, 1);
            }

            ComputeBuffer.CopyCount(_bakeOutputBuffer, bladeCounts, _metaDataIndex * sizeof(int));
            _bakeStage = BakeStage.ReadingBladeCount;
            return true;
        }

        // 원본처럼 2^32-2로 나눠 0~1로 만든다. Lerp가 1을 넘는 값을 잘라 비어 있는 최소 경계도 덩어리 경계 안에 남는다.
        static Vector3 UnpackVector3_32Bit(uint x, uint y, uint z, Vector3 min, Vector3 max)
        {
            const double scale = 4294967294.0;
            return new Vector3(Mathf.Lerp(min.x, max.x, (float)(x / scale)), Mathf.Lerp(min.y, max.y, (float)(y / scale)),
                Mathf.Lerp(min.z, max.z, (float)(z / scale)));
        }

        static void ReleaseBuffer(ref ComputeBuffer? buffer)
        {
            if (buffer == null)
                return;
            buffer.Release();
            buffer = null;
        }
    }
}

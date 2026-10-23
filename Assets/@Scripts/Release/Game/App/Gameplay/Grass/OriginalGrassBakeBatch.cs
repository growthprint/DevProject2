#nullable enable annotations
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// 장면 전환이 풀 굽기를 부르므로 풀 코드 전체를 게임 어셈블리에 둔다.
namespace Game.App
{
    // 굽기 펌프 한 번이 시작한 풀 덩어리 묶음이다. 덩어리마다 디스패치하는 값과 순서는 원본 TgsInstance와 같다.
    // 원작 차이: 원본은 덩어리마다 메타데이터와 풀잎 수를 동기로 읽는다. 묶음은 두 값을 덩어리 칸에 모아 각각 한 번씩 비동기로 읽는다.
    // 원작 차이: 원본은 덩어리마다 배치 삼각형과 노이즈 버퍼를 계속 들고 있다. 묶음은 배치 삼각형 버퍼를 묶음끼리 돌려 쓰고,
    // 같은 설정의 노이즈 버퍼를 묶음 안에서 함께 쓰며, BakePass를 넣은 뒤 놓는다.
    internal sealed class OriginalGrassBakeBatch
    {
        enum Stage
        {
            ReadingMetaData,
            ReadingBladeCount,
            Done
        }

        // 결과를 기다리는 묶음이다. 렌더러가 마지막 덩어리를 빼면 남은 묶음의 버퍼를 놓고 늦게 온 결과를 버린다.
        static readonly List<OriginalGrassBakeBatch> _active = new List<OriginalGrassBakeBatch>();
        // BakePass까지 넣고 돌려받은 배치 삼각형 버퍼다. GPU는 넣은 순서대로 실행하므로, 다음 묶음이 바로 다시 써도 앞 묶음의 BakePass가 먼저 읽는다.
        static readonly List<ComputeBuffer> _placementPool = new List<ComputeBuffer>();
        // 메타데이터 버퍼의 시작값이다. 묶음의 칸 수만큼 앞쪽을 올린다.
        static OriginalGrassChunk.InstanceMetaDataGpu[] _emptyMetaData = Array.Empty<OriginalGrassChunk.InstanceMetaDataGpu>();

        readonly List<OriginalGrassChunk> _chunks;
        // 덩어리 칸 번호 순서의 배치 삼각형 버퍼다.
        readonly List<ComputeBuffer> _placementTriangles;
        // 같은 번호끼리 짝인 노이즈 설정과 버퍼다. 한 지형 층의 덩어리는 설정이 같아 버퍼 하나를 함께 쓴다.
        readonly List<OriginalGrassPreset.Settings> _noiseSettings = new List<OriginalGrassPreset.Settings>();
        readonly List<ComputeBuffer> _noiseLayers = new List<ComputeBuffer>();
        readonly Action<AsyncGPUReadbackRequest> _onMetaDataRead;
        readonly Action<AsyncGPUReadbackRequest> _onBladeCountRead;
        ComputeBuffer? _metaData;
        ComputeBuffer? _bladeCounts;
        Stage _stage;

        // capacity는 이 묶음에 들어올 수 있는 덩어리 수의 상한이다. 렌더러가 남은 굽기 한도로 정한다.
        internal OriginalGrassBakeBatch(int capacity)
        {
            _chunks = new List<OriginalGrassChunk>(capacity);
            _placementTriangles = new List<ComputeBuffer>(capacity);
            _onMetaDataRead = OnMetaDataRead;
            _onBladeCountRead = OnBladeCountRead;
            _metaData = new ComputeBuffer(capacity, OriginalGrassChunk._metaDataStride);
            _metaData.SetData(EmptyMetaData(capacity), 0, 0, capacity);
            _active.Add(this);
        }

        // 덩어리에 다음 칸을 주고 TerrainPass를 디스패치하게 한다.
        internal void Add(OriginalGrassChunk chunk)
        {
            int index = _chunks.Count;
            _chunks.Add(chunk);
            ComputeBuffer placementTriangles = RentPlacementTriangles(chunk.PlacementTriangleCapacity);
            _placementTriangles.Add(placementTriangles);
            chunk.DispatchTerrainPass(this, index, _metaData, placementTriangles, GetNoiseLayers(chunk.BakeSettings));
        }

        // 모든 덩어리의 TerrainPass를 넣은 뒤 부른다. 쓴 칸만큼 메타데이터를 한 번에 읽는다.
        internal void Submit()
        {
            _stage = Stage.ReadingMetaData;
            AsyncGPUReadback.Request(_metaData, _chunks.Count * OriginalGrassChunk._metaDataStride, 0, _onMetaDataRead);
        }

        // 렌더러의 마지막 덩어리가 빠지면 부른다. 덩어리는 이미 모두 놓였으므로 남은 묶음의 버퍼를 놓고 늦게 올 결과를 버린다.
        internal static void AbandonAll()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                OriginalGrassBakeBatch batch = _active[i];
                batch.ReleaseBakeInputs(false);
                ReleaseBuffer(ref batch._bladeCounts);
                batch._stage = Stage.Done;
            }
            _active.Clear();
            ReleasePool();
        }

        // 굽기가 모두 끝나면 렌더러가 부른다. 다음 레벨은 덩어리 크기가 다를 수 있으므로 빌려줄 버퍼를 남기지 않는다.
        internal static void ReleasePool()
        {
            for (int i = 0; i < _placementPool.Count; i++)
                _placementPool[i].Release();
            _placementPool.Clear();
        }

        // 메타데이터가 오면 칸 순서대로 덩어리마다 BakePass를 넣고, 풀잎 수를 한 번에 읽는다.
        // 묶음이 이미 끝났으면 늦게 온 결과라 버린다. 읽기 오류는 덩어리를 다시 굽게 한다.
        void OnMetaDataRead(AsyncGPUReadbackRequest request)
        {
            if (_stage != Stage.ReadingMetaData)
                return;
            if (request.hasError)
            {
                Fail();
                return;
            }
            NativeArray<OriginalGrassChunk.InstanceMetaDataGpu> metaData = request.GetData<OriginalGrassChunk.InstanceMetaDataGpu>();
            _bladeCounts = new ComputeBuffer(_chunks.Count, sizeof(int), ComputeBufferType.Raw);
            bool readBladeCounts = false;
            for (int i = 0; i < _chunks.Count; i++)
            {
                if (_chunks[i].OnMetaDataRead(this, metaData[i], _bladeCounts))
                    readBladeCounts = true;
            }
            ReleaseBakeInputs(true);
            if (!readBladeCounts)
            {
                Finish();
                return;
            }
            _stage = Stage.ReadingBladeCount;
            AsyncGPUReadback.Request(_bladeCounts, _onBladeCountRead);
        }

        void OnBladeCountRead(AsyncGPUReadbackRequest request)
        {
            if (_stage != Stage.ReadingBladeCount)
                return;
            if (request.hasError)
            {
                Fail();
                return;
            }
            NativeArray<int> bladeCounts = request.GetData<int>();
            for (int i = 0; i < _chunks.Count; i++)
                _chunks[i].OnBladeCountRead(this, bladeCounts[i]);
            Finish();
        }

        void Fail()
        {
            for (int i = 0; i < _chunks.Count; i++)
                _chunks[i].OnReadFailed(this);
            Finish();
        }

        void Finish()
        {
            ReleaseBakeInputs(true);
            ReleaseBuffer(ref _bladeCounts);
            _stage = Stage.Done;
            _active.Remove(this);
        }

        // BakePass가 읽는 버퍼를 놓는다. 배치 삼각형 버퍼는 굽기가 이어지면 돌려주고, 렌더러가 비면 놓는다.
        void ReleaseBakeInputs(bool returnToPool)
        {
            for (int i = 0; i < _placementTriangles.Count; i++)
            {
                if (returnToPool)
                    _placementPool.Add(_placementTriangles[i]);
                else
                    _placementTriangles[i].Release();
            }
            _placementTriangles.Clear();
            for (int i = 0; i < _noiseLayers.Count; i++)
                _noiseLayers[i].Release();
            _noiseLayers.Clear();
            _noiseSettings.Clear();
            ReleaseBuffer(ref _metaData);
        }

        // 원본처럼 배치 삼각형 수와 크기가 같은 버퍼만 다시 쓰고, 쓰기 전에 Append 계수기를 0으로 돌린다.
        static ComputeBuffer RentPlacementTriangles(int capacity)
        {
            for (int i = _placementPool.Count - 1; i >= 0; i--)
            {
                ComputeBuffer pooled = _placementPool[i];
                if (pooled.count != capacity)
                    continue;
                _placementPool.RemoveAt(i);
                pooled.SetCounterValue(0u);
                return pooled;
            }
            var created = new ComputeBuffer(capacity, OriginalGrassChunk._sizeofPlacementTriangle, ComputeBufferType.Append);
            created.SetCounterValue(0u);
            return created;
        }

        // 원본 ApplyLayerSettingsToBuffer 값은 프리셋과 굽기 설정으로만 정해지므로, 둘이 같은 덩어리는 같은 버퍼를 쓴다.
        ComputeBuffer GetNoiseLayers(OriginalGrassPreset.Settings settings)
        {
            for (int i = 0; i < _noiseSettings.Count; i++)
            {
                if (SameSettings(_noiseSettings[i], settings))
                    return _noiseLayers[i];
            }
            var noiseLayers = new ComputeBuffer(OriginalGrassPreset._noiseLayerCount, OriginalGrassPreset._noiseSettingGpuStride);
            settings._preset!.ApplyLayerSettingsToBuffer(noiseLayers, settings);
            _noiseSettings.Add(settings);
            _noiseLayers.Add(noiseLayers);
            return noiseLayers;
        }

        // 설정 필드를 모두 정확히 비교한다. 프리셋은 참조로 비교해 장면 전환 중 네이티브 객체 확인을 피한다.
        static bool SameSettings(OriginalGrassPreset.Settings a, OriginalGrassPreset.Settings b)
        {
            return ReferenceEquals(a._preset, b._preset) && a._amount.Equals(b._amount) && a._height.Equals(b._height)
                && a._thickness.Equals(b._thickness) && a._angle.Equals(b._angle) && a._tiling.Equals(b._tiling)
                && a._slopeBias.Equals(b._slopeBias) && a._tint.Equals(b._tint) && a._hueShift.Equals(b._hueShift)
                && a._saturation.Equals(b._saturation) && a._camouflage.Equals(b._camouflage);
        }

        // 묶음 크기는 프레임당 굽기 한도를 넘지 않으므로 시작값 배열은 처음 한 번만 만든다.
        static OriginalGrassChunk.InstanceMetaDataGpu[] EmptyMetaData(int count)
        {
            if (_emptyMetaData.Length >= count)
                return _emptyMetaData;
            _emptyMetaData = new OriginalGrassChunk.InstanceMetaDataGpu[count];
            for (int i = 0; i < count; i++)
                _emptyMetaData[i] = OriginalGrassChunk.InstanceMetaDataGpu.Empty;
            return _emptyMetaData;
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

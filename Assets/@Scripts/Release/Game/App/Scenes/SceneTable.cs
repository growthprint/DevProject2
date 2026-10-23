#nullable enable annotations
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Game.App
{
    // 논리 씬마다 Addressables 씬 참조를 두는 표로, GameplayDataSettings의 _sceneManagement에 내장된다. GameScene 값이 배열 색인이다.
    // 제작 도구가 ComponentAuthoring.ConfigureScene으로 _scenes를 채우고, Runtime은 읽기만 한다.
    [Serializable]
    public sealed class SceneTable
    {
        // 논리 씬 목록의 정본인 GameScenes.All이다. 열거형 값 순서와 같다.
        static readonly IReadOnlyList<GameScene> _all = GameScenes.All;

        // 원작 로딩 커튼 씬의 수다. SceneCurtain 값이 0부터 이 수 미만까지다.
        internal const int _curtainCount = 5;

        // GameScene 값 순서대로 둔 씬 참조다. GUID로 가리키는 약한 참조다.
        [SerializeField] AssetReference[] _scenes = new AssetReference[_all.Count];
        // SceneCurtain 값 순서대로 둔 로딩 커튼 씬 참조다. 원작 GameplayDataSettings.SceneManagement의 실패와 철수 화면,
        // SceneLoader.defaultCurtainScene, 호출 지점의 overrideCurtainScene을 한곳에 둔다.
        [SerializeField] AssetReference[] _curtains = new AssetReference[_curtainCount];

        // 시작 때 TryValidate를 통과한 표에서만 쓴다.
        public AssetReference this[GameScene scene] => _scenes[(int)scene];

        // 시작 때 TryValidate를 통과한 표에서만 쓴다.
        public AssetReference Curtain(SceneCurtain curtain) => _curtains[(int)curtain];

        // 모든 논리 씬과 커튼에 유효한 참조가 있는지 확인한다. 첫 누락을 여기서 한 번 보고하고 false를 돌려주므로 호출자는 다시 기록하지 않는다.
        public bool TryValidate(UnityEngine.Object? context = null)
        {
            // 항목 수가 다르면 색인이 어긋나므로 먼저 거절한다. 누락 문구에는 열거형 이름과 저장에 쓰는 문자열 ID를 함께 남긴다.
            if (_scenes == null || _scenes.Length != _all.Count)
                return Log.Instance.Fail(new InvalidOperationException("씬 표의 항목 수가 논리 씬 수와 다릅니다."), context);
            foreach (GameScene scene in _all)
            {
                AssetReference reference = _scenes[(int)scene];
                if (reference == null || !reference.RuntimeKeyIsValid())
                    return Log.Instance.Fail(new InvalidOperationException(
                        "씬 표에 씬이 없습니다: " + scene + " (" + GameScenes.Id(scene) + ")"), context);
            }
            if (_curtains == null || _curtains.Length != _curtainCount)
                return Log.Instance.Fail(new InvalidOperationException("씬 표의 커튼 수가 원작 로딩 커튼 수와 다릅니다."), context);
            for (int i = 0; i < _curtainCount; i++)
            {
                AssetReference reference = _curtains[i];
                if (reference == null || !reference.RuntimeKeyIsValid())
                    return Log.Instance.Fail(new InvalidOperationException("씬 표에 로딩 커튼이 없습니다: " + (SceneCurtain)i), context);
            }
            return true;
        }
    }
}

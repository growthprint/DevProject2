#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    [Serializable]
    public sealed class SceneMapRegion
    {
        [SerializeField] string _id = "";
        [SerializeField] Texture2D _texture = null!;
        [SerializeField] Vector3 _worldCenter;
        [SerializeField] float _worldSize;
        [SerializeField] Vector2 _offset, _imageCenter;
        public string Id => _id;
        public Texture2D Texture => _texture;
        public float WorldSize => _worldSize;
        public Vector2 ImageCenter => _imageCenter;
        public bool HasMap => _texture != null && _worldSize > 0;
        public Vector2 WorldToMap(Vector3 world) => new Vector2(world.x - _worldCenter.x, world.z - _worldCenter.z) + _offset;
        public Vector3 MapToWorld(Vector2 map, float height) => new Vector3(map.x - _offset.x + _worldCenter.x, height, map.y - _offset.y + _worldCenter.z);
        public bool ContainsMapPoint(Vector2 point) => Mathf.Abs(point.x - _imageCenter.x) <= _worldSize * .5f && Mathf.Abs(point.y - _imageCenter.y) <= _worldSize * .5f;
    }

    // 지도 구역, 좌표 변환, 지도 블러를 소유한다. 캠페인 월드와 프롤로그 컨트롤러가 플레이어 모터를 붙인 직후 Bind하고
    // 자기 Release에서 Release한다. 그래서 컴포넌트 준비 순서에 기대지 않는다.
    [DisallowMultipleComponent]
    public sealed class SceneMapContext : MonoBehaviour
    {
        [SerializeField] string _sceneId = "";
        [SerializeField] bool _showHud;
        [SerializeField] float _rotation;
        [SerializeField] SceneMapRegion[] _regions = Array.Empty<SceneMapRegion>();
        Transform? _player;
        OriginalPlayerMotor? _motor;
        MapBlurCameraState? _blur;
        // 활성 하위 씬의 구역 번호다. 하위 씬이 하나뿐인 프롤로그는 알림 없이 첫 구역을 쓴다.
        int _regionIndex;
        // Bind가 직렬화 문자열 _sceneId를 한 번 변환한 논리 씬이다. 지도 화면은 Bind 뒤에만 읽는다.
        GameScene _scene;
        // 직렬화 필드는 문자열이지만, 밖에는 논리 씬 열거형으로 내보낸다.
        public GameScene SceneId => _scene;
        public bool ShowHud => _showHud;
        public float Rotation => _rotation;
        public Transform? Player => _player;
        // 앱 수명 지도 화면이 매 프레임 묻는 값이라 파괴 확인은 MonoBehaviour인 모터로만 한다. 파괴된 Transform의 Unity null 비교는
        // 엔진에 존재를 묻고, 씬을 읽는 동안 그 질문이 로딩 잠금을 기다린다. _player는 OriginalPlayerRig가 넘긴 모터 자신의 Transform이라 함께 파괴된다.
        public bool CanOpen => isActiveAndEnabled && _motor != null && !_motor.InputSuppressed;
        public Vector3 AimDirection => _motor != null ? _motor.AimDirection : Vector3.forward;
        public SceneMapRegion Region => _regions[_regionIndex];

        // 레벨 초기화마다 한 번 만드는 플레이어 리그가 방금 붙인 모터와 그 Transform, 월드를 그리는 카메라를 연결한다.
        // 블러 카메라는 리그가 모터와 게임 카메라에 넘기는 것과 같다. 씬의 꺼진 원본 Main Camera에 블러를 걸면 지도 뒤가 흐려지지 않는다.
        // 지도 구역은 씬에 저작한 연결이고, 지도 블러는 꺼 둔 채 준비한다.
        internal void Bind(Transform player, OriginalPlayerMotor motor, Camera camera)
        {
            // 씬 ID는 제작 도구가 쓰는 논리 씬 이름이라 변환 결과만 쓴다.
            GameScenes.TryParse(_sceneId, out _scene);
            _player = player;
            _motor = motor;
            _blur = GameplayComponents.GetOrAdd<MapBlurCameraState>(camera.gameObject);
            _blur.enabled = false;
        }

        // 블러를 끄고 Bind가 연결한 참조를 모두 끊는다. 여러 번 불러도 안전하고, 이미 파괴된 블러는 건너뛴다.
        internal void Release()
        {
            SetMapVisible(false);
            _player = null;
            _motor = null;
            _blur = null;
        }

        // 원본 MiniMapDisplay가 MultiSceneCore.ActiveSubSceneID로 지도 항목을 고르듯, 컨트롤러가 하위 씬을 켤 때 넘긴 ID로 구역을 고른다.
        // 활성 하위 씬의 출처를 컨트롤러 하나로 두어 매 프레임 지형 판정을 하지 않는다. 하위 씬마다 맞는 구역이 저작돼 있다.
        internal void SetActiveSubscene(string subsceneId)
        {
            for (int region = 0; region < _regions.Length; region++)
                if (_regions[region].Id == subsceneId) { _regionIndex = region; return; }
        }

        public void SetMapVisible(bool visible) { if (_blur != null) _blur.enabled = visible; }
        void OnDisable() => SetMapVisible(false);

        // 지도 이미지, 좌표, 마커 클릭이 같은 회전 공식을 쓰도록 공용으로 둔다. 역회전은 각도에 음수를 넣는다.
        public static Vector2 Rotate(Vector2 value, float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            return new Vector2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
        }
    }
}

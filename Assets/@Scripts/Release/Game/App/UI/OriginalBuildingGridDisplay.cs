#nullable enable annotations
using UnityEngine;

namespace Game.App
{
    // 원작 Duckov.Buildings.UI.GridDisplay다. 건설 격자 셰이더(DevProject/Duckov/BuildingGrid)가 읽는 전역 값을 쓴다.
    // 영역 칸 범위, 놓을 건물 칸, 놓을 수 있는지, 격자 표시 정도를 넣고, 표시 정도는 원작처럼 곡선을 따라 시간 배율과 상관없이 바꾼다.
    // 꺼지면 격자와 놓을 칸을 바로 숨겨, 창이 닫힌 뒤 기지 바닥에 격자가 남지 않게 한다.
    public sealed class OriginalBuildingGridDisplay : MonoBehaviour
    {
        static readonly int _areaPosAndSize = Shader.PropertyToID("BuildingGrid_AreaPosAndSize");
        static readonly int _buildingPosAndSize = Shader.PropertyToID("BuildingGrid_BuildingPosAndSize");
        static readonly int _canBuild = Shader.PropertyToID("BuildingGrid_CanBuild");
        static readonly int _building = Shader.PropertyToID("BuildingGrid_Building");

        // 원작 animationDuration, showCurve, hideCurve다. 기지 건설 창은 0.5초다.
        [SerializeField] float _animationDuration = .5f;
        [SerializeField] AnimationCurve _showCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] AnimationCurve _hideCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // 원작 SetGridShowHide의 진행 상태다. 새 전환이 시작되면 앞 전환은 그 자리에서 멈춘다.
        bool _animating, _showing;
        float _time;

        // 원작 Setup이다. lowerLeft와 cellSpan은 영역 왼쪽 아래 칸과 그 칸에서 끝 칸까지의 칸 차이다.
        internal void Show(Vector2Int lowerLeft, Vector2Int cellSpan)
        {
            Shader.SetGlobalVector(_areaPosAndSize, new Vector4(lowerLeft.x, lowerLeft.y, cellSpan.x, cellSpan.y));
            HidePreview();
            Animate(true);
        }

        // 원작 Close다.
        internal void Close()
        {
            HidePreview();
            Animate(false);
        }

        // 원작 SetBuildingPreviewCoord다. size는 회전을 반영한 칸 수다.
        internal void SetPreview(Vector2Int coord, Vector2Int size, bool validPlacement)
        {
            Shader.SetGlobalVector(_buildingPosAndSize, new Vector4(coord.x, coord.y, size.x, size.y));
            Shader.SetGlobalFloat(_canBuild, validPlacement ? 1f : 0f);
        }

        internal void HidePreview() => Shader.SetGlobalVector(_buildingPosAndSize, Vector4.zero);

        // 원작처럼 전환마다 0에서 1, 또는 1에서 0으로 처음부터 섞는다. 시간이 0 이하이거나 꺼져 있으면 바로 끝 값을 쓴다.
        void Animate(bool show)
        {
            _showing = show;
            _time = 0f;
            _animating = _animationDuration > 0f && isActiveAndEnabled;
            if (!_animating) Shader.SetGlobalFloat(_building, show ? 1f : 0f);
        }

        void Update()
        {
            if (!_animating) return;
            _time += Time.unscaledDeltaTime;
            if (_time >= _animationDuration)
            {
                _animating = false;
                Shader.SetGlobalFloat(_building, _showing ? 1f : 0f);
                return;
            }
            AnimationCurve curve = _showing ? _showCurve : _hideCurve;
            Shader.SetGlobalFloat(_building, Mathf.Lerp(_showing ? 0f : 1f, _showing ? 1f : 0f, curve.Evaluate(_time / _animationDuration)));
        }

        void OnDisable()
        {
            _animating = false;
            HidePreview();
            Shader.SetGlobalFloat(_building, 0f);
        }
    }
}

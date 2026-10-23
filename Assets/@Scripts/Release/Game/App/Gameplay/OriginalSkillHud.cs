#nullable enable annotations
using System;
using UnityEngine;

namespace Game.App
{
    // 원작 LevelManager의 SkillInWorldHUD(SkillHud3D, SkillRangeHUD, SkillProjectileLineHUD)다. 조준 중인 스킬의 바닥 범위 원과
    // 수류탄 궤적을 그린다. 원작 ShapesSkillLine의 점과 선은 제작 도구가 저작한 LineRenderer 두 개와 점 렌더러가 대신 그린다.
    internal sealed class OriginalSkillHud : MonoBehaviour
    {
        // 원작 SkillRangeHUD의 위치 대상과 rangeTarget, rangeRenderer다.
        [SerializeField] Transform _rangeTarget = null!;
        [SerializeField] Transform _range = null!;
        [SerializeField] Renderer _rangeRenderer = null!;
        // 원작 projectileLine 객체다. 점은 fragmentCount + 1개이고, 장애물에 닿은 점 하나를 따로 둔다.
        [SerializeField] GameObject _line = null!;
        [SerializeField] LineRenderer _clearSegments = null!;
        [SerializeField] LineRenderer _blockedSegments = null!;
        [SerializeField] Renderer[] _dots = Array.Empty<Renderer>();
        [SerializeField] Renderer _hitDot = null!;
        // 원작 lineColor와 obsticleColor를 색으로 가진 재질이다.
        [SerializeField] Material _clearMaterial = null!;
        [SerializeField] Material _blockedMaterial = null!;
        static readonly int _progressId = Shader.PropertyToID("_Progress");
        // 원작 SetProgress는 재질 사본을 만들지만, 여기서는 공유 재질을 두고 렌더러 값만 바꾼다.
        MaterialPropertyBlock? _progress;
        readonly RaycastHit[] _hits = new RaycastHit[3];
        Vector3[] _points = Array.Empty<Vector3>();

        // 원작 SkillHud3D가 스킬 조준을 시작할 때처럼 범위를 1 이상으로 맞추고, 수류탄이면 궤적도 켠다.
        internal void Show(float effectRange, bool grenade)
        {
            _range.localScale = Vector3.one * (effectRange > 1f ? effectRange : 1f);
            _line.SetActive(grenade);
        }

        // 원작 SkillHud3D.LateUpdate다. 범위 원을 조준점에 두고 준비 진행률을 넣은 뒤, 궤적이 켜져 있으면 다시 그린다.
        internal void Tick(Vector3 start, Vector3 aimPoint, float verticalSpeed, float progress)
        {
            _rangeTarget.position = aimPoint;
            _progress ??= new MaterialPropertyBlock();
            _rangeRenderer.GetPropertyBlock(_progress);
            _progress.SetFloat(_progressId, progress);
            _rangeRenderer.SetPropertyBlock(_progress);
            if (_line.activeSelf) UpdateLine(start, aimPoint, verticalSpeed);
        }

        // 원작 SkillProjectileLineHUD.UpdateLine이다. Skill_Grenade와 같은 포물선을 fragmentCount 조각으로 나누고,
        // 처음과 끝 점을 뺀 구간을 반지름 0.2 구로 검사해 장애물에 닿은 점부터 장애물 색으로 칠한다.
        void UpdateLine(Vector3 start, Vector3 target, float verticalSpeed)
        {
            int fragments = _dots.Length - 1;
            if (_points.Length != _dots.Length) _points = new Vector3[_dots.Length];
            float gravity = Physics.gravity.magnitude;
            float rise = verticalSpeed / gravity;
            float total = rise + Mathf.Sqrt(2f * (rise * verticalSpeed * .5f + start.y - target.y) / gravity);
            Vector3 from = new(start.x, 0f, start.z), to = new(target.x, 0f, target.z);
            Vector3 direction = to - from;
            float speed = 0f;
            if (direction.magnitude > 0f) { speed = Vector3.Distance(from, to) / total; direction = direction.normalized; }
            else direction = Vector3.zero;
            float step = total / fragments;
            // 원작처럼 장애물이 없으면 모든 점이 첫 구간 색이고, 첫 장애물 점부터 끝까지 장애물 색이다.
            int blockedFrom = _points.Length;
            Vector3 hitPoint = Vector3.zero;
            for (int i = 0; i < _points.Length; i++)
            {
                float time = step * i;
                _points[i] = start + Vector3.up * (verticalSpeed - gravity * time * .5f) * time + direction * speed * time;
                if (i > 0 && i < _points.Length - 1 && blockedFrom == _points.Length && Blocked(_points[i - 1], _points[i], out hitPoint))
                    blockedFrom = i;
                bool blocked = i >= blockedFrom;
                _dots[i].transform.position = _points[i];
                _dots[i].sharedMaterial = blocked ? _blockedMaterial : _clearMaterial;
            }
            // 원작은 구간마다 시작 점의 색으로 선을 그리므로, 장애물 점 앞까지는 첫 색 선, 그 점부터는 장애물 색 선이다.
            int clearCount = Mathf.Min(blockedFrom + 1, _points.Length);
            _clearSegments.positionCount = clearCount;
            for (int i = 0; i < clearCount; i++) _clearSegments.SetPosition(i, _points[i]);
            int blockedCount = blockedFrom < _points.Length ? _points.Length - blockedFrom : 0;
            _blockedSegments.positionCount = blockedCount;
            for (int i = 0; i < blockedCount; i++) _blockedSegments.SetPosition(i, _points[blockedFrom + i]);
            // 원작처럼 장애물에 닿았으면 닿은 지점에 첫 점 색의 점을 하나 더 그린다.
            bool hit = blockedFrom < _points.Length;
            _hitDot.gameObject.SetActive(hit);
            if (hit) _hitDot.transform.position = hitPoint;
        }

        // 원작 CheckObsticle이다. 닿은 지점은 원작처럼 구간 시작에서 구간 방향으로 닿은 거리만큼 간 점이다.
        bool Blocked(Vector3 from, Vector3 to, out Vector3 hitPoint)
        {
            GameplayDataSettings.LayersData layers = GameplayDataSettings.Layers;
            int mask = (int)layers.WallLayerMask | (int)layers.GroundLayerMask | (int)layers.FowBlockLayers;
            Vector3 delta = to - from;
            hitPoint = Vector3.zero;
            if (Physics.SphereCastNonAlloc(from, .2f, delta.normalized, _hits, delta.magnitude, mask) <= 0) return false;
            hitPoint = from + delta.normalized * (_hits[0].point - from).magnitude;
            return true;
        }
    }
}

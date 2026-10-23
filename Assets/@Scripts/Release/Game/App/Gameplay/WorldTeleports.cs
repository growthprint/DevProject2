#nullable enable annotations
using System;
using Cysharp.Threading.Tasks;
using Game.Core;
using UnityEngine;

namespace Game.App
{
    // 전투 월드의 순간이동이다. 원작 SimpleTeleporter의 효과, 대기, 이동 순서, 원작 MultiSceneCore.LoadAndTeleport의 검은 화면 하위 씬 전환,
    // 원작 LevelManager.InitLevel의 출발점 배치를 맡는다. 상호작용 객체가 만들어 순간이동 대상과 레벨 시작 배치를 넘기고, 정리 때 기다리던 순서를 멈춘다.
    internal sealed class WorldTeleports
    {
        // 순간이동 직후 이 시간 동안은 순간이동 대상을 다시 고르지 않는다. 도착 지점의 대상으로 바로 되돌아가지 않게 한다.
        const float _teleportCooldown = 1f;
        // 원작 SimpleTeleporter.VolumeFx가 쓰는 효과 세기 전역 셰이더 값이다. 텔레포터 받침 셰이더(DuckovSodaTeleporter 포트)가 읽는다.
        static readonly int _teleportFxShaderId = Shader.PropertyToID("TeleportFXStrength");

        readonly GameplayWorldContext _world;
        readonly Action<int> _switchSubscene;
        float _teleportUntil;
        // 효과를 보이며 이동을 기다리는 순간이동의 입력 억제 토큰이다. 기다리는 동안에만 있다. 세대는 정리가 기다리던 순서를 멈출 때 올린다.
        IDisposable? _teleportSuppression;
        int _teleportGeneration;
        // 원작 MultiSceneCore.isLoading이다. 하위 씬 문이 검은 화면을 덮고 하위 씬을 바꾼 뒤 플레이어를 옮길 때까지 참이다.
        bool _loading;
        // 이 순서가 덮기를 요청하고 아직 걷기를 요청하지 않은 검은 화면이다. 덮기 요청 수가 남지 않게 걷기를 정확히 한 번 부르는 표시다.
        OriginalBlackScreen? _coveredScreen;

        internal WorldTeleports(GameplayWorldContext world, Action<int> switchSubscene)
        {
            _world = world;
            _switchSubscene = switchSubscene;
        }

        // 효과를 보이거나 검은 화면으로 하위 씬을 바꾸며 순간이동을 기다리는 중인지다. 그동안 F 탐색은 대상을 고르지 않는다.
        internal bool Waiting => _teleportSuppression != null || _loading;
        // 하위 씬 문 순서가 진행 중이거나 순간이동한 지 1초가 지나지 않았는지다. 원작은 시작과 끝에 각각 1초 재사용 금지를 건다.
        internal bool RecentlyTeleported => _loading || Time.time <= _teleportUntil;

        // 원작 SimpleTeleporter.Teleport다. 효과 시간이나 지연이 있으면 효과를 켜고 기다린 뒤 옮기고 효과를 끈다.
        // 원작 MultiSceneTeleporter인 동굴·기지 출입은 둘 다 0이고 목적지 하위 씬이 있어 원작 LoadAndTeleport처럼 검은 화면 아래에서 옮긴다.
        internal void Begin(MultiSceneTeleporter teleporter)
        {
            if (teleporter.EffectTime + teleporter.Delay <= 0f)
            {
                if (teleporter.DestinationSubscene >= 0) BeginLoadAndTeleport(teleporter);
                else Teleport(teleporter);
                return;
            }
            // 기다리는 동안 이동, 조준, 대시를 막는 토큰을 쥔다(원작 차이: 원작은 막지 않는다). 기다리는 동안에는 F 탐색이 대상을 고르지 않아
            // 두 번째 순간이동이 시작되지 않으므로 토큰은 늘 비어 있다. 아무도 기다리지 않는 순서라 예외가 나면 상태를 먼저 되돌린 뒤 한 번 기록한다.
            int generation = ++_teleportGeneration;
            _teleportSuppression = _world.Motor.SuppressInput();
            TeleportAfterEffectAsync(teleporter, generation).Forget(error =>
            {
                if (generation == _teleportGeneration) EndTeleportWait();
                Shader.SetGlobalFloat(_teleportFxShaderId, 0f);
                Log.Instance.Exception(error, _world.LogContext);
            });
        }

        // 씬 해제 정리가 부른다. 효과를 보이며 기다리던 순간이동을 멈추고 억제를 반납하며 효과를 0으로 되돌린다.
        // 하위 씬 문 순서는 멈추고 덮은 검은 화면을 한 번 걷어, 뒤이은 씬 전환의 덮기 요청과 짝이 맞게 한다. 기다리는 순서가 없으면 아무것도 하지 않는다.
        internal void Stop()
        {
            if (_teleportSuppression == null && !_loading) return;
            _teleportGeneration++;
            EndLoad();
            EndTeleportWait();
            Shader.SetGlobalFloat(_teleportFxShaderId, 0f);
        }

        // 원작 MultiSceneCore.LoadSubScene의 거부 조건이다. 씬을 읽는 중이거나 원작 로딩 화면 전환이 진행 중이거나 이미 하위 씬을 바꾸는
        // 중이면 시작하지 않는다(F 입력이 닿는 거절이라 기록하지 않는다). 아무도 기다리지 않는 순서라 예외가 나면 상태를 먼저 되돌린 뒤 한 번 기록한다.
        void BeginLoadAndTeleport(MultiSceneTeleporter teleporter)
        {
            if (_loading || SceneFlow.IsSceneLoading) return;
            SceneFlow scenes = GameManager.Scenes;
            if (scenes.IsShowingLoadingScreen) return;
            int generation = ++_teleportGeneration;
            _loading = true;
            LoadAndTeleportAsync(teleporter, scenes.BlackScreen, generation).Forget(error =>
            {
                if (generation == _teleportGeneration) EndLoad();
                Log.Instance.Exception(error, _world.LogContext);
            });
        }

        // 원작 LoadSubScene과 LoadAndTeleport 순서다. 기본 곡선으로 0.5초 덮은 뒤 하위 씬을 바꾸고, 한 프레임 뒤 걷기를 시작만 하고
        // 플레이어를 옮긴다. 원작처럼 입력은 막지 않는다. 정리가 세대를 올렸거나 공용 HUD가 떨어졌으면(씬 정리 중) 멈춘다.
        async UniTask LoadAndTeleportAsync(MultiSceneTeleporter teleporter, OriginalBlackScreen screen, int generation)
        {
            _coveredScreen = screen;
            screen.ShowDefault();
            while (screen.IsFading)
            {
                await UniTask.Yield();
                if (!IsCurrentLoad(generation)) return;
            }
            _switchSubscene(teleporter.DestinationSubscene);
            await UniTask.NextFrame();
            if (!IsCurrentLoad(generation)) return;
            EndLoad();
            MovePlayer(teleporter.Destination, -1);
            _teleportUntil = Time.time + _teleportCooldown;
        }

        // 하위 씬 문 순서가 아직 이 월드의 것인지다. 아니면 정리가 이미 끝냈거나, HUD가 떨어진 경우 여기서 검은 화면을 걷고 끝낸다.
        // 모터는 Unity 객체라 파괴 검사를 거치는 비교로 본다.
        bool IsCurrentLoad(int generation)
        {
            if (generation != _teleportGeneration) return false;
            if (_world.PresentationAttached && _world.Motor != null) return true;
            EndLoad();
            return false;
        }

        // 하위 씬 문 순서를 끝내고, 덮기를 요청했으면 걷기를 한 번만 요청한다. 여러 번 불러도 안전하다. 앱이 먼저 파괴한 검은 화면은
        // 요청 수도 함께 사라졌으므로 Unity null 비교로 건너뛴다.
        void EndLoad()
        {
            _loading = false;
            OriginalBlackScreen? screen = _coveredScreen;
            _coveredScreen = null;
            if (screen != null) screen.HideDefault();
        }

        // 원작 Teleport의 순서다. VolumeFx(true)처럼 게임 시간으로 세기를 올리는 동안, 이동은 원작 WaitForSeconds(ignoreTimeScale)처럼
        // 실제 시간으로 효과 시간 + 지연 뒤에 한다. 옮긴 뒤 VolumeFx(false)처럼 같은 시간에 걸쳐 내린다. 정리가 세대를 올렸거나 공용 HUD가
        // 떨어졌으면(씬 정리 중) 효과를 0으로 되돌리고 멈춘다. 원작 Volume 가중치는 제품에 순간이동 Volume이 없어 옮기지 않는다.
        async UniTask TeleportAfterEffectAsync(MultiSceneTeleporter teleporter, int generation)
        {
            float effectTime = teleporter.EffectTime, shown = Time.time, moveAt = Time.unscaledTime + effectTime + teleporter.Delay;
            while (Time.unscaledTime < moveAt)
            {
                Shader.SetGlobalFloat(_teleportFxShaderId, FxStrength(Time.time - shown, effectTime, true));
                await UniTask.Yield();
                if (!IsCurrentTeleport(generation)) return;
            }
            EndTeleportWait();
            Teleport(teleporter);
            float hidden = Time.time;
            while (true)
            {
                float elapsed = Time.time - hidden;
                Shader.SetGlobalFloat(_teleportFxShaderId, FxStrength(elapsed, effectTime, false));
                if (elapsed > effectTime) return;
                await UniTask.Yield();
                if (!IsCurrentTeleport(generation)) return;
            }
        }

        // 원작 VolumeFx의 세기다. 켤 때는 경과 / 효과 시간을 0~1로 자르고, 끌 때는 1에서 뺀다. 효과 시간이 0이면 바로 끝값이다.
        internal static float FxStrength(float elapsed, float effectTime, bool show)
        {
            float value = effectTime > 0f ? Mathf.Clamp01(elapsed / effectTime) : 1f;
            return show ? value : 1f - value;
        }

        // 기다리는 순서가 아직 이 월드의 것인지다. 아니면 효과를 0으로 되돌린다. 억제 토큰은 정리가 이미 반납했거나,
        // HUD가 떨어진 경우 여기서 반납한다. 모터는 Unity 객체라 파괴 검사를 거치는 비교로 본다.
        bool IsCurrentTeleport(int generation)
        {
            if (generation == _teleportGeneration && _world.PresentationAttached && _world.Motor != null) return true;
            if (generation == _teleportGeneration) EndTeleportWait();
            Shader.SetGlobalFloat(_teleportFxShaderId, 0f);
            return false;
        }

        // 기다리는 순간이동의 억제 토큰을 한 번만 반납한다. 여러 번 불러도 안전하다.
        void EndTeleportWait()
        {
            _teleportSuppression?.Dispose();
            _teleportSuppression = null;
        }

        // 원작 LevelManager.InitLevel의 GetPlayerStartLocation과 LoadAndTeleport다. 컨트롤러가 첫 월드 초기화에서 하위 씬을 켠 뒤 한 번 부른다.
        // 출격 방식은 이 씬 진입에 한 번만 꺼내므로 출발점이 없는 씬(기지)도 불러 남은 값을 비운다. 벙커 문 출격은 씬에 저작된 캐릭터 자리 그대로다.
        // 원작은 매번 무작위지만, 제품은 상자처럼 출격마다 같은 시드라 같은 출격을 다시 불러도 같은 출발점이다(원작 차이).
        // 지도 선택인데 고를 출발점이 없으면 저작 결함이라 한 번 경고하고 캐릭터 자리에서 시작한다.
        internal void PlaceAtRaidStart(OriginalStartPoints? points, Func<string, int> seed)
        {
            RaidEntry entry = GameManager.Scenes.TakeRaidEntry();
            // 첫 월드 초기화는 플레이어 리그가 만든 모터가 살아 있을 때만 돌아 모터를 다시 보지 않는다.
            if (entry.Kind != RaidEntryKind.MapSelection) return;
            if (points == null || !points.TryPick(entry.BeaconIndex, new System.Random(seed("StartPoints")), out Vector3 position, out int subscene))
            {
                Log.Instance.Warning("지도 선택 출격의 출발점이 없어 캐릭터 자리에서 시작합니다. 비콘 번호 " + entry.BeaconIndex, _world.LogContext);
                return;
            }
            MovePlayer(position, subscene);
        }

        // 원작 SimpleTeleporter의 이동이다. 대상 하위 씬이 있으면 먼저 켠 뒤 모터를 옮긴다. 모터는 이동 의도와 수직 속도도 초기화한다.
        // 새 공간의 카메라와 충돌을 같은 프레임에 맞추고, 1초 동안 다시 순간이동하지 않게 막는다. 진행 표시는 제품 호출자 TickTimedInteraction이 내린다.
        void Teleport(MultiSceneTeleporter teleporter)
        {
            MovePlayer(teleporter.Destination, teleporter.DestinationSubscene);
            _teleportUntil = Time.time + _teleportCooldown;
        }

        // 하위 씬 번호가 0 이상이면 그 하위 씬을 먼저 켜고 모터를 옮긴 뒤, 새 자리의 충돌과 카메라를 같은 프레임에 맞춘다.
        // 순간이동과 레벨 시작 배치가 함께 쓴다. 하위 씬 문은 하위 씬을 먼저 바꿔 두고 -1로 옮기기만 한다.
        void MovePlayer(Vector3 position, int subscene)
        {
            if (subscene >= 0) _switchSubscene(subscene);
            _world.Motor.Teleport(position);
            Physics.SyncTransforms();
            _world.GameCamera.Snap();
        }
    }
}

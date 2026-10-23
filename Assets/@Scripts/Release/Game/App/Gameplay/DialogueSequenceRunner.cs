#nullable enable annotations
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace Game.App
{
    // 프롤로그와 캠페인의 대사 한 묶음을 원본 Dialogues.DialogueUI처럼 진행한다. 진행 중에는 이동 입력을 막고 공용 HUD를 숨긴다.
    // 확인을 누르면 공개 중인 글자를 마저 보이고, 다 보인 줄에서는 다음 줄로 넘긴다. 일시정지 같은 차단 중에는 확인만 무시하고 글자 공개는 계속한다.
    // 마지막 줄을 넘기면 상자를 닫고 HUD와 입력을 되돌린 뒤 종료 콜백을 부른다. 이동은 모터의 2프레임 대기 뒤에 다시 풀린다.
    internal sealed class DialogueSequenceRunner : IDisposable
    {
        internal const string _startSound = "event:/UI/dialogue_start";
        internal const string _bumpSound = "event:/UI/dialogue_bump";
        readonly DialogueBoxView _box;
        readonly SharedHudView _hud;
        readonly OriginalPlayerMotor _motor;
        readonly InputAction _confirm, _click;
        readonly Action<string> _playSound;
        IReadOnlyList<string>? _lineKeys;
        string? _speakerKey;
        Action<int>? _lineStarted;
        Action? _finished;
        IDisposable? _suppression;
        bool _disposed;
        // 대사 상자의 글자 공개 이벤트를 구독 중인지다. Attach가 켜고 Detach가 끈다.
        bool _attached;

        // 확인 입력은 원본 DialogueUI처럼 Space(UI/DialogueConfirm)와 클릭(Player/Attack)이다. F 상호작용은 대사를 넘기지 않는다.
        internal DialogueSequenceRunner(DialogueBoxView box, SharedHudView hud, OriginalPlayerMotor motor, InputAction confirm, InputAction click,
            Action<string> playSound)
        {
            _box = box;
            _hud = hud;
            _motor = motor;
            _confirm = confirm;
            _click = click;
            _playSound = playSound;
        }

        // 소유자가 공용 표시를 붙일 때 글자 공개 표시음 구독을 건다. 대사 상자는 앱 수명 UI라 레벨 초기화 중에는 걸지 않는다.
        // 해제 뒤나 이미 걸었으면 아무것도 하지 않는다.
        internal void Attach()
        {
            if (_disposed || _attached) return;
            _attached = true;
            _box.CharacterRevealed += OnCharacterRevealed;
        }

        // 진행 중인 대사를 종료 콜백 없이 닫고 글자 공개 구독을 푼다. 소유자가 공용 표시를 뗄 때 HUD를 떼기 전에 부른다.
        // 두 번째 호출은 공용 UI를 건드리지 않는다.
        internal void Detach()
        {
            Close();
            if (!_attached) return;
            _attached = false;
            _box.CharacterRevealed -= OnCharacterRevealed;
        }

        // 대사 묶음을 시작한 뒤 마지막 줄을 넘기거나 취소될 때까지 참이다.
        internal bool IsOpen => _lineKeys != null;
        // 표시 중인 줄 번호다. 진행 중이 아니면 -1이다.
        internal int LineIndex { get; private set; } = -1;

        // 대사 묶음을 시작한다. speakerKey가 null이면 이름 탭을 숨긴다. lineStarted는 각 줄을 보이기 직전에, finished는 정리를 마친 뒤 부른다.
        // 진행 중에 다시 부르면 입력 억제 토큰은 그대로 두고 새 묶음으로 바꾼다.
        internal void Begin(IReadOnlyList<string> lineKeys, string? speakerKey, Action<int>? lineStarted, Action? finished)
        {
            _lineKeys = lineKeys;
            _speakerKey = speakerKey;
            _lineStarted = lineStarted;
            _finished = finished;
            _suppression ??= _motor.SuppressInput();
            _hud.SetDialogueActive(true);
            ShowLine(0);
        }

        // 이번 프레임의 확인 입력을 처리한다. acceptsInput이 false면 상자와 줄은 그대로 두고 입력만 무시한다.
        internal void Tick(bool acceptsInput)
        {
            if (_lineKeys == null || !acceptsInput) return;
            if (!_confirm.WasPressedThisFrame() && !_click.WasPressedThisFrame()) return;
            if (!_box.Confirm()) return;
            if (LineIndex + 1 < _lineKeys.Count)
            {
                ShowLine(LineIndex + 1);
                return;
            }
            Action? finished = _finished;
            Close();
            if (finished != null) finished();
        }

        // 진행 중인 대사를 종료 콜백 없이 취소하고 구독을 푼다. 소유자가 이미 Detach했으면 공용 HUD와 대사 상자를 다시 건드리지 않는다.
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
        }

        void ShowLine(int index)
        {
            LineIndex = index;
            if (_lineStarted != null) _lineStarted(index);
            _box.ShowLine(GameLocalization.Text(_lineKeys![index]), _speakerKey != null ? GameLocalization.Text(_speakerKey) : null);
        }

        // 상태를 먼저 비우고 상자와 HUD를 되돌린 뒤 이 묶음의 억제 토큰을 반납한다. 진행 중인 대사가 없으면 아무것도 바꾸지 않는다.
        // 공용 HUD와 GameplayUI는 앱 수명이지만 Play Mode 종료처럼 씬 정리 없이 파괴될 때는 먼저 사라질 수 있다. 그래도 토큰은 반납하도록
        // Unity null 검사를 둔다.
        void Close()
        {
            if (_lineKeys == null) return;
            _lineKeys = null;
            _speakerKey = null;
            _lineStarted = null;
            _finished = null;
            LineIndex = -1;
            if (_box != null) _box.Hide();
            if (_hud != null) _hud.SetDialogueActive(false);
            IDisposable? suppression = _suppression;
            _suppression = null;
            if (suppression != null) suppression.Dispose();
        }

        // 원본 DoSubtitle처럼 첫 글자는 dialogue_start, 이후 글자마다 dialogue_bump를 낸다. 확인으로 한 번에 드러난 글자도 각각 낸다.
        // 표시음 콜백은 던지지 않는 GameAudioManager.PlayOneShot으로 이어지므로 예외 경계를 두지 않는다.
        // 상자는 이 진행자가 ShowLine한 뒤부터 Close의 Hide까지만 글자를 드러내므로 _lineKeys를 검사하지 않는다.
        void OnCharacterRevealed(bool first) => _playSound(first ? _startSound : _bumpSound);
    }
}

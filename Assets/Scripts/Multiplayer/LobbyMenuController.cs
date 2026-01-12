// LobbyMenuController.cs
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using Unity.Services.Multiplayer;
using System.Threading.Tasks;


public class LobbyMenuController : MonoBehaviour
{
    public static LobbyMenuController Instance { get; private set; }

    [Header("Scene")]
    [SerializeField] string gameSceneName = "Game";

    [Header("UI References")]
    [SerializeField] Button hostButton;
    [SerializeField] TMP_InputField joinCodeInput;
    [SerializeField] Button joinButton;
    [SerializeField] Button playButton;

    [SerializeField] TMP_Text joinCodeText;
    [SerializeField] Button copyCodeButton;

    [SerializeField] TMP_Text statusText;

    ISession _session;

    //Reuse delegates to avoid allocations
    Action<string> _onPlayerJoined;
    Action<string> _onPlayerLeaving;
    Action _onPlayerProps;
    Action _onSessionProps;
    Action _onChanged;

    readonly System.Collections.Concurrent.ConcurrentQueue<Action> _mainThreadQueue = new System.Collections.Concurrent.ConcurrentQueue<Action>();
    int _mainThreadId;

    enum LobbyState {
        Idle,       //Not in a session, no operation in progress
        Creating,   //HostCreateAsync in progress
        Joining,    //JoinByCodeAsync in progress
        InSession,  //_session is non-null and joined/hosting
        Leaving     //LeaveSessionAsync in progress
    }
    LobbyState _state = LobbyState.Idle;
    bool IsBusy =>
        _state == LobbyState.Creating ||
        _state == LobbyState.Joining ||
        _state == LobbyState.Leaving;
    bool IsInSession => _state == LobbyState.InSession && _session != null;

    void Awake() {
        if (Instance != null && Instance != this) {
            //Destroy duplicate and exit
            Destroy(this.gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(this.gameObject);

        _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        
        //Wire UI
        hostButton.onClick.AddListener(OnHostButtonClicked);
        joinButton.onClick.AddListener(OnJoinButtonClicked);
        
        playButton.onClick.AddListener(OnHostPressPlay);
        copyCodeButton.onClick.AddListener(CopyJoinCodeToClipboard);

        //Initial UI state
        SetLobbyUIEnabled(false);
        playButton.interactable = false;
        statusText.text = "Not in a session.";
        joinCodeText.text = "-";
        
        //Pre-allocate delegates to avoid GC
        _onPlayerJoined = OnPlayerJoinedCallback;
        _onPlayerLeaving = OnPlayerLeavingCallback;
        _onPlayerProps = OnPlayerPropsCallback;
        _onSessionProps = OnSessionPropsCallback;
        _onChanged = OnChangedCallback;
    }

    //Fire and forget
    private void OnHostButtonClicked() { FireAndForget(HostCreateAsync()); }
    private void OnJoinButtonClicked() { FireAndForget(JoinByCodeAsync(joinCodeInput.text)); }
    private void FireAndForget(Task task) {
        //Centralized error logging
        if (task == null) return;
        task.ContinueWith(t => {
            if (t.Exception != null) Debug.LogException(t.Exception.Flatten().InnerException ?? t.Exception);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void Update() {
        while (_mainThreadQueue.TryDequeue(out var a)) {
            if (this) { //Check if the object is still valid, else stop processing the queue
                a?.Invoke();
            }
            else return;
        }
    }

    void RunOnMainThread(Action action) {
        if (System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId)  action();
        else  _mainThreadQueue.Enqueue(action);
    }

    //Pre-allocated callback methods to reduce GC
    void OnPlayerJoinedCallback(string playerId) => RunOnMainThread(RefreshLobbyUI);
    void OnPlayerLeavingCallback(string playerId) => RunOnMainThread(RefreshLobbyUI);
    void OnPlayerPropsCallback() => RunOnMainThread(RefreshLobbyUI);
    void OnSessionPropsCallback() => RunOnMainThread(RefreshLobbyUI);
    void OnChangedCallback() => RunOnMainThread(RefreshLobbyUI);

    //Session lifecycle (create/join/leave)
    public async Task HostCreateAsync() {
        //Guard against overlapping operations
        if (IsBusy) {
            RunOnMainThread(() => {
                if (statusText != null) statusText.text = "Busy, please wait...";
            });
            return;
        }

        if (IsInSession) {  //Auto-leave existing session before hosting
            await LeaveSessionAsync();
        }

        _state = LobbyState.Creating;
        SetUiBusy(true);

        try {
            await UgsReady.EnsureAsync();

            var options = new SessionOptions {
                Name = "My Session",
                MaxPlayers = 4,
                IsPrivate = false
            }.WithRelayNetwork();

            var svc = MultiplayerService.Instance
                    ?? throw new InvalidOperationException("MultiplayerService.Instance is null.");

            var newSession = await svc.CreateSessionAsync(options)
                            ?? throw new InvalidOperationException("CreateSessionAsync returned null.");

            _session = newSession;
            HookSessionEvents(_session);

            RunOnMainThread(() => {
                if (statusText != null) statusText.text = $"Hosting. SessionId={_session.Id}";
                if (joinCodeText != null) joinCodeText.text = _session.Code ?? "-";
                SetLobbyUIEnabled(true);
                RefreshLobbyUI();
            });

            _state = LobbyState.InSession;
        }
        catch (Exception e) {
            Debug.LogException(e);
            RunOnMainThread(() => {
                if (statusText != null) statusText.text = $"Host failed: {e.Message}";
            });

            if (_session != null) {
                try { await _session.LeaveAsync(); } catch { }
                _session = null;
            }

            _state = LobbyState.Idle;
            throw; //if FireAndForget logs, this will end up there
        }
        finally {
            SetUiBusy(false);
        }
    }


    public async Task JoinByCodeAsync(string code) {
        if (string.IsNullOrWhiteSpace(code)) {
            if (statusText != null) statusText.text = "Enter a join code.";
            return;
        }

        if (IsBusy) {
            RunOnMainThread(() => {
                if (statusText != null) statusText.text = "Busy, please wait...";
            });
            return;
        }

        //Force leave current session before joining a new one
        if (IsInSession) {
            await LeaveSessionAsync();
        }

        _state = LobbyState.Joining;
        SetUiBusy(true);

        try {
            await UgsReady.EnsureAsync();

            var svc = MultiplayerService.Instance ?? throw new InvalidOperationException("MultiplayerService.Instance is null after init.");

            var newSession = await svc.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant()) ?? throw new InvalidOperationException("Join returned null session (invalid/expired code?).");

            _session = newSession;
            HookSessionEvents(_session);

            RunOnMainThread(() =>
            {
                if (statusText != null) statusText.text = $"Joined. SessionId={_session.Id}";
                if (joinCodeText != null) joinCodeText.text = _session.Code ?? "-";
                SetLobbyUIEnabled(true);
                RefreshLobbyUI();
            });

            _state = LobbyState.InSession;
        }
        catch (Unity.Services.Core.RequestFailedException rfe) {
            Debug.LogException(rfe);
            RunOnMainThread(() =>
            {
                if (statusText != null) statusText.text = $"Join failed ({rfe.ErrorCode}): {rfe.Message}";
            });
            _state = LobbyState.Idle;
        }
        catch (Exception e) {
            Debug.LogException(e);
            RunOnMainThread(() =>
            {
                if (statusText != null) statusText.text = $"Join failed: {e.Message}";
            });
            _state = LobbyState.Idle;
        }
        finally {
            SetUiBusy(false);
        }
    }

    public async Task LeaveSessionAsync()
    {
        if (_state == LobbyState.Leaving || _session == null) return;

        _state = LobbyState.Leaving;
        SetUiBusy(true);

        var session = _session;
        _session = null; //Detach early to avoid races

        try {
            if (session != null) {
                UnhookSessionEvents(session);
                await session.LeaveAsync();
            }
        }
        catch (Exception e) {
            Debug.LogException(e);
        }
        finally {
            _state = LobbyState.Idle;

            RunOnMainThread(() => {
                if (joinCodeText != null) joinCodeText.text = "-";
                if (statusText != null) statusText.text = "Left session.";
                SetLobbyUIEnabled(false);
                if (playButton != null) playButton.interactable = false;
            });

            SetUiBusy(false);
        }
    }

    //UI actions
    public void OnHostPressPlay() {
        if (_session == null) return;
        if (!_session.IsHost) return;

        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null && nm.IsServer) {
            nm.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }
    }

    void CopyJoinCodeToClipboard() {
        if (!string.IsNullOrEmpty(_session?.Code)) {
            GUIUtility.systemCopyBuffer = _session.Code;
            if (statusText != null) statusText.text = "Join code copied.";
        }
    }

    //Events & UI updates
    void HookSessionEvents(ISession s) {
        if (s == null) return;

        s.PlayerJoined += _onPlayerJoined;
        s.PlayerLeaving += _onPlayerLeaving;
        s.PlayerPropertiesChanged += _onPlayerProps;
        s.SessionPropertiesChanged += _onSessionProps;
        s.Changed += _onChanged;
    }

    void UnhookSessionEvents(ISession s) {
        if (s == null) return;
        
        s.PlayerJoined -= _onPlayerJoined;
        s.PlayerLeaving -= _onPlayerLeaving;
        s.PlayerPropertiesChanged -= _onPlayerProps;
        s.SessionPropertiesChanged -= _onSessionProps;
        s.Changed -= _onChanged;
    }

    void SetLobbyUIEnabled(bool inSession) {
        if (copyCodeButton != null) copyCodeButton.interactable = inSession;

        //Base host/join interactivity on state:
        bool canStartSession = _state == LobbyState.Idle;
        if (hostButton != null) hostButton.interactable = canStartSession;
        if (joinButton != null) joinButton.interactable = canStartSession;
        if (joinCodeInput != null) joinCodeInput.interactable = canStartSession;

        if (playButton != null) playButton.interactable = inSession && _session != null && _session.IsHost;
    }

    void SetUiBusy(bool busy) {
        //Disable buttons during an operation.
        if (busy) {
            if (hostButton != null) hostButton.interactable = false;
            if (joinButton != null) joinButton.interactable = false;
            if (joinCodeInput != null) joinCodeInput.interactable = false;
        }
        else {
            //Recompute from state
            SetLobbyUIEnabled(IsInSession);
        }
    }

    private const string STATUS_FORMAT = "{0}/{1} in lobby";
    private const string HOST_SUFFIX = " (You are Host)";
    void RefreshLobbyUI() {
        if (_session == null) {
            if (playButton != null) playButton.interactable = false;
            return;
        }

        //Enable Play only for host
        if (playButton != null) playButton.interactable = _session.IsHost;

        if (joinCodeText != null) joinCodeText.text = _session.Code ?? "-";
        if (statusText != null) statusText.text = string.Format(STATUS_FORMAT, _session.PlayerCount, _session.MaxPlayers) + (_session.IsHost ? HOST_SUFFIX : "");
    }

    static string ShortId(string id) => string.IsNullOrEmpty(id) ? "-" : (id.Length <= 8 ? id : id.Substring(0, 8));

    async void OnApplicationQuit() {
        try { await LeaveSessionAsync(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    void OnDestroy() {
        //Unhook to avoid further callbacks into a dead object
        if (_session != null) UnhookSessionEvents(_session);
        _session = null;
        //Don’t await here, full leave is handled on other code paths.
    }


}
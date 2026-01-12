// MultiplayerBootstrap.cs
using System;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using System.Threading.Tasks;
using Unity.Services.Core.Environments;

public class MultiplayerBootstrap : MonoBehaviour
{
    async void Awake()
    {
        DontDestroyOnLoad(gameObject);
        try
        {
            var options = new InitializationOptions().SetEnvironmentName("Production");
            await UnityServices.InitializeAsync(); //UGS init
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                var myPlayerName = "CustomName";
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                await AuthenticationService.Instance.UpdatePlayerNameAsync(myPlayerName);
            }
            Debug.Log($"Signed in. PlayerID: {AuthenticationService.Instance.PlayerId}");
        }
        catch (Exception e) { Debug.LogException(e); }
    }
}

static class UgsReady
{
    static Task _readyTask;

    public static Task EnsureAsync()
    {
        //Prevent multiple init calls
        return _readyTask ??= EnsureImpl();
    }

    static async Task EnsureImpl()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
}
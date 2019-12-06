﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Rest;
using UnityEngine;
using WikidataGame;
using WikidataGame.Models;

/// <summary>
///     The communicator acts as a connector between backend and frontend.
///     All functions are static for now to ensure simple usage
/// </summary>
public class Communicator : MonoBehaviour
{
    private const string AUTH_TOKEN = "AUTH_TOKEN";
    private const string CURRENT_GAME_ID = "CURRENT_GAME_ID";

    private static WikidataGameAPI _gameApi;
    private static Guid? _currentGameId;
    private static string _authToken { get; set; }
    private static string SERVER_URL => Configuration.Instance.ServerURL;

    /// <summary>
    ///     Indicates if client is connected to WikiData API
    /// </summary>
    /// <returns>if client is connected</returns>
    public static bool IsConnected()
    {
        return _gameApi != null;
    }

    /// <summary>
    ///     This function checks whether we have an auth token and restores it;
    ///     if not, it will request a new token from the server
    ///     This means: Before you do anything else, call this method.
    /// </summary>
    /// <returns>asynchronous Task</returns>
    public static async Task SetupApiConnection()
    {
        // have we already set up the api connection?
        if (_gameApi != null) return;

        // do we have an auth token that's saved?
        Debug.Log("Trying to restore previously saved auth token…");
        var authToken = PlayerPrefs.GetString(AUTH_TOKEN);

        if (string.IsNullOrEmpty(authToken))
        {
            
            const string password = "password";
            
            Debug.Log("No auth token in PlayerPrefs, fetching new token from server");
            var apiClient = new WikidataGameAPI(new Uri(SERVER_URL), new TokenCredentials("auth"));
            // CancellationTokenSource cts = new CancellationTokenSource(); // <-- Cancellation Token if you want to cancel the request, user quits, etc. [cts.Cancel()]
            var pushToken = PushHandler.Instance.pushToken ?? "";
            
            Debug.Log($"Push token is {pushToken}");
            Debug.Log($"Password is {password}");
            Debug.Log($"Username is {Configuration.Instance.UserName}");
            
            var authResponse = await apiClient.AuthenticateAsync(
                Configuration.Instance.UserName, password, pushToken);
            authToken = authResponse.Bearer;
            PlayerPrefs.SetString(AUTH_TOKEN, authToken);
        }

        Debug.Log($"Auth token: {authToken}");
        
        

        // this _gameApi can now be used by all other methods
        _gameApi = new WikidataGameAPI(new Uri(SERVER_URL), new TokenCredentials(authToken));
        
    }

    /// <summary>
    ///     This function updates the auth token when a push token is received
    /// </summary>
    /// <param name="token">Authentication token</param>
    /// <returns>asynchronous Task</returns>
    public static async Task UpdateApiConnection(string token)
    {
        /**
         * set current auth token to null to prevent inconsistencies
         */
        PlayerPrefs.SetString(AUTH_TOKEN, null);

        /**
         * if game API was not built yet, use standard setup function
         */
        if (_gameApi == null)
        {
            await SetupApiConnection();
            return;
        }

        Debug.Log("Rebuilding auth token with newly generated push token");

        /**
         * regenerate API and auth process
         */

        var apiClient = new WikidataGameAPI(new Uri(SERVER_URL), new TokenCredentials("auth"));
        var pushToken = token;
        var authResponse = await apiClient.AuthenticateAsync(SystemInfo.deviceUniqueIdentifier, pushToken);
        var authToken = authResponse.Bearer;

        Debug.Log($"Saving new auth token {authToken} in Player Prefs");
        PlayerPrefs.SetString(AUTH_TOKEN, authToken);

        /**
         * reset game api with new auth token
         */

        _gameApi = new WikidataGameAPI(new Uri(SERVER_URL), new TokenCredentials(authToken));
    }

    /// <summary>
    ///     Creates a game when no previous game was found or joins a game
    /// </summary>
    /// <returns>asynchronous Task</returns>
    public static async Task CreateOrJoinGame()
    {
        var gameInfo = await _gameApi.CreateNewGameAsync();
        Debug.Log(gameInfo);
        _currentGameId = gameInfo.GameId;
        Debug.Log($"Initialized new game with id: {_currentGameId}");
        PlayerPrefs.SetString(CURRENT_GAME_ID, _currentGameId.ToString());
    }

    /// <summary>
    ///     Checks if a game already exists
    /// </summary>
    /// <returns>previous game if it exists, otherwise null</returns>
    public static async Task<Game> RestorePreviousGame()
    {
        var previousGameId = PlayerPrefs.GetString(CURRENT_GAME_ID);
        if (!string.IsNullOrEmpty(previousGameId))
            try
            {
                _currentGameId = Guid.Parse(previousGameId);
                var state = await GetCurrentGameState();
                return state;
            }
            catch (Exception e)
            {
                _currentGameId = null;
                Debug.LogError(e);
                Debug.Log($"Game with ID {_currentGameId} could not be restored - deleting from player prefs");
                PlayerPrefs.DeleteKey(CURRENT_GAME_ID);
                return null;
            }

        return null;
    }

    /// <summary>
    ///     Use this function to create a new minigame by providing a Tile object and the categoryId
    /// </summary>
    /// <param name="tileId">ID of current tile</param>
    /// <param name="categoryId">ID of selected category</param>
    /// <returns>new MiniGame</returns>
    public static async Task<MiniGame> InitializeMinigame(Guid? tileId, Guid? categoryId)
    {
        if (_currentGameId == null)
            throw new Exception("Client is not part of any game.");
        
        var init = new MiniGameInit(tileId, categoryId);
        var minigame = await _gameApi.InitalizeMinigameAsync(_currentGameId.Value, init);
        Debug.Log($"TASK:{minigame.TaskDescription}");
        Debug.Log($"Started minigame with id {minigame.Id} on tile {tileId} with category {categoryId}");
        return minigame;
    }

    /// <summary>
    ///     Use this function to get needed information about the minigame you just created
    /// </summary>
    /// <param name="minigameId">ID of the current MiniGame</param>
    /// <returns>MiniGame reference</returns>
    public static async Task<MiniGame> RetrieveMinigameInfo(Guid minigameId)
    {
        if (_currentGameId == null)
            throw new Exception("Client is not part of any game.");
        
        var cts = new CancellationTokenSource();
        var miniGame = await _gameApi.RetrieveMinigameInfoAsync(_currentGameId.Value, minigameId, cts.Token);
        return miniGame;
    }

    /// <summary>
    ///     Use this function to POST answers for a minigame to the backend
    ///     you are getting a MiniGameResult back, which indicates if the answer was right or wrong
    /// </summary>
    /// <param name="minigameId">ID of the current MiniGame</param>
    /// <param name="answers">One or more answers</param>
    /// <returns>result of the MiniGame</returns>
    public static async Task<MiniGameResult> AnswerMinigame(Guid minigameId, IList<string> answers)
    {
        if (_currentGameId == null)
            throw new Exception("Client is not part of any game.");
        
        var result = await _gameApi.AnswerMinigameAsync(_currentGameId.Value, minigameId, answers);
        return result;
    }

    /// <summary>
    ///     Use this function to delete the current game
    /// </summary>
    /// <returns>asynchronous Task</returns>
    public static async Task AbortCurrentGame()
    {
        if (_currentGameId == null)
            throw new Exception("Client is not part of any game.");
        
        PlayerPrefs.DeleteKey(CURRENT_GAME_ID);
        await _gameApi.DeleteGameAsync(_currentGameId.Value);
    }

    /// <summary>
    ///     Use this function to get the game state of the current game
    /// </summary>
    /// <returns>state of the current game</returns>
    public static async Task<Game> GetCurrentGameState()
    {
        if (_currentGameId == null)
            throw new Exception("Client is not part of any game.");
        
        return await _gameApi.RetrieveGameStateAsync(_currentGameId.Value);
    }
}
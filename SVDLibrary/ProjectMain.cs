using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Player;
using SDG.Unturned;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Environment = Rocket.Unturned.Environment;
using Logger = Rocket.Core.Logging.Logger;

namespace SeniorS.SVDLibrary;
public class SVDLibrary : RocketPlugin<Configuration>
{
    public static SVDLibrary Instance;
    public Dictionary<ulong, List<byte[]>> Voices;

    public string Token = string.Empty;
    private Harmony _harmony;

    protected override void Load()
    {
        Instance = this;
        Voices = new Dictionary<ulong, List<byte[]>>();

        _harmony = new Harmony("com.seniors.svdlibrary");
        _harmony.PatchAll(this.Assembly);

        StartCoroutine(SaveVoices());

        Provider.onEnemyConnected += OnEnemyConnected;
        Level.onLevelLoaded += OnLevelLoaded;

        Logger.Log($"SVDLibrary v{this.Assembly.GetName().Version}");
        Logger.Log("<<SSPlugins>>");
    }

    // This probably wouldn't be the best idea of using it.
    // The best will to only convert data when required.
    // Example: User A reported User B, only then, convert the voice data of these players.
    private IEnumerator SaveVoices()
    {
        while(Instance)
        {
            yield return new WaitForSeconds(20);
            long fileTimeUtc = DateTime.UtcNow.ToFileTimeUtc();
            foreach (KeyValuePair<ulong, List<byte[]>> data in Voices)
            {
                Logger.Log($"Sending data of {data.Key}");
                Task.Run(async () =>
                {
                    try
                    {
                        using HttpClient client = new();
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                        string decoderURL = $"{Configuration.Instance.apiURL}/decoder";
                        
                        string jsonData = JsonConvert.SerializeObject(new { source = data.Value });
                        StringContent content = new(jsonData, Encoding.UTF8, "application/json");
                        HttpResponseMessage response = await client.PostAsync(decoderURL, content);
                        
                        if(response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            // Your token expired. You will need to get a new one.
                        }

                        if (response.IsSuccessStatusCode)
                        {
                            byte[] audioData = await response.Content.ReadAsByteArrayAsync();
                            
                            // This directory should be replaced with one of user election.
                            string path = Path.Combine(Environment.RocketDirectory, $"{data.Key} - {fileTimeUtc}.wav");
                            File.WriteAllBytes(path, audioData);
                        }
                        else
                        {
                            throw new HttpRequestException($"HTTP request failed with status code {response.StatusCode} - {response.RequestMessage}");
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        Console.WriteLine($"An error occurred: {ex.InnerException.Message}");
                    }
                });
                yield return new WaitForSeconds(0.5f);
            }
        } 

        yield break;
    }

    private void OnEnemyConnected(SteamPlayer player)
    {
        UnturnedPlayer user = UnturnedPlayer.FromSteamPlayer(player);

        if (user.HasPermission(Configuration.Instance.recordPermission))
        {
            Voices.Add(player.playerID.steamID.m_SteamID, new());
        }
    }

    private void OnLevelLoaded(int level)
    {
        Task.Run(async () =>
        {
            try
            {
                using HttpClient client = new();
                string authenticationUrl = $"{Configuration.Instance.apiURL}/authentication?username={Configuration.Instance.username}&key={Configuration.Instance.key}";
                using HttpResponseMessage response = await client.GetAsync(authenticationUrl);
                if (response.IsSuccessStatusCode)
                {
                    JObject responseObj = JObject.Parse(await response.Content.ReadAsStringAsync());
                    string token = responseObj["token"].Value<string>();
                    this.Token = token;
                }
                else
                {
                    Console.WriteLine($"Failed to get token. Status code: {response.StatusCode} - {response.RequestMessage}");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"An error occurred: {ex.InnerException.Message}");
            }
        });
    }

    // This function is better prepare for a daily use so you can safely copy paste this function.
    IEnumerator Upload(List<byte[]> data, string token, string key)
    {
        long fileTimeUTC = DateTime.UtcNow.ToFileTimeUtc();
        List<IMultipartFormSection> multipartFormSections = new();
        data.ForEach(c =>
        {
            int index = data.IndexOf(c);
            multipartFormSections.Add(new MultipartFormFileSection($"packet_{index}", c, $"{index}.bin", "application/octet-stream"));
        });

        using UnityWebRequest request = UnityWebRequest.Post(Configuration.Instance.apiURL, multipartFormSections);
        request.SetRequestHeader("Authorization", "Bearer " + token);
        request.downloadHandler = new DownloadHandlerBuffer();
        yield return request.SendWebRequest();

        if (request.responseCode == 401)
        {
            // Your token expired. You will need to get a new one.
            yield break;
        }

        if (request.responseCode != 200)
        {
            Logger.LogError($"UnityWebRequest failed with status code {request.responseCode} - {request.result.ToString()}");
            yield break;
        }

        byte[] audioData = request.downloadHandler.data;
        // This directory should be replaced with one of user election.
        string path = Path.Combine(Rocket.Unturned.Environment.RocketDirectory, $"{key} - {fileTimeUTC}.wav");
        File.WriteAllBytes(path, audioData);
    }

    protected override void Unload()
    {
        Instance = null;
        Voices.Clear();

        StopCoroutine(SaveVoices());

        _harmony.UnpatchAll(_harmony.Id);

        Provider.onEnemyConnected -= OnEnemyConnected;
        Level.onLevelLoaded -= OnLevelLoaded;

        Logger.Log("<<SSPlugins>>");
    }
}
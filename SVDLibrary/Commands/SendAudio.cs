using System;
using System.Collections;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Rocket.Core.Utils;
using SDG.NetPak;
using SDG.Unturned;
using SeniorS.SVDLibrary.Helpers;
using SeniorS.SVDLibrary.Models;
using UnityEngine;

namespace SeniorS.SVDLibrary.Commands;

public class SendAudio : IRocketCommand
{
    public AllowedCaller AllowedCaller => AllowedCaller.Player;

    public string Name => "SendAudio";

    public string Help => "";

    public string Syntax => "/SendAudio";
    private string SyntaxError => $"Wrong syntax! Correct usage: {Syntax}";

    public List<string> Aliases => [];
    public List<string> Permissions => ["ss.command.SendAudio"];

    public void Execute(IRocketPlayer caller, string[] command)
    {
        if (command.Length != 0)
        {
            UnturnedChat.Say(caller, SyntaxError, Color.red, true);
            return;
        }

        UnturnedPlayer user = (UnturnedPlayer)caller;
        
        // This can be any wav file.
        string wavFile = Path.Combine(System.Environment.CurrentDirectory, "audio.wav");
        float[] wavData = WavReader.ReadWavFile(wavFile);
        
        HttpClient httpClient = new();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SVDLibrary.Instance.Token);

        AudioRequest requestData = new()
        {
            WavData = wavData,
            /* This SHOULD be the steam id of the player which will play the packets, but it could be any valid steam id and will work fine. */
            SteamID = user.CSteamID.m_SteamID 
        };

        string json = JsonConvert.SerializeObject(requestData);
        StringContent content = new(json, Encoding.UTF8, "application/json");
        string apiUrl = $"{SVDLibrary.Instance.Configuration.Instance.apiURL}/encoder"; // Update to match your API URL

        Task.Run(async () =>
        {
            HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                string result = await response.Content.ReadAsStringAsync();
                List<byte[]> sections = JsonConvert.DeserializeObject<List<byte[]>>(result);
                
                TaskDispatcher.QueueOnMainThread(() =>
                {
                    // If the packets are sent through the player then he won't hear the audio.
                    // It's recommended to use a bot/dummy to send the packets. Or black magic ;)
                    SVDLibrary.Instance.StartCoroutine(SendVoice(sections, user.Player.channel.owner));
                    UnturnedChat.Say("Song loaded, starting broadcast...");
                });
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                string error = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Details: " + error);
            }
        });
    }
    
    private IEnumerator SendVoice(List<byte[]> packets, SteamPlayer dummy)
    {
        // You shouldn't do this everytime, it's greatly recommended to load the method on plugin load and here just call the reference.
        FieldInfo fieldInfo = typeof(PlayerVoice).GetField("SendPlayVoiceChat", BindingFlags.NonPublic | BindingFlags.Static);
        if (fieldInfo == null)
        {
            throw new Exception("NELSON BROKE STUFF AGAIN <3");
        }
        ClientInstanceMethod sendPlayVoiceChat = (ClientInstanceMethod)fieldInfo.GetValue(null);

        foreach (byte[] packet in packets)
        {
            sendPlayVoiceChat.Invoke(dummy.player.voice.GetNetId(), SDG.NetTransport.ENetReliability.Unreliable, Provider.GatherRemoteClientConnections(),
                delegate (NetPakWriter writer)
                {
                    writer.WriteUInt16((ushort)packet.Length);
                    writer.WriteBit(false);
                    writer.WriteBytes(packet, 0, (ushort)packet.Length);
                }
            );
            
            // This seems really specific... that's because it's.
            // You can change the .67 if you want, but basically:
            // If the packet is sent too early the sound will be glitchy
            // If the packet is sent too late then the sound will be delayed.
            // From my tests this is how it works the best, feel free to play and find the best delay for it ^^
            yield return new WaitForSeconds((packet.Length / 3.67f) / 1000f);
        }
        yield break;
    }
}
using HarmonyLib;
using JetBrains.Annotations;
using Rocket.Core.Logging;
using SDG.NetPak;
using SDG.Unturned;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace SeniorS.SVDLibrary.Patches;
/// <summary>
/// Transpile to ReceiveVoiceChatRelay
/// All rights to BlazingFlame (https://github.com/DanielWillett) for making this class.
/// </summary>
[HarmonyPatch]
class PlayerVoiceTranspiler
{
    private static void OnVoiceActivity(PlayerVoice playerVoice, ArraySegment<byte> data)
    {
        byte[] array = data.ToArray();
        ulong steamID = playerVoice.channel.owner.playerID.steamID.m_SteamID;
        if (SVDLibrary.Instance.Voices.ContainsKey(steamID))
        {
            SVDLibrary.Instance.Voices[steamID].Add(array);
        }
    }

    [UsedImplicitly]
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(PlayerVoice), "ReceiveVoiceChatRelay")]
    private static IEnumerable<CodeInstruction> TranspileReceiveVoiceChatRelay(IEnumerable<CodeInstruction> instructions, MethodBase method)
    {
        MethodInfo? readbytesPtr = typeof(NetPakReader)
                                   .GetMethod(nameof(NetPakReader.ReadBytesPtr), BindingFlags.Instance | BindingFlags.Public);
        if (readbytesPtr == null)
        {
            Logger.LogWarning($"{method.FullDescription()} - Failed to find NetPakReader.ReadBytesPtr(int, out byte[], out int).");
            return instructions;
        }

        ConstructorInfo? arrSegmentCtor = typeof(ArraySegment<byte>)
                                         .GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, [ typeof(byte[]), typeof(int), typeof(int)], null);
        if (arrSegmentCtor == null)
        {
            Logger.LogWarning($"{method.FullDescription()} - Failed to find ArraySegment<byte>(byte[], int, int).");
            return instructions;
        }

        FieldInfo? rpcField = typeof(PlayerVoice).GetField("SendPlayVoiceChat", BindingFlags.NonPublic | BindingFlags.Static);
        if (readbytesPtr == null)
        {
            Logger.LogWarning($"{method.FullDescription()} - Failed to find PlayerVoice.SendPlayVoiceChat.");
            return instructions;
        }

        MethodInfo invokeTarget = new Action<PlayerVoice, ArraySegment<byte>>(OnVoiceActivity).Method;

        List<CodeInstruction> ins = [.. instructions];

        int readCallPos = -1;
        bool success = false;
        for (int i = 0; i < ins.Count; ++i)
        {
            CodeInstruction opcode = ins[i];
            if (readCallPos == -1 && opcode.Calls(readbytesPtr))
            {
                readCallPos = i;
                if (i == ins.Count - 1)
                    break;
            }
            else if (readCallPos != -1 && opcode.LoadsField(rpcField))
            {
                object? displayClassLocal = null;

                FieldInfo? byteArrLocal = null,
                           offsetLocal = null,
                           lengthLocal = null;

                for (int j = readCallPos - 6; j < readCallPos; ++j)
                {
                    CodeInstruction backtrackOpcode = ins[j];
                    if (backtrackOpcode.IsLdloc())
                    {
                        if (backtrackOpcode.opcode == OpCodes.Ldloc_0)
                            displayClassLocal = 0;
                        else if (backtrackOpcode.opcode == OpCodes.Ldloc_1)
                            displayClassLocal = 1;
                        else if (backtrackOpcode.opcode == OpCodes.Ldloc_2)
                            displayClassLocal = 2;
                        else if (backtrackOpcode.opcode == OpCodes.Ldloc_3)
                            displayClassLocal = 3;
                        else
                            displayClassLocal = (LocalBuilder)backtrackOpcode.operand;
                    }
                    else if (backtrackOpcode.opcode == OpCodes.Ldfld || backtrackOpcode.opcode == OpCodes.Ldflda)
                    {
                        FieldInfo loadedField = (FieldInfo)backtrackOpcode.operand;
                        if (lengthLocal == null)
                            lengthLocal = loadedField;
                        else if (byteArrLocal == null)
                            byteArrLocal = loadedField;
                        else if (offsetLocal == null)
                            offsetLocal = loadedField;
                    }
                }

                if (displayClassLocal == null || byteArrLocal == null || offsetLocal == null || lengthLocal == null)
                {
                    Logger.LogWarning($"{method.FullDescription()} - Failed to discover local fields or display class local." +
                                                   $"disp class: {displayClassLocal != null}, byte arr: {byteArrLocal != null}," +
                                                   $"offset: {offsetLocal != null}, length: {lengthLocal != null}.");
                    break;
                }

                CodeInstruction startInstruction = new CodeInstruction(
                    displayClassLocal switch
                    {
                        int index => index switch
                        {
                            0 => OpCodes.Ldloc_0,
                            1 => OpCodes.Ldloc_1,
                            2 => OpCodes.Ldloc_2,
                            _ => OpCodes.Ldloc_3
                        },
                        _ => OpCodes.Ldloc
                    }, displayClassLocal is LocalBuilder ? displayClassLocal : null);
                ins.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                ins.Insert(i + 1, startInstruction);
                ins.Insert(i + 2, new CodeInstruction(OpCodes.Ldfld, byteArrLocal));
                ins.Insert(i + 3, new CodeInstruction(startInstruction.opcode, startInstruction.operand));
                ins.Insert(i + 4, new CodeInstruction(OpCodes.Ldfld, offsetLocal));
                ins.Insert(i + 5, new CodeInstruction(startInstruction.opcode, startInstruction.operand));
                ins.Insert(i + 6, new CodeInstruction(OpCodes.Ldfld, lengthLocal));

                ins.Insert(i + 7, new CodeInstruction(OpCodes.Newobj, arrSegmentCtor));
                ins.Insert(i + 8, new CodeInstruction(OpCodes.Call, invokeTarget));
                success = true;
                break;
            }
        }

        if (success)
            return ins;

        Logger.LogWarning($"{method.FullDescription()} - Failed to patch voice to copy voice data for recording.");
        return instructions;
    }
}

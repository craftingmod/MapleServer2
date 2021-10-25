﻿using System.Text.RegularExpressions;
using MaplePacketLib2.Tools;
using MapleServer2.Constants;
using MapleServer2.Network;
using NLog;

namespace MapleServer2.Tools
{
    /*
    * This class is a way to resolve packets sent by the server.
    * It will try to find the packet structure using the client error logging system.
    * It will save the packet structure in the packet structure file located in the MapleServer2\PacketStructure folder.
    * You can change each packet value in the file and it'll try to continue resolving from the last value.
    * More info in the mapleme.me/docs/tutorials/packet-resolver
    */
    public class PacketStructureResolver
    {
        private const int HEADER_LENGTH = 6;

        private readonly string DefaultValue;
        private readonly ushort OpCode;
        private readonly string PacketName;
        private readonly PacketWriter Packet;
        private readonly Dictionary<uint, SockHintInfo> Overrides;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly Regex infoRegex = new Regex(@"\[type=(\d+)\]\[offset=(\d+)\]\[hint=(\w+)\]");

        private PacketStructureResolver(ushort opCode)
        {
            DefaultValue = "0";
            OpCode = opCode;
            Packet = PacketWriter.Of(opCode);
            PacketName = Enum.GetName(typeof(SendOp), opCode);
            Overrides = new Dictionary<uint, SockHintInfo>();
        }

        // resolve opcode
        // Example: resolve 81
        public static PacketStructureResolver Parse(string input)
        {
            string[] args = input.Split(" ", StringSplitOptions.RemoveEmptyEntries);

            // Parse opCode: 81 0081 0x81 0x0081
            ushort opCode;
            string firstArg = args[0];
            if (firstArg.ToLower().StartsWith("0x"))
            {
                opCode = Convert.ToUInt16(firstArg, 16);
            }
            else
            {
                if (firstArg.Length == 2)
                {
                    opCode = firstArg.ToByte();
                }
                else if (firstArg.Length == 4)
                {
                    // Reverse bytes
                    byte[] bytes = firstArg.ToByteArray();
                    Array.Reverse(bytes);

                    opCode = BitConverter.ToUInt16(bytes);
                }
                else
                {
                    Logger.Info("Invalid opcode.");
                    return null;
                }
            }

            PacketStructureResolver resolver = new PacketStructureResolver(opCode);
            DirectoryInfo dir = Directory.CreateDirectory("PacketStructures");

            string filePath = $"{dir.FullName}/{resolver.OpCode:D4} - {resolver.PacketName}.txt";
            if (!File.Exists(filePath))
            {
                StreamWriter writer = File.CreateText(filePath);
                writer.WriteLine("#Generated by MapleServer2 PacketStructureResolver");
                IEnumerable<char> enumerable = opCode.ToString("D4").Reverse();
                writer.WriteLine($"PacketWriter pWriter = PacketWriter.Of(SendOp.{resolver.PacketName});");
                writer.Close();
                return resolver;
            }

            List<string> fileLines = File.ReadAllLines(filePath).Skip(2).ToList();
            foreach (string line in fileLines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                string[] packetLine = line.Split("(");
                string type = packetLine[0][13..];
                string valueAsString = packetLine[1].Split(")")[0];
                valueAsString = string.IsNullOrEmpty(valueAsString) ? "0" : valueAsString;
                try
                {
                    switch (type)
                    {
                        case "Byte":
                            resolver.Packet.WriteByte(byte.Parse(valueAsString));
                            break;
                        case "Short":
                            resolver.Packet.WriteShort(short.Parse(valueAsString));
                            break;
                        case "Int":
                            resolver.Packet.WriteInt(int.Parse(valueAsString));
                            break;
                        case "Long":
                            resolver.Packet.WriteLong(long.Parse(valueAsString));
                            break;
                        case "Float":
                            resolver.Packet.WriteFloat(float.Parse(valueAsString));
                            break;
                        case "UnicodeString":
                            resolver.Packet.WriteUnicodeString(type.Replace("\"", ""));
                            break;
                        case "String":
                            resolver.Packet.WriteString(type.Replace("\"", ""));
                            break;
                        default:
                            Logger.Info($"Unknown type: {type}");
                            break;
                    }
                }
                catch
                {
                    Logger.Info($"Couldn't parse value on function: {line}");
                    return null;
                }
            }
            return resolver;
        }

        public void Start(Session session)
        {
            session.OnError = AppendAndRetry;

            // Start off the feedback loop
            session.Send(Packet);
        }

        private void AppendAndRetry(object session, string err)
        {
            SockExceptionInfo info = ParseError(err);
            if (info.Type == 0)
            {
                return;
            }
            if (OpCode != info.Type)
            {
                Logger.Warn($"Error for unexpected op code:{info.Type:X4}");
                return;
            }
            if (Packet.Length + HEADER_LENGTH != info.Offset)
            {
                Logger.Warn($"Offset:{info.Offset} does not match Packet length:{Packet.Length + HEADER_LENGTH}");
                return;
            }

            new SockHintInfo(info.Hint, DefaultValue).Update(Packet);
            string hint = info.Hint switch
            {
                SockHint.Decode1 => $"pWriter.WriteByte();\r\n",
                SockHint.Decode2 => $"pWriter.WriteShort();\r\n",
                SockHint.Decode4 => $"pWriter.WriteInt();\r\n",
                SockHint.Decodef => $"pWriter.WriteFloat();\r\n",
                SockHint.Decode8 => $"pWriter.WriteLong();\r\n",
                SockHint.DecodeStr => $"pWriter.WriteUnicodeString();\r\n",
                SockHint.DecodeStrA => $"pWriter.WriteString();\r\n",
                _ => $"[]\r\n",
            };
            DirectoryInfo dir = Directory.CreateDirectory("PacketStructures");
            StreamWriter file = File.AppendText($"{dir.FullName}/{OpCode:D4} - {PacketName}.txt");
            file.Write(hint);
            file.Close();

            (session as Session)?.Send(Packet);
        }

        private static SockExceptionInfo ParseError(string error)
        {
            Match match = infoRegex.Match(error);
            if (match.Groups.Count != 4)
            {
                throw new ArgumentException($"Failed to parse error: {error}");
            }

            SockExceptionInfo info;
            info.Type = ushort.Parse(match.Groups[1].Value);
            info.Offset = uint.Parse(match.Groups[2].Value);
            info.Hint = match.Groups[3].Value.ToSockHint();

            return info;
        }

        private struct SockExceptionInfo
        {
            public ushort Type;
            public uint Offset;
            public SockHint Hint;
        }
    }
}
